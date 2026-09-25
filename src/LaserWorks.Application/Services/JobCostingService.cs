using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record CostSheetEntry(long Id, DateTime Date, CostComponent Component, string SourceType, string? Description, decimal Quantity, decimal Hours, decimal Amount, string? JournalNumber);

public sealed record JobCostSheet(
    Job Job, string Customer, string? EstimateNumber, string? QuotationNumber, IReadOnlyList<CostSheetEntry> Entries, VarianceReport Variance,
    IReadOnlyList<JobMaterialLine> Materials, IReadOnlyList<OperationRow> Operations, IReadOnlyList<ScrapRow> Scrap, IReadOnlyList<QualityRow> Quality,
    decimal Revenue, bool RevenueIsInvoiced, decimal ActualCost, decimal GrossProfit, decimal MarginPercent, decimal EstimatedProfit, decimal EstimatedMarginPercent,
    decimal PlannedHours, decimal ActualHours, decimal MachineHours);

public sealed record JobProfitRow(long JobId, string JobNumber, string Customer, DateTime OrderDate, JobStatus Status, decimal Revenue, decimal EstimatedCost, decimal ActualCost,
    decimal MaterialEstimated, decimal MaterialActual, decimal MachineEstimated, decimal MachineActual, decimal ScrapCost, decimal ReworkCost, string? Machine)
{
    public decimal GrossProfit => Revenue - ActualCost;
    public decimal MarginPercent => Money.Percent(GrossProfit, Revenue);
    public decimal CostVariance => ActualCost - EstimatedCost;
    public decimal CostVariancePercent => EstimatedCost == 0 ? 0 : Money.Round(CostVariance / EstimatedCost * 100m);
    public decimal MaterialVariancePercent => MaterialEstimated == 0 ? (MaterialActual == 0 ? 0 : 100) : Money.Round((MaterialActual - MaterialEstimated) / MaterialEstimated * 100m);
    public decimal MachineVariancePercent => MachineEstimated == 0 ? (MachineActual == 0 ? 0 : 100) : Money.Round((MachineActual - MachineEstimated) / MachineEstimated * 100m);
    public decimal ScrapPercent => ActualCost == 0 ? 0 : Money.Round(ScrapCost / ActualCost * 100m);
    public decimal ReworkPercent => ActualCost == 0 ? 0 : Money.Round(ReworkCost / ActualCost * 100m);
}

public sealed record ProfitFlags(bool LowMargin, bool NegativeMargin, bool HighMaterialVariance, bool HighScrap, bool HighMachineVariance)
{
    public bool Any => LowMargin || NegativeMargin || HighMaterialVariance || HighScrap || HighMachineVariance;
}

public sealed record GroupProfitRow(string Key, string Name, int Jobs, decimal Revenue, decimal Cost, decimal Hours = 0, decimal Quantity = 0)
{
    public decimal GrossProfit => Revenue - Cost;
    public decimal MarginPercent => Money.Percent(GrossProfit, Revenue);
}

public sealed class JobCostingService : ServiceBase
{
    public JobCostingService(ServiceContext ctx) : base(ctx) { }

    /// <summary>Estimated cost per component: the estimate snapshot, or a single "other" line for jobs without an estimate.</summary>
    internal static async Task<Dictionary<CostComponent, decimal>> EstimatedAsync(IAppDb db, Job job)
    {
        if (job.EstimateId is { } eid)
        {
            var comps = await db.EstimateComponentLines.AsNoTracking().Where(c => c.EstimateId == eid).ToListAsync();
            if (comps.Count > 0) return comps.GroupBy(c => c.Component).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        }
        return new Dictionary<CostComponent, decimal> { [CostComponent.OtherDirect] = job.EstimatedCost };
    }

    internal static async Task<Dictionary<CostComponent, decimal>> ActualAsync(IAppDb db, long jobId) =>
        (await db.JobCostEntries.AsNoTracking().Where(e => e.JobId == jobId).GroupBy(e => e.Component).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync())
        .ToDictionary(x => x.Key, x => x.Sum);

    public static decimal RevenueOf(Job j) => j.Status >= JobStatus.Invoiced || j.InvoicedRevenue != 0 ? j.InvoicedRevenue : j.SellingPrice;

    public async Task<VarianceReport> VarianceAsync(long jobId)
    {
        await using var db = Factory.Create();
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId) ?? throw new DomainException("Err.NotFound");
        return VarianceCalculator.Compare(await EstimatedAsync(db, job), await ActualAsync(db, jobId));
    }

    public async Task<JobCostSheet> CostSheetAsync(long jobId)
    {
        Demand(AppModule.Jobs, Permission.View);
        await using var db = Factory.Create();
        var job = await db.Jobs.AsNoTracking().Include(j => j.Customer).Include(j => j.Machine).Include(j => j.Operator).Include(j => j.DesignRevision)
            .FirstOrDefaultAsync(j => j.Id == jobId) ?? throw new DomainException("Err.NotFound");
        var entries = await db.JobCostEntries.AsNoTracking().Where(e => e.JobId == jobId).OrderBy(e => e.Date).ThenBy(e => e.Id)
            .Select(e => new CostSheetEntry(e.Id, e.Date, e.Component, e.SourceType, e.Description, e.Quantity, e.Hours, e.Amount, e.JournalEntry != null ? e.JournalEntry.Number : null)).ToListAsync();
        var variance = VarianceCalculator.Compare(await EstimatedAsync(db, job), entries.GroupBy(e => e.Component).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)));
        var materials = await InventoryService.JobMaterialsAsync(db, jobId);
        var ops = await db.JobOperations.AsNoTracking().Where(o => o.JobId == jobId).OrderBy(o => o.Sequence)
            .Select(o => new OperationRow(o.Id, o.JobId, job.Number, job.Customer!.Name, o.Sequence, o.OperationType, o.Employee != null ? o.Employee.Name : null, o.Machine != null ? o.Machine.Name : null,
                o.PlannedHours, o.ActualHours, o.MachineHours, o.Quantity, o.ScrapQuantity, o.ReworkQuantity, o.IsRework, o.Status, o.StartTime, o.EndTime, o.CostPosted,
                o.MachineCost + o.MaintenanceCost + o.LaborCost, job.DueDate, o.Notes)).ToListAsync();
        var scrap = await db.ScrapRecords.AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Date)
            .Select(x => new ScrapRow(x.Id, x.Number, x.Date, x.Type, x.JobId, job.Number, x.Operation != null ? x.Operation.OperationType.ToString() : null, x.Material != null ? x.Material.Name : null,
                x.Machine != null ? x.Machine.Name : null, x.Quantity, x.Hours, x.Reason, x.Cost)).ToListAsync();
        var quality = await db.QualityChecks.AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Date)
            .Select(x => new QualityRow(x.Id, x.JobId, job.Number, job.Customer!.Name, x.Date, x.Inspector != null ? x.Inspector.Name : null, x.QuantityProduced, x.QuantityAccepted,
                x.QuantityRejected, x.ReworkQuantity, x.Status, x.Notes)).ToListAsync();
        var estNumber = job.EstimateId is { } eid ? await db.CostEstimates.Where(e => e.Id == eid).Select(e => e.Number).FirstOrDefaultAsync() : null;
        var quoteNumber = job.QuotationId is { } qid ? await db.Quotations.Where(q => q.Id == qid).Select(q => q.Number + " v" + q.VersionNo).FirstOrDefaultAsync() : null;
        var revenue = RevenueOf(job);
        var actual = entries.Sum(e => e.Amount);
        var gp = revenue - actual;
        var estProfit = job.SellingPrice - job.EstimatedCost;
        return new JobCostSheet(job, job.Customer!.Name, estNumber, quoteNumber, entries, variance, materials, ops, scrap, quality, revenue, job.Status >= JobStatus.Invoiced, actual, gp,
            Money.Percent(gp, revenue), estProfit, Money.Percent(estProfit, job.SellingPrice), ops.Sum(o => o.PlannedHours), ops.Sum(o => o.ActualHours), ops.Sum(o => o.MachineHours));
    }

    // ------------------------------------------------------------------ profitability
    public async Task<List<JobProfitRow>> JobProfitabilityAsync(DateRange? range = null, bool completedOnly = false, long? customerId = null, string? search = null, int max = 5000)
    {
        Demand(AppModule.Profitability, Permission.View);
        await using var db = Factory.Create();
        var q = db.Jobs.AsNoTracking().Where(j => j.Status != JobStatus.Cancelled);
        if (range != null) q = q.Where(j => j.OrderDate >= range.From.Date && j.OrderDate < range.ToExclusive);
        if (completedOnly) q = q.Where(j => j.Status >= JobStatus.Completed);
        if (customerId.HasValue) q = q.Where(j => j.CustomerId == customerId);
        if (search.Norm() is { } s) q = q.Where(j => j.Number.Contains(s) || j.Customer!.Name.Contains(s) || j.Title.Contains(s));
        var jobs = await q.OrderByDescending(j => j.OrderDate).ThenByDescending(j => j.Id).Take(max)
            .Select(j => new { j.Id, j.Number, Customer = j.Customer!.Name, j.OrderDate, j.Status, j.SellingPrice, j.InvoicedRevenue, j.EstimatedCost, j.ActualCost, j.EstimateId, Machine = j.Machine != null ? j.Machine.Name : null })
            .ToListAsync();
        var ids = jobs.Select(j => j.Id).ToList();
        var actual = new List<(long JobId, CostComponent C, decimal Sum)>();
        foreach (var chunk in ids.Chunk(900))
            actual.AddRange((await db.JobCostEntries.AsNoTracking().Where(e => chunk.Contains(e.JobId)).GroupBy(e => new { e.JobId, e.Component })
                .Select(g => new { g.Key.JobId, g.Key.Component, Sum = g.Sum(x => x.Amount) }).ToListAsync()).Select(x => (x.JobId, x.Component, x.Sum)));
        var estIds = jobs.Where(j => j.EstimateId != null).Select(j => j.EstimateId!.Value).Distinct().ToList();
        var est = new List<(long EstimateId, CostComponent C, decimal Amount)>();
        foreach (var chunk in estIds.Chunk(900))
            est.AddRange((await db.EstimateComponentLines.AsNoTracking().Where(c => chunk.Contains(c.EstimateId)).Select(c => new { c.EstimateId, c.Component, c.Amount }).ToListAsync())
                .Select(x => (x.EstimateId, x.Component, x.Amount)));
        var actualLookup = actual.ToLookup(a => a.JobId);
        var estLookup = est.ToLookup(e => e.EstimateId);
        return jobs.Select(j =>
        {
            var a = actualLookup[j.Id].ToDictionary(x => x.C, x => x.Sum);
            var e = j.EstimateId is { } eid ? estLookup[eid].GroupBy(x => x.C).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)) : new Dictionary<CostComponent, decimal>();
            var revenue = j.Status >= JobStatus.Invoiced || j.InvoicedRevenue != 0 ? j.InvoicedRevenue : j.SellingPrice;
            return new JobProfitRow(j.Id, j.Number, j.Customer, j.OrderDate, j.Status, revenue, j.EstimatedCost, j.ActualCost,
                e.GetValueOrDefault(CostComponent.Material), a.GetValueOrDefault(CostComponent.Material),
                e.GetValueOrDefault(CostComponent.Machine) + e.GetValueOrDefault(CostComponent.Maintenance), a.GetValueOrDefault(CostComponent.Machine) + a.GetValueOrDefault(CostComponent.Maintenance),
                a.GetValueOrDefault(CostComponent.Scrap), a.GetValueOrDefault(CostComponent.Rework), j.Machine);
        }).ToList();
    }

    public ProfitFlags Flags(JobProfitRow r)
    {
        var s = Ctx.Settings.Current;
        var hasCost = r.ActualCost != 0;
        return new ProfitFlags(
            LowMargin: hasCost && r.Revenue > 0 && r.MarginPercent >= 0 && r.MarginPercent < s.LowMarginThresholdPercent,
            NegativeMargin: hasCost && r.GrossProfit < 0,
            HighMaterialVariance: r.MaterialActual > 0 && r.MaterialVariancePercent > s.HighVariancePercent,
            HighScrap: r.ScrapPercent > s.HighScrapPercent,
            HighMachineVariance: r.MachineActual > 0 && r.MachineVariancePercent > s.HighVariancePercent);
    }

    /// <summary>Customer profitability from posted invoices and returns (recognised revenue and COGS).</summary>
    public async Task<List<GroupProfitRow>> ByCustomerAsync(DateRange range)
    {
        Demand(AppModule.Profitability, Permission.View);
        await using var db = Factory.Create();
        var inv = await db.SalesInvoices.AsNoTracking().Where(i => i.Status == DocumentStatus.Posted && i.Date >= range.From.Date && i.Date < range.ToExclusive)
            .GroupBy(i => new { i.CustomerId, i.Customer!.Code, i.Customer.Name })
            .Select(g => new { g.Key.CustomerId, g.Key.Code, g.Key.Name, Revenue = g.Sum(x => x.Subtotal - x.DiscountAmount), Cost = g.Sum(x => x.CogsAmount), Count = g.Count(), Jobs = g.Count(x => x.JobId != null) })
            .ToListAsync();
        var ret = await db.SalesReturns.AsNoTracking().Where(r => r.Status == DocumentStatus.Posted && r.Date >= range.From.Date && r.Date < range.ToExclusive)
            .GroupBy(r => r.CustomerId).Select(g => new { CustomerId = g.Key, Revenue = g.Sum(x => x.Subtotal), Cost = g.Sum(x => x.CogsReversed) }).ToListAsync();
        var jobCosts = await db.JournalEntries.AsNoTracking().Where(e => e.SourceType == "Job" && e.Status != JournalStatus.Draft && e.Date >= range.From.Date && e.Date < range.ToExclusive)
            .SelectMany(e => e.Lines).Where(l => l.Account!.SystemKey == SystemAccounts.COGS && l.JobId != null)
            .Join(db.Jobs, l => l.JobId, j => j.Id, (l, j) => new { j.CustomerId, Amount = l.Debit - l.Credit })
            .GroupBy(x => x.CustomerId).Select(g => new { CustomerId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync();
        return inv.Select(i =>
        {
            var r = ret.FirstOrDefault(x => x.CustomerId == i.CustomerId);
            var late = jobCosts.FirstOrDefault(x => x.CustomerId == i.CustomerId)?.Amount ?? 0;
            return new GroupProfitRow(i.Code, i.Name, i.Jobs, i.Revenue - (r?.Revenue ?? 0), i.Cost - (r?.Cost ?? 0) + late);
        }).OrderByDescending(x => x.GrossProfit).ToList();
    }

    /// <summary>Machine profitability: machine hours and cost absorbed, and the result of jobs assigned to each machine.</summary>
    public async Task<List<GroupProfitRow>> ByMachineAsync(DateRange range)
    {
        Demand(AppModule.Profitability, Permission.View);
        var rows = await JobProfitabilityAsync(range);
        await using var db = Factory.Create();
        var hours = await db.JobCostEntries.AsNoTracking().Where(e => e.MachineId != null && e.EmployeeId == null && (e.Component == CostComponent.Machine || e.Component == CostComponent.Rework)
                                                                      && e.Date >= range.From.Date && e.Date < range.ToExclusive)
            .GroupBy(e => e.MachineId).Select(g => new { MachineId = g.Key, Hours = g.Sum(x => x.Hours), Cost = g.Sum(x => x.Amount) }).ToListAsync();
        var machines = await db.Machines.AsNoTracking().ToListAsync();
        return machines.Select(m =>
        {
            var jobs = rows.Where(r => r.Machine == m.Name).ToList();
            var h = hours.FirstOrDefault(x => x.MachineId == m.Id);
            return new GroupProfitRow(m.Code, m.Name, jobs.Count, jobs.Sum(j => j.Revenue), jobs.Sum(j => j.ActualCost), h?.Hours ?? 0, h?.Cost ?? 0);
        }).OrderByDescending(x => x.GrossProfit).ToList();
    }

    /// <summary>Material view: consumption and the result of jobs that used each material.</summary>
    public async Task<List<GroupProfitRow>> ByMaterialAsync(DateRange range)
    {
        Demand(AppModule.Profitability, Permission.View);
        var rows = (await JobProfitabilityAsync(range)).ToDictionary(r => r.JobId);
        await using var db = Factory.Create();
        var usage = await db.JobCostEntries.AsNoTracking().Where(e => e.Component == CostComponent.Material && e.MaterialId != null && e.Date >= range.From.Date && e.Date < range.ToExclusive)
            .GroupBy(e => new { e.MaterialId, e.JobId }).Select(g => new { g.Key.MaterialId, g.Key.JobId, Qty = g.Sum(x => x.Quantity), Cost = g.Sum(x => x.Amount) }).ToListAsync();
        var mats = await db.Materials.AsNoTracking().Select(m => new { m.Id, m.Code, m.Name }).ToListAsync();
        return usage.GroupBy(u => u.MaterialId).Select(g =>
        {
            var m = mats.First(x => x.Id == g.Key);
            var jobRows = g.Select(x => rows.GetValueOrDefault(x.JobId)).Where(x => x != null).ToList();
            return new GroupProfitRow(m.Code, m.Name, g.Select(x => x.JobId).Distinct().Count(), jobRows.Sum(r => r!.Revenue), jobRows.Sum(r => r!.ActualCost), 0, g.Sum(x => x.Qty));
        }).OrderByDescending(x => x.GrossProfit).ToList();
    }

    /// <summary>Monthly gross profit from the general ledger (sales − returns − COGS).</summary>
    public async Task<List<GroupProfitRow>> ByMonthAsync(DateRange range)
    {
        Demand(AppModule.Profitability, Permission.View);
        await using var db = Factory.Create();
        var lines = await db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry!.Status != JournalStatus.Draft && l.JournalEntry.Date >= range.From.Date && l.JournalEntry.Date < range.ToExclusive
                        && (l.Account!.SystemKey == SystemAccounts.Sales || l.Account.SystemKey == SystemAccounts.SalesReturns || l.Account.SystemKey == SystemAccounts.COGS))
            .Select(l => new { l.JournalEntry!.Date, l.Account!.SystemKey, l.Debit, l.Credit }).ToListAsync();
        return lines.GroupBy(l => new DateTime(l.Date.Year, l.Date.Month, 1)).OrderBy(g => g.Key).Select(g =>
        {
            var revenue = g.Where(x => x.SystemKey != SystemAccounts.COGS).Sum(x => x.Credit - x.Debit);
            var cogs = g.Where(x => x.SystemKey == SystemAccounts.COGS).Sum(x => x.Debit - x.Credit);
            return new GroupProfitRow(g.Key.ToString("yyyy-MM"), g.Key.ToString("MMM yyyy"), 0, revenue, cogs);
        }).ToList();
    }
}
