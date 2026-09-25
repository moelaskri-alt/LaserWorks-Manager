using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record ExpenseRow(long Id, string Number, DateTime Date, string Category, string Description, decimal Amount, decimal TaxAmount, PaymentMethod PaymentMethod,
    string? Supplier, string? JobNumber, string? Machine, string? CostCenter, DocumentStatus Status)
{
    public decimal Total => Amount + TaxAmount;
}

public sealed class ExpenseService : ServiceBase
{
    public ExpenseService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<ExpenseRow>> ListAsync(PageRequest req, DateRange? range = null, long? categoryId = null, long? jobId = null, long? machineId = null, long? costCenterId = null, DocumentStatus? status = null)
    {
        Demand(AppModule.Expenses, Permission.View);
        await using var db = Factory.Create();
        var q = db.Expenses.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(e => e.Date >= range.From.Date && e.Date < range.ToExclusive);
        if (categoryId.HasValue) q = q.Where(e => e.CategoryId == categoryId);
        if (jobId.HasValue) q = q.Where(e => e.JobId == jobId);
        if (machineId.HasValue) q = q.Where(e => e.MachineId == machineId);
        if (costCenterId.HasValue) q = q.Where(e => e.CostCenterId == costCenterId);
        if (status.HasValue) q = q.Where(e => e.Status == status);
        if (req.Search.Norm() is { } s) q = q.Where(e => e.Number.Contains(s) || e.Description.Contains(s) || (e.Reference != null && e.Reference.Contains(s)));
        return await q.Select(e => new ExpenseRow(e.Id, e.Number, e.Date, e.Category!.Name, e.Description, e.Amount, e.TaxAmount, e.PaymentMethod, e.Supplier != null ? e.Supplier.Name : null,
                e.Job != null ? e.Job.Number : null, e.Machine != null ? e.Machine.Name : null, e.CostCenter != null ? e.CostCenter.Name : null, e.Status))
            .SortBy(req.SortBy, req.Descending, r => r.Id, defaultDesc: true).ToPagedAsync(req);
    }

    public async Task<Expense?> GetAsync(long id) => await ReadAsync(db => db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id));

    public async Task<long> SaveAsync(Expense input)
    {
        if (input.CategoryId == 0) throw new DomainException("Err.Required", "Category");
        Validation.Required(input.Description, "Description");
        if (input.Amount <= 0) throw new DomainException("Err.AmountPositive");
        Validation.NonNegative(input.TaxAmount, "Tax");
        if (input.PaymentMethod == PaymentMethod.OnCredit && input.SupplierId == null) throw new DomainException("Err.SupplierRequiredForCredit");
        Demand(AppModule.Expenses, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Expense e;
            if (input.Id == 0)
            {
                e = new Expense { Number = await Numbering.NextAsync(db, SequenceKey.Expense), Status = DocumentStatus.Draft };
                db.Expenses.Add(e);
            }
            else
            {
                e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (e.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
            }
            e.Date = input.Date.Date; e.CategoryId = input.CategoryId; e.Description = input.Description.Trim(); e.Amount = Money.Round(input.Amount); e.TaxAmount = Money.Round(input.TaxAmount);
            e.PaymentMethod = input.PaymentMethod; e.SupplierId = input.SupplierId; e.JobId = input.JobId; e.MachineId = input.MachineId; e.CostCenterId = input.CostCenterId; e.Reference = input.Reference.Norm();
            await db.SaveChangesAsync();
            return e.Id;
        });
    }

    /// <summary>
    /// Posts an expense. Dr expense account (or WIP when charged to a job) + Dr Input Tax / Cr Cash, Bank or Accounts Payable.
    /// A job expense becomes an actual cost of that job using the category's job cost component.
    /// </summary>
    public async Task PostAsync(long id)
    {
        Demand(AppModule.Expenses, Permission.Post);
        await TxAsync(async db =>
        {
            var e = await db.Expenses.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (e.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
            Job? job = e.JobId is { } jid ? await db.Jobs.FirstAsync(j => j.Id == jid) : null;
            if (job != null && job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
            var tags = new LineTags(JobId: e.JobId, SupplierId: e.SupplierId, CostCenterId: e.CostCenterId, MachineId: e.MachineId);
            var draft = new JournalDraft { Date = e.Date, Description = $"Expense {e.Number}: {e.Description}", SourceType = "Expense", SourceId = e.Id, SourceNumber = e.Number };
            if (job != null) draft.Dr(SystemAccounts.WIP, e.Amount, e.Category!.Name, tags);
            else draft.Dr(e.Category!.AccountId, e.Amount, e.Description, tags);
            draft.Dr(SystemAccounts.InputTax, e.TaxAmount);
            if (e.PaymentMethod == PaymentMethod.OnCredit) draft.Cr(SystemAccounts.AP, e.Total, tags: new LineTags(SupplierId: e.SupplierId));
            else draft.Cr(AccountingEngine.CashAccountKey(e.PaymentMethod), e.Total);
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            e.Status = DocumentStatus.Posted;
            if (job != null) JobCostEngine.Add(db, job, e.Category.JobComponent, e.Amount, e.Date, "Expense", e.Id, $"{e.Category.Name}: {e.Description}", machineId: e.MachineId, journal: je);
            await db.SaveChangesAsync();
            e.JournalEntryId = je?.Id;
            Audit(db, AuditAction.Posted, nameof(Expense), e.Id, e.Number);
        });
    }

    /// <summary>Cancels a posted expense by reversal (the original entry stays in the ledger).</summary>
    public async Task CancelAsync(long id, string reason)
    {
        Demand(AppModule.Expenses, Permission.Post);
        Validation.Required(reason, "Reason");
        await TxAsync(async db =>
        {
            var e = await db.Expenses.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (e.Status != DocumentStatus.Posted || e.JournalEntryId == null) throw new DomainException("Err.DocumentNotPosted");
            var rev = await AccountingEngine.ReverseAsync(db, e.JournalEntryId.Value, Now.Date, reason, UserName, Now);
            if (e.JobId is { } jid)
            {
                var job = await db.Jobs.FirstAsync(j => j.Id == jid);
                JobCostEngine.Add(db, job, e.Category!.JobComponent, -e.Amount, Now.Date, "Expense", e.Id, $"Cancelled: {reason}", machineId: e.MachineId, journal: rev);
            }
            e.Status = DocumentStatus.Cancelled;
            Audit(db, AuditAction.Reversed, nameof(Expense), e.Id, reason);
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Expenses, Permission.Delete);
        await using var db = Factory.Create();
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (e.Status != DocumentStatus.Draft) throw new DomainException("Err.DocumentPosted");
        db.Expenses.Remove(e);
        await db.SaveChangesAsync();
    }
}
