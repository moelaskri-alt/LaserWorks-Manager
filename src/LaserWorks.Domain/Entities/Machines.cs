using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class Machine : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? MachineType { get; set; }
    /// <summary>Tube power in watts (e.g. 100 W CO2 tube).</summary>
    public decimal LaserPowerWatts { get; set; }
    /// <summary>Total electrical draw of the machine incl. chiller/extraction, kW.</summary>
    public decimal ElectricalLoadKw { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal UsefulLifeYears { get; set; } = 5;
    public decimal ResidualValue { get; set; }
    public decimal AnnualWorkingHours { get; set; } = 2000;
    public decimal ElectricityPricePerKwh { get; set; }
    public decimal MaintenanceCostPerYear { get; set; }
    /// <summary>Operator cost built into the machine rate. Keep 0 when operator labor is recorded separately.</summary>
    public decimal OperatorCostPerHour { get; set; }
    public decimal OtherOverheadPerYear { get; set; }
    public bool UseHourlyCostOverride { get; set; }
    public decimal HourlyCostOverride { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public MachineRateBreakdown CalculateRate() => MachineCostCalculator.Calculate(ToInput());

    public MachineRateInput ToInput() => new(PurchaseCost, UsefulLifeYears, ResidualValue, AnnualWorkingHours, ElectricalLoadKw,
        ElectricityPricePerKwh, MaintenanceCostPerYear, OperatorCostPerHour, OtherOverheadPerYear, UseHourlyCostOverride, HourlyCostOverride);
}

public class LaserParameter : AuditableEntity
{
    public long MaterialId { get; set; }
    public Material? Material { get; set; }
    public long? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public decimal Thickness { get; set; }
    public OperationType OperationType { get; set; } = OperationType.Cutting;
    public decimal PowerPercent { get; set; }
    public decimal SpeedMmPerSec { get; set; }
    public decimal? FrequencyHz { get; set; }
    public int PassCount { get; set; } = 1;
    public string? Notes { get; set; }
}
