using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record OperationRow(long Id, long JobId, string JobNumber, string Customer, int Sequence, OperationType OperationType, string? Employee, string? Machine,
    decimal PlannedHours, decimal ActualHours, decimal MachineHours, decimal Quantity, decimal ScrapQuantity, decimal ReworkQuantity, bool IsRework, OperationStatus Status,
    DateTime? StartTime, DateTime? EndTime, bool CostPosted, decimal TotalCost, DateTime? DueDate, string? Notes)
{
    public decimal HoursVariance => ActualHours - PlannedHours;
}

public sealed record OperationCompletion(decimal LaborHours, decimal MachineHours, decimal Quantity, decimal ScrapQuantity = 0, decimal ReworkQuantity = 0,
    DateTime? StartTime = null, DateTime? EndTime = null, long? EmployeeId = null, long? MachineId = null, string? Notes = null);

public sealed record ScrapRow(long Id, string Number, DateTime Date, ScrapType Type, long JobId, string JobNumber, string? Operation, string? Material, string? Machine,
    decimal Quantity, decimal Hours, string Reason, decimal Cost);

public sealed record QualityRow(long Id, long JobId, string JobNumber, string Customer, DateTime Date, string? Inspector, decimal QuantityProduced, decimal QuantityAccepted,
    decimal QuantityRejected, decimal ReworkQuantity, QualityStatus Status, string? Notes)
{
    public decimal AcceptanceRate => QuantityProduced == 0 ? 0 : Money.Round(QuantityAccepted / QuantityProduced * 100m);
}

public sealed class ProductionService : ServiceBase
{
    public ProductionService(ServiceContext ctx) : base(ctx) { }

    // ------------------------------------------------------------------ operations
    public async Task<PagedResult<OperationRow>> ListOperationsAsync(PageRequest req, long? jobId = null, OperationStatus? status = null, bool openJobsOnly = false, long? machineId = null, long? employeeId = null)
    {
        Demand(AppModule.Production, Permission.View);
        await using var db = Factory.Create();
        var q = db.JobOperations.AsNoTracking().AsQueryable();
        if (jobId.HasValue) q = q.Where(o => o.JobId == jobId);
        if (status.HasValue) q = q.Where(o => o.Status == status);
        if (openJobsOnly) q = q.Where(o => o.Job!.Status < JobStatus.Delivered);
        if (machineId.HasValue) q = q.Where(o => o.MachineId == machineId);
        if (employeeId.HasValue) q = q.Where(o => o.EmployeeId == employeeId);
        if (req.Search.Norm() is { } s) q = q.Where(o => o.Job!.Number.Contains(s) || o.Job.Customer!.Name.Contains(s) || o.Job.Title.Contains(s));
        var ordered = jobId.HasValue ? q.OrderBy(o => o.Sequence).ThenBy(o => o.Id) : q.OrderBy(o => o.Job!.DueDate).ThenBy(o => o.JobId).ThenBy(o => o.Sequence);
        return await ordered.Select(o => new OperationRow(o.Id, o.JobId, o.Job!.Number, o.Job.Customer!.Name, o.Sequence, o.OperationType, o.Employee != null ? o.Employee.Name : null,
                o.Machine != null ? o.Machine.Name : null, o.PlannedHours, o.ActualHours, o.MachineHours, o.Quantity, o.ScrapQuantity, o.ReworkQuantity, o.IsRework, o.Status,
                o.StartTime, o.EndTime, o.CostPosted, o.MachineCost + o.MaintenanceCost + o.LaborCost, o.Job.DueDate, o.Notes))
            .ToPagedAsync(req);
    }

    public async Task<JobOperation?> GetOperationAsync(long id) => await ReadAsync(db => db.JobOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id));

    public async Task<long> SaveOperationAsync(JobOperation input)
    {
        if (input.JobId == 0) throw new DomainException("Err.Required", "Job");
        Validation.NonNegative(input.PlannedHours, "PlannedHours");
        Validation.NonNegative(input.Quantity, "Quantity");
        Demand(AppModule.Production, input.Id == 0 ? Permission.Create : Permission.Edit);
        return await TxAsync(async db =>
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.JobId) ?? throw new DomainException("Err.NotFound");
            if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
            JobOperation o;
            if (input.Id == 0)
            {
                var seq = (await db.JobOperations.Where(x => x.JobId == job.Id).MaxAsync(x => (int?)x.Sequence) ?? 0) + 1;
                o = new JobOperation { JobId = job.Id, Sequence = input.Sequence > 0 ? input.Sequence : seq, Status = OperationStatus.Pending };
                db.JobOperations.Add(o);
            }
            else
            {
                o = await db.JobOperations.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new DomainException("Err.NotFound");
                if (o.CostPosted) throw new DomainException("Err.OperationPosted");
                if (input.Sequence > 0) o.Sequence = input.Sequence;
            }
            o.OperationType = input.OperationType; o.EmployeeId = input.EmployeeId; o.MachineId = input.MachineId; o.PlannedHours = input.PlannedHours;
            o.Quantity = input.Quantity; o.IsRework = input.IsRework; o.Notes = input.Notes.Norm();
            await db.SaveChangesAsync();
            return o.Id;
        });
    }

    public async Task DeleteOperationAsync(long id)
    {
        Demand(AppModule.Production, Permission.Edit);
        await TxAsync(async db =>
        {
            var o = await db.JobOperations.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (o.CostPosted || o.Status != OperationStatus.Pending) throw new DomainException("Err.OperationPosted");
            db.JobOperations.Remove(o);
        });
    }

    public async Task StartOperationAsync(long id)
    {
        Demand(AppModule.Production, Permission.Edit);
        await TxAsync(async db =>
        {
            var o = await db.JobOperations.Include(x => x.Job).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (o.Status != OperationStatus.Pending) throw new DomainException("Err.OperationNotPending");
            if (o.Job!.Status is JobStatus.Closed or JobStatus.Cancelled or JobStatus.Invoiced) throw new DomainException("Err.JobClosed", o.Job.Number);
            o.Status = OperationStatus.InProgress;
            o.StartTime = Now;
            if (o.Job.Status is JobStatus.New or JobStatus.Planned) { o.Job.Status = JobStatus.InProduction; o.Job.StartedAt ??= Now; }
        });
    }

    private static CostComponent LaborComponent(JobOperation o) => o.IsRework ? CostComponent.Rework : o.OperationType switch
    {
        OperationType.Design => CostComponent.Design,
        OperationType.MaterialPreparation => CostComponent.Setup,
        _ => CostComponent.Labor
    };

    /// <summary>
    /// Records actual time and posts the actual cost: machine hours × machine rate (split into Machine and Maintenance) and
    /// labor hours × employee rate. Dr WIP / Cr Machine cost absorbed, Cr Labor absorbed.
    /// </summary>
    public async Task CompleteOperationAsync(long id, OperationCompletion c)
    {
        Demand(AppModule.Production, Permission.Post);
        if (c.LaborHours < 0 || c.MachineHours < 0 || c.Quantity < 0 || c.ScrapQuantity < 0 || c.ReworkQuantity < 0) throw new DomainException("Err.NegativeValue");
        if (c.LaborHours == 0 && c.MachineHours == 0) throw new DomainException("Err.HoursRequired");
        if (c.StartTime.HasValue && c.EndTime.HasValue && c.EndTime < c.StartTime) throw new DomainException("Err.EndBeforeStart");
        var s = await SettingsAsync();
        await TxAsync(async db =>
        {
            var o = await db.JobOperations.Include(x => x.Job).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (o.CostPosted) throw new DomainException("Err.OperationPosted");
            var job = o.Job!;
            if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
            o.EmployeeId = c.EmployeeId ?? o.EmployeeId;
            o.MachineId = c.MachineId ?? o.MachineId;
            if (c.MachineHours > 0 && o.MachineId == null) throw new DomainException("Err.Required", "Machine");
            o.ActualHours = c.LaborHours; o.MachineHours = c.MachineHours; o.Quantity = c.Quantity; o.ScrapQuantity = c.ScrapQuantity; o.ReworkQuantity = c.ReworkQuantity;
            o.StartTime = c.StartTime ?? o.StartTime ?? Now; o.EndTime = c.EndTime ?? Now; if (c.Notes.Norm() is { } n) o.Notes = n;

            var machine = o.MachineId is { } mid ? await db.Machines.FirstAsync(m => m.Id == mid) : null;
            var employee = o.EmployeeId is { } eid ? await db.Employees.FirstAsync(e => e.Id == eid) : null;
            var rate = machine?.CalculateRate();
            o.MachineRate = rate?.EffectiveRateExcludingMaintenance ?? 0;
            o.MaintenanceRate = rate?.EffectiveMaintenanceRate ?? 0;
            o.LaborRate = employee?.HourlyCost ?? s.DefaultLaborRate;
            o.MachineCost = Money.Round(c.MachineHours * o.MachineRate);
            o.MaintenanceCost = Money.Round(c.MachineHours * o.MaintenanceRate);
            o.LaborCost = Money.Round(c.LaborHours * o.LaborRate);
            o.Status = OperationStatus.Done;
            o.CostPosted = true;

            var tags = new LineTags(JobId: job.Id, MachineId: o.MachineId);
            var draft = new JournalDraft { Date = (o.EndTime ?? Now).Date, Description = $"{job.Number} {o.OperationType}: {c.MachineHours:0.##} machine h, {c.LaborHours:0.##} labor h", SourceType = "JobOperation", SourceId = o.Id, SourceNumber = job.Number }
                .Dr(SystemAccounts.WIP, o.MachineCost + o.MaintenanceCost + o.LaborCost, tags: tags)
                .Cr(SystemAccounts.MachineApplied, o.MachineCost + o.MaintenanceCost, tags: tags)
                .Cr(SystemAccounts.LaborApplied, o.LaborCost, tags: tags);
            var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
            var date = draft.Date;
            var machineComponent = o.IsRework ? CostComponent.Rework : CostComponent.Machine;
            var maintComponent = o.IsRework ? CostComponent.Rework : CostComponent.Maintenance;
            if (o.MachineCost != 0) JobCostEngine.Add(db, job, machineComponent, o.MachineCost, date, "JobOperation", o.Id, $"{o.OperationType} — {machine!.Name}", 0, null, machine.Id, null, c.MachineHours, je);
            if (o.MaintenanceCost != 0) JobCostEngine.Add(db, job, maintComponent, o.MaintenanceCost, date, "JobOperation", o.Id, $"{o.OperationType} maintenance — {machine!.Name}", 0, null, null, null, 0, je);
            if (o.LaborCost != 0) JobCostEngine.Add(db, job, LaborComponent(o), o.LaborCost, date, "JobOperation", o.Id, $"{o.OperationType} — {employee?.Name ?? "labor"}", 0, null, null, employee?.Id, c.LaborHours, je);
            if (job.Status is JobStatus.New or JobStatus.Planned) { job.Status = JobStatus.InProduction; job.StartedAt ??= Now; }
        });
    }

    /// <summary>Reverses the posted cost of an operation (reversal entries; nothing is deleted) so it can be corrected and re-recorded.</summary>
    public async Task ReopenOperationAsync(long id, string reason)
    {
        Demand(AppModule.Production, Permission.Post);
        Validation.Required(reason, "Reason");
        await TxAsync(async db =>
        {
            var o = await db.JobOperations.Include(x => x.Job).FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (!o.CostPosted) throw new DomainException("Err.OperationNotPosted");
            var job = o.Job!;
            if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
            var entries = await db.JobCostEntries.Where(e => e.JobId == job.Id && e.SourceType == "JobOperation" && e.SourceId == o.Id).ToListAsync();
            var jeIds = entries.Where(e => e.JournalEntryId != null).Select(e => e.JournalEntryId!.Value).Distinct().ToList();
            JournalEntry? rev = null;
            foreach (var jeId in jeIds)
            {
                var status = await db.JournalEntries.Where(x => x.Id == jeId).Select(x => x.Status).FirstAsync();
                if (status == JournalStatus.Posted) rev = await AccountingEngine.ReverseAsync(db, jeId, Now.Date, reason, UserName, Now);
            }
            foreach (var g in entries.GroupBy(e => new { e.Component, e.MachineId, e.EmployeeId }))
            {
                var amt = g.Sum(x => x.Amount);
                if (amt != 0) JobCostEngine.Add(db, job, g.Key.Component, -amt, Now.Date, "JobOperation", o.Id, $"Reversal: {reason}", 0, null, g.Key.MachineId, g.Key.EmployeeId, -g.Sum(x => x.Hours), rev);
            }
            o.CostPosted = false;
            o.Status = OperationStatus.InProgress;
            o.MachineCost = o.MaintenanceCost = o.LaborCost = 0;
            Audit(db, AuditAction.Reversed, nameof(JobOperation), o.Id, reason);
        });
    }

    // ------------------------------------------------------------------ overhead
    /// <summary>
    /// Applies (or tops up) overhead on a job from its actual cost: overhead% × actual direct cost, or rate × actual machine hours.
    /// Posts only the difference to what was already applied: Dr WIP / Cr Overhead absorbed.
    /// </summary>
    public static async Task ApplyOverheadAsync(IAppDb db, Job job, DateTime now, string user)
    {
        var settings = await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstAsync();
        var entries = await db.JobCostEntries.Where(e => e.JobId == job.Id).ToListAsync();
        entries.AddRange(db.JobCostEntries.Local.Where(e => e.JobId == job.Id && db.Entry(e).State == EntityState.Added));
        decimal target;
        if (settings.OverheadMethod == OverheadMethod.PercentOfDirectCost)
            target = Money.Round(entries.Where(e => e.Component != CostComponent.Overhead).Sum(e => e.Amount) * settings.OverheadRate / 100m);
        else
        {
            var hours = entries.Where(e => e.MachineId != null && e.EmployeeId == null && (e.Component == CostComponent.Machine || e.Component == CostComponent.Rework)).Sum(e => e.Hours);
            target = Money.Round(hours * settings.OverheadRate);
        }
        var applied = entries.Where(e => e.Component == CostComponent.Overhead).Sum(e => e.Amount);
        var delta = target - applied;
        if (delta == 0) return;
        var tags = new LineTags(JobId: job.Id);
        var draft = new JournalDraft { Date = now.Date, Description = $"Overhead applied to {job.Number}", SourceType = "Job", SourceId = job.Id, SourceNumber = job.Number };
        if (delta > 0) draft.Dr(SystemAccounts.WIP, delta, tags: tags).Cr(SystemAccounts.OverheadApplied, delta, tags: tags);
        else draft.Dr(SystemAccounts.OverheadApplied, -delta, tags: tags).Cr(SystemAccounts.WIP, -delta, tags: tags);
        var je = await AccountingEngine.PostAsync(db, draft, user, now);
        JobCostEngine.Add(db, job, CostComponent.Overhead, delta, now.Date, "Overhead", job.Id,
            settings.OverheadMethod == OverheadMethod.PercentOfDirectCost ? $"{settings.OverheadRate:0.##}% of direct cost" : $"{settings.OverheadRate:0.##} per machine hour", journal: je);
    }

    /// <summary>Marks production complete: applies overhead and moves the job to Quality Check.</summary>
    public async Task CompleteProductionAsync(long jobId)
    {
        Demand(AppModule.Production, Permission.Post);
        await TxAsync(async db =>
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId) ?? throw new DomainException("Err.NotFound");
            if (job.Status is not (JobStatus.InProduction or JobStatus.Planned or JobStatus.New)) throw new DomainException("Err.InvalidStatusTransition", job.Status, JobStatus.QualityCheck);
            if (await db.JobOperations.AnyAsync(o => o.JobId == jobId && o.Status == OperationStatus.InProgress)) throw new DomainException("Err.OperationsInProgress");
            await ApplyOverheadAsync(db, job, Now, UserName);
            job.Status = JobStatus.QualityCheck;
            Audit(db, AuditAction.StatusChanged, nameof(Job), jobId, "QualityCheck");
        });
    }

    // ------------------------------------------------------------------ scrap & rework
    public async Task<PagedResult<ScrapRow>> ListScrapAsync(PageRequest req, long? jobId = null, ScrapType? type = null, DateRange? range = null)
    {
        Demand(AppModule.Production, Permission.View);
        await using var db = Factory.Create();
        var q = db.ScrapRecords.AsNoTracking().AsQueryable();
        if (jobId.HasValue) q = q.Where(x => x.JobId == jobId);
        if (type.HasValue) q = q.Where(x => x.Type == type);
        if (range != null) q = q.Where(x => x.Date >= range.From.Date && x.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(x => x.Number.Contains(s) || x.Reason.Contains(s) || x.Job!.Number.Contains(s));
        return await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Select(x => new ScrapRow(x.Id, x.Number, x.Date, x.Type, x.JobId, x.Job!.Number, x.Operation != null ? x.Operation.OperationType.ToString() : null,
                x.Material != null ? x.Material.Name : null, x.Machine != null ? x.Machine.Name : null, x.Quantity, x.Hours, x.Reason, x.Cost))
            .ToPagedAsync(req);
    }

    /// <summary>
    /// Scrap / damage / waste: cost is moved from the job's Material component to its Scrap component (value already in WIP; no GL effect).
    /// Rework: new actual cost from rework hours × (machine rate + labor rate), Dr WIP / Cr absorbed accounts.
    /// </summary>
    public async Task<long> RecordScrapAsync(ScrapRecord input)
    {
        Demand(AppModule.Production, Permission.Create);
        if (input.JobId == 0) throw new DomainException("Err.Required", "Job");
        Validation.Required(input.Reason, "Reason");
        if (input.Quantity < 0 || input.Hours < 0 || input.Cost < 0) throw new DomainException("Err.NegativeValue");
        var s = await SettingsAsync();
        return await TxAsync(async db =>
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.JobId) ?? throw new DomainException("Err.NotFound");
            if (job.Status is JobStatus.Closed or JobStatus.Cancelled) throw new DomainException("Err.JobClosed", job.Number);
            var r = new ScrapRecord
            {
                Number = await Numbering.NextAsync(db, SequenceKey.Scrap), Date = input.Date == default ? Now.Date : input.Date.Date,
                Type = input.Type, JobId = job.Id, OperationId = input.OperationId, MaterialId = input.MaterialId, MachineId = input.MachineId, EmployeeId = input.EmployeeId,
                Quantity = input.Quantity, Hours = input.Hours, Reason = input.Reason.Trim(), Notes = input.Notes.Norm()
            };
            db.ScrapRecords.Add(r);
            if (input.Type == ScrapType.Rework)
            {
                if (input.Hours <= 0 && input.Cost <= 0) throw new DomainException("Err.HoursRequired");
                var machine = input.MachineId is { } mid ? await db.Machines.FirstAsync(m => m.Id == mid) : null;
                var employee = input.EmployeeId is { } eid ? await db.Employees.FirstAsync(e => e.Id == eid) : null;
                var machineCost = machine != null ? Money.Round(input.Hours * machine.CalculateRate().EffectiveRate) : 0;
                var laborCost = input.Hours > 0 ? Money.Round(input.Hours * (employee?.HourlyCost ?? s.DefaultLaborRate)) : 0;
                var total = input.Hours > 0 ? machineCost + laborCost : Money.Round(input.Cost);
                r.Cost = total;
                await db.SaveChangesAsync();
                var tags = new LineTags(JobId: job.Id, MachineId: machine?.Id);
                var draft = new JournalDraft { Date = r.Date, Description = $"Rework {job.Number}: {r.Reason}", SourceType = "Scrap", SourceId = r.Id, SourceNumber = r.Number }
                    .Dr(SystemAccounts.WIP, total, tags: tags);
                if (input.Hours > 0) draft.Cr(SystemAccounts.MachineApplied, machineCost, tags: tags).Cr(SystemAccounts.LaborApplied, laborCost, tags: tags);
                else draft.Cr(SystemAccounts.LaborApplied, total, tags: tags);
                var je = await AccountingEngine.PostAsync(db, draft, UserName, Now);
                r.JournalEntryId = je?.Id;
                if (machineCost > 0 && input.Hours > 0) JobCostEngine.Add(db, job, CostComponent.Rework, machineCost, r.Date, "Scrap", r.Id, $"Rework machine: {r.Reason}", r.Quantity, null, machine!.Id, null, input.Hours, je);
                var laborPart = input.Hours > 0 ? laborCost : total;
                if (laborPart > 0) JobCostEngine.Add(db, job, CostComponent.Rework, laborPart, r.Date, "Scrap", r.Id, $"Rework labor: {r.Reason}", r.Quantity, null, null, employee?.Id, input.Hours, je);
            }
            else
            {
                decimal cost;
                // the scrapped item's component line (and its cost category) — a damaged LED is scrapped out of purchased components
                var line = input.MaterialId is { } lineMat
                    ? await db.JobComponents.Where(c => c.JobId == job.Id && c.MaterialId == lineMat).OrderBy(c => c.LineNo).FirstOrDefaultAsync()
                    : null;
                var fromComponent = line != null ? ComponentRules.CostComponentOf(line.Category) : CostComponent.Material;
                if (input.MaterialId is { } matId && input.Quantity > 0 && input.Cost == 0)
                {
                    var issued = (await InventoryService.JobMaterialsAsync(db, job.Id)).FirstOrDefault(m => m.MaterialId == matId);
                    var unit = issued?.AverageIssueCost ?? 0;
                    if (unit == 0)
                    {
                        var mat = await db.Materials.FirstAsync(m => m.Id == matId);
                        unit = mat.AverageCost;
                    }
                    cost = Money.Round(unit * input.Quantity);
                }
                else cost = Money.Round(input.Cost);
                var available = line != null
                    ? await db.JobCostEntries.Where(e => e.JobId == job.Id && e.JobComponentId == line.Id).SumAsync(e => e.Amount)
                    : await db.JobCostEntries.Where(e => e.JobId == job.Id && e.Component == CostComponent.Material).SumAsync(e => e.Amount);
                if (cost > available) throw new DomainException("Err.ScrapExceedsMaterial", available, cost);
                r.Cost = cost;
                await db.SaveChangesAsync();
                if (cost > 0)
                {
                    JobCostEngine.Add(db, job, fromComponent, -cost, r.Date, "Scrap", r.Id, $"Reclassified to scrap: {r.Reason}", -r.Quantity, r.MaterialId, jobComponentId: line?.Id);
                    JobCostEngine.Add(db, job, CostComponent.Scrap, cost, r.Date, "Scrap", r.Id, $"{r.Type}: {r.Reason}", r.Quantity, r.MaterialId, r.MachineId);
                }
            }
            if (input.OperationId is { } opId)
            {
                var op = await db.JobOperations.FirstOrDefaultAsync(o => o.Id == opId && o.JobId == job.Id);
                if (op != null)
                {
                    if (input.Type == ScrapType.Rework) op.ReworkQuantity += input.Quantity;
                    else op.ScrapQuantity += input.Quantity;
                }
            }
            await db.SaveChangesAsync();
            return r.Id;
        });
    }

    // ------------------------------------------------------------------ quality
    public async Task<PagedResult<QualityRow>> ListQualityAsync(PageRequest req, long? jobId = null, QualityStatus? status = null, DateRange? range = null)
    {
        Demand(AppModule.Production, Permission.View);
        await using var db = Factory.Create();
        var q = db.QualityChecks.AsNoTracking().AsQueryable();
        if (jobId.HasValue) q = q.Where(x => x.JobId == jobId);
        if (status.HasValue) q = q.Where(x => x.Status == status);
        if (range != null) q = q.Where(x => x.Date >= range.From.Date && x.Date < range.ToExclusive);
        if (req.Search.Norm() is { } s) q = q.Where(x => x.Job!.Number.Contains(s) || x.Job.Customer!.Name.Contains(s));
        return await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Select(x => new QualityRow(x.Id, x.JobId, x.Job!.Number, x.Job.Customer!.Name, x.Date, x.Inspector != null ? x.Inspector.Name : null, x.QuantityProduced, x.QuantityAccepted,
                x.QuantityRejected, x.ReworkQuantity, x.Status, x.Notes))
            .ToPagedAsync(req);
    }

    /// <summary>Lightweight quality checkpoint. Passed → job Completed; Rework Required → back to production; Failed → stays in Quality Check.</summary>
    public async Task<long> RecordQualityCheckAsync(QualityCheck input)
    {
        Demand(AppModule.Production, Permission.Approve);
        if (input.QuantityProduced < 0 || input.QuantityAccepted < 0 || input.QuantityRejected < 0 || input.ReworkQuantity < 0) throw new DomainException("Err.NegativeValue");
        if (input.QuantityAccepted + input.QuantityRejected > input.QuantityProduced) throw new DomainException("Err.QualityQuantities");
        if (input.Status == QualityStatus.Passed && input.QuantityAccepted <= 0) throw new DomainException("Err.QualityQuantities");
        return await TxAsync(async db =>
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.JobId) ?? throw new DomainException("Err.NotFound");
            if (job.Status is not (JobStatus.QualityCheck or JobStatus.InProduction or JobStatus.Completed)) throw new DomainException("Err.JobNotInQuality");
            if (job.Status == JobStatus.InProduction)
            {
                await ApplyOverheadAsync(db, job, Now, UserName);
                job.Status = JobStatus.QualityCheck;
            }
            var qc = new QualityCheck
            {
                JobId = job.Id, Date = input.Date == default ? Now.Date : input.Date.Date, InspectorId = input.InspectorId, QuantityProduced = input.QuantityProduced,
                QuantityAccepted = input.QuantityAccepted, QuantityRejected = input.QuantityRejected, ReworkQuantity = input.ReworkQuantity, Status = input.Status, Notes = input.Notes.Norm()
            };
            db.QualityChecks.Add(qc);
            switch (input.Status)
            {
                case QualityStatus.Passed:
                    job.Status = JobStatus.Completed;
                    job.CompletedAt = Now;
                    break;
                case QualityStatus.ReworkRequired:
                    job.Status = JobStatus.InProduction;
                    break;
            }
            await db.SaveChangesAsync();
            Audit(db, input.Status == QualityStatus.Passed ? AuditAction.Approved : AuditAction.StatusChanged, nameof(QualityCheck), qc.Id, $"{job.Number}: {input.Status}");
            return qc.Id;
        });
    }
}
