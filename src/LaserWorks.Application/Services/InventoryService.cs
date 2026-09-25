using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record InventoryTxRow(long Id, string Number, DateTime Date, InventoryTxType Type, string? MaterialCode, string? MaterialName, string? RemnantCode,
    string Warehouse, decimal Quantity, decimal UnitCost, decimal TotalCost, decimal QuantityAfter, string? JobNumber, string? Reference, string? Notes, string? JournalNumber, string? CreatedBy);

public sealed record RemnantRow(long Id, string Code, string MaterialCode, string MaterialName, decimal Thickness, decimal Length, decimal Width, string Warehouse,
    string? SourceJob, decimal Cost, DateTime Date, RemnantStatus Status, string? ConsumedJob, DateTime? ConsumedDate)
{
    public decimal Area => Length * Width;
    public string Display => $"{Code} — {MaterialName} {Length:0.#}×{Width:0.#} cm ({Cost:N2})";
    public override string ToString() => Display;
}

public sealed record JobMaterialLine(long MaterialId, string MaterialCode, string MaterialName, decimal NetQuantity, decimal NetValue)
{
    public decimal AverageIssueCost => NetQuantity == 0 ? 0 : Math.Round(NetValue / NetQuantity, 6);
}

public sealed record RemnantInput(long MaterialId, long WarehouseId, decimal Length, decimal Width, decimal? Thickness, long? SourceJobId, decimal? Cost, DateTime Date, string? Notes);

public sealed class InventoryService : ServiceBase
{
    public InventoryService(ServiceContext ctx) : base(ctx) { }

    // --------------------------------------------------------------- queries
    public async Task<PagedResult<InventoryTxRow>> ListTransactionsAsync(PageRequest req, DateRange? range = null, long? materialId = null, InventoryTxType? type = null, long? jobId = null, long? warehouseId = null)
    {
        Demand(AppModule.Inventory, Permission.View);
        await using var db = Factory.Create();
        var q = db.InventoryTransactions.AsNoTracking().AsQueryable();
        if (range != null) q = q.Where(t => t.Date >= range.From.Date && t.Date < range.ToExclusive);
        if (materialId.HasValue) q = q.Where(t => t.MaterialId == materialId);
        if (type.HasValue) q = q.Where(t => t.Type == type);
        if (jobId.HasValue) q = q.Where(t => t.JobId == jobId);
        if (warehouseId.HasValue) q = q.Where(t => t.WarehouseId == warehouseId);
        if (req.Search.Norm() is { } s) q = q.Where(t => t.Number.Contains(s) || (t.Material != null && (t.Material.Name.Contains(s) || t.Material.Code.Contains(s))) || (t.Reference != null && t.Reference.Contains(s)) || (t.Job != null && t.Job.Number.Contains(s)));
        return await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new InventoryTxRow(t.Id, t.Number, t.Date, t.Type, t.Material != null ? t.Material.Code : null, t.Material != null ? t.Material.Name : null,
                t.Remnant != null ? t.Remnant.Code : null, t.Warehouse!.Name, t.Quantity, t.UnitCost, t.TotalCost, t.QuantityAfter, t.Job != null ? t.Job.Number : null,
                t.Reference, t.Notes, t.JournalEntry != null ? t.JournalEntry.Number : null, t.CreatedBy))
            .ToPagedAsync(req);
    }

    public async Task<PagedResult<RemnantRow>> ListRemnantsAsync(PageRequest req, RemnantStatus? status = RemnantStatus.Available, long? materialId = null)
    {
        Demand(AppModule.Inventory, Permission.View);
        await using var db = Factory.Create();
        var q = db.Remnants.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status);
        if (materialId.HasValue) q = q.Where(r => r.MaterialId == materialId);
        if (req.Search.Norm() is { } s) q = q.Where(r => r.Code.Contains(s) || r.Material!.Name.Contains(s) || r.Material.Code.Contains(s));
        return await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Select(r => new RemnantRow(r.Id, r.Code, r.Material!.Code, r.Material.Name, r.Thickness, r.Length, r.Width, r.Warehouse!.Name,
                r.SourceJob != null ? r.SourceJob.Number : null, r.Cost, r.Date, r.Status, r.ConsumedJob != null ? r.ConsumedJob.Number : null, r.ConsumedDate))
            .ToPagedAsync(req);
    }

    /// <summary>Available remnants that can cover a piece of the given size (either orientation).</summary>
    public async Task<List<RemnantRow>> FindRemnantsAsync(long materialId, decimal minLength, decimal minWidth)
    {
        var all = (await ListRemnantsAsync(new PageRequest(PageSize: 1000), RemnantStatus.Available, materialId)).Items;
        return all.Where(r => (r.Length >= minLength && r.Width >= minWidth) || (r.Length >= minWidth && r.Width >= minLength)).OrderBy(r => r.Area).ToList();
    }

    public async Task<List<JobMaterialLine>> JobMaterialsAsync(long jobId) => await ReadAsync(db => JobMaterialsAsync(db, jobId));

    internal static async Task<List<JobMaterialLine>> JobMaterialsAsync(IAppDb db, long jobId)
    {
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.JobId == jobId && t.RemnantId == null && (t.Type == InventoryTxType.MaterialIssue || t.Type == InventoryTxType.MaterialReturn))
            .GroupBy(t => new { t.MaterialId, t.Material!.Code, t.Material.Name })
            .Select(g => new { g.Key.MaterialId, g.Key.Code, g.Key.Name, Qty = -g.Sum(x => x.Quantity), Value = -g.Sum(x => x.TotalCost) })
            .ToListAsync();
        return rows.Select(r => new JobMaterialLine(r.MaterialId!.Value, r.Code, r.Name, r.Qty, r.Value)).ToList();
    }

    // --------------------------------------------------------------- stock movements
    private static async Task<Material> MaterialAsync(IAppDb db, long id) =>
        await db.Materials.FirstOrDefaultAsync(m => m.Id == id) ?? throw new DomainException("Err.NotFound");

    private static async Task<Job> OpenJobAsync(IAppDb db, long id)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id) ?? throw new DomainException("Err.NotFound");
        if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
        return job;
    }

    private async Task PostAsync(IAppDb db, InventoryTransaction tx, JournalDraft draft)
    {
        var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
        tx.JournalEntry = je;
    }

    public async Task<long> OpeningBalanceAsync(long materialId, long warehouseId, decimal qty, decimal unitCost, DateTime date)
    {
        Demand(AppModule.Inventory, Permission.Post);
        return await TxAsync(async db =>
        {
            var m = await MaterialAsync(db, materialId);
            var tx = await InventoryEngine.ReceiveAsync(db, m, new InventoryEngine.Movement(InventoryTxType.OpeningBalance, date, warehouseId, qty, unitCost, Notes: "Opening balance"));
            await PostAsync(db, tx, new JournalDraft { Date = date, Description = $"Opening stock {m.Code}", SourceType = "InventoryTx", SourceNumber = tx.Number }
                .Dr(AccountingEngine.InventoryAccountKey(m.Kind), tx.TotalCost).Cr(SystemAccounts.OpeningEquity, tx.TotalCost));
            await db.SaveChangesAsync();
            return tx.Id;
        });
    }

    /// <summary>Stock count adjustment. Positive quantity = found stock (valued at given or average cost); negative = shortage at average cost.</summary>
    public async Task<long> AdjustAsync(long materialId, long warehouseId, decimal quantityDelta, decimal? unitCost, DateTime date, string reason)
    {
        Demand(AppModule.Inventory, Permission.Post);
        Validation.Required(reason, "Reason");
        if (quantityDelta == 0) throw new DomainException("Err.QuantityNonZero");
        return await TxAsync(async db =>
        {
            var m = await MaterialAsync(db, materialId);
            var invKey = AccountingEngine.InventoryAccountKey(m.Kind);
            InventoryTransaction tx;
            var draft = new JournalDraft { Date = date, Description = $"Stock adjustment {m.Code}: {reason}", SourceType = "InventoryTx" };
            if (quantityDelta > 0)
            {
                var cost = unitCost ?? (m.AverageCost > 0 ? m.AverageCost : m.PurchaseCost);
                tx = await InventoryEngine.ReceiveAsync(db, m, new InventoryEngine.Movement(InventoryTxType.Adjustment, date, warehouseId, quantityDelta, cost, Notes: reason));
                draft = new JournalDraft { Date = date, Description = draft.Description, SourceType = "InventoryTx", SourceNumber = tx.Number };
                draft.Dr(invKey, tx.TotalCost).Cr(SystemAccounts.InventoryGain, tx.TotalCost);
            }
            else
            {
                tx = await InventoryEngine.IssueAsync(db, m, new InventoryEngine.Movement(InventoryTxType.Adjustment, date, warehouseId, -quantityDelta, Notes: reason));
                draft = new JournalDraft { Date = date, Description = draft.Description, SourceType = "InventoryTx", SourceNumber = tx.Number };
                draft.Dr(SystemAccounts.InventoryLoss, -tx.TotalCost).Cr(invKey, -tx.TotalCost);
            }
            await PostAsync(db, tx, draft);
            await db.SaveChangesAsync();
            return tx.Id;
        });
    }

    /// <summary>Issue material to a job at moving average cost: Dr WIP / Cr Inventory, and an actual Material cost entry on the job.</summary>
    public async Task<long> IssueToJobAsync(long jobId, long materialId, long warehouseId, decimal qty, DateTime date, string? notes = null)
    {
        Demand(AppModule.Inventory, Permission.Post);
        return await TxAsync(async db =>
        {
            var job = await OpenJobAsync(db, jobId);
            var m = await MaterialAsync(db, materialId);
            var tx = await InventoryEngine.IssueAsync(db, m, new InventoryEngine.Movement(InventoryTxType.MaterialIssue, date, warehouseId, qty, JobId: job.Id, SourceType: "Job", SourceId: job.Id, Reference: job.Number, Notes: notes));
            var value = -tx.TotalCost;
            await PostAsync(db, tx, new JournalDraft { Date = date, Description = $"Material issue {m.Code} to {job.Number}", SourceType = "InventoryTx", SourceNumber = tx.Number }
                .Dr(SystemAccounts.WIP, value, tags: new LineTags(JobId: job.Id)).Cr(AccountingEngine.InventoryAccountKey(m.Kind), value));
            JobCostEngine.Add(db, job, CostComponent.Material, value, date, "InventoryTx", null, $"{m.Code} {m.Name} × {qty:0.###}", qty, m.Id, journal: tx.JournalEntry);
            if (job.Status is JobStatus.New or JobStatus.Planned) { job.Status = JobStatus.InProduction; job.StartedAt ??= Now; }
            await db.SaveChangesAsync();
            return tx.Id;
        });
    }

    /// <summary>Return unused material from a job, valued at the job's average issue cost for that material.</summary>
    public async Task<long> ReturnFromJobAsync(long jobId, long materialId, long warehouseId, decimal qty, DateTime date, string? notes = null)
    {
        Demand(AppModule.Inventory, Permission.Post);
        return await TxAsync(async db =>
        {
            var job = await OpenJobAsync(db, jobId);
            var m = await MaterialAsync(db, materialId);
            var issued = (await JobMaterialsAsync(db, jobId)).FirstOrDefault(x => x.MaterialId == materialId);
            if (issued == null || issued.NetQuantity < qty) throw new DomainException("Err.ReturnExceedsIssued", issued?.NetQuantity ?? 0, qty);
            var unitCost = issued.AverageIssueCost;
            if (qty == issued.NetQuantity) unitCost = issued.NetValue / qty;
            var tx = await InventoryEngine.ReceiveAsync(db, m, new InventoryEngine.Movement(InventoryTxType.MaterialReturn, date, warehouseId, qty, unitCost, job.Id, "Job", job.Id, job.Number, notes));
            await PostAsync(db, tx, new JournalDraft { Date = date, Description = $"Material return {m.Code} from {job.Number}", SourceType = "InventoryTx", SourceNumber = tx.Number }
                .Dr(AccountingEngine.InventoryAccountKey(m.Kind), tx.TotalCost).Cr(SystemAccounts.WIP, tx.TotalCost, tags: new LineTags(JobId: job.Id)));
            JobCostEngine.Add(db, job, CostComponent.Material, -tx.TotalCost, date, "InventoryTx", null, $"Return {m.Code} × {qty:0.###}", -qty, m.Id, journal: tx.JournalEntry);
            await db.SaveChangesAsync();
            return tx.Id;
        });
    }

    public async Task TransferAsync(long materialId, long fromWarehouse, long toWarehouse, decimal qty, DateTime date, string? notes)
    {
        Demand(AppModule.Inventory, Permission.Post);
        await TxAsync(async db =>
        {
            var m = await MaterialAsync(db, materialId);
            await InventoryEngine.TransferAsync(db, m, fromWarehouse, toWarehouse, qty, date, notes);
        });
    }

    /// <summary>Write off damaged stock in the warehouse (not related to a job): Dr Inventory Loss / Cr Inventory.</summary>
    public async Task<long> ScrapStockAsync(long materialId, long warehouseId, decimal qty, DateTime date, string reason)
    {
        Demand(AppModule.Inventory, Permission.Post);
        Validation.Required(reason, "Reason");
        return await TxAsync(async db =>
        {
            var m = await MaterialAsync(db, materialId);
            var tx = await InventoryEngine.IssueAsync(db, m, new InventoryEngine.Movement(InventoryTxType.Scrap, date, warehouseId, qty, Notes: reason));
            await PostAsync(db, tx, new JournalDraft { Date = date, Description = $"Stock scrap {m.Code}: {reason}", SourceType = "InventoryTx", SourceNumber = tx.Number }
                .Dr(SystemAccounts.InventoryLoss, -tx.TotalCost).Cr(AccountingEngine.InventoryAccountKey(m.Kind), -tx.TotalCost));
            await db.SaveChangesAsync();
            return tx.Id;
        });
    }

    // --------------------------------------------------------------- remnants
    /// <summary>
    /// Records a reusable offcut. From a job: value moves out of the job's material cost (Dr Remnant inventory / Cr WIP).
    /// Default cost = sheet average cost × remnant area / sheet area.
    /// </summary>
    public async Task<long> CreateRemnantAsync(RemnantInput input)
    {
        Demand(AppModule.Inventory, Permission.Post);
        if (input.Length <= 0 || input.Width <= 0) throw new DomainException("Err.DimensionsRequired");
        return await TxAsync(async db =>
        {
            var m = await MaterialAsync(db, input.MaterialId);
            decimal cost;
            if (input.Cost is { } c)
            {
                if (c < 0) throw new DomainException("Err.NegativeValue");
                cost = Money.Round(c);
            }
            else
            {
                if (m.SheetArea <= 0) throw new DomainException("Err.RemnantCostRequired");
                var unitCost = m.AverageCost > 0 ? m.AverageCost : m.PurchaseCost;
                cost = MaterialUtilizationCalculator.RemnantCost(m.SheetArea, unitCost, input.Length, input.Width);
            }
            Job? job = null;
            if (input.SourceJobId is { } jid)
            {
                job = await OpenJobAsync(db, jid);
                var materialCost = await db.JobCostEntries.Where(e => e.JobId == jid && e.Component == CostComponent.Material).SumAsync(e => e.Amount);
                if (cost > materialCost) throw new DomainException("Err.RemnantExceedsJobMaterial", materialCost, cost);
            }
            var r = new Remnant
            {
                Code = await Numbering.NextAsync(db, SequenceKey.Remnant), MaterialId = m.Id, Thickness = input.Thickness ?? m.Thickness, Length = input.Length, Width = input.Width,
                WarehouseId = input.WarehouseId, SourceJobId = job?.Id, Cost = cost, Date = input.Date, Notes = input.Notes.Norm()
            };
            db.Remnants.Add(r);
            await db.SaveChangesAsync();
            var tx = await InventoryEngine.RemnantTxAsync(db, r, InventoryTxType.RemnantCreation, input.Date, 1, cost, job?.Id, input.Notes);
            var draft = new JournalDraft { Date = input.Date, Description = $"Remnant {r.Code} created" + (job != null ? $" from {job.Number}" : ""), SourceType = "Remnant", SourceId = r.Id, SourceNumber = r.Code }
                .Dr(SystemAccounts.InventoryRemnants, cost);
            if (job != null) draft.Cr(SystemAccounts.WIP, cost, tags: new LineTags(JobId: job.Id));
            else draft.Cr(SystemAccounts.InventoryGain, cost);
            await PostAsync(db, tx, draft);
            if (job != null)
                JobCostEngine.Add(db, job, CostComponent.Material, -cost, input.Date, "Remnant", r.Id, $"Remnant {r.Code} {r.Length:0.#}×{r.Width:0.#}", 0, m.Id, journal: tx.JournalEntry);
            await db.SaveChangesAsync();
            return r.Id;
        });
    }

    /// <summary>Use a remnant in a job at its recorded cost: Dr WIP / Cr Remnant inventory.</summary>
    public async Task ConsumeRemnantAsync(long remnantId, long jobId, DateTime date)
    {
        Demand(AppModule.Inventory, Permission.Post);
        await TxAsync(async db =>
        {
            var r = await db.Remnants.Include(x => x.Material).FirstOrDefaultAsync(x => x.Id == remnantId) ?? throw new DomainException("Err.NotFound");
            if (r.Status != RemnantStatus.Available) throw new DomainException("Err.RemnantNotAvailable", r.Code);
            var job = await OpenJobAsync(db, jobId);
            r.Status = RemnantStatus.Consumed;
            r.ConsumedJobId = job.Id;
            r.ConsumedDate = date;
            var tx = await InventoryEngine.RemnantTxAsync(db, r, InventoryTxType.RemnantConsumption, date, -1, -r.Cost, job.Id, null);
            await PostAsync(db, tx, new JournalDraft { Date = date, Description = $"Remnant {r.Code} used in {job.Number}", SourceType = "Remnant", SourceId = r.Id, SourceNumber = r.Code }
                .Dr(SystemAccounts.WIP, r.Cost, tags: new LineTags(JobId: job.Id)).Cr(SystemAccounts.InventoryRemnants, r.Cost));
            JobCostEngine.Add(db, job, CostComponent.Material, r.Cost, date, "Remnant", r.Id, $"Remnant {r.Code} {r.Material!.Name} {r.Length:0.#}×{r.Width:0.#}", 0, r.MaterialId, journal: tx.JournalEntry);
            if (job.Status is JobStatus.New or JobStatus.Planned) { job.Status = JobStatus.InProduction; job.StartedAt ??= Now; }
        });
    }

    /// <summary>Adjust a remnant's recorded size/value, or scrap it. Value decreases go to Inventory Loss.</summary>
    public async Task AdjustRemnantAsync(long remnantId, decimal length, decimal width, decimal newCost, bool scrap, DateTime date, string reason)
    {
        Demand(AppModule.Inventory, Permission.Post);
        Validation.Required(reason, "Reason");
        await TxAsync(async db =>
        {
            var r = await db.Remnants.FirstOrDefaultAsync(x => x.Id == remnantId) ?? throw new DomainException("Err.NotFound");
            if (r.Status != RemnantStatus.Available) throw new DomainException("Err.RemnantNotAvailable", r.Code);
            var target = scrap ? 0 : Money.Round(newCost);
            if (target < 0) throw new DomainException("Err.NegativeValue");
            if (!scrap && (length <= 0 || width <= 0)) throw new DomainException("Err.DimensionsRequired");
            var delta = target - r.Cost;
            if (scrap) r.Status = RemnantStatus.Scrapped;
            else { r.Length = length; r.Width = width; }
            r.Cost = target;
            r.Notes = string.IsNullOrWhiteSpace(r.Notes) ? reason : r.Notes + " | " + reason;
            var tx = await InventoryEngine.RemnantTxAsync(db, r, InventoryTxType.RemnantAdjustment, date, scrap ? -1 : 0, delta, null, reason);
            var draft = new JournalDraft { Date = date, Description = $"Remnant {r.Code} adjustment: {reason}", SourceType = "Remnant", SourceId = r.Id, SourceNumber = r.Code };
            if (delta < 0) draft.Dr(SystemAccounts.InventoryLoss, -delta).Cr(SystemAccounts.InventoryRemnants, -delta);
            else if (delta > 0) draft.Dr(SystemAccounts.InventoryRemnants, delta).Cr(SystemAccounts.InventoryGain, delta);
            await PostAsync(db, tx, draft);
        });
    }
}
