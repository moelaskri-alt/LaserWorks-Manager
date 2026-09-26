using System.Text.Json;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record ProductTemplateRow(long Id, string Code, string Name, string? Description, decimal Quantity, int Lines, DateTime CreatedAt);

/// <summary>
/// Reusable product templates (standard bill of materials and routing) for repeat products.
/// A template is saved from an estimate and starts new estimates; custom jobs never need one.
/// </summary>
public sealed class ProductTemplateService : ServiceBase
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public ProductTemplateService(ServiceContext ctx) : base(ctx) { }

    // serialized structure (ids of materials / machines / employees; missing ones are skipped when used)
    private sealed record PieceDef(string? Name, decimal Length, decimal Width, decimal QuantityPerUnit);
    private sealed record LineDef(ComponentCategory Category, ComponentSource Source, long? MaterialId, string? Description, string? Unit, bool SheetBased,
        decimal SheetLength, decimal SheetWidth, decimal Spacing, decimal NestingEfficiency, bool ChargeFullSheets, decimal QuantityPerUnit, decimal UnitCost, List<PieceDef> Pieces);
    private sealed record MachineDef(long MachineId, OperationType Operation, decimal MinutesPerUnit);
    private sealed record LaborDef(long? EmployeeId, OperationType Operation, decimal MinutesPerUnit, decimal HourlyRate);
    private sealed record Definition(List<LineDef> Lines, List<MachineDef> Machines, List<LaborDef> Labor, decimal DesignHours, decimal DesignRate, decimal SetupHours, decimal SetupRate,
        decimal FinishingPerUnit, decimal PackagingPerUnit, decimal ConsumablesPerUnit, decimal ScrapAllowancePercent, decimal ReworkAllowancePercent);

    public async Task<List<ProductTemplateRow>> ListAsync(bool activeOnly = true)
    {
        Demand(AppModule.Estimates, Permission.View);
        var rows = await ReadAsync(db => db.ProductTemplates.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Name).ToListAsync());
        return rows.Select(t => new ProductTemplateRow(t.Id, t.Code, t.Name, t.Description, t.Quantity, Parse(t.Definition)?.Lines.Count ?? 0, t.CreatedAt)).ToList();
    }

    /// <summary>Saves the structure of an estimate as a reusable template.</summary>
    public async Task<long> SaveFromEstimateAsync(long estimateId, string name, string? description = null)
    {
        Demand(AppModule.Estimates, Permission.Create);
        Validation.Required(name, "Name");
        var e = await ReadAsync(db => db.CostEstimates.AsNoTracking().Include(x => x.MaterialLines).ThenInclude(l => l.Pieces).Include(x => x.MachineLines).Include(x => x.LaborLines)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == estimateId)) ?? throw new DomainException("Err.NotFound");
        var def = new Definition(
            e.MaterialLines.OrderBy(l => l.LineNo).Select(l => new LineDef(l.Category, l.Source == ComponentSource.Remnant ? ComponentSource.Inventory : l.Source, l.MaterialId, l.Description, l.Unit,
                l.SheetBased, l.SheetLength, l.SheetWidth, l.Spacing, l.NestingEfficiency, l.ChargeFullSheets,
                // a remnant is job specific: the template keeps the material as a normal stock line
                l.Source == ComponentSource.Remnant ? (l.SheetBased ? 0 : 1) : l.QuantityPerUnit, l.UnitCost,
                l.Pieces.Select(p => new PieceDef(p.Name, p.Length, p.Width, p.QuantityPerUnit)).ToList())).ToList(),
            e.MachineLines.Select(m => new MachineDef(m.MachineId, m.Operation, m.MinutesPerUnit)).ToList(),
            e.LaborLines.Select(l => new LaborDef(l.EmployeeId, l.Operation, l.MinutesPerUnit, l.HourlyRate)).ToList(),
            e.DesignHours, e.DesignRate, e.SetupHours, e.SetupRate, e.FinishingPerUnit, e.PackagingPerUnit, e.ConsumablesPerUnit, e.ScrapAllowancePercent, e.ReworkAllowancePercent);
        return await TxAsync(async db =>
        {
            var t = new ProductTemplate
            {
                Code = await Numbering.NextAsync(db, SequenceKey.ProductTemplate), Name = name.Trim(), Description = description.Norm() ?? e.Description,
                Quantity = e.Quantity, Definition = JsonSerializer.Serialize(def, Json), SourceEstimateId = e.Id
            };
            db.ProductTemplates.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });
    }

    /// <summary>
    /// Saves a job as a template: its component lines (including lines added during production) become the bill of materials;
    /// machine, labor and other parameters come from the job's estimate when there is one.
    /// </summary>
    public async Task<long> SaveFromJobAsync(long jobId, string name, string? description = null)
    {
        Demand(AppModule.Estimates, Permission.Create);
        Validation.Required(name, "Name");
        var job = await ReadAsync(db => db.Jobs.AsNoTracking().Include(j => j.Components).FirstOrDefaultAsync(j => j.Id == jobId)) ?? throw new DomainException("Err.NotFound");
        CostEstimate? e = job.EstimateId is { } eid
            ? await ReadAsync(db => db.CostEstimates.AsNoTracking().Include(x => x.MaterialLines).ThenInclude(l => l.Pieces).Include(x => x.MachineLines).Include(x => x.LaborLines)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == eid))
            : null;
        var qty = job.Quantity > 0 ? job.Quantity : 1;
        var lines = new List<LineDef>();
        foreach (var c in job.Components.OrderBy(c => c.LineNo))
        {
            var src = c.Source == ComponentSource.Remnant ? ComponentSource.Inventory : c.Source;
            var el = e?.MaterialLines.FirstOrDefault(l => l.Id == c.EstimateLineId);
            if (el is { SheetBased: true } && c.Source != ComponentSource.Remnant)
                lines.Add(new LineDef(c.Category, src, c.MaterialId, c.Description, c.Unit, true, el.SheetLength, el.SheetWidth, el.Spacing, el.NestingEfficiency, el.ChargeFullSheets, 0, el.UnitCost,
                    el.Pieces.Select(p => new PieceDef(p.Name, p.Length, p.Width, p.QuantityPerUnit)).ToList()));
            else
                lines.Add(new LineDef(c.Category, src, c.MaterialId, c.Description, c.Unit, false, 0, 0, 0, 85, false,
                    Math.Round(c.PlannedQuantity / qty, 4), c.EstimatedUnitCost, new List<PieceDef>()));
        }
        var def = new Definition(lines,
            e?.MachineLines.Select(m => new MachineDef(m.MachineId, m.Operation, m.MinutesPerUnit)).ToList() ?? new(),
            e?.LaborLines.Select(l => new LaborDef(l.EmployeeId, l.Operation, l.MinutesPerUnit, l.HourlyRate)).ToList() ?? new(),
            e?.DesignHours ?? 0, e?.DesignRate ?? 0, e?.SetupHours ?? 0, e?.SetupRate ?? 0, e?.FinishingPerUnit ?? 0, e?.PackagingPerUnit ?? 0, e?.ConsumablesPerUnit ?? 0,
            e?.ScrapAllowancePercent ?? 0, e?.ReworkAllowancePercent ?? 0);
        return await TxAsync(async db =>
        {
            var t = new ProductTemplate
            {
                Code = await Numbering.NextAsync(db, SequenceKey.ProductTemplate), Name = name.Trim(), Description = description.Norm() ?? job.Title,
                Quantity = qty, Definition = JsonSerializer.Serialize(def, Json), SourceEstimateId = e?.Id
            };
            db.ProductTemplates.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });
    }

    /// <summary>A new, unsaved draft estimate for the customer built from the template (current item costs and machine rates).</summary>
    public async Task<CostEstimate> NewEstimateAsync(long templateId, long customerId, EstimateService estimates)
    {
        Demand(AppModule.Estimates, Permission.Create);
        var t = await ReadAsync(db => db.ProductTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId)) ?? throw new DomainException("Err.NotFound");
        var def = Parse(t.Definition) ?? throw new DomainException("Err.NotFound");
        var e = await estimates.NewDraftAsync(customerId);
        e.Description = t.Description ?? t.Name;
        e.Quantity = t.Quantity;
        await using var db = Factory.Create();
        var materials = await db.Materials.AsNoTracking().Include(m => m.Unit).Where(m => m.IsActive).ToDictionaryAsync(m => m.Id);
        var machines = await db.Machines.AsNoTracking().Where(m => m.IsActive).ToDictionaryAsync(m => m.Id);
        var no = 1;
        foreach (var l in def.Lines)
        {
            Material? m = null;
            if (l.MaterialId is { } mid && !materials.TryGetValue(mid, out m)) continue; // item no longer available
            e.MaterialLines.Add(new EstimateMaterialLine
            {
                LineNo = no++, Category = l.Category, Source = l.Source, MaterialId = m?.Id, Material = m, Description = l.Description, Unit = l.Unit ?? m?.Unit?.Code,
                SheetBased = l.SheetBased, SheetLength = l.SheetLength, SheetWidth = l.SheetWidth, Spacing = l.Spacing, NestingEfficiency = l.NestingEfficiency,
                ChargeFullSheets = l.ChargeFullSheets, QuantityPerUnit = l.QuantityPerUnit,
                // stock lines are re-priced at today's average cost; direct purchases and services keep the price saved with the template
                UnitCost = m != null && ComponentRules.IsStocked(l.Source) ? (m.AverageCost > 0 ? m.AverageCost : m.PurchaseCost) : l.UnitCost,
                Pieces = l.Pieces.Select(p => new EstimatePiece { Name = p.Name, Length = p.Length, Width = p.Width, QuantityPerUnit = p.QuantityPerUnit }).ToList()
            });
        }
        e.MachineLines.Clear();
        foreach (var mdef in def.Machines.Where(x => machines.ContainsKey(x.MachineId)))
        {
            var rate = machines[mdef.MachineId].CalculateRate();
            e.MachineLines.Add(new EstimateMachineLine
            {
                MachineId = mdef.MachineId, Machine = machines[mdef.MachineId], Operation = mdef.Operation, MinutesPerUnit = mdef.MinutesPerUnit,
                HourlyRate = rate.EffectiveRateExcludingMaintenance, MaintenanceRate = rate.EffectiveMaintenanceRate
            });
        }
        foreach (var ldef in def.Labor)
            e.LaborLines.Add(new EstimateLaborLine { EmployeeId = ldef.EmployeeId, Operation = ldef.Operation, MinutesPerUnit = ldef.MinutesPerUnit, HourlyRate = ldef.HourlyRate });
        e.DesignHours = def.DesignHours; e.DesignRate = def.DesignRate; e.SetupHours = def.SetupHours; e.SetupRate = def.SetupRate;
        e.FinishingPerUnit = def.FinishingPerUnit; e.PackagingPerUnit = def.PackagingPerUnit; e.ConsumablesPerUnit = def.ConsumablesPerUnit;
        e.ScrapAllowancePercent = def.ScrapAllowancePercent; e.ReworkAllowancePercent = def.ReworkAllowancePercent;
        return e;
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Estimates, Permission.Delete);
        await TxAsync(async db =>
        {
            var t = await db.ProductTemplates.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            db.ProductTemplates.Remove(t);
        });
    }

    private static Definition? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<Definition>(json, Json); }
        catch (JsonException) { return null; }
    }
}
