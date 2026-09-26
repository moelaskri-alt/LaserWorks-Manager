using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

/// <summary>Cost estimate for a custom job. Stores inputs and a snapshot of calculated results.
/// Estimated costs are never mixed with actual costs; they live only here and in quotation/job snapshots.</summary>
public class CostEstimate : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? RequestId { get; set; }
    public CustomerRequest? Request { get; set; }
    public long? DesignRevisionId { get; set; }
    public DesignRevision? DesignRevision { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public EstimateStatus Status { get; set; } = EstimateStatus.Draft;

    // Parameters for automatically calculated components
    public decimal DesignHours { get; set; }
    public decimal DesignRate { get; set; }
    public decimal SetupHours { get; set; }
    public decimal SetupRate { get; set; }
    public decimal FinishingPerUnit { get; set; }
    public decimal PackagingPerUnit { get; set; }
    public decimal ConsumablesPerUnit { get; set; }
    public OverheadMethod OverheadMethod { get; set; }
    public decimal OverheadRate { get; set; }
    public decimal ScrapAllowancePercent { get; set; }
    /// <summary>Rework allowance as % of machine, maintenance and labor cost.</summary>
    public decimal ReworkAllowancePercent { get; set; }

    // Pricing
    public decimal TargetMarginPercent { get; set; }
    public decimal MinimumMarginPercent { get; set; }
    public decimal SellingPrice { get; set; }

    // Snapshot of results
    public decimal DirectCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public decimal SuggestedPrice { get; set; }
    public decimal MinimumPrice { get; set; }
    public decimal TotalMachineHours { get; set; }
    public string? Notes { get; set; }

    public List<EstimateComponentLine> Components { get; set; } = new();
    public List<EstimateMaterialLine> MaterialLines { get; set; } = new();
    public List<EstimateMachineLine> MachineLines { get; set; } = new();
    public List<EstimateLaborLine> LaborLines { get; set; } = new();

    public decimal ComponentAmount(CostComponent c) => Components.Where(x => x.Component == c).Sum(x => x.Amount);
}

public class EstimateComponentLine : Entity
{
    public long EstimateId { get; set; }
    public CostComponent Component { get; set; }
    public ComponentMode Mode { get; set; }
    public decimal ManualAmount { get; set; }
    public decimal CalculatedAmount { get; set; }
    /// <summary>Effective amount (manual when Mode = Manual, otherwise calculated).</summary>
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// One component line of an estimate: a sheet material laid out on sheets, a quantity-based item (purchased component,
/// consumable, packaging), a remnant, an external service or a manual cost. An estimate has 0..N lines.
/// </summary>
public class EstimateMaterialLine : Entity
{
    public long EstimateId { get; set; }
    public int LineNo { get; set; }
    public ComponentCategory Category { get; set; } = ComponentCategory.RawMaterial;
    public ComponentSource Source { get; set; } = ComponentSource.Inventory;
    /// <summary>Stock item (null for services or manual costs without an item).</summary>
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    /// <summary>Remnant planned for this line (Source = Remnant).</summary>
    public long? RemnantId { get; set; }
    public Remnant? Remnant { get; set; }
    /// <summary>Line text (required when there is no item).</summary>
    public string? Description { get; set; }
    public string? Unit { get; set; }
    /// <summary>True: sheet based (pieces laid out on sheets). False: simple quantity × unit cost.</summary>
    public bool SheetBased { get; set; } = true;
    public decimal SheetLength { get; set; }
    public decimal SheetWidth { get; set; }
    /// <summary>Spacing/kerf between pieces in cm.</summary>
    public decimal Spacing { get; set; } = 0.5m;
    /// <summary>Expected nesting efficiency % used by the area method.</summary>
    public decimal NestingEfficiency { get; set; } = 85m;
    public bool ChargeFullSheets { get; set; } = true;
    /// <summary>Manual override of the required sheets (0 = use calculation).</summary>
    public decimal SheetsOverride { get; set; }
    /// <summary>Quantity per finished unit for non-sheet materials.</summary>
    public decimal QuantityPerUnit { get; set; }
    public decimal UnitCost { get; set; }
    // results
    public decimal SheetsRequired { get; set; }
    public decimal UtilizationPercent { get; set; }
    public decimal WasteArea { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal Cost { get; set; }
    public List<EstimatePiece> Pieces { get; set; } = new();
}

public class EstimatePiece : Entity
{
    public long MaterialLineId { get; set; }
    public string? Name { get; set; }
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    /// <summary>Pieces per finished unit.</summary>
    public decimal QuantityPerUnit { get; set; } = 1;
}

public class EstimateMachineLine : Entity
{
    public long EstimateId { get; set; }
    public long MachineId { get; set; }
    public Machine? Machine { get; set; }
    public OperationType Operation { get; set; } = OperationType.Cutting;
    public decimal MinutesPerUnit { get; set; }
    public decimal Hours { get; set; }
    /// <summary>Machine rate excluding the maintenance portion (snapshot).</summary>
    public decimal HourlyRate { get; set; }
    public decimal MaintenanceRate { get; set; }
    public decimal MachineCost { get; set; }
    public decimal MaintenanceCost { get; set; }
}

public class EstimateLaborLine : Entity
{
    public long EstimateId { get; set; }
    public long? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public OperationType Operation { get; set; } = OperationType.Assembly;
    public decimal MinutesPerUnit { get; set; }
    public decimal Hours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Cost { get; set; }
}
