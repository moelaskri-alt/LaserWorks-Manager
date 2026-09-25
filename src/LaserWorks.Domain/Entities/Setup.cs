using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

/// <summary>Single-row company and configuration record.</summary>
public class CompanySettings : AuditableEntity, IAudited
{
    public string CompanyName { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxNumber { get; set; }
    public string? LogoPath { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public string CurrencySymbol { get; set; } = "ر.س";
    public int DecimalPlaces { get; set; } = 2;
    public decimal DefaultTaxRate { get; set; } = 15m;
    public decimal DefaultMarginPercent { get; set; } = 30m;
    public decimal DefaultMarkupPercent { get; set; } = 40m;
    public decimal MinimumMarginPercent { get; set; } = 10m;
    public OverheadMethod OverheadMethod { get; set; } = OverheadMethod.PercentOfDirectCost;
    /// <summary>Percent of direct cost (PercentOfDirectCost) or amount per machine hour (PerMachineHour).</summary>
    public decimal OverheadRate { get; set; } = 10m;
    public decimal DefaultLaborRate { get; set; } = 25m;
    public decimal DefaultElectricityPricePerKwh { get; set; } = 0.18m;
    public decimal DefaultScrapAllowancePercent { get; set; } = 5m;
    public int QuotationValidityDays { get; set; } = 15;
    public string? DefaultPaymentTerms { get; set; }
    /// <summary>Only moving weighted average is supported; kept configurable for future methods.</summary>
    public string InventoryCostingMethod { get; set; } = "WeightedAverage";
    public string Language { get; set; } = "ar";
    public string Theme { get; set; } = "System";
    public bool SetupCompleted { get; set; }
    public long? DefaultWarehouseId { get; set; }
    public int LowMarginThresholdPercent { get; set; } = 15;
    public int HighVariancePercent { get; set; } = 15;
    public int HighScrapPercent { get; set; } = 8;
}

public class NumberSequence : Entity
{
    public SequenceKey Key { get; set; }
    public string Prefix { get; set; } = "";
    public long NextNumber { get; set; } = 1;
    public int Padding { get; set; } = 5;

    public string Format(long number) => $"{Prefix}{number.ToString().PadLeft(Padding, '0')}";
}

public class FiscalYear : Entity
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
    public List<FiscalPeriod> Periods { get; set; } = new();
}

public class FiscalPeriod : Entity
{
    public long FiscalYearId { get; set; }
    public FiscalYear? FiscalYear { get; set; }
    public int PeriodNo { get; set; }
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
}

public class Warehouse : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class UnitOfMeasure : Entity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsSystem { get; set; }
    /// <summary>True when the unit is a sheet/panel whose dimensions drive area-based costing.</summary>
    public bool IsSheet { get; set; }
}

public class MaterialCategory : Entity
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class CostCenter : Entity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ExpenseCategory : Entity
{
    public string Name { get; set; } = "";
    public long AccountId { get; set; }
    public Account? Account { get; set; }
    /// <summary>Job cost component used when an expense of this category is charged to a job.</summary>
    public CostComponent JobComponent { get; set; } = CostComponent.OtherDirect;
    public bool IsActive { get; set; } = true;
}
