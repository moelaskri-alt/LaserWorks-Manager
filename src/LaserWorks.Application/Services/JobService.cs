using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record JobRow(long Id, string Number, DateTime OrderDate, long CustomerId, string Customer, string Title, decimal Quantity, DateTime? DueDate, JobPriority Priority,
    JobStatus Status, decimal EstimatedCost, decimal ActualCost, decimal SellingPrice, string? Machine, string? Operator)
{
    public decimal GrossProfit => SellingPrice - ActualCost;
    public decimal MarginPercent => Money.Percent(SellingPrice - ActualCost, SellingPrice);
    public bool IsOverdue(DateTime today) => DueDate.HasValue && DueDate.Value.Date < today && Status < JobStatus.Delivered;
}

public sealed record JobFilter(JobStatus? Status = null, long? CustomerId = null, bool OpenOnly = false, bool DueSoon = false, bool Overdue = false, DateRange? Range = null, long? MachineId = null);

public sealed class JobService : ServiceBase
{
    public JobService(ServiceContext ctx) : base(ctx) { }

    private static readonly Dictionary<JobStatus, JobStatus[]> Transitions = new()
    {
        [JobStatus.New] = new[] { JobStatus.Planned, JobStatus.InProduction, JobStatus.Cancelled },
        [JobStatus.Planned] = new[] { JobStatus.New, JobStatus.InProduction, JobStatus.Cancelled },
        [JobStatus.InProduction] = new[] { JobStatus.Planned, JobStatus.QualityCheck },
        [JobStatus.QualityCheck] = new[] { JobStatus.InProduction, JobStatus.Completed },
        [JobStatus.Completed] = new[] { JobStatus.InProduction, JobStatus.QualityCheck, JobStatus.Delivered },
        [JobStatus.Delivered] = Array.Empty<JobStatus>(), // Invoiced is set by invoice posting
        [JobStatus.Invoiced] = Array.Empty<JobStatus>(),  // Closed via CloseAsync
        [JobStatus.Closed] = Array.Empty<JobStatus>(),
        [JobStatus.Cancelled] = Array.Empty<JobStatus>(),
    };

    public static bool CanTransition(JobStatus from, JobStatus to) => Transitions.TryGetValue(from, out var t) && t.Contains(to);

    public async Task<PagedResult<JobRow>> ListAsync(PageRequest req, JobFilter? f = null)
    {
        Demand(AppModule.Jobs, Permission.View);
        f ??= new JobFilter();
        await using var db = Factory.Create();
        var today = Now.Date;
        var q = db.Jobs.AsNoTracking().AsQueryable();
        if (f.Status.HasValue) q = q.Where(j => j.Status == f.Status);
        if (f.CustomerId.HasValue) q = q.Where(j => j.CustomerId == f.CustomerId);
        if (f.MachineId.HasValue) q = q.Where(j => j.MachineId == f.MachineId);
        if (f.OpenOnly) q = q.Where(j => j.Status < JobStatus.Delivered);
        if (f.DueSoon) { var limit = today.AddDays(7); q = q.Where(j => j.Status < JobStatus.Delivered && j.DueDate != null && j.DueDate >= today && j.DueDate <= limit); }
        if (f.Overdue) q = q.Where(j => j.Status < JobStatus.Delivered && j.DueDate != null && j.DueDate < today);
        if (f.Range != null) q = q.Where(j => j.OrderDate >= f.Range.From.Date && j.OrderDate < f.Range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(j => j.Number.Contains(s) || j.Title.Contains(s) || j.Customer!.Name.Contains(s));
        return await q.Select(j => new JobRow(j.Id, j.Number, j.OrderDate, j.CustomerId, j.Customer!.Name, j.Title, j.Quantity, j.DueDate, j.Priority, j.Status, j.EstimatedCost,
                j.ActualCost, j.SellingPrice, j.Machine != null ? j.Machine.Name : null, j.Operator != null ? j.Operator.Name : null))
            .SortBy(req.SortBy, req.Descending, r => r.Id, defaultDesc: true).ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync(bool openOnly = true, long? customerId = null) => await ReadAsync(db => db.Jobs.AsNoTracking()
        .Where(j => (!openOnly || (j.Status != JobStatus.Closed && j.Status != JobStatus.Cancelled)) && (customerId == null || j.CustomerId == customerId))
        .OrderByDescending(j => j.Id).Take(1000).Select(j => new Lookup(j.Id, j.Number, j.Title)).ToListAsync());

    public async Task<Job?> GetAsync(long id) => await ReadAsync(db => db.Jobs.AsNoTracking()
        .Include(j => j.Customer).Include(j => j.Machine).Include(j => j.Operator).Include(j => j.Quotation).Include(j => j.Request).Include(j => j.DesignRevision)
        .Include(j => j.Operations).ThenInclude(o => o.Employee).Include(j => j.Operations).ThenInclude(o => o.Machine)
        .AsSplitQuery().FirstOrDefaultAsync(j => j.Id == id));

    /// <summary>Direct job without a quotation (walk-in work). Estimated cost and price are entered manually.</summary>
    public async Task<long> SaveAsync(Job input)
    {
        if (input.CustomerId == 0) throw new DomainException("Err.Required", "Customer");
        Validation.Required(input.Title, "Title");
        if (input.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        Validation.NonNegative(input.EstimatedCost, "EstimatedCost");
        Validation.NonNegative(input.SellingPrice, "SellingPrice");
        if (input.DueDate.HasValue && input.DueDate.Value.Date < input.OrderDate.Date) throw new DomainException("Err.DueBeforeStart");
        Demand(AppModule.Jobs, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            Job j;
            if (input.Id == 0)
            {
                j = new Job { Number = await Numbering.NextAsync(db, SequenceKey.Job), Status = JobStatus.New, CustomerId = input.CustomerId, EstimatedCost = input.EstimatedCost };
                db.Jobs.Add(j);
            }
            else
            {
                j = await db.Jobs.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (j.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", j.Number);
                if (j.CustomerId != input.CustomerId && j.Status >= JobStatus.Invoiced) throw new DomainException("Err.JobInvoiced");
                if (j.QuotationId == null && j.EstimateId == null) j.EstimatedCost = input.EstimatedCost; // estimate snapshot from a quotation is never edited
            }
            if (j.Status < JobStatus.Invoiced) { j.SellingPrice = input.SellingPrice; j.CustomerId = input.CustomerId; j.Quantity = input.Quantity; }
            j.Title = input.Title.Trim(); j.Description = input.Description.Norm(); j.OrderDate = input.OrderDate == default ? Now.Date : input.OrderDate.Date;
            j.DueDate = input.DueDate?.Date; j.Priority = input.Priority; j.MachineId = input.MachineId; j.OperatorId = input.OperatorId; j.Notes = input.Notes.Norm();
            await db.SaveChangesAsync();
            return j.Id;
        });
    }

    public async Task SetStatusAsync(long id, JobStatus status)
    {
        Demand(AppModule.Jobs, Permission.Edit);
        await TxAsync(async db =>
        {
            var j = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (!CanTransition(j.Status, status)) throw new DomainException("Err.InvalidStatusTransition", j.Status, status);
            if (status == JobStatus.Cancelled)
            {
                var hasCost = await db.JobCostEntries.AnyAsync(e => e.JobId == id);
                if (hasCost || j.ActualCost != 0) throw new DomainException("Err.CannotCancelJobWithCost");
            }
            if (status == JobStatus.QualityCheck)
            {
                await ProductionService.ApplyOverheadAsync(db, j, Now, UserName);
            }
            if (status == JobStatus.Delivered) throw new DomainException("Err.UseDeliver");
            if (status == JobStatus.InProduction) j.StartedAt ??= Now;
            if (status == JobStatus.Completed) j.CompletedAt = Now;
            j.Status = status;
            Audit(db, AuditAction.StatusChanged, nameof(Job), id, status.ToString());
        });
    }

    /// <summary>Delivery requires a passed quality check (or a job already Completed).</summary>
    public async Task DeliverAsync(long id, DateTime date, string? note)
    {
        Demand(AppModule.Jobs, Permission.Edit);
        await TxAsync(async db =>
        {
            var j = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (j.Status != JobStatus.Completed) throw new DomainException("Err.JobNotCompleted");
            j.Status = JobStatus.Delivered;
            j.DeliveredAt = date;
            j.DeliveryNote = note.Norm();
            Audit(db, AuditAction.StatusChanged, nameof(Job), id, "Delivered");
        });
    }

    /// <summary>
    /// Final step: re-applies overhead on any late cost, moves the remaining WIP balance to COGS and locks the job.
    /// </summary>
    public async Task CloseAsync(long id)
    {
        Demand(AppModule.Jobs, Permission.Post);
        await TxAsync(async db =>
        {
            var j = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (j.Status != JobStatus.Invoiced) throw new DomainException("Err.JobNotInvoiced");
            await ProductionService.ApplyOverheadAsync(db, j, Now, UserName);
            var remaining = j.ActualCost - j.CostTransferredToCogs;
            if (remaining != 0)
            {
                var draft = new JournalDraft { Date = Now.Date, Description = $"Close job {j.Number}: remaining WIP to COGS", SourceType = "Job", SourceId = j.Id, SourceNumber = j.Number };
                if (remaining > 0) draft.Dr(SystemAccounts.COGS, remaining, tags: new LineTags(JobId: j.Id)).Cr(SystemAccounts.WIP, remaining, tags: new LineTags(JobId: j.Id));
                else draft.Dr(SystemAccounts.WIP, -remaining, tags: new LineTags(JobId: j.Id)).Cr(SystemAccounts.COGS, -remaining, tags: new LineTags(JobId: j.Id));
                await AccountingEngine.PostAsync(db, draft, UserName, Now);
                j.CostTransferredToCogs += remaining;
            }
            j.Status = JobStatus.Closed;
            j.ClosedAt = Now;
            Audit(db, AuditAction.StatusChanged, nameof(Job), id, "Closed");
        });
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Jobs, Permission.Delete);
        await using var db = Factory.Create();
        var j = await db.Jobs.Include(x => x.Operations).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
        if (j.Status != JobStatus.New || await db.JobCostEntries.AnyAsync(e => e.JobId == id) || await db.InventoryTransactions.AnyAsync(t => t.JobId == id))
            throw new DomainException("Err.InUseCannotDelete");
        db.Jobs.Remove(j);
        await Validation.SaveDeleteAsync(db);
    }
}
