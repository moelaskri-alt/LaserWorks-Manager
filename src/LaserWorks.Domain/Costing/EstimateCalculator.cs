using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Costing;

public sealed record MachineTimeInput(decimal MinutesPerUnit, decimal RateExcludingMaintenance, decimal MaintenanceRate);

public sealed record LaborTimeInput(decimal MinutesPerUnit, decimal HourlyRate);

public sealed record ItemCost(CostComponent Component, decimal Amount);

public sealed record EstimateInput
{
    public decimal Quantity { get; init; } = 1;
    /// <summary>Cost of each component line for the full quantity, with the cost component it feeds
    /// (raw material, purchased component, consumable, packaging, external service or other direct cost).</summary>
    public IReadOnlyList<ItemCost> ItemCosts { get; init; } = Array.Empty<ItemCost>();
    public IReadOnlyList<MachineTimeInput> Machines { get; init; } = Array.Empty<MachineTimeInput>();
    public IReadOnlyList<LaborTimeInput> Labor { get; init; } = Array.Empty<LaborTimeInput>();
    public decimal DesignHours { get; init; }
    public decimal DesignRate { get; init; }
    public decimal SetupHours { get; init; }
    public decimal SetupRate { get; init; }
    public decimal FinishingPerUnit { get; init; }
    public decimal PackagingPerUnit { get; init; }
    public decimal ConsumablesPerUnit { get; init; }
    public OverheadMethod OverheadMethod { get; init; }
    public decimal OverheadRate { get; init; }
    public decimal ScrapAllowancePercent { get; init; }
    /// <summary>Rework allowance % applied to machine, maintenance and labor cost.</summary>
    public decimal ReworkAllowancePercent { get; init; }
    /// <summary>Components the user estimated manually, with their amounts. They replace the calculated amount.</summary>
    public IReadOnlyDictionary<CostComponent, decimal> Manual { get; init; } = new Dictionary<CostComponent, decimal>();
    /// <summary>Optional what-if override of total machine hours (scales every machine line proportionally).</summary>
    public decimal? MachineHoursOverride { get; init; }
    public int Decimals { get; init; } = 2;
}

public sealed record EstimateResult(
    IReadOnlyDictionary<CostComponent, decimal> Calculated,
    IReadOnlyDictionary<CostComponent, decimal> Effective,
    decimal TotalMachineHours,
    decimal DirectCost,
    decimal Overhead,
    decimal TotalCost,
    decimal UnitCost)
{
    public decimal this[CostComponent c] => Effective.TryGetValue(c, out var v) ? v : 0;
}

/// <summary>
/// Estimated cost build-up. Every component can be automatic or manual.
///   Material, Purchased components, External services, Other direct = Σ component lines of that category
///   Consumables, Packaging = Σ lines of that category + per-unit amount × qty
///   Machine      = Σ hours × machine rate (excl. maintenance)
///   Maintenance  = Σ hours × maintenance rate
///   Labor        = Σ (minutes per unit × qty / 60) × hourly rate
///   Design       = design hours × design rate (fixed per job)
///   Setup        = setup hours × setup rate (fixed per job)
///   Finishing    = per-unit amount × qty
///   Scrap        = scrap allowance % × Material
///   Rework       = rework allowance % × (Machine + Maintenance + Labor)
///   Direct cost  = Σ all components above
///   Overhead     = overhead % × direct cost, or rate × machine hours
///   Total        = direct cost + overhead
/// </summary>
public static class EstimateCalculator
{
    public static readonly CostComponent[] EstimateComponents =
    {
        CostComponent.Material, CostComponent.PurchasedComponents, CostComponent.Consumables, CostComponent.Packaging, CostComponent.ExternalServices,
        CostComponent.Machine, CostComponent.Maintenance, CostComponent.Labor, CostComponent.Design, CostComponent.Setup, CostComponent.Finishing,
        CostComponent.OtherDirect, CostComponent.Scrap, CostComponent.Rework, CostComponent.Overhead
    };

    public static EstimateResult Calculate(EstimateInput i)
    {
        if (i.Quantity <= 0) throw new DomainException("Err.QuantityPositive");
        int d = i.Decimals;
        decimal R(decimal v) => Money.Round(v, d);

        var calc = new Dictionary<CostComponent, decimal>();
        var hoursPerLine = i.Machines.Select(m => m.MinutesPerUnit * i.Quantity / 60m).ToList();
        var totalHours = hoursPerLine.Sum();
        if (i.MachineHoursOverride is { } overrideHours)
        {
            if (overrideHours < 0) throw new DomainException("Err.NegativeValue");
            if (totalHours > 0)
                hoursPerLine = hoursPerLine.Select(h => h * overrideHours / totalHours).ToList();
            else if (i.Machines.Count > 0)
                hoursPerLine = i.Machines.Select((_, idx) => idx == 0 ? overrideHours : 0).ToList();
            totalHours = overrideHours;
        }

        decimal Items(CostComponent c) => i.ItemCosts.Where(x => x.Component == c).Sum(x => x.Amount);
        if (i.ItemCosts.Any(x => x.Amount < 0)) throw new DomainException("Err.NegativeValue");
        calc[CostComponent.Material] = R(Items(CostComponent.Material));
        calc[CostComponent.PurchasedComponents] = R(Items(CostComponent.PurchasedComponents));
        calc[CostComponent.ExternalServices] = R(Items(CostComponent.ExternalServices));
        calc[CostComponent.OtherDirect] = R(Items(CostComponent.OtherDirect));
        calc[CostComponent.Machine] = R(i.Machines.Select((m, idx) => hoursPerLine[idx] * m.RateExcludingMaintenance).Sum());
        calc[CostComponent.Maintenance] = R(i.Machines.Select((m, idx) => hoursPerLine[idx] * m.MaintenanceRate).Sum());
        calc[CostComponent.Labor] = R(i.Labor.Sum(l => l.MinutesPerUnit * i.Quantity / 60m * l.HourlyRate));
        calc[CostComponent.Design] = R(i.DesignHours * i.DesignRate);
        calc[CostComponent.Setup] = R(i.SetupHours * i.SetupRate);
        calc[CostComponent.Finishing] = R(i.FinishingPerUnit * i.Quantity);
        calc[CostComponent.Packaging] = R(Items(CostComponent.Packaging) + i.PackagingPerUnit * i.Quantity);
        calc[CostComponent.Consumables] = R(Items(CostComponent.Consumables) + i.ConsumablesPerUnit * i.Quantity);

        var eff = new Dictionary<CostComponent, decimal>();
        decimal Pick(CostComponent c) => i.Manual.TryGetValue(c, out var m) ? R(m) : calc[c];
        foreach (var c in new[] { CostComponent.Material, CostComponent.PurchasedComponents, CostComponent.Consumables, CostComponent.Packaging,
                     CostComponent.ExternalServices, CostComponent.Machine, CostComponent.Maintenance, CostComponent.Labor, CostComponent.Design,
                     CostComponent.Setup, CostComponent.Finishing, CostComponent.OtherDirect })
            eff[c] = Pick(c);

        // Scrap allowance is based on the effective material cost (manual or calculated).
        calc[CostComponent.Scrap] = R(eff[CostComponent.Material] * i.ScrapAllowancePercent / 100m);
        eff[CostComponent.Scrap] = Pick(CostComponent.Scrap);
        calc[CostComponent.Rework] = R((eff[CostComponent.Machine] + eff[CostComponent.Maintenance] + eff[CostComponent.Labor]) * i.ReworkAllowancePercent / 100m);
        eff[CostComponent.Rework] = Pick(CostComponent.Rework);

        var direct = eff.Values.Sum();
        calc[CostComponent.Overhead] = i.OverheadMethod == OverheadMethod.PercentOfDirectCost
            ? R(direct * i.OverheadRate / 100m)
            : R(totalHours * i.OverheadRate);
        eff[CostComponent.Overhead] = Pick(CostComponent.Overhead);

        if (eff.Values.Any(v => v < 0)) throw new DomainException("Err.NegativeValue");
        var total = direct + eff[CostComponent.Overhead];
        return new EstimateResult(calc, eff, Math.Round(totalHours, 4), direct, eff[CostComponent.Overhead], total, Money.Round(total / i.Quantity, 4));
    }
}
