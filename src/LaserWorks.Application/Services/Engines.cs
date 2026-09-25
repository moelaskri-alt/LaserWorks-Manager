using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

/// <summary>Assigns document numbers from configurable sequences (inside the caller's transaction).</summary>
public static class Numbering
{
    private static readonly Dictionary<SequenceKey, string> DefaultPrefixes = new()
    {
        [SequenceKey.Customer] = "CUS-", [SequenceKey.Supplier] = "SUP-", [SequenceKey.Employee] = "EMP-", [SequenceKey.Machine] = "MC-",
        [SequenceKey.Material] = "MAT-", [SequenceKey.Request] = "REQ-", [SequenceKey.Estimate] = "EST-", [SequenceKey.Quotation] = "QT-",
        [SequenceKey.Job] = "JOB-", [SequenceKey.Invoice] = "INV-", [SequenceKey.SalesReturn] = "SRN-", [SequenceKey.CustomerPayment] = "RCV-",
        [SequenceKey.PurchaseOrder] = "PO-", [SequenceKey.PurchaseReceipt] = "GRN-", [SequenceKey.SupplierInvoice] = "PINV-",
        [SequenceKey.SupplierPayment] = "PAY-", [SequenceKey.PurchaseReturn] = "PRN-", [SequenceKey.Expense] = "EXP-",
        [SequenceKey.Journal] = "JV-", [SequenceKey.InventoryTx] = "IT-", [SequenceKey.Remnant] = "RM-", [SequenceKey.Scrap] = "SCR-"
    };

    public static string DefaultPrefix(SequenceKey key) => DefaultPrefixes.TryGetValue(key, out var p) ? p : key + "-";

    public static async Task<string> NextAsync(IAppDb db, SequenceKey key, CancellationToken ct = default)
    {
        var seq = db.NumberSequences.Local.FirstOrDefault(s => s.Key == key)
                  ?? await db.NumberSequences.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (seq == null)
        {
            seq = new NumberSequence { Key = key, Prefix = DefaultPrefix(key), NextNumber = 1, Padding = 5 };
            db.NumberSequences.Add(seq);
        }
        var n = seq.NextNumber;
        seq.NextNumber = n + 1;
        return seq.Format(n);
    }
}

public sealed class JournalDraft
{
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public string? SourceType { get; init; }
    public long? SourceId { get; init; }
    public string? SourceNumber { get; init; }
    public bool IsManual { get; init; }
    internal List<(string? Key, long? AccountId, decimal Debit, decimal Credit, string? Desc, LineTags Tags)> Lines { get; } = new();

    public JournalDraft Dr(string key, decimal amount, string? desc = null, LineTags? tags = null) => Add(key, null, amount, desc, tags);
    public JournalDraft Cr(string key, decimal amount, string? desc = null, LineTags? tags = null) => Add(key, null, -amount, desc, tags);
    public JournalDraft Dr(long accountId, decimal amount, string? desc = null, LineTags? tags = null) => Add(null, accountId, amount, desc, tags);
    public JournalDraft Cr(long accountId, decimal amount, string? desc = null, LineTags? tags = null) => Add(null, accountId, -amount, desc, tags);

    /// <summary>Signed amount: positive = debit, negative = credit.</summary>
    private JournalDraft Add(string? key, long? accountId, decimal signed, string? desc, LineTags? tags)
    {
        signed = Money.Round(signed);
        if (signed == 0) return this;
        Lines.Add((key, accountId, signed > 0 ? signed : 0, signed < 0 ? -signed : 0, desc, tags ?? LineTags.None));
        return this;
    }

    public bool IsEmpty => Lines.Count == 0;
}

public sealed record LineTags(long? JobId = null, long? CustomerId = null, long? SupplierId = null, long? CostCenterId = null, long? MachineId = null)
{
    public static readonly LineTags None = new();
}

/// <summary>Double-entry posting rules shared by every module.</summary>
public static class AccountingEngine
{
    public static async Task<Account> AccountAsync(IAppDb db, string systemKey, CancellationToken ct = default)
    {
        var acc = db.Accounts.Local.FirstOrDefault(a => a.SystemKey == systemKey)
                  ?? await db.Accounts.FirstOrDefaultAsync(a => a.SystemKey == systemKey, ct);
        return acc ?? throw new DomainException("Err.SystemAccountMissing", systemKey);
    }

    public static string InventoryAccountKey(MaterialKind kind) => kind switch
    {
        MaterialKind.FinishedGood => SystemAccounts.InventoryFG,
        MaterialKind.Consumable => SystemAccounts.InventoryConsumables,
        _ => SystemAccounts.Inventory
    };

    public static string CashAccountKey(PaymentMethod m) => m switch
    {
        PaymentMethod.Bank => SystemAccounts.Bank,
        PaymentMethod.Cash => SystemAccounts.Cash,
        _ => throw new DomainException("Err.PaymentMethodNotCash")
    };

    /// <summary>Validates and posts a journal entry. Returns null when the draft has no lines (nothing to post).</summary>
    public static async Task<JournalEntry?> PostAsync(IAppDb db, JournalDraft draft, string user, DateTime now, CancellationToken ct = default)
    {
        if (draft.IsEmpty) return null;
        await EnsureOpenPeriodAsync(db, draft.Date, ct);

        var entry = new JournalEntry
        {
            Number = await Numbering.NextAsync(db, SequenceKey.Journal, ct),
            Date = draft.Date.Date,
            Description = draft.Description,
            SourceType = draft.SourceType,
            SourceId = draft.SourceId,
            SourceNumber = draft.SourceNumber,
            IsManual = draft.IsManual,
            Status = JournalStatus.Posted,
            PostedAt = now,
            PostedBy = user,
            CreatedBy = user
        };
        foreach (var l in draft.Lines)
        {
            Account acc;
            if (l.Key != null) acc = await AccountAsync(db, l.Key, ct);
            else acc = await db.Accounts.FindAsync(new object[] { l.AccountId!.Value }, ct) ?? throw new DomainException("Err.AccountNotFound");
            if (!acc.IsPostable) throw new DomainException("Err.AccountNotPostable", acc.Code);
            if (!acc.IsActive) throw new DomainException("Err.AccountInactive", acc.Code);
            entry.Lines.Add(new JournalLine
            {
                Account = acc,
                AccountId = acc.Id,
                Debit = l.Debit,
                Credit = l.Credit,
                Description = l.Desc,
                JobId = l.Tags.JobId,
                CustomerId = l.Tags.CustomerId,
                SupplierId = l.Tags.SupplierId,
                CostCenterId = l.Tags.CostCenterId,
                MachineId = l.Tags.MachineId
            });
        }
        var dr = entry.Lines.Sum(x => x.Debit);
        var cr = entry.Lines.Sum(x => x.Credit);
        if (dr != cr) throw new DomainException("Err.UnbalancedEntry", dr, cr);
        db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>Creates a mirror entry and marks the original as reversed. The original is never edited or deleted.</summary>
    public static async Task<JournalEntry> ReverseAsync(IAppDb db, long entryId, DateTime date, string reason, string user, DateTime now, CancellationToken ct = default)
    {
        var original = await db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == entryId, ct)
                       ?? throw new DomainException("Err.NotFound");
        if (original.Status != JournalStatus.Posted) throw new DomainException("Err.OnlyPostedCanBeReversed");
        await EnsureOpenPeriodAsync(db, date, ct);
        var rev = new JournalEntry
        {
            Number = await Numbering.NextAsync(db, SequenceKey.Journal, ct),
            Date = date.Date,
            Description = $"Reversal of {original.Number}: {reason}",
            SourceType = original.SourceType,
            SourceId = original.SourceId,
            SourceNumber = original.SourceNumber,
            ReversalOfId = original.Id,
            Status = JournalStatus.Posted,
            PostedAt = now,
            PostedBy = user,
            CreatedBy = user,
            IsManual = original.IsManual
        };
        foreach (var l in original.Lines)
            rev.Lines.Add(new JournalLine
            {
                AccountId = l.AccountId, Debit = l.Credit, Credit = l.Debit, Description = l.Description,
                JobId = l.JobId, CustomerId = l.CustomerId, SupplierId = l.SupplierId, CostCenterId = l.CostCenterId, MachineId = l.MachineId
            });
        db.JournalEntries.Add(rev);
        await db.SaveChangesAsync(ct);
        original.Status = JournalStatus.Reversed;
        original.ReversedById = rev.Id;
        return rev;
    }

    /// <summary>A posting date must fall in an open fiscal period. Missing calendar years are created automatically.</summary>
    public static async Task EnsureOpenPeriodAsync(IAppDb db, DateTime date, CancellationToken ct = default)
    {
        var d = date.Date;
        var period = db.FiscalPeriods.Local.FirstOrDefault(p => p.StartDate <= d && p.EndDate >= d)
                     ?? await db.FiscalPeriods.Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.StartDate <= d && p.EndDate >= d, ct);
        if (period == null)
        {
            var year = await CreateFiscalYearAsync(db, d, ct);
            period = year.Periods.First(p => p.StartDate <= d && p.EndDate >= d);
        }
        var fy = period.FiscalYear ?? await db.FiscalYears.FindAsync(new object[] { period.FiscalYearId }, ct);
        if (period.IsClosed || fy?.IsClosed == true) throw new DomainException("Err.PeriodClosed", d.ToString("yyyy-MM-dd"));
    }

    /// <summary>Creates the fiscal year (12 monthly periods) containing <paramref name="date"/>, aligned to the first year's start month.</summary>
    public static async Task<FiscalYear> CreateFiscalYearAsync(IAppDb db, DateTime date, CancellationToken ct = default)
    {
        var first = db.FiscalYears.Local.OrderBy(y => y.StartDate).FirstOrDefault() ?? await db.FiscalYears.OrderBy(y => y.StartDate).FirstOrDefaultAsync(ct);
        var startMonth = first?.StartDate.Month ?? 1;
        var start = new DateTime(date.Month >= startMonth ? date.Year : date.Year - 1, startMonth, 1);
        var fy = new FiscalYear { Name = startMonth == 1 ? $"FY {start.Year}" : $"FY {start.Year}/{start.Year + 1}", StartDate = start, EndDate = start.AddYears(1).AddDays(-1) };
        for (int i = 0; i < 12; i++)
        {
            var ps = start.AddMonths(i);
            fy.Periods.Add(new FiscalPeriod { PeriodNo = i + 1, Name = ps.ToString("yyyy-MM"), StartDate = ps, EndDate = ps.AddMonths(1).AddDays(-1), FiscalYear = fy });
        }
        db.FiscalYears.Add(fy);
        await Task.CompletedTask;
        return fy;
    }
}

/// <summary>Inventory ledger engine: moving weighted average cost, per-warehouse quantities, append-only transactions.</summary>
public static class InventoryEngine
{
    public sealed record Movement(
        InventoryTxType Type, DateTime Date, long WarehouseId, decimal Quantity, decimal? UnitCost = null,
        long? JobId = null, string? SourceType = null, long? SourceId = null, string? Reference = null, string? Notes = null, bool AllowNegative = false);

    public static async Task<StockBalance> BalanceAsync(IAppDb db, long materialId, long warehouseId, CancellationToken ct = default)
    {
        var sb = db.StockBalances.Local.FirstOrDefault(b => b.MaterialId == materialId && b.WarehouseId == warehouseId)
                 ?? await db.StockBalances.FirstOrDefaultAsync(b => b.MaterialId == materialId && b.WarehouseId == warehouseId, ct);
        if (sb == null)
        {
            sb = new StockBalance { MaterialId = materialId, WarehouseId = warehouseId };
            db.StockBalances.Add(sb);
        }
        return sb;
    }

    /// <summary>Stock increase. Unit cost is required and becomes part of the moving average.</summary>
    public static async Task<InventoryTransaction> ReceiveAsync(IAppDb db, Material material, Movement mv, CancellationToken ct = default)
    {
        if (mv.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        var cost = mv.UnitCost ?? throw new DomainException("Err.UnitCostRequired");
        var pos = InventoryMath.Receive(new StockPosition(material.QuantityOnHand, material.StockValue), mv.Quantity, cost);
        var value = pos.Value - material.StockValue;
        var bal = await BalanceAsync(db, material.Id, mv.WarehouseId, ct);
        bal.Quantity += mv.Quantity;
        Apply(material, pos);
        return await AddTxAsync(db, material, mv, mv.Quantity, cost, value, ct);
    }

    /// <summary>Stock decrease valued at the current moving average cost.</summary>
    public static async Task<InventoryTransaction> IssueAsync(IAppDb db, Material material, Movement mv, CancellationToken ct = default)
    {
        if (mv.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        var bal = await BalanceAsync(db, material.Id, mv.WarehouseId, ct);
        if (!mv.AllowNegative && bal.Quantity < mv.Quantity) throw new DomainException("Err.InsufficientStockWarehouse", material.Code, bal.Quantity, mv.Quantity);
        var (pos, value, avg) = InventoryMath.Issue(new StockPosition(material.QuantityOnHand, material.StockValue), mv.Quantity, mv.AllowNegative);
        bal.Quantity -= mv.Quantity;
        Apply(material, pos);
        return await AddTxAsync(db, material, mv, -mv.Quantity, avg, -value, ct);
    }

    /// <summary>Stock decrease at a specific cost (purchase return at the original receipt cost).</summary>
    public static async Task<InventoryTransaction> IssueAtCostAsync(IAppDb db, Material material, Movement mv, CancellationToken ct = default)
    {
        if (mv.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        var bal = await BalanceAsync(db, material.Id, mv.WarehouseId, ct);
        if (bal.Quantity < mv.Quantity) throw new DomainException("Err.InsufficientStockWarehouse", material.Code, bal.Quantity, mv.Quantity);
        var (pos, value) = InventoryMath.IssueAtCost(new StockPosition(material.QuantityOnHand, material.StockValue), mv.Quantity, mv.UnitCost ?? material.AverageCost);
        bal.Quantity -= mv.Quantity;
        Apply(material, pos);
        return await AddTxAsync(db, material, mv, -mv.Quantity, mv.Quantity == 0 ? 0 : Math.Round(value / mv.Quantity, 6), -value, ct);
    }

    /// <summary>Moves quantity between warehouses. Value and average cost are unchanged.</summary>
    public static async Task<(InventoryTransaction Out, InventoryTransaction In)> TransferAsync(IAppDb db, Material material, long fromWarehouse, long toWarehouse, decimal qty, DateTime date, string? notes, CancellationToken ct = default)
    {
        if (fromWarehouse == toWarehouse) throw new DomainException("Err.SameWarehouse");
        if (qty <= 0) throw new DomainException("Err.QuantityPositive");
        var from = await BalanceAsync(db, material.Id, fromWarehouse, ct);
        if (from.Quantity < qty) throw new DomainException("Err.InsufficientStockWarehouse", material.Code, from.Quantity, qty);
        var to = await BalanceAsync(db, material.Id, toWarehouse, ct);
        from.Quantity -= qty;
        to.Quantity += qty;
        var avg = material.AverageCost;
        var value = Money.Round(qty * avg);
        var o = await AddTxAsync(db, material, new Movement(InventoryTxType.Transfer, date, fromWarehouse, qty, Notes: notes), -qty, avg, -value, ct);
        var i = await AddTxAsync(db, material, new Movement(InventoryTxType.Transfer, date, toWarehouse, qty, Notes: notes, Reference: o.Number), qty, avg, value, ct);
        return (o, i);
    }

    private static void Apply(Material m, StockPosition pos)
    {
        m.QuantityOnHand = pos.Quantity;
        m.StockValue = pos.Value;
        if (pos.Quantity > 0) m.AverageCost = pos.AverageCost;
    }

    private static async Task<InventoryTransaction> AddTxAsync(IAppDb db, Material material, Movement mv, decimal signedQty, decimal unitCost, decimal signedValue, CancellationToken ct)
    {
        var tx = new InventoryTransaction
        {
            Number = await Numbering.NextAsync(db, SequenceKey.InventoryTx, ct),
            Date = mv.Date,
            Type = mv.Type,
            MaterialId = material.Id,
            WarehouseId = mv.WarehouseId,
            Quantity = signedQty,
            UnitCost = Math.Round(unitCost, 6),
            TotalCost = signedValue,
            QuantityAfter = material.QuantityOnHand,
            AverageCostAfter = material.AverageCost,
            JobId = mv.JobId,
            SourceType = mv.SourceType,
            SourceId = mv.SourceId,
            Reference = mv.Reference,
            Notes = mv.Notes
        };
        db.InventoryTransactions.Add(tx);
        return tx;
    }

    /// <summary>Records a remnant movement in the inventory ledger (value only; remnants are tracked individually).</summary>
    public static async Task<InventoryTransaction> RemnantTxAsync(IAppDb db, Remnant remnant, InventoryTxType type, DateTime date, decimal signedQty, decimal signedValue, long? jobId, string? notes, CancellationToken ct = default)
    {
        var tx = new InventoryTransaction
        {
            Number = await Numbering.NextAsync(db, SequenceKey.InventoryTx, ct),
            Date = date,
            Type = type,
            MaterialId = remnant.MaterialId,
            Remnant = remnant,
            RemnantId = remnant.Id == 0 ? null : remnant.Id,
            WarehouseId = remnant.WarehouseId,
            Quantity = signedQty,
            UnitCost = Math.Abs(signedValue),
            TotalCost = signedValue,
            JobId = jobId,
            SourceType = "Remnant",
            Reference = remnant.Code,
            Notes = notes
        };
        db.InventoryTransactions.Add(tx);
        return tx;
    }
}

/// <summary>Adds actual cost entries to a job. Never used for estimated values.</summary>
public static class JobCostEngine
{
    public static JobCostEntry Add(IAppDb db, Job job, CostComponent component, decimal amount, DateTime date, string sourceType, long? sourceId,
        string? description = null, decimal quantity = 0, long? materialId = null, long? machineId = null, long? employeeId = null, decimal hours = 0, JournalEntry? journal = null)
    {
        if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
        var e = new JobCostEntry
        {
            JobId = job.Id, Job = job, Component = component, Amount = Money.Round(amount), Date = date, SourceType = sourceType, SourceId = sourceId,
            Description = description, Quantity = quantity, MaterialId = materialId, MachineId = machineId, EmployeeId = employeeId, Hours = hours,
            JournalEntry = journal
        };
        db.JobCostEntries.Add(e);
        job.ActualCost += e.Amount;
        return e;
    }
}
