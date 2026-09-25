using LaserWorks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LaserWorks.Application.Abstractions;

/// <summary>
/// Provider-agnostic unit of work. The Infrastructure layer implements it with SQLite; replacing the provider
/// does not require changing business logic.
/// </summary>
public interface IAppDb : IDisposable, IAsyncDisposable
{
    DbSet<CompanySettings> CompanySettings { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<FiscalYear> FiscalYears { get; }
    DbSet<FiscalPeriod> FiscalPeriods { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<UnitOfMeasure> Units { get; }
    DbSet<MaterialCategory> MaterialCategories { get; }
    DbSet<CostCenter> CostCenters { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<User> Users { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Employee> Employees { get; }
    DbSet<Machine> Machines { get; }
    DbSet<LaserParameter> LaserParameters { get; }
    DbSet<Material> Materials { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<Remnant> Remnants { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<CustomerRequest> CustomerRequests { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<DesignRevision> DesignRevisions { get; }
    DbSet<CostEstimate> CostEstimates { get; }
    DbSet<EstimateComponentLine> EstimateComponentLines { get; }
    DbSet<EstimateMaterialLine> EstimateMaterialLines { get; }
    DbSet<EstimatePiece> EstimatePieces { get; }
    DbSet<EstimateMachineLine> EstimateMachineLines { get; }
    DbSet<EstimateLaborLine> EstimateLaborLines { get; }
    DbSet<Quotation> Quotations { get; }
    DbSet<Job> Jobs { get; }
    DbSet<JobCostEntry> JobCostEntries { get; }
    DbSet<JobOperation> JobOperations { get; }
    DbSet<ScrapRecord> ScrapRecords { get; }
    DbSet<QualityCheck> QualityChecks { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesInvoiceLine> SalesInvoiceLines { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<SalesReturnLine> SalesReturnLines { get; }
    DbSet<CustomerPayment> CustomerPayments { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<PurchaseReceiptLine> PurchaseReceiptLines { get; }
    DbSet<SupplierInvoice> SupplierInvoices { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }
    DbSet<PurchaseReturn> PurchaseReturns { get; }
    DbSet<PurchaseReturnLine> PurchaseReturnLines { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<Account> Accounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalLine> JournalLines { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAppDbFactory
{
    IAppDb Create();
}

public interface ICurrentUser
{
    long? UserId { get; }
    string Username { get; }
    Domain.Enums.UserRole? Role { get; }
}

public interface IClock
{
    DateTime Now { get; }
    DateTime Today => Now.Date;
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

/// <summary>Filesystem locations used by the application.</summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabasePath { get; }
    string AttachmentsDirectory { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string ExportsDirectory { get; }
}

/// <summary>Stores attachment files outside the database (copied into the application data folder).</summary>
public interface IAttachmentStore
{
    Task<(string StoredPath, long Size)> SaveAsync(string sourceFile, CancellationToken ct = default);
    string Resolve(string storedPath);
    void Delete(string storedPath);
}

/// <summary>Clock that can be pinned to a date (used by demo-data generation and tests).</summary>
public sealed class MutableClock : IClock
{
    private DateTime? _fixed;
    public DateTime Now => _fixed ?? DateTime.Now;
    public void Set(DateTime value) => _fixed = value;
    public void Reset() => _fixed = null;
}
