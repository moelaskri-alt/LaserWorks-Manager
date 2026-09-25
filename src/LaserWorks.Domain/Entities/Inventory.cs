using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class Material : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? CategoryId { get; set; }
    public MaterialCategory? Category { get; set; }
    public MaterialKind Kind { get; set; } = MaterialKind.RawMaterial;
    /// <summary>Free-text material type e.g. MDF, Acrylic, Leather.</summary>
    public string? MaterialType { get; set; }
    /// <summary>Thickness in millimetres.</summary>
    public decimal Thickness { get; set; }
    /// <summary>Sheet/roll length in centimetres (0 when not applicable).</summary>
    public decimal Length { get; set; }
    /// <summary>Sheet/roll width in centimetres (0 when not applicable).</summary>
    public decimal Width { get; set; }
    public long UnitId { get; set; }
    public UnitOfMeasure? Unit { get; set; }
    /// <summary>Last purchase cost per unit (informational).</summary>
    public decimal PurchaseCost { get; set; }
    /// <summary>Moving weighted average cost per unit. Maintained only by inventory transactions.</summary>
    public decimal AverageCost { get; set; }
    /// <summary>Total quantity on hand across all warehouses (maintained by inventory transactions).</summary>
    public decimal QuantityOnHand { get; set; }
    /// <summary>Total inventory value (maintained by inventory transactions).</summary>
    public decimal StockValue { get; set; }
    /// <summary>Default selling price (finished goods only).</summary>
    public decimal SalesPrice { get; set; }
    public long? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>Sheet area in square centimetres, 0 if dimensions not set.</summary>
    public decimal SheetArea => Length * Width;
}

public class StockBalance : Entity
{
    public long MaterialId { get; set; }
    public Material? Material { get; set; }
    public long WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public decimal Quantity { get; set; }
}

public class Remnant : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public long MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Thickness { get; set; }
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public long WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public long? SourceJobId { get; set; }
    public Job? SourceJob { get; set; }
    public decimal Cost { get; set; }
    public DateTime Date { get; set; }
    public RemnantStatus Status { get; set; } = RemnantStatus.Available;
    public long? ConsumedJobId { get; set; }
    public Job? ConsumedJob { get; set; }
    public DateTime? ConsumedDate { get; set; }
    public string? Notes { get; set; }

    public decimal Area => Length * Width;
}

/// <summary>Append-only inventory ledger. Every stock/value movement is one row.</summary>
public class InventoryTransaction : Entity, IImmutableRecord
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public InventoryTxType Type { get; set; }
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public long? RemnantId { get; set; }
    public Remnant? Remnant { get; set; }
    public long WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    /// <summary>Signed quantity: positive = in, negative = out.</summary>
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>Signed value: positive = inventory value increase.</summary>
    public decimal TotalCost { get; set; }
    public decimal QuantityAfter { get; set; }
    public decimal AverageCostAfter { get; set; }
    public long? JobId { get; set; }
    public Job? Job { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public string? Reference { get; set; }
    public long? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
