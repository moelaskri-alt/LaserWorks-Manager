using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

/// <summary>One job component line with its planned and actual figures.</summary>
public sealed record JobComponentRow(long Id, long JobId, int LineNo, ComponentCategory Category, ComponentSource Source, long? MaterialId, string? ItemCode, string Item,
    string? Unit, decimal PlannedQuantity, decimal UsedQuantity, decimal EstimatedUnitCost, decimal EstimatedCost, decimal ActualCost,
    long? RemnantId, string? RemnantCode, long? SupplierId, string? Supplier, string? Notes, bool FromEstimate, bool HasCost)
{
    public decimal Variance => ActualCost - EstimatedCost;
    public decimal VariancePercent => EstimatedCost == 0 ? 0 : Money.Round(Variance / EstimatedCost * 100m);
    public decimal RemainingQuantity => Math.Max(0, PlannedQuantity - UsedQuantity);
    public bool IsStocked => ComponentRules.IsStocked(Source);
    public string Display => string.IsNullOrEmpty(ItemCode) ? Item : $"{ItemCode} {Item}";
}

/// <summary>A cost charged straight to a job component (direct purchase, external service or manual cost) — never passes through stock.</summary>
public sealed record DirectCostInput(long JobComponentId, DateTime Date, decimal Quantity, decimal Amount, decimal TaxAmount, PaymentMethod Method,
    long? SupplierId, string? Reference, string? Notes);

/// <summary>
/// Job component lines: the job-specific list of materials, purchased components, consumables, packaging, remnants and services.
/// Stocked lines are costed only by stock issues / remnant use; direct lines only by their direct cost documents,
/// so the same cost can never be charged twice.
/// </summary>
public sealed class JobComponentService : ServiceBase
{
    private readonly InventoryService _inventory;

    public JobComponentService(ServiceContext ctx, InventoryService inventory) : base(ctx) => _inventory = inventory;

    public async Task<List<JobComponentRow>> ListAsync(long jobId)
    {
        Demand(AppModule.Jobs, Permission.View);
        return await ReadAsync(db => ListAsync(db, jobId));
    }

    internal static async Task<List<JobComponentRow>> ListAsync(IAppDb db, long jobId)
    {
        var lines = await db.JobComponents.AsNoTracking().Where(c => c.JobId == jobId).OrderBy(c => c.LineNo).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.JobId, c.LineNo, c.Category, c.Source, c.MaterialId, Code = c.Material != null ? c.Material.Code : null, Name = c.Material != null ? c.Material.Name : null,
                c.Description, Unit = c.Unit ?? (c.Material != null ? c.Material.Unit!.Code : null), c.PlannedQuantity, c.EstimatedUnitCost, c.EstimatedCost, c.RemnantId, RemnantCode = c.Remnant != null ? c.Remnant.Code : null,
                c.SupplierId, Supplier = c.Supplier != null ? c.Supplier.Name : null, c.Notes, c.EstimateLineId
            }).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var costs = (await db.JobCostEntries.AsNoTracking().Where(e => e.JobId == jobId && e.JobComponentId != null)
                .GroupBy(e => e.JobComponentId!.Value)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount), Direct = g.Where(x => x.SourceType == "DirectCost").Sum(x => x.Quantity), Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Id);
        var stock = (await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.JobId == jobId && t.JobComponentId != null &&
                            (t.Type == InventoryTxType.MaterialIssue || t.Type == InventoryTxType.MaterialReturn || t.Type == InventoryTxType.RemnantConsumption))
                .GroupBy(t => t.JobComponentId!.Value).Select(g => new { Id = g.Key, Qty = -g.Sum(x => x.Quantity) }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.Qty);
        return lines.Select(l =>
        {
            costs.TryGetValue(l.Id, out var c);
            var used = (stock.TryGetValue(l.Id, out var q) ? q : 0) + (c?.Direct ?? 0);
            return new JobComponentRow(l.Id, l.JobId, l.LineNo, l.Category, l.Source, l.MaterialId, l.Code, l.Name ?? l.Description ?? "", l.Unit, l.PlannedQuantity, used,
                l.EstimatedUnitCost, l.EstimatedCost, c?.Amount ?? 0, l.RemnantId, l.RemnantCode, l.SupplierId, l.Supplier, l.Notes, l.EstimateLineId != null, (c?.Count ?? 0) > 0);
        }).ToList();
    }

    public async Task<JobComponent?> GetAsync(long id) => await ReadAsync(db => db.JobComponents.AsNoTracking().Include(c => c.Material).FirstOrDefaultAsync(c => c.Id == id));

    /// <summary>Adds or edits a line. Once a line carries cost, its item, source and category are fixed.</summary>
    public async Task<long> SaveAsync(JobComponent input)
    {
        Demand(AppModule.Jobs, Permission.Edit);
        if (input.PlannedQuantity < 0 || input.EstimatedUnitCost < 0) throw new DomainException("Err.NegativeValue");
        return await TxAsync(async db =>
        {
            var job = await OpenJobAsync(db, input.JobId);
            Material? m = input.MaterialId is { } mid ? await db.Materials.FirstOrDefaultAsync(x => x.Id == mid) ?? throw new DomainException("Err.NotFound") : null;
            if (m != null && input.Source != ComponentSource.ManualCost) input.Category = ComponentRules.CategoryOf(m.Kind);
            ComponentRules.Validate(input.Source, m?.Kind, input.Category, input.Description);
            if (input.RemnantId is { } rid)
            {
                var r = await db.Remnants.FirstOrDefaultAsync(x => x.Id == rid) ?? throw new DomainException("Err.NotFound");
                if (r.MaterialId != m?.Id) throw new DomainException("Err.ComponentItemMismatch");
            }
            JobComponent c;
            if (input.Id == 0)
            {
                c = new JobComponent { JobId = job.Id, LineNo = (await db.JobComponents.Where(x => x.JobId == job.Id).MaxAsync(x => (int?)x.LineNo) ?? 0) + 1 };
                db.JobComponents.Add(c);
            }
            else
            {
                c = await db.JobComponents.FirstOrDefaultAsync(x => x.Id == input.Id && x.JobId == job.Id) ?? throw new DomainException("Err.NotFound");
                var hasCost = await db.JobCostEntries.AnyAsync(e => e.JobComponentId == c.Id) || await db.InventoryTransactions.AnyAsync(t => t.JobComponentId == c.Id);
                if (hasCost && (c.MaterialId != input.MaterialId || c.Source != input.Source || c.Category != input.Category))
                    throw new DomainException("Err.ComponentHasCost");
            }
            c.Category = input.Category; c.Source = input.Source; c.MaterialId = m?.Id;
            c.RemnantId = input.Source == ComponentSource.Remnant ? input.RemnantId : null;
            c.Description = input.Description.Norm(); c.Unit = input.Unit.Norm() ?? (m != null ? await db.Units.Where(u => u.Id == m.UnitId).Select(u => u.Code).FirstOrDefaultAsync() : null);
            c.PlannedQuantity = input.PlannedQuantity; c.SupplierId = input.SupplierId; c.Notes = input.Notes.Norm();
            // estimated figures of a line taken from the estimate are a snapshot and never change
            if (c.EstimateLineId == null)
            {
                c.EstimatedUnitCost = input.EstimatedUnitCost;
                c.EstimatedCost = Money.Round(input.PlannedQuantity * input.EstimatedUnitCost);
            }
            await db.SaveChangesAsync();
            return c.Id;
        });
    }

    /// <summary>Copies a line (without any cost) to the end of the list.</summary>
    public async Task<long> DuplicateAsync(long id)
    {
        var c = await GetAsync(id) ?? throw new DomainException("Err.NotFound");
        return await SaveAsync(new JobComponent
        {
            JobId = c.JobId, Category = c.Category, Source = c.Source, MaterialId = c.MaterialId, Description = c.Description, Unit = c.Unit,
            PlannedQuantity = c.PlannedQuantity, EstimatedUnitCost = c.EstimatedUnitCost, SupplierId = c.SupplierId, Notes = c.Notes
        });
    }

    /// <summary>Removes a line that has no cost and no stock movement.</summary>
    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Jobs, Permission.Edit);
        await TxAsync(async db =>
        {
            var c = await db.JobComponents.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            await OpenJobAsync(db, c.JobId);
            if (await db.JobCostEntries.AnyAsync(e => e.JobComponentId == id) || await db.InventoryTransactions.AnyAsync(t => t.JobComponentId == id))
                throw new DomainException("Err.ComponentHasCost");
            db.JobComponents.Remove(c);
        });
    }

    public async Task<long> IssueAsync(long id, long warehouseId, decimal quantity, DateTime date, string? notes = null)
    {
        var c = await GetAsync(id) ?? throw new DomainException("Err.NotFound");
        if (c.Source != ComponentSource.Inventory || c.MaterialId == null) throw new DomainException("Err.ComponentNotStocked");
        return await _inventory.IssueToJobAsync(c.JobId, c.MaterialId.Value, warehouseId, quantity, date, notes, c.Id);
    }

    public async Task<long> ReturnAsync(long id, long warehouseId, decimal quantity, DateTime date, string? notes = null)
    {
        var c = await GetAsync(id) ?? throw new DomainException("Err.NotFound");
        if (c.Source != ComponentSource.Inventory || c.MaterialId == null) throw new DomainException("Err.ComponentNotStocked");
        return await _inventory.ReturnFromJobAsync(c.JobId, c.MaterialId.Value, warehouseId, quantity, date, notes, c.Id);
    }

    public async Task UseRemnantAsync(long id, long remnantId, DateTime date)
    {
        var c = await GetAsync(id) ?? throw new DomainException("Err.NotFound");
        if (!ComponentRules.IsStocked(c.Source) || c.MaterialId == null) throw new DomainException("Err.ComponentNotStocked");
        await _inventory.ConsumeRemnantAsync(remnantId, c.JobId, date, c.Id);
    }

    /// <summary>
    /// Charges a direct purchase, external service or manual cost to its line: Dr WIP (+ Dr Input tax) / Cr Accounts payable (supplier) or Cash / Bank.
    /// </summary>
    public async Task<long> RecordDirectCostAsync(DirectCostInput input)
    {
        Demand(AppModule.Jobs, Permission.Post);
        if (input.Amount <= 0) throw new DomainException("Err.AmountPositive");
        if (input.TaxAmount < 0 || input.Quantity < 0) throw new DomainException("Err.NegativeValue");
        return await TxAsync(async db =>
        {
            var c = await db.JobComponents.Include(x => x.Material).FirstOrDefaultAsync(x => x.Id == input.JobComponentId) ?? throw new DomainException("Err.NotFound");
            if (ComponentRules.IsStocked(c.Source)) throw new DomainException("Err.ComponentIsStocked");
            var job = await OpenJobAsync(db, c.JobId);
            var supplierId = input.SupplierId ?? c.SupplierId;
            if (input.Method == PaymentMethod.OnCredit && supplierId == null) throw new DomainException("Err.SupplierRequiredForCredit");
            var date = input.Date.Date;
            var what = c.Material?.Name ?? c.Description ?? "";
            var total = Money.Round(input.Amount) + Money.Round(input.TaxAmount);
            var draft = new JournalDraft
            {
                Date = date, Description = $"{job.Number}: {what}" + (string.IsNullOrWhiteSpace(input.Reference) ? "" : $" ({input.Reference.Trim()})"),
                SourceType = "JobComponent", SourceId = c.Id, SourceNumber = job.Number
            }.Dr(SystemAccounts.WIP, Money.Round(input.Amount), tags: new LineTags(JobId: job.Id));
            if (input.TaxAmount > 0) draft.Dr(SystemAccounts.InputTax, Money.Round(input.TaxAmount));
            if (input.Method == PaymentMethod.OnCredit) draft.Cr(SystemAccounts.AP, total, tags: new LineTags(SupplierId: supplierId));
            else draft.Cr(AccountingEngine.CashAccountKey(input.Method), total);
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            var entry = JobCostEngine.Add(db, job, ComponentRules.CostComponentOf(c.Category), input.Amount, date, "DirectCost", c.Id,
                $"{what}" + (string.IsNullOrWhiteSpace(input.Reference) ? "" : $" — {input.Reference.Trim()}"), input.Quantity, c.MaterialId, journal: je, jobComponentId: c.Id);
            if (c.SupplierId == null && supplierId != null) c.SupplierId = supplierId;
            await db.SaveChangesAsync();
            Audit(db, AuditAction.Posted, nameof(JobComponent), c.Id, $"direct cost {input.Amount} on {job.Number}");
            return entry.Id;
        });
    }

    /// <summary>Reverses a direct cost (wrong amount, wrong line): reversing journal entry and a negative cost entry. The original stays for the audit trail.</summary>
    public async Task ReverseDirectCostAsync(long costEntryId, DateTime date, string reason)
    {
        Demand(AppModule.Jobs, Permission.Post);
        Validation.Required(reason, "Reason");
        await TxAsync(async db =>
        {
            var e = await db.JobCostEntries.FirstOrDefaultAsync(x => x.Id == costEntryId) ?? throw new DomainException("Err.NotFound");
            if (e.SourceType != "DirectCost" || e.JournalEntryId == null) throw new DomainException("Err.ReverseThroughDocument");
            if (await db.JobCostEntries.AnyAsync(x => x.SourceType == "DirectCostReversal" && x.SourceId == e.Id)) throw new DomainException("Err.OnlyPostedCanBeReversed");
            var job = await OpenJobAsync(db, e.JobId);
            var rev = await AccountingEngine.ReverseAsync(db, e.JournalEntryId.Value, date.Date, reason.Trim(), UserName, Now);
            JobCostEngine.Add(db, job, e.Component, -e.Amount, date.Date, "DirectCostReversal", e.Id, $"Reversal: {reason.Trim()}", -e.Quantity, e.MaterialId,
                journal: rev, jobComponentId: e.JobComponentId);
        });
    }

    /// <summary>Planned job components from the estimate's component lines (one job line per estimate line).</summary>
    internal static void CreateFromEstimate(Job job, CostEstimate estimate)
    {
        var no = 1;
        foreach (var l in estimate.MaterialLines.OrderBy(l => l.LineNo).ThenBy(l => l.Id))
        {
            job.Components.Add(new JobComponent
            {
                LineNo = no++, Category = l.Category, Source = l.Source, MaterialId = l.MaterialId, RemnantId = l.RemnantId, Description = l.Description, Unit = l.Unit,
                PlannedQuantity = l.TotalQuantity, EstimatedUnitCost = l.UnitCost, EstimatedCost = l.Cost, EstimateLineId = l.Id
            });
        }
    }

    private static async Task<Job> OpenJobAsync(IAppDb db, long id)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id) ?? throw new DomainException("Err.NotFound");
        if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
        return job;
    }
}
