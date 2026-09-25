using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class PurchaseOrder : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public class PurchaseOrderLine : Entity
{
    public long PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public long MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRate { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseReceipt : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public long WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public string? SupplierDeliveryRef { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public decimal Subtotal { get; set; }
    public bool IsInvoiced { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }
    public List<PurchaseReceiptLine> Lines { get; set; } = new();
}

public class PurchaseReceiptLine : Entity
{
    public long ReceiptId { get; set; }
    public PurchaseReceipt? Receipt { get; set; }
    public long? PurchaseOrderLineId { get; set; }
    public long MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
    public decimal ReturnedQuantity { get; set; }
}

public class SupplierInvoice : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public string? SupplierInvoiceNo { get; set; }
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long? ReceiptId { get; set; }
    public PurchaseReceipt? Receipt { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ReturnedAmount { get; set; }
    public string? Notes { get; set; }
    public long? JournalEntryId { get; set; }

    public decimal Balance => Total - PaidAmount - ReturnedAmount;
}

public class SupplierPayment : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long? SupplierInvoiceId { get; set; }
    public SupplierInvoice? SupplierInvoice { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Bank;
    public string? Reference { get; set; }
    public long? JournalEntryId { get; set; }
}

public class PurchaseReturn : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long ReceiptId { get; set; }
    public PurchaseReceipt? Receipt { get; set; }
    public string? Reason { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public long? JournalEntryId { get; set; }
    public List<PurchaseReturnLine> Lines { get; set; } = new();
}

public class PurchaseReturnLine : Entity
{
    public long PurchaseReturnId { get; set; }
    public long ReceiptLineId { get; set; }
    public PurchaseReceiptLine? ReceiptLine { get; set; }
    public long MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRate { get; set; }
}
