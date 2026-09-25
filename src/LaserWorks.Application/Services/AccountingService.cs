using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record AccountRow(long Id, string Code, string NameAr, string NameEn, AccountType Type, string? ParentCode, bool IsPostable, bool IsSystem, bool IsActive, decimal Balance);

public sealed record JournalRow(long Id, string Number, DateTime Date, string Description, string? SourceType, string? SourceNumber, JournalStatus Status, bool IsManual,
    decimal TotalDebit, decimal TotalCredit, string? ReversalOf, string? ReversedBy, string? PostedBy);

public sealed record JournalLineRow(long Id, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Description, string? Job, string? Customer, string? Supplier, string? CostCenter);

public sealed record LedgerRow(DateTime Date, string EntryNumber, long EntryId, string Description, string? SourceType, string? SourceNumber, decimal Debit, decimal Credit, decimal Balance);

public sealed record TrialBalanceRow(string Code, string Name, AccountType Type, decimal OpeningDebit, decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit);

public sealed record StatementLine(string Code, string Name, decimal Amount, bool IsTotal = false, int Level = 1);

public sealed record ManualJournalLine(long AccountId, decimal Debit, decimal Credit, string? Description, long? CostCenterId = null, long? JobId = null, long? CustomerId = null, long? SupplierId = null);

public sealed class AccountingService : ServiceBase
{
    public AccountingService(ServiceContext ctx) : base(ctx) { }

    // ------------------------------------------------------------------ chart of accounts
    public async Task<List<AccountRow>> AccountsAsync(DateTime? asOf = null)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var to = (asOf ?? DateTime.MaxValue.Date.AddDays(-2)).Date.AddDays(1);
        var balances = await db.JournalLines.AsNoTracking().Where(l => l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < to)
            .GroupBy(l => l.AccountId).Select(g => new { g.Key, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) }).ToListAsync();
        var accounts = await db.Accounts.AsNoTracking().Include(a => a.Parent).OrderBy(a => a.Code).ToListAsync();
        var raw = accounts.ToDictionary(a => a.Id, a =>
        {
            var b = balances.FirstOrDefault(x => x.Key == a.Id);
            return b == null ? 0 : a.IsDebitNormal ? b.D - b.C : b.C - b.D;
        });
        decimal Total(Account a) => raw[a.Id] + accounts.Where(c => c.ParentId == a.Id).Sum(Total);
        return accounts.Select(a => new AccountRow(a.Id, a.Code, a.NameAr, a.NameEn, a.Type, a.Parent?.Code, a.IsPostable, a.IsSystem, a.IsActive, Total(a))).ToList();
    }

    public async Task<List<Account>> PostableAccountsAsync(AccountType? type = null) => await ReadAsync(db => db.Accounts.AsNoTracking()
        .Where(a => a.IsPostable && a.IsActive && (type == null || a.Type == type)).OrderBy(a => a.Code).ToListAsync());

    public async Task<long> SaveAccountAsync(Account input)
    {
        Demand(AppModule.Accounting, input.Id == 0 ? Permission.Create : Permission.Edit);
        Validation.Required(input.Code, "Code");
        Validation.Required(input.NameEn, "Name");
        await using var db = Factory.Create();
        if (await db.Accounts.AnyAsync(a => a.Code == input.Code.Trim() && a.Id != input.Id)) throw new DomainException("Err.DuplicateCode", input.Code);
        Account a;
        if (input.Id == 0) { a = new Account(); db.Accounts.Add(a); }
        else
        {
            a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
            var hasLines = await db.JournalLines.AnyAsync(l => l.AccountId == a.Id);
            if (a.IsSystem && (a.Type != input.Type || a.IsPostable != input.IsPostable)) throw new DomainException("Err.SystemRecord");
            if (hasLines && (a.Type != input.Type || !input.IsPostable)) throw new DomainException("Err.AccountHasEntries");
            if (a.IsSystem && !input.IsActive) throw new DomainException("Err.SystemRecord");
        }
        if (input.ParentId is { } pid)
        {
            var parent = await db.Accounts.FirstAsync(x => x.Id == pid);
            if (parent.IsPostable) throw new DomainException("Err.ParentMustBeHeader");
            if (parent.Type != input.Type) throw new DomainException("Err.ParentTypeMismatch");
            if (pid == input.Id) throw new DomainException("Err.ParentTypeMismatch");
        }
        a.Code = input.Code.Trim(); a.NameEn = input.NameEn.Trim(); a.NameAr = string.IsNullOrWhiteSpace(input.NameAr) ? input.NameEn.Trim() : input.NameAr.Trim();
        a.Type = input.Type; a.ParentId = input.ParentId; a.IsPostable = input.IsPostable; a.IsActive = input.IsActive; a.IsCashOrBank = input.IsCashOrBank;
        Audit(db, input.Id == 0 ? AuditAction.Created : AuditAction.Updated, nameof(Account), input.Id == 0 ? null : input.Id, a.Code);
        await db.SaveChangesAsync();
        return a.Id;
    }

    public async Task DeleteAccountAsync(long id)
    {
        Demand(AppModule.Accounting, Permission.Delete);
        await using var db = Factory.Create();
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (a.IsSystem) throw new DomainException("Err.SystemRecord");
        if (await db.JournalLines.AnyAsync(l => l.AccountId == id) || await db.Accounts.AnyAsync(x => x.ParentId == id) || await db.ExpenseCategories.AnyAsync(c => c.AccountId == id))
            throw new DomainException("Err.InUseCannotDelete");
        db.Accounts.Remove(a);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ journal entries
    public async Task<PagedResult<JournalRow>> ListEntriesAsync(PageRequest req, DateRange? range = null, string? sourceType = null, bool manualOnly = false, JournalStatus? status = null)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var q = db.JournalEntries.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(e => e.Date >= range.From.Date && e.Date < range.ToExclusive);
        if (!string.IsNullOrEmpty(sourceType)) q = q.Where(e => e.SourceType == sourceType);
        if (manualOnly) q = q.Where(e => e.IsManual);
        if (status.HasValue) q = q.Where(e => e.Status == status);
        if (req.Search.Norm() is { } s) q = q.Where(e => e.Number.Contains(s) || e.Description.Contains(s) || (e.SourceNumber != null && e.SourceNumber.Contains(s)));
        return await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new JournalRow(e.Id, e.Number, e.Date, e.Description, e.SourceType, e.SourceNumber, e.Status, e.IsManual, e.Lines.Sum(l => l.Debit), e.Lines.Sum(l => l.Credit),
                db.JournalEntries.Where(x => x.Id == e.ReversalOfId).Select(x => x.Number).FirstOrDefault(), db.JournalEntries.Where(x => x.Id == e.ReversedById).Select(x => x.Number).FirstOrDefault(), e.PostedBy))
            .ToPagedAsync(req);
    }

    public async Task<List<JournalLineRow>> EntryLinesAsync(long entryId) => await ReadAsync(db => db.JournalLines.AsNoTracking().Where(l => l.JournalEntryId == entryId).OrderBy(l => l.Id)
        .Select(l => new JournalLineRow(l.Id, l.Account!.Code, l.Account.NameEn, l.Debit, l.Credit, l.Description,
            db.Jobs.Where(j => j.Id == l.JobId).Select(j => j.Number).FirstOrDefault(), db.Customers.Where(c => c.Id == l.CustomerId).Select(c => c.Name).FirstOrDefault(),
            db.Suppliers.Where(c => c.Id == l.SupplierId).Select(c => c.Name).FirstOrDefault(), db.CostCenters.Where(c => c.Id == l.CostCenterId).Select(c => c.Name).FirstOrDefault()))
        .ToListAsync());

    public async Task<JournalEntry?> GetEntryAsync(long id) => await ReadAsync(db => db.JournalEntries.AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account).FirstOrDefaultAsync(e => e.Id == id));

    /// <summary>Saves a manual draft journal (drafts can be edited/deleted; posting makes it immutable).</summary>
    public async Task<long> SaveManualDraftAsync(long id, DateTime date, string description, IReadOnlyList<ManualJournalLine> lines)
    {
        Demand(AppModule.Accounting, id == 0 ? Permission.Create : Permission.Edit);
        Validation.Required(description, "Description");
        if (lines.Count < 2) throw new DomainException("Err.JournalMinLines");
        foreach (var l in lines)
        {
            if (l.AccountId == 0) throw new DomainException("Err.Required", "Account");
            if (l.Debit < 0 || l.Credit < 0 || (l.Debit != 0 && l.Credit != 0) || (l.Debit == 0 && l.Credit == 0)) throw new DomainException("Err.InvalidJournalLine");
        }
        return await TxAsync(async db =>
        {
            JournalEntry e;
            if (id == 0)
            {
                e = new JournalEntry { Number = await Numbering.NextAsync(db, SequenceKey.Journal), Status = JournalStatus.Draft, IsManual = true, SourceType = "Manual" };
                db.JournalEntries.Add(e);
            }
            else
            {
                e = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
                if (e.Status != JournalStatus.Draft) throw new DomainException("Err.PostedEntryImmutable", e.Number);
                db.JournalLines.RemoveRange(e.Lines);
                e.Lines = new();
            }
            e.Date = date.Date;
            e.Description = description.Trim();
            foreach (var l in lines)
            {
                var acc = await db.Accounts.FirstAsync(a => a.Id == l.AccountId);
                if (!acc.IsPostable || !acc.IsActive) throw new DomainException("Err.AccountNotPostable", acc.Code);
                e.Lines.Add(new JournalLine { AccountId = l.AccountId, Debit = Money.Round(l.Debit), Credit = Money.Round(l.Credit), Description = l.Description.Norm(), CostCenterId = l.CostCenterId, JobId = l.JobId, CustomerId = l.CustomerId, SupplierId = l.SupplierId });
            }
            await db.SaveChangesAsync();
            return e.Id;
        });
    }

    public async Task PostManualAsync(long id)
    {
        Demand(AppModule.Accounting, Permission.Post);
        await TxAsync(async db =>
        {
            var e = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (e.Status != JournalStatus.Draft) throw new DomainException("Err.PostedEntryImmutable", e.Number);
            await AccountingEngine.EnsureOpenPeriodAsync(db, e.Date);
            if (e.TotalDebit != e.TotalCredit || e.TotalDebit == 0) throw new DomainException("Err.UnbalancedEntry", e.TotalDebit, e.TotalCredit);
            e.Status = JournalStatus.Posted;
            e.PostedAt = Now;
            e.PostedBy = UserName;
            Audit(db, AuditAction.Posted, nameof(JournalEntry), e.Id, e.Number);
        });
    }

    public async Task DeleteDraftAsync(long id)
    {
        Demand(AppModule.Accounting, Permission.Delete);
        await using var db = Factory.Create();
        var e = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (e.Status != JournalStatus.Draft) throw new DomainException("Err.PostedEntryImmutable", e.Number);
        db.JournalEntries.Remove(e);
        await db.SaveChangesAsync();
    }

    /// <summary>Reverses a posted entry with a mirror entry. Automatic (document) entries are reversed through their documents where possible.</summary>
    public async Task<long> ReverseAsync(long id, DateTime date, string reason)
    {
        Demand(AppModule.Accounting, Permission.Post);
        Validation.Required(reason, "Reason");
        return await TxAsync(async db =>
        {
            var e = await db.JournalEntries.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (!e.IsManual) throw new DomainException("Err.ReverseThroughDocument");
            var rev = await AccountingEngine.ReverseAsync(db, id, date, reason, UserName, Now);
            Audit(db, AuditAction.Reversed, nameof(JournalEntry), id, $"{e.Number} → {rev.Number}: {reason}");
            return rev.Id;
        });
    }

    /// <summary>Opening balance of a customer or supplier against Opening Balance Equity.</summary>
    public async Task PostPartyOpeningBalanceAsync(long? customerId, long? supplierId, decimal amount, DateTime date)
    {
        Demand(AppModule.Accounting, Permission.Post);
        if (amount == 0) throw new DomainException("Err.AmountPositive");
        await TxAsync(async db =>
        {
            JournalDraft draft;
            if (customerId is { } cid)
            {
                var c = await db.Customers.FirstAsync(x => x.Id == cid);
                draft = new JournalDraft { Date = date, Description = $"Opening balance — {c.Name}", SourceType = "Opening", SourceId = cid, SourceNumber = c.Code, IsManual = true };
                if (amount > 0) draft.Dr(SystemAccounts.AR, amount, tags: new LineTags(CustomerId: cid)).Cr(SystemAccounts.OpeningEquity, amount);
                else draft.Dr(SystemAccounts.OpeningEquity, -amount).Cr(SystemAccounts.AR, -amount, tags: new LineTags(CustomerId: cid));
            }
            else if (supplierId is { } sid)
            {
                var s = await db.Suppliers.FirstAsync(x => x.Id == sid);
                draft = new JournalDraft { Date = date, Description = $"Opening balance — {s.Name}", SourceType = "Opening", SourceId = sid, SourceNumber = s.Code, IsManual = true };
                if (amount > 0) draft.Dr(SystemAccounts.OpeningEquity, amount).Cr(SystemAccounts.AP, amount, tags: new LineTags(SupplierId: sid));
                else draft.Dr(SystemAccounts.AP, -amount, tags: new LineTags(SupplierId: sid)).Cr(SystemAccounts.OpeningEquity, -amount);
            }
            else throw new DomainException("Err.Required", "Party");
            await AccountingEngine.PostAsync(db, draft, UserName, Now);
        });
    }

    /// <summary>Opening balance of a GL account (cash, bank, capital...) against Opening Balance Equity. Positive = debit.</summary>
    public async Task PostAccountOpeningBalanceAsync(long accountId, decimal debitAmount, DateTime date)
    {
        Demand(AppModule.Accounting, Permission.Post);
        if (debitAmount == 0) throw new DomainException("Err.AmountPositive");
        await TxAsync(async db =>
        {
            var a = await db.Accounts.FirstAsync(x => x.Id == accountId);
            if (a.SystemKey is SystemAccounts.AR or SystemAccounts.AP or SystemAccounts.Inventory or SystemAccounts.InventoryFG or SystemAccounts.InventoryConsumables or SystemAccounts.InventoryRemnants)
                throw new DomainException("Err.UseSubledgerOpening");
            var draft = new JournalDraft { Date = date, Description = $"Opening balance — {a.Code} {a.NameEn}", SourceType = "Opening", SourceNumber = a.Code, IsManual = true };
            if (debitAmount > 0) draft.Dr(a.Id, debitAmount).Cr(SystemAccounts.OpeningEquity, debitAmount);
            else draft.Dr(SystemAccounts.OpeningEquity, -debitAmount).Cr(a.Id, -debitAmount);
            await AccountingEngine.PostAsync(db, draft, UserName, Now);
        });
    }

    // ------------------------------------------------------------------ ledgers & statements
    public async Task<List<LedgerRow>> GeneralLedgerAsync(long accountId, DateRange range, long? customerId = null, long? supplierId = null, long? jobId = null)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var acc = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        var baseQ = db.JournalLines.AsNoTracking().Where(l => l.AccountId == accountId && l.JournalEntry!.Status != JournalStatus.Draft);
        if (customerId.HasValue) baseQ = baseQ.Where(l => l.CustomerId == customerId);
        if (supplierId.HasValue) baseQ = baseQ.Where(l => l.SupplierId == supplierId);
        if (jobId.HasValue) baseQ = baseQ.Where(l => l.JobId == jobId);
        var from = range.From.Date;
        var opening = await baseQ.Where(l => l.JournalEntry!.Date < from).SumAsync(l => l.Debit - l.Credit);
        var lines = await baseQ.Where(l => l.JournalEntry!.Date >= from && l.JournalEntry.Date < range.ToExclusive)
            .OrderBy(l => l.JournalEntry!.Date).ThenBy(l => l.JournalEntryId).ThenBy(l => l.Id)
            .Select(l => new { l.JournalEntry!.Date, l.JournalEntry.Number, l.JournalEntryId, Desc = l.Description ?? l.JournalEntry.Description, l.JournalEntry.SourceType, l.JournalEntry.SourceNumber, l.Debit, l.Credit })
            .ToListAsync();
        var sign = acc.IsDebitNormal ? 1 : -1;
        var bal = opening * sign;
        var result = new List<LedgerRow> { new(from, "", 0, "Opening balance", null, null, opening > 0 ? opening : 0, opening < 0 ? -opening : 0, bal) };
        foreach (var l in lines)
        {
            bal += (l.Debit - l.Credit) * sign;
            result.Add(new LedgerRow(l.Date, l.Number, l.JournalEntryId, l.Desc, l.SourceType, l.SourceNumber, l.Debit, l.Credit, bal));
        }
        return result;
    }

    public async Task<List<TrialBalanceRow>> TrialBalanceAsync(DateRange range, bool includeZero = false)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var from = range.From.Date; var to = range.ToExclusive;
        var agg = await db.JournalLines.AsNoTracking().Where(l => l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < to)
            .GroupBy(l => new { l.AccountId, Before = l.JournalEntry!.Date < from })
            .Select(g => new { g.Key.AccountId, g.Key.Before, D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) }).ToListAsync();
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IsPostable).OrderBy(a => a.Code).ToListAsync();
        var rows = new List<TrialBalanceRow>();
        foreach (var a in accounts)
        {
            var o = agg.Where(x => x.AccountId == a.Id && x.Before).Sum(x => x.D - x.C);
            var pd = agg.Where(x => x.AccountId == a.Id && !x.Before).Sum(x => x.D);
            var pc = agg.Where(x => x.AccountId == a.Id && !x.Before).Sum(x => x.C);
            var c = o + pd - pc;
            if (!includeZero && o == 0 && pd == 0 && pc == 0) continue;
            rows.Add(new TrialBalanceRow(a.Code, a.NameEn, a.Type, o > 0 ? o : 0, o < 0 ? -o : 0, pd, pc, c > 0 ? c : 0, c < 0 ? -c : 0));
        }
        return rows;
    }

    private async Task<Dictionary<long, decimal>> NetByAccountAsync(IAppDb db, DateTime? from, DateTime toExclusive) =>
        (await db.JournalLines.AsNoTracking().Where(l => l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < toExclusive && (from == null || l.JournalEntry.Date >= from))
            .GroupBy(l => l.AccountId).Select(g => new { g.Key, N = g.Sum(x => x.Debit - x.Credit) }).ToListAsync()).ToDictionary(x => x.Key, x => x.N);

    /// <summary>Income statement: revenue, cost of sales, gross profit, operating expenses (net of costs absorbed into jobs), net profit.</summary>
    public async Task<List<StatementLine>> IncomeStatementAsync(DateRange range, bool arabic = false)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var net = await NetByAccountAsync(db, range.From.Date, range.ToExclusive);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IsPostable && (a.Type == AccountType.Revenue || a.Type == AccountType.Expense)).OrderBy(a => a.Code).ToListAsync();
        string N(Account a) => arabic ? a.NameAr : a.NameEn;
        var lines = new List<StatementLine>();
        var rev = accounts.Where(a => a.Type == AccountType.Revenue).Select(a => (a, amt: -net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        var cos = accounts.Where(a => a.Type == AccountType.Expense && a.Code.StartsWith('5')).Select(a => (a, amt: net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        var opex = accounts.Where(a => a.Type == AccountType.Expense && !a.Code.StartsWith('5')).Select(a => (a, amt: net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        lines.Add(new StatementLine("4000", arabic ? "الإيرادات" : "Revenue", 0, true, 0));
        lines.AddRange(rev.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        var totalRev = rev.Sum(x => x.amt);
        lines.Add(new StatementLine("", arabic ? "صافي الإيرادات" : "Net revenue", totalRev, true));
        lines.Add(new StatementLine("5000", arabic ? "تكلفة المبيعات" : "Cost of sales", 0, true, 0));
        lines.AddRange(cos.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        var totalCos = cos.Sum(x => x.amt);
        lines.Add(new StatementLine("", arabic ? "إجمالي تكلفة المبيعات" : "Total cost of sales", totalCos, true));
        lines.Add(new StatementLine("GP", arabic ? "مجمل الربح" : "Gross profit", totalRev - totalCos, true, 0));
        lines.Add(new StatementLine("6000", arabic ? "المصروفات التشغيلية" : "Operating expenses", 0, true, 0));
        lines.AddRange(opex.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        var totalOpex = opex.Sum(x => x.amt);
        lines.Add(new StatementLine("", arabic ? "إجمالي المصروفات التشغيلية" : "Total operating expenses", totalOpex, true));
        lines.Add(new StatementLine("NP", arabic ? "صافي الربح" : "Net profit", totalRev - totalCos - totalOpex, true, 0));
        return lines;
    }

    /// <summary>Balance sheet as of a date. Current year earnings = all revenue − expenses to date (no separate closing entries are required).</summary>
    public async Task<List<StatementLine>> BalanceSheetAsync(DateTime asOf, bool arabic = false)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var net = await NetByAccountAsync(db, null, asOf.Date.AddDays(1));
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IsPostable).OrderBy(a => a.Code).ToListAsync();
        string N(Account a) => arabic ? a.NameAr : a.NameEn;
        var lines = new List<StatementLine>();
        var assets = accounts.Where(a => a.Type == AccountType.Asset).Select(a => (a, amt: net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        var liabs = accounts.Where(a => a.Type == AccountType.Liability).Select(a => (a, amt: -net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        var equity = accounts.Where(a => a.Type == AccountType.Equity).Select(a => (a, amt: -net.GetValueOrDefault(a.Id))).Where(x => x.amt != 0).ToList();
        var earnings = -accounts.Where(a => a.Type is AccountType.Revenue or AccountType.Expense).Sum(a => net.GetValueOrDefault(a.Id));
        lines.Add(new StatementLine("1000", arabic ? "الأصول" : "Assets", 0, true, 0));
        lines.AddRange(assets.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        lines.Add(new StatementLine("TA", arabic ? "إجمالي الأصول" : "Total assets", assets.Sum(x => x.amt), true));
        lines.Add(new StatementLine("2000", arabic ? "الخصوم" : "Liabilities", 0, true, 0));
        lines.AddRange(liabs.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        lines.Add(new StatementLine("TL", arabic ? "إجمالي الخصوم" : "Total liabilities", liabs.Sum(x => x.amt), true));
        lines.Add(new StatementLine("3000", arabic ? "حقوق الملكية" : "Equity", 0, true, 0));
        lines.AddRange(equity.Select(x => new StatementLine(x.a.Code, N(x.a), x.amt)));
        lines.Add(new StatementLine("CYE", arabic ? "أرباح الفترة" : "Current earnings", earnings));
        lines.Add(new StatementLine("TE", arabic ? "إجمالي حقوق الملكية" : "Total equity", equity.Sum(x => x.amt) + earnings, true));
        lines.Add(new StatementLine("TLE", arabic ? "إجمالي الخصوم وحقوق الملكية" : "Total liabilities & equity", liabs.Sum(x => x.amt) + equity.Sum(x => x.amt) + earnings, true, 0));
        return lines;
    }

    /// <summary>Direct-method cash flow: movements of cash/bank accounts grouped by the source of the entry.</summary>
    public async Task<List<StatementLine>> CashFlowAsync(DateRange range, bool arabic = false)
    {
        Demand(AppModule.Accounting, Permission.View);
        await using var db = Factory.Create();
        var cashIds = await db.Accounts.Where(a => a.IsCashOrBank).Select(a => a.Id).ToListAsync();
        var opening = await db.JournalLines.Where(l => cashIds.Contains(l.AccountId) && l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < range.From.Date).SumAsync(l => l.Debit - l.Credit);
        var moves = await db.JournalLines.AsNoTracking().Where(l => cashIds.Contains(l.AccountId) && l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date >= range.From.Date && l.JournalEntry.Date < range.ToExclusive)
            .Select(l => new { l.JournalEntry!.SourceType, Net = l.Debit - l.Credit }).ToListAsync();
        string Label(string? src) => src switch
        {
            "CustomerPayment" => arabic ? "تحصيلات من العملاء" : "Receipts from customers",
            "SalesReturn" => arabic ? "مردودات نقدية للعملاء" : "Refunds to customers",
            "SupplierPayment" => arabic ? "مدفوعات للموردين" : "Payments to suppliers",
            "Expense" => arabic ? "مصروفات مدفوعة" : "Expenses paid",
            "Opening" => arabic ? "أرصدة افتتاحية" : "Opening balances",
            _ => arabic ? "حركات أخرى / قيود يدوية" : "Other / manual entries"
        };
        var lines = new List<StatementLine> { new("OB", arabic ? "رصيد النقدية أول الفترة" : "Opening cash balance", opening, true, 0) };
        foreach (var g in moves.GroupBy(m => Label(m.SourceType)).OrderBy(g => g.Key))
            lines.Add(new StatementLine("", g.Key, g.Sum(x => x.Net)));
        var change = moves.Sum(m => m.Net);
        lines.Add(new StatementLine("NC", arabic ? "صافي التغير في النقدية" : "Net change in cash", change, true));
        lines.Add(new StatementLine("CB", arabic ? "رصيد النقدية آخر الفترة" : "Closing cash balance", opening + change, true, 0));
        return lines;
    }

    // ------------------------------------------------------------------ fiscal periods
    public async Task<List<FiscalYear>> FiscalYearsAsync() => await ReadAsync(db => db.FiscalYears.AsNoTracking().Include(y => y.Periods).OrderByDescending(y => y.StartDate).ToListAsync());

    public async Task CreateFiscalYearAsync(DateTime start)
    {
        Demand(AppModule.Accounting, Permission.Create);
        await TxAsync(async db =>
        {
            var s = start.Date;
            if (await db.FiscalYears.AnyAsync(y => y.StartDate <= s.AddYears(1).AddDays(-1) && y.EndDate >= s)) throw new DomainException("Err.FiscalYearOverlap");
            var fy = new FiscalYear { Name = s.Month == 1 ? $"FY {s.Year}" : $"FY {s.Year}/{s.Year + 1}", StartDate = s, EndDate = s.AddYears(1).AddDays(-1) };
            for (int i = 0; i < 12; i++)
            {
                var ps = s.AddMonths(i);
                fy.Periods.Add(new FiscalPeriod { PeriodNo = i + 1, Name = ps.ToString("yyyy-MM"), StartDate = ps, EndDate = ps.AddMonths(1).AddDays(-1) });
            }
            db.FiscalYears.Add(fy);
        });
    }

    public async Task SetPeriodClosedAsync(long periodId, bool closed)
    {
        Demand(AppModule.Accounting, Permission.Approve);
        await TxAsync(async db =>
        {
            var p = await db.FiscalPeriods.Include(x => x.FiscalYear).FirstAsync(x => x.Id == periodId);
            if (!closed && p.FiscalYear!.IsClosed) throw new DomainException("Err.PeriodClosed", p.Name);
            p.IsClosed = closed;
            Audit(db, AuditAction.StatusChanged, nameof(FiscalPeriod), p.Id, $"{p.Name} closed={closed}");
        });
    }

    /// <summary>Checks the whole ledger balances (Σ debit = Σ credit) — shown in integrity checks.</summary>
    public async Task<(decimal Debit, decimal Credit)> LedgerTotalsAsync() => await ReadAsync(async db =>
    {
        var d = await db.JournalLines.Where(l => l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Debit);
        var c = await db.JournalLines.Where(l => l.JournalEntry!.Status != JournalStatus.Draft).SumAsync(l => l.Credit);
        return (d, c);
    });

    /// <summary>Balance of a system account (debit − credit) up to a date.</summary>
    public async Task<decimal> SystemBalanceAsync(string key, DateTime? asOf = null) => await ReadAsync(async db =>
    {
        var acc = await AccountingEngine.AccountAsync(db, key);
        var to = (asOf ?? new DateTime(9000, 1, 1)).Date.AddDays(1);
        return await db.JournalLines.Where(l => l.AccountId == acc.Id && l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date < to).SumAsync(l => l.Debit - l.Credit);
    });
}
