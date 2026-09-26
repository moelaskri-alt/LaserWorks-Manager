using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record EstimateRow(long Id, string Number, DateTime Date, string Customer, string? RequestNumber, string Description, decimal Quantity,
    decimal TotalCost, decimal SuggestedPrice, decimal SellingPrice, EstimateStatus Status);

public sealed record WhatIfInput(decimal? Quantity = null, decimal? MaterialCost = null, decimal? MachineHours = null, decimal? MarginPercent = null, decimal? SellingPrice = null);

public sealed record WhatIfResult(decimal Quantity, EstimateResult Cost, decimal SellingPrice, decimal SuggestedPrice, decimal MinimumPrice, PriceAnalysis Analysis, decimal UnitPrice);

/// <summary>Pure computation of an estimate from its inputs (used by the service, the editor and what-if).</summary>
public static class EstimateBuilder
{
    public static void EnsureComponents(CostEstimate e)
    {
        foreach (var c in EstimateCalculator.EstimateComponents)
            if (e.Components.All(x => x.Component != c))
                e.Components.Add(new EstimateComponentLine { Component = c, Mode = ComponentMode.Auto });
        e.Components = e.Components.OrderBy(x => Array.IndexOf(EstimateCalculator.EstimateComponents, x.Component)).ToList();
    }

    /// <summary>Calculates each material line (sheet utilisation or quantity based).</summary>
    public static void ComputeMaterialLine(EstimateMaterialLine l, decimal quantity, int decimals = 2)
    {
        if (l.Source == ComponentSource.Remnant)
        {
            // a remnant is used whole, at its recorded value
            l.SheetBased = false;
            l.TotalQuantity = 1; l.SheetsRequired = 0; l.UtilizationPercent = 0; l.WasteArea = 0;
            l.Cost = Money.Round(l.UnitCost, decimals);
            return;
        }
        if (l.SheetBased && l.Source != ComponentSource.Inventory && l.Source != ComponentSource.DirectPurchase) l.SheetBased = false;
        if (l.SheetBased)
        {
            var pieces = l.Pieces.Select(p => new PieceSpec(p.Name, p.Length, p.Width, p.QuantityPerUnit * quantity)).ToList();
            if (pieces.Count == 0)
            {
                l.SheetsRequired = l.SheetsOverride; l.UtilizationPercent = 0; l.WasteArea = 0; l.TotalQuantity = l.SheetsOverride;
                l.Cost = Money.Round(l.SheetsOverride * l.UnitCost, decimals);
                return;
            }
            var r = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(l.SheetLength, l.SheetWidth, pieces, l.Spacing, l.NestingEfficiency, l.UnitCost, l.ChargeFullSheets, l.SheetsOverride));
            l.SheetsRequired = r.SheetsRequired;
            l.UtilizationPercent = r.UtilizationPercent;
            l.WasteArea = r.WasteArea;
            l.TotalQuantity = l.ChargeFullSheets ? r.SheetsRequired : Math.Min(r.SheetsRequired, r.SheetsConsumedExact);
            l.Cost = r.MaterialCost;
        }
        else
        {
            if (l.QuantityPerUnit < 0) throw new DomainException("Err.NegativeValue");
            l.TotalQuantity = Math.Round(l.QuantityPerUnit * quantity, 4);
            l.SheetsRequired = 0; l.UtilizationPercent = 0; l.WasteArea = 0;
            l.Cost = Money.Round(l.TotalQuantity * l.UnitCost, decimals);
        }
    }

    public static EstimateInput BuildInput(CostEstimate e, int decimals, decimal? quantity = null, decimal? materialCost = null, decimal? machineHours = null)
    {
        var manual = e.Components.Where(c => c.Mode == ComponentMode.Manual).ToDictionary(c => c.Component, c => c.ManualAmount);
        if (materialCost.HasValue) manual[CostComponent.Material] = materialCost.Value;
        var qty = quantity ?? e.Quantity;
        IReadOnlyList<ItemCost> itemCosts;
        if (quantity.HasValue && quantity != e.Quantity)
        {
            itemCosts = e.MaterialLines.Select(l =>
            {
                var clone = new EstimateMaterialLine
                {
                    Category = l.Category, Source = l.Source,
                    SheetBased = l.SheetBased, SheetLength = l.SheetLength, SheetWidth = l.SheetWidth, Spacing = l.Spacing, NestingEfficiency = l.NestingEfficiency,
                    ChargeFullSheets = l.ChargeFullSheets, SheetsOverride = 0, QuantityPerUnit = l.QuantityPerUnit, UnitCost = l.UnitCost, Pieces = l.Pieces
                };
                ComputeMaterialLine(clone, qty, decimals);
                return new ItemCost(ComponentRules.CostComponentOf(l.Category), clone.Cost);
            }).ToList();
        }
        else itemCosts = e.MaterialLines.Select(l => new ItemCost(ComponentRules.CostComponentOf(l.Category), l.Cost)).ToList();

        return new EstimateInput
        {
            Quantity = qty,
            ItemCosts = itemCosts,
            Machines = e.MachineLines.Select(m => new MachineTimeInput(m.MinutesPerUnit, m.HourlyRate, m.MaintenanceRate)).ToList(),
            Labor = e.LaborLines.Select(l => new LaborTimeInput(l.MinutesPerUnit, l.HourlyRate)).ToList(),
            DesignHours = e.DesignHours, DesignRate = e.DesignRate, SetupHours = e.SetupHours, SetupRate = e.SetupRate,
            FinishingPerUnit = e.FinishingPerUnit, PackagingPerUnit = e.PackagingPerUnit, ConsumablesPerUnit = e.ConsumablesPerUnit,
            OverheadMethod = e.OverheadMethod, OverheadRate = e.OverheadRate, ScrapAllowancePercent = e.ScrapAllowancePercent, ReworkAllowancePercent = e.ReworkAllowancePercent,
            Manual = manual, MachineHoursOverride = machineHours, Decimals = decimals
        };
    }

    /// <summary>
    /// Recalculates every result field of the estimate.
    /// When <paramref name="machineRates"/> is supplied (draft estimates), machine rate snapshots are refreshed from the machine master.
    /// </summary>
    public static EstimateResult Compute(CostEstimate e, int decimals, IReadOnlyDictionary<long, MachineRateBreakdown>? machineRates = null)
    {
        EnsureComponents(e);
        foreach (var l in e.MaterialLines) ComputeMaterialLine(l, e.Quantity, decimals);
        foreach (var m in e.MachineLines)
        {
            if (m.MinutesPerUnit < 0) throw new DomainException("Err.NegativeValue");
            if (machineRates != null && machineRates.TryGetValue(m.MachineId, out var rate))
            {
                m.HourlyRate = rate.EffectiveRateExcludingMaintenance;
                m.MaintenanceRate = rate.EffectiveMaintenanceRate;
            }
            m.Hours = Math.Round(m.MinutesPerUnit * e.Quantity / 60m, 4);
            m.MachineCost = Money.Round(m.Hours * m.HourlyRate, decimals);
            m.MaintenanceCost = Money.Round(m.Hours * m.MaintenanceRate, decimals);
        }
        foreach (var l in e.LaborLines)
        {
            if (l.MinutesPerUnit < 0 || l.HourlyRate < 0) throw new DomainException("Err.NegativeValue");
            l.Hours = Math.Round(l.MinutesPerUnit * e.Quantity / 60m, 4);
            l.Cost = Money.Round(l.Hours * l.HourlyRate, decimals);
        }
        var result = EstimateCalculator.Calculate(BuildInput(e, decimals));
        foreach (var c in e.Components)
        {
            c.CalculatedAmount = result.Calculated.GetValueOrDefault(c.Component);
            c.Amount = result[c.Component];
        }
        e.DirectCost = result.DirectCost;
        e.TotalCost = result.TotalCost;
        e.UnitCost = result.UnitCost;
        e.TotalMachineHours = result.TotalMachineHours;
        e.SuggestedPrice = PricingCalculator.PriceFromMargin(result.TotalCost, e.TargetMarginPercent, decimals);
        e.MinimumPrice = PricingCalculator.PriceFromMargin(result.TotalCost, e.MinimumMarginPercent, decimals);
        if (e.SellingPrice <= 0) e.SellingPrice = e.SuggestedPrice;
        return result;
    }

    /// <summary>What-if: recompute cost and price for changed quantity, material cost, machine hours, margin or price without touching the saved estimate.</summary>
    public static WhatIfResult WhatIf(CostEstimate e, WhatIfInput w, int decimals)
    {
        var qty = w.Quantity ?? e.Quantity;
        if (qty <= 0) throw new DomainException("Err.QuantityPositive");
        var cost = EstimateCalculator.Calculate(BuildInput(e, decimals, qty, w.MaterialCost, w.MachineHours));
        var margin = w.MarginPercent ?? e.TargetMarginPercent;
        var suggested = PricingCalculator.PriceFromMargin(cost.TotalCost, margin, decimals);
        var minimum = PricingCalculator.PriceFromMargin(cost.TotalCost, e.MinimumMarginPercent, decimals);
        var price = w.SellingPrice ?? (w.MarginPercent.HasValue || w.Quantity.HasValue || w.MaterialCost.HasValue || w.MachineHours.HasValue ? suggested : e.SellingPrice);
        return new WhatIfResult(qty, cost, price, suggested, minimum, PricingCalculator.Analyze(cost.TotalCost, price), Money.Round(price / qty, decimals));
    }
}

public sealed class EstimateService : ServiceBase
{
    public EstimateService(ServiceContext ctx) : base(ctx) { }

    public async Task<PagedResult<EstimateRow>> ListAsync(PageRequest req, EstimateStatus? status = null, long? customerId = null)
    {
        Demand(AppModule.Estimates, Permission.View);
        await using var db = Factory.Create();
        var q = db.CostEstimates.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(e => e.Status == status);
        if (customerId.HasValue) q = q.Where(e => e.CustomerId == customerId);
        if (req.Search.Norm() is { } s) q = q.Where(e => e.Number.Contains(s) || e.Description.Contains(s) || e.Customer!.Name.Contains(s));
        return await q.SortBy(req.SortBy, req.Descending, e => e.Id, defaultDesc: true).Select(e => new EstimateRow(e.Id, e.Number, e.Date, e.Customer!.Name, e.Request != null ? e.Request.Number : null, e.Description, e.Quantity,
                e.TotalCost, e.SuggestedPrice, e.SellingPrice, e.Status))
            .ToPagedAsync(req);
    }

    public async Task<List<Lookup>> LookupAsync(long? customerId = null) => await ReadAsync(db => db.CostEstimates.AsNoTracking()
        .Where(e => customerId == null || e.CustomerId == customerId).OrderByDescending(e => e.Id).Take(500)
        .Select(e => new Lookup(e.Id, e.Number, e.Description)).ToListAsync());

    public async Task<CostEstimate?> GetAsync(long id) => await ReadAsync(db => LoadAsync(db, id, tracking: false));

    private static async Task<CostEstimate?> LoadAsync(IAppDb db, long id, bool tracking)
    {
        var q = db.CostEstimates.Include(e => e.Customer).Include(e => e.Request)
            .Include(e => e.Components).Include(e => e.MaterialLines).ThenInclude(l => l.Pieces).Include(e => e.MaterialLines).ThenInclude(l => l.Material).ThenInclude(m => m!.Unit).Include(e => e.MaterialLines).ThenInclude(l => l.Remnant)
            .Include(e => e.MachineLines).ThenInclude(l => l.Machine).Include(e => e.LaborLines).ThenInclude(l => l.Employee).AsSplitQuery();
        var e = tracking ? await q.FirstOrDefaultAsync(x => x.Id == id) : await q.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e != null && !tracking) e.MaterialLines = e.MaterialLines.OrderBy(l => l.LineNo).ThenBy(l => l.Id).ToList();
        return e;
    }

    /// <summary>New draft pre-filled from settings, the request and its approved design revision.</summary>
    public async Task<CostEstimate> NewDraftAsync(long customerId, long? requestId = null)
    {
        var s = await SettingsAsync();
        await using var db = Factory.Create();
        var e = new CostEstimate
        {
            CustomerId = customerId, RequestId = requestId, Date = Now.Date, Quantity = 1, Status = EstimateStatus.Draft,
            OverheadMethod = s.OverheadMethod, OverheadRate = s.OverheadRate, ScrapAllowancePercent = s.DefaultScrapAllowancePercent,
            TargetMarginPercent = s.DefaultMarginPercent, MinimumMarginPercent = s.MinimumMarginPercent, DesignRate = s.DefaultLaborRate, SetupRate = s.DefaultLaborRate
        };
        if (requestId is { } rid)
        {
            var req = await db.CustomerRequests.AsNoTracking().Include(r => r.Items).ThenInclude(i => i.Material).FirstOrDefaultAsync(r => r.Id == rid) ?? throw new DomainException("Err.NotFound");
            e.CustomerId = req.CustomerId;
            e.Quantity = req.Quantity;
            e.Description = req.Description;
            var rev = await db.DesignRevisions.AsNoTracking().Include(d => d.Material).Where(d => d.RequestId == rid && d.Status == RevisionStatus.Approved).FirstOrDefaultAsync();
            if (rev != null) e.DesignRevisionId = rev.Id;
            var designPieceUsed = false;
            EstimateMaterialLine FromItem(Material material, decimal qtyPerUnit)
            {
                var sheet = material.Length > 0 && material.Width > 0 && material.Kind == MaterialKind.RawMaterial;
                var line = new EstimateMaterialLine
                {
                    MaterialId = material.Id, Material = material, Category = ComponentRules.CategoryOf(material.Kind), Source = ComponentRules.DefaultSource(material.Kind),
                    SheetBased = sheet, SheetLength = material.Length, SheetWidth = material.Width, Unit = material.Unit?.Code,
                    UnitCost = material.AverageCost > 0 ? material.AverageCost : material.PurchaseCost
                };
                // the approved design's outline is laid out on its main sheet material
                if (sheet && !designPieceUsed && rev != null && rev.Width > 0 && rev.Height > 0 && (rev.MaterialId == null || rev.MaterialId == material.Id))
                {
                    line.Pieces.Add(new EstimatePiece { Name = rev.RevisionLabel, Length = rev.Width, Width = rev.Height, QuantityPerUnit = 1 });
                    designPieceUsed = true;
                }
                else if (sheet) line.SheetsOverride = Math.Max(1, Math.Ceiling(qtyPerUnit * req.Quantity));
                else line.QuantityPerUnit = qtyPerUnit;
                return line;
            }
            foreach (var item in req.Items.OrderBy(i => i.LineNo))
            {
                if (item.Material != null) e.MaterialLines.Add(FromItem(item.Material, item.Quantity));
                else
                    e.MaterialLines.Add(new EstimateMaterialLine
                    {
                        Category = item.Category, Source = ComponentRules.DefaultSource(item.Category),
                        Description = item.Description, Unit = item.Unit, SheetBased = false, QuantityPerUnit = item.Quantity
                    });
            }
            if (!e.MaterialLines.Any(l => l.MaterialId != null && l.MaterialId == rev?.MaterialId) && rev?.Material != null)
                e.MaterialLines.Insert(0, FromItem(rev.Material, 1));
            var lineNo = 1;
            foreach (var l in e.MaterialLines) l.LineNo = lineNo++;
            if (rev is { EstimatedMachineMinutes: > 0 })
            {
                var machine = await db.Machines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Id).FirstOrDefaultAsync();
                if (machine != null)
                {
                    var rate = machine.CalculateRate();
                    e.MachineLines.Add(new EstimateMachineLine
                    {
                        MachineId = machine.Id, Machine = machine, Operation = rev.EngravingAreaCm2 > 0 && rev.CuttingLengthM == 0 ? OperationType.Engraving : OperationType.Cutting,
                        MinutesPerUnit = rev.EstimatedMachineMinutes, HourlyRate = rate.EffectiveRateExcludingMaintenance, MaintenanceRate = rate.EffectiveMaintenanceRate
                    });
                }
            }
        }
        e.Customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == e.CustomerId);
        EstimateBuilder.EnsureComponents(e);
        return e;
    }

    public async Task<Dictionary<long, MachineRateBreakdown>> MachineRatesAsync() => await ReadAsync(async db =>
        (await db.Machines.AsNoTracking().ToListAsync()).ToDictionary(m => m.Id, m => m.CalculateRate()));

    /// <summary>Recalculate in memory (for live editing). Draft estimates pick up current machine rates.</summary>
    public async Task<EstimateResult> CalculateAsync(CostEstimate e)
    {
        var s = await SettingsAsync();
        return EstimateBuilder.Compute(e, s.DecimalPlaces, e.Status == EstimateStatus.Draft ? await MachineRatesAsync() : null);
    }

    public async Task<long> SaveAsync(CostEstimate input)
    {
        if (input.CustomerId == 0) throw new DomainException("Err.Required", "Customer");
        Validation.Required(input.Description, "Description");
        if (input.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        await ValidateLinesAsync(input);
        if (input.MachineLines.Any(l => l.MachineId == 0)) throw new DomainException("Err.Required", "Machine");
        if (input.TargetMarginPercent >= 100 || input.MinimumMarginPercent >= 100) throw new DomainException("Err.MarginBelow100");
        Demand(AppModule.Estimates, input.Id == 0 ? Permission.Create : Permission.Edit);
        await CalculateAsync(input);
        return await TxAsync(async db =>
        {
            CostEstimate e;
            if (input.Id == 0)
            {
                e = new CostEstimate { Number = await Numbering.NextAsync(db, SequenceKey.Estimate) };
                db.CostEstimates.Add(e);
                await RequestService.AdvanceStatusAsync(db, input.RequestId, RequestStatus.Estimating);
            }
            else
            {
                e = await LoadAsync(db, input.Id, tracking: true) ?? throw new DomainException("Err.NotFound");
                if (e.Status == EstimateStatus.Final) throw new DomainException("Err.EstimateFinal");
                db.EstimateComponentLines.RemoveRange(e.Components);
                foreach (var ml in e.MaterialLines) db.EstimatePieces.RemoveRange(ml.Pieces);
                db.EstimateMaterialLines.RemoveRange(e.MaterialLines);
                db.EstimateMachineLines.RemoveRange(e.MachineLines);
                db.EstimateLaborLines.RemoveRange(e.LaborLines);
                e.Components = new(); e.MaterialLines = new(); e.MachineLines = new(); e.LaborLines = new();
            }
            e.CustomerId = input.CustomerId; e.RequestId = input.RequestId; e.DesignRevisionId = input.DesignRevisionId; e.Date = input.Date.Date; e.Description = input.Description.Trim();
            e.Quantity = input.Quantity; e.DesignHours = input.DesignHours; e.DesignRate = input.DesignRate; e.SetupHours = input.SetupHours; e.SetupRate = input.SetupRate;
            e.FinishingPerUnit = input.FinishingPerUnit; e.PackagingPerUnit = input.PackagingPerUnit; e.ConsumablesPerUnit = input.ConsumablesPerUnit;
            e.OverheadMethod = input.OverheadMethod; e.OverheadRate = input.OverheadRate; e.ScrapAllowancePercent = input.ScrapAllowancePercent; e.ReworkAllowancePercent = input.ReworkAllowancePercent;
            e.TargetMarginPercent = input.TargetMarginPercent; e.MinimumMarginPercent = input.MinimumMarginPercent; e.SellingPrice = input.SellingPrice;
            e.DirectCost = input.DirectCost; e.TotalCost = input.TotalCost; e.UnitCost = input.UnitCost; e.SuggestedPrice = input.SuggestedPrice; e.MinimumPrice = input.MinimumPrice;
            e.TotalMachineHours = input.TotalMachineHours; e.Notes = input.Notes.Norm();
            foreach (var c in input.Components)
                e.Components.Add(new EstimateComponentLine { Component = c.Component, Mode = c.Mode, ManualAmount = c.ManualAmount, CalculatedAmount = c.CalculatedAmount, Amount = c.Amount, Notes = c.Notes });
            var no = 1;
            foreach (var l in input.MaterialLines)
            {
                var nl = new EstimateMaterialLine
                {
                    LineNo = no++, Category = l.Category, Source = l.Source, RemnantId = l.Source == ComponentSource.Remnant ? l.RemnantId : null,
                    Description = l.Description.Norm(), Unit = l.Unit.Norm(),
                    MaterialId = l.MaterialId, SheetBased = l.SheetBased, SheetLength = l.SheetLength, SheetWidth = l.SheetWidth, Spacing = l.Spacing, NestingEfficiency = l.NestingEfficiency,
                    ChargeFullSheets = l.ChargeFullSheets, SheetsOverride = l.SheetsOverride, QuantityPerUnit = l.QuantityPerUnit, UnitCost = l.UnitCost, SheetsRequired = l.SheetsRequired,
                    UtilizationPercent = l.UtilizationPercent, WasteArea = l.WasteArea, TotalQuantity = l.TotalQuantity, Cost = l.Cost
                };
                foreach (var p in l.Pieces) nl.Pieces.Add(new EstimatePiece { Name = p.Name, Length = p.Length, Width = p.Width, QuantityPerUnit = p.QuantityPerUnit });
                e.MaterialLines.Add(nl);
            }
            foreach (var m in input.MachineLines)
                e.MachineLines.Add(new EstimateMachineLine { MachineId = m.MachineId, Operation = m.Operation, MinutesPerUnit = m.MinutesPerUnit, Hours = m.Hours, HourlyRate = m.HourlyRate, MaintenanceRate = m.MaintenanceRate, MachineCost = m.MachineCost, MaintenanceCost = m.MaintenanceCost });
            foreach (var l in input.LaborLines)
                e.LaborLines.Add(new EstimateLaborLine { EmployeeId = l.EmployeeId, Operation = l.Operation, MinutesPerUnit = l.MinutesPerUnit, Hours = l.Hours, HourlyRate = l.HourlyRate, Cost = l.Cost });
            await db.SaveChangesAsync();
            return e.Id;
        });
    }

    /// <summary>Checks every component line: item, source and category must fit together, quantities and costs non-negative.</summary>
    private async Task ValidateLinesAsync(CostEstimate input)
    {
        var ids = input.MaterialLines.Where(l => l.MaterialId != null).Select(l => l.MaterialId!.Value).Distinct().ToList();
        var kinds = await ReadAsync(db => db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Kind));
        foreach (var l in input.MaterialLines)
        {
            MaterialKind? kind = l.MaterialId is { } mid ? kinds.TryGetValue(mid, out var k) ? k : throw new DomainException("Err.NotFound") : null;
            if (kind != null && l.Source != ComponentSource.ManualCost) l.Category = ComponentRules.CategoryOf(kind.Value);
            ComponentRules.Validate(l.Source, kind, l.Category, l.Description);
            if (l.Source == ComponentSource.Remnant && l.RemnantId == null) throw new DomainException("Err.Required", "Remnant");
            if (l.UnitCost < 0 || l.QuantityPerUnit < 0 || l.SheetsOverride < 0) throw new DomainException("Err.NegativeValue");
        }
    }

    /// <summary>Locks the estimate so its figures can be quoted and later compared with actual cost.</summary>
    public async Task FinalizeAsync(long id)
    {
        Demand(AppModule.Estimates, Permission.Approve);
        await TxAsync(async db =>
        {
            var e = await db.CostEstimates.FirstOrDefaultAsync(x => x.Id == id) ?? throw new DomainException("Err.NotFound");
            if (e.TotalCost <= 0) throw new DomainException("Err.EstimateEmpty");
            e.Status = EstimateStatus.Final;
            Audit(db, AuditAction.Approved, nameof(CostEstimate), id, e.Number);
        });
    }

    /// <summary>Creates an editable copy of an estimate (e.g. to re-estimate a finalised one).</summary>
    public async Task<long> DuplicateAsync(long id)
    {
        var e = await GetAsync(id) ?? throw new DomainException("Err.NotFound");
        e.Id = 0; e.Number = ""; e.Status = EstimateStatus.Draft; e.Date = Now.Date;
        return await SaveAsync(e);
    }

    public async Task DeleteAsync(long id)
    {
        Demand(AppModule.Estimates, Permission.Delete);
        await using var db = Factory.Create();
        var e = await LoadAsync(db, id, tracking: true) ?? throw new DomainException("Err.NotFound");
        if (e.Status == EstimateStatus.Final || await db.Quotations.AnyAsync(q => q.EstimateId == id) || await db.Jobs.AnyAsync(j => j.EstimateId == id))
            throw new DomainException("Err.InUseCannotDelete");
        db.CostEstimates.Remove(e);
        await Validation.SaveDeleteAsync(db);
    }
}
