using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class CustomerRequest : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime RequestDate { get; set; }
    public string Description { get; set; } = "";
    /// <summary>Free-form dimensions text, e.g. "60 x 40 cm".</summary>
    public string? Dimensions { get; set; }
    public decimal Quantity { get; set; } = 1;
    public DateTime? RequiredDate { get; set; }
    public string? Notes { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.New;
    /// <summary>Materials, purchased components, consumables, packaging and services the customer asked for (0..N lines).</summary>
    public List<RequestItem> Items { get; set; } = new();
}

/// <summary>One requested material / component line of a customer request.</summary>
public class RequestItem : Entity
{
    public long RequestId { get; set; }
    public CustomerRequest? Request { get; set; }
    public int LineNo { get; set; }
    public ComponentCategory Category { get; set; } = ComponentCategory.RawMaterial;
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public string? Description { get; set; }
    /// <summary>Quantity per finished unit.</summary>
    public decimal Quantity { get; set; } = 1;
    public string? Unit { get; set; }
    public string? Notes { get; set; }
}

public class Attachment : Entity
{
    public AttachmentOwner OwnerType { get; set; }
    public long OwnerId { get; set; }
    public string FileName { get; set; } = "";
    /// <summary>Path relative to the attachments root folder.</summary>
    public string StoredPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Kind { get; set; }
    public DateTime AddedAt { get; set; }
    public string? AddedBy { get; set; }
}

public class DesignRevision : AuditableEntity, IAudited
{
    public long RequestId { get; set; }
    public CustomerRequest? Request { get; set; }
    public int RevisionNo { get; set; }
    public string RevisionLabel { get; set; } = "";
    public DateTime Date { get; set; }
    public long? DesignerId { get; set; }
    public Employee? Designer { get; set; }
    /// <summary>Design width in cm.</summary>
    public decimal Width { get; set; }
    /// <summary>Design height in cm.</summary>
    public decimal Height { get; set; }
    /// <summary>Main sheet material the design is cut from (the full list of materials and components is on the estimate and the job).</summary>
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Thickness { get; set; }
    /// <summary>Total vector cutting length in metres.</summary>
    public decimal CuttingLengthM { get; set; }
    /// <summary>Engraving area in cm².</summary>
    public decimal EngravingAreaCm2 { get; set; }
    public decimal EstimatedMachineMinutes { get; set; }
    public string? Notes { get; set; }
    public RevisionStatus Status { get; set; } = RevisionStatus.Draft;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
}

public class Quotation : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public int VersionNo { get; set; } = 1;
    /// <summary>Id of the first version; all versions share it.</summary>
    public long? RootQuotationId { get; set; }
    public bool IsLatestVersion { get; set; } = true;
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? RequestId { get; set; }
    public CustomerRequest? Request { get; set; }
    public long? EstimateId { get; set; }
    public CostEstimate? Estimate { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal EstimatedCost { get; set; }
    /// <summary>Selling price before discount and tax (for the whole quantity).</summary>
    public decimal SellingPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public DateTime ValidUntil { get; set; }
    public int DeliveryDays { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;
    public DateTime? ApprovedAt { get; set; }

    public decimal NetAmount => SellingPrice - DiscountAmount;

    public void Recalculate(int decimals = 2)
    {
        var net = SellingPrice - DiscountAmount;
        if (net < 0) throw new DomainException("Err.DiscountExceedsPrice");
        TaxAmount = Money.Round(net * TaxRate / 100m, decimals);
        Total = Money.Round(net + TaxAmount, decimals);
    }
}

public class SalesInvoice : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? JobId { get; set; }
    public Job? Job { get; set; }
    public long? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ReturnedAmount { get; set; }
    public decimal CogsAmount { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
    public DateTime? PostedAt { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = new();

    public decimal Balance => Total - PaidAmount - ReturnedAmount;
}

public class SalesInvoiceLine : Entity
{
    public long InvoiceId { get; set; }
    public SalesInvoice? Invoice { get; set; }
    public InvoiceLineType LineType { get; set; }
    public long? JobId { get; set; }
    public Job? Job { get; set; }
    public long? MaterialId { get; set; }
    public Material? Material { get; set; }
    public long? WarehouseId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>Inventory/job cost per unit captured at posting (used for COGS reversal on returns).</summary>
    public decimal UnitCost { get; set; }
    public decimal CogsAmount { get; set; }
    public decimal ReturnedQuantity { get; set; }
}

public class SalesReturn : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long InvoiceId { get; set; }
    public SalesInvoice? Invoice { get; set; }
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? Reason { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal CogsReversed { get; set; }
    public PaymentMethod RefundMethod { get; set; } = PaymentMethod.OnCredit;
    public long? JournalEntryId { get; set; }
    public List<SalesReturnLine> Lines { get; set; } = new();
}

public class SalesReturnLine : Entity
{
    public long SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }
    public long InvoiceLineId { get; set; }
    public SalesInvoiceLine? InvoiceLine { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>Original unit cost from the invoice line — never the current average cost.</summary>
    public decimal UnitCost { get; set; }
    public decimal CogsAmount { get; set; }
    public bool Restock { get; set; }
    public long? WarehouseId { get; set; }
}

public class CustomerPayment : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long? InvoiceId { get; set; }
    public SalesInvoice? Invoice { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
}
