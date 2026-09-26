using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

/// <summary>The central business object: one custom order from approval to profitability.</summary>
public class Job : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? RequestId { get; set; }
    public CustomerRequest? Request { get; set; }
    public long? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }
    public long? EstimateId { get; set; }
    public CostEstimate? Estimate { get; set; }
    public long? DesignRevisionId { get; set; }
    public DesignRevision? DesignRevision { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public DateTime OrderDate { get; set; }
    public DateTime? DueDate { get; set; }
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    public JobStatus Status { get; set; } = JobStatus.New;
    /// <summary>Estimated total cost snapshot (from the estimate at conversion time).</summary>
    public decimal EstimatedCost { get; set; }
    /// <summary>Agreed selling price, net of discount, excluding tax.</summary>
    public decimal SellingPrice { get; set; }
    /// <summary>Cached sum of actual job cost entries (maintained by the costing service).</summary>
    public decimal ActualCost { get; set; }
    /// <summary>Cost transferred from WIP to COGS so far.</summary>
    public decimal CostTransferredToCogs { get; set; }
    /// <summary>Revenue recognised from posted invoices net of returns (excl. tax).</summary>
    public decimal InvoicedRevenue { get; set; }
    public long? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public long? OperatorId { get; set; }
    public Employee? Operator { get; set; }
    public string? Notes { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? DeliveryNote { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<JobOperation> Operations { get; set; } = new();
    /// <summary>Planned materials and components of this job (0..N lines); actual cost is linked to them.</summary>
    public List<JobComponent> Components { get; set; } = new();
}

/// <summary>
/// One material / component requirement of a job (the job-specific bill of materials).
/// Estimated figures come from the estimate line; actual quantities and cost come from the stock issues,
/// remnant consumptions and direct cost documents linked to the line.
/// </summary>
public class JobComponent : AuditableEntity, IAudited
{
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public int LineNo { get; set; }
    public ComponentCategory Category { get; set; } = ComponentCategory.RawMaterial;
    public ComponentSource Source { get; set; } = ComponentSource.Inventory;
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public long? RemnantId { get; set; }
    public Remnant? Remnant { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    /// <summary>Planned quantity for the whole job.</summary>
    public decimal PlannedQuantity { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal EstimatedCost { get; set; }
    public long? EstimateLineId { get; set; }
    public long? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Append-only actual cost ledger per job. Never contains estimated values.</summary>
public class JobCostEntry : Entity, IImmutableRecord
{
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public DateTime Date { get; set; }
    public CostComponent Component { get; set; }
    public string SourceType { get; set; } = "";
    public long? SourceId { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public long? MaterialId { get; set; }
    public long? MachineId { get; set; }
    public long? EmployeeId { get; set; }
    /// <summary>Component line this cost belongs to (materials, purchased components, services…).</summary>
    public long? JobComponentId { get; set; }
    public JobComponent? JobComponent { get; set; }
    public decimal Hours { get; set; }
    public long? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class JobOperation : AuditableEntity
{
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public int Sequence { get; set; }
    public OperationType OperationType { get; set; }
    public long? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public long? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public decimal PlannedHours { get; set; }
    /// <summary>Operator (labor) hours.</summary>
    public decimal ActualHours { get; set; }
    /// <summary>Machine running hours (may differ from operator hours).</summary>
    public decimal MachineHours { get; set; }
    public decimal Quantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public decimal ReworkQuantity { get; set; }
    public bool IsRework { get; set; }
    public string? Notes { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.Pending;
    public bool CostPosted { get; set; }
    public decimal MachineRate { get; set; }
    public decimal MaintenanceRate { get; set; }
    public decimal LaborRate { get; set; }
    public decimal MachineCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal LaborCost { get; set; }
}

public class ScrapRecord : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public ScrapType Type { get; set; }
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public long? OperationId { get; set; }
    public JobOperation? Operation { get; set; }
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public long? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public long? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Rework hours (for rework records).</summary>
    public decimal Hours { get; set; }
    public string Reason { get; set; } = "";
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
}

public class QualityCheck : AuditableEntity, IAudited
{
    public long JobId { get; set; }
    public Job? Job { get; set; }
    public DateTime Date { get; set; }
    public long? InspectorId { get; set; }
    public Employee? Inspector { get; set; }
    public decimal QuantityProduced { get; set; }
    public decimal QuantityAccepted { get; set; }
    public decimal QuantityRejected { get; set; }
    public decimal ReworkQuantity { get; set; }
    public QualityStatus Status { get; set; } = QualityStatus.Pending;
    public string? Notes { get; set; }
}
