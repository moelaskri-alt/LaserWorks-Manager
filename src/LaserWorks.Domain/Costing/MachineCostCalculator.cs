using LaserWorks.Domain.Common;

namespace LaserWorks.Domain.Costing;

public sealed record MachineRateInput(
    decimal PurchaseCost,
    decimal UsefulLifeYears,
    decimal ResidualValue,
    decimal AnnualWorkingHours,
    decimal ElectricalLoadKw,
    decimal ElectricityPricePerKwh,
    decimal MaintenanceCostPerYear,
    decimal OperatorCostPerHour,
    decimal OtherOverheadPerYear,
    bool UseOverride,
    decimal OverrideRate);

public sealed record MachineRateBreakdown(
    decimal DepreciationPerHour,
    decimal ElectricityPerHour,
    decimal MaintenancePerHour,
    decimal OperatorPerHour,
    decimal OverheadPerHour,
    decimal CalculatedRate,
    decimal EffectiveRate,
    bool IsOverridden)
{
    /// <summary>Portion of the effective rate attributed to maintenance (0 when overridden).</summary>
    public decimal EffectiveMaintenanceRate => IsOverridden ? 0 : MaintenancePerHour;

    /// <summary>Effective rate excluding maintenance, so both can be reported as separate cost components.</summary>
    public decimal EffectiveRateExcludingMaintenance => EffectiveRate - EffectiveMaintenanceRate;
}

/// <summary>
/// Machine hourly cost:
///   depreciation/h = (purchase cost − residual value) / (useful life years × annual working hours)
///   electricity/h  = electrical load kW × price per kWh
///   maintenance/h  = annual maintenance cost / annual working hours
///   operator/h     = operator cost per hour (0 if operators are costed separately as labor)
///   overhead/h     = other annual overhead / annual working hours
/// </summary>
public static class MachineCostCalculator
{
    public const int RateDecimals = 4;

    public static MachineRateBreakdown Calculate(MachineRateInput i)
    {
        if (i.PurchaseCost < 0 || i.ResidualValue < 0 || i.UsefulLifeYears < 0 || i.AnnualWorkingHours < 0)
            throw new DomainException("Err.NegativeValue");
        if (i.ResidualValue > i.PurchaseCost)
            throw new DomainException("Err.ResidualExceedsCost");

        var lifetimeHours = i.UsefulLifeYears * i.AnnualWorkingHours;
        var depreciation = lifetimeHours > 0 ? (i.PurchaseCost - i.ResidualValue) / lifetimeHours : 0m;
        var electricity = i.ElectricalLoadKw * i.ElectricityPricePerKwh;
        var maintenance = i.AnnualWorkingHours > 0 ? i.MaintenanceCostPerYear / i.AnnualWorkingHours : 0m;
        var overhead = i.AnnualWorkingHours > 0 ? i.OtherOverheadPerYear / i.AnnualWorkingHours : 0m;

        depreciation = R(depreciation);
        electricity = R(electricity);
        maintenance = R(maintenance);
        overhead = R(overhead);
        var op = R(i.OperatorCostPerHour);
        var calculated = depreciation + electricity + maintenance + op + overhead;
        var effective = i.UseOverride ? R(i.OverrideRate) : calculated;
        if (effective < 0) throw new DomainException("Err.NegativeValue");
        return new MachineRateBreakdown(depreciation, electricity, maintenance, op, overhead, calculated, effective, i.UseOverride);
    }

    private static decimal R(decimal v) => Math.Round(v, RateDecimals, MidpointRounding.AwayFromZero);
}
