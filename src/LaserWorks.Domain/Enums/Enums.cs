namespace LaserWorks.Domain.Enums;

public enum UserRole { Administrator = 1, Manager = 2, Accountant = 3, Sales = 4, Production = 5, Storekeeper = 6 }

[Flags]
public enum Permission
{
    None = 0,
    View = 1,
    Create = 2,
    Edit = 4,
    Delete = 8,
    Post = 16,
    Approve = 32,
    Print = 64,
    Export = 128,
    All = View | Create | Edit | Delete | Post | Approve | Print | Export
}

public enum AppModule
{
    Dashboard = 1, Customers, Requests, Jobs, Design, Estimates, Quotations, Production, Inventory, Machines,
    Employees, Expenses, Sales, Purchases, Accounting, Profitability, Reports, Settings, Backup, Users
}

public enum AuditAction { Created = 1, Updated, Deleted, Posted, Approved, Reversed, Login, LoginFailed, Logout, Backup, Restore, StatusChanged }

public enum AccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5 }

public enum JournalStatus { Draft = 0, Posted = 1, Reversed = 2 }

public enum MaterialKind { RawMaterial = 1, Consumable = 2, FinishedGood = 3 }

public enum RequestStatus { New = 0, UnderReview, Designing, Estimating, Quoted, Approved, Rejected, ConvertedToJob }

public enum RevisionStatus { Draft = 0, UnderReview, Approved, Superseded, Rejected }

public enum EstimateStatus { Draft = 0, Final = 1 }

public enum QuotationStatus { Draft = 0, Sent, Approved, Rejected, Expired, Superseded }

public enum JobStatus { New = 0, Planned, InProduction, QualityCheck, Completed, Delivered, Invoiced, Closed, Cancelled }

public enum JobPriority { Low = 0, Normal, High, Urgent }

public enum OperationType { Design = 1, MaterialPreparation, Cutting, Engraving, Cleaning, Assembly, Finishing, Packaging }

public enum OperationStatus { Pending = 0, InProgress, Done }

public enum ScrapType { NormalScrap = 1, AbnormalScrap, Rework, Damage, MaterialWaste }

public enum QualityStatus { Pending = 0, Passed, Failed, ReworkRequired }

/// <summary>Cost components used for both estimates and actual job cost so they can be compared line by line.</summary>
public enum CostComponent
{
    Material = 1, Machine, Labor, Design, Setup, Finishing, Packaging, Consumables, Maintenance, Overhead, Scrap, Rework, OtherDirect
}

public enum ComponentMode { Auto = 0, Manual = 1 }

public enum OverheadMethod { PercentOfDirectCost = 0, PerMachineHour = 1 }

public enum InventoryTxType
{
    OpeningBalance = 1, PurchaseReceipt, PurchaseReturn, MaterialIssue, MaterialReturn, Transfer, Adjustment, Scrap,
    RemnantCreation, RemnantConsumption, RemnantAdjustment, SalesIssue, SalesReturn, FinishedGoodsReceipt
}

public enum RemnantStatus { Available = 0, Consumed, Scrapped }

public enum PurchaseOrderStatus { Draft = 0, Approved, PartiallyReceived, Received, Cancelled }

public enum DocumentStatus { Draft = 0, Posted = 1, Cancelled = 2 }

public enum PaymentMethod { Cash = 0, Bank = 1, OnCredit = 2 }

public enum InvoiceLineType { Job = 0, StockItem = 1, Service = 2 }

public enum AttachmentOwner { Request = 1, DesignRevision, Job, Quotation, Expense }

public enum SequenceKey
{
    Customer = 1, Supplier, Employee, Machine, Material, Request, Estimate, Quotation, Job, Invoice, SalesReturn,
    CustomerPayment, PurchaseOrder, PurchaseReceipt, SupplierInvoice, SupplierPayment, PurchaseReturn, Expense,
    Journal, InventoryTx, Remnant, Scrap
}
