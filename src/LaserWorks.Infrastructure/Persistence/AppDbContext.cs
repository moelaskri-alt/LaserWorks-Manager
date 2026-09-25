using System.Text.Json;
using LaserWorks.Application.Abstractions;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LaserWorks.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDb
{
    private readonly ICurrentUser? _user;
    private readonly IClock _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? user = null, IClock? clock = null) : base(options)
    {
        _user = user;
        _clock = clock ?? new SystemClock();
    }

    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<LaserParameter> LaserParameters => Set<LaserParameter>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<Remnant> Remnants => Set<Remnant>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<CustomerRequest> CustomerRequests => Set<CustomerRequest>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<DesignRevision> DesignRevisions => Set<DesignRevision>();
    public DbSet<CostEstimate> CostEstimates => Set<CostEstimate>();
    public DbSet<EstimateComponentLine> EstimateComponentLines => Set<EstimateComponentLine>();
    public DbSet<EstimateMaterialLine> EstimateMaterialLines => Set<EstimateMaterialLine>();
    public DbSet<EstimatePiece> EstimatePieces => Set<EstimatePiece>();
    public DbSet<EstimateMachineLine> EstimateMachineLines => Set<EstimateMachineLine>();
    public DbSet<EstimateLaborLine> EstimateLaborLines => Set<EstimateLaborLine>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobCostEntry> JobCostEntries => Set<JobCostEntry>();
    public DbSet<JobOperation> JobOperations => Set<JobOperation>();
    public DbSet<ScrapRecord> ScrapRecords => Set<ScrapRecord>();
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 6);
        b.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        // Safe deletion: nothing cascades unless it is a document's own lines.
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        m.Entity<CompanySettings>(e => { e.Property(x => x.CompanyName).IsRequired().HasMaxLength(200); e.Property(x => x.Address).HasMaxLength(1000); });
        m.Entity<NumberSequence>().HasIndex(x => x.Key).IsUnique();
        m.Entity<FiscalYear>().HasMany(x => x.Periods).WithOne(x => x.FiscalYear).HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<FiscalPeriod>().HasIndex(x => new { x.StartDate, x.EndDate });
        m.Entity<Warehouse>().HasIndex(x => x.Code).IsUnique();
        m.Entity<UnitOfMeasure>().HasIndex(x => x.Code).IsUnique();
        m.Entity<MaterialCategory>().HasIndex(x => x.Name).IsUnique();
        m.Entity<CostCenter>().HasIndex(x => x.Code).IsUnique();
        m.Entity<ExpenseCategory>().HasIndex(x => x.Name).IsUnique();
        m.Entity<User>().HasIndex(x => x.Username).IsUnique();
        m.Entity<RolePermission>().HasIndex(x => new { x.Role, x.Module }).IsUnique();
        m.Entity<AuditLog>(e => { e.HasIndex(x => x.Timestamp); e.HasIndex(x => new { x.Entity, x.RecordId }); e.Property(x => x.Details).HasMaxLength(4000); });

        m.Entity<Customer>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.Name); e.Property(x => x.Name).IsRequired().HasMaxLength(200); e.Property(x => x.Notes).HasMaxLength(4000); });
        m.Entity<Supplier>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.Name); e.Property(x => x.Name).IsRequired().HasMaxLength(200); });
        m.Entity<Employee>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).IsRequired().HasMaxLength(200); });
        m.Entity<Machine>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).IsRequired().HasMaxLength(200); });
        m.Entity<LaserParameter>().HasIndex(x => new { x.MaterialId, x.Thickness });

        m.Entity<Material>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Ignore(x => x.SheetArea);
        });
        m.Entity<StockBalance>().HasIndex(x => new { x.MaterialId, x.WarehouseId }).IsUnique();
        m.Entity<Remnant>(e => { e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.MaterialId, x.Status }); e.Ignore(x => x.Area); });
        m.Entity<InventoryTransaction>(e =>
        {
            e.HasIndex(x => x.Number);
            e.HasIndex(x => new { x.MaterialId, x.Date });
            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.SourceType, x.SourceId });
        });

        m.Entity<CustomerRequest>(e => { e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.CustomerId); e.HasIndex(x => x.Status); e.Property(x => x.Description).IsRequired().HasMaxLength(4000); e.Property(x => x.Notes).HasMaxLength(4000); });
        m.Entity<Attachment>().HasIndex(x => new { x.OwnerType, x.OwnerId });
        m.Entity<DesignRevision>(e => { e.HasIndex(x => new { x.RequestId, x.RevisionNo }).IsUnique(); e.Property(x => x.Notes).HasMaxLength(4000); });

        m.Entity<CostEstimate>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Components).WithOne().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MaterialLines).WithOne().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MachineLines).WithOne().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.LaborLines).WithOne().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Description).HasMaxLength(4000);
        });
        m.Entity<EstimateMaterialLine>().HasMany(x => x.Pieces).WithOne().HasForeignKey(x => x.MaterialLineId).OnDelete(DeleteBehavior.Cascade);

        m.Entity<Quotation>(e =>
        {
            e.HasIndex(x => new { x.Number, x.VersionNo }).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Ignore(x => x.NetAmount);
        });

        m.Entity<Job>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.OrderDate);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.HasMany(x => x.Operations).WithOne(x => x.Job).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<JobCostEntry>(e => { e.HasIndex(x => new { x.JobId, x.Component }); e.HasIndex(x => x.Date); e.HasIndex(x => new { x.SourceType, x.SourceId }); });
        m.Entity<ScrapRecord>(e => { e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.JobId); });
        m.Entity<QualityCheck>().HasIndex(x => x.JobId);

        m.Entity<SalesInvoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.JobId);
            e.Ignore(x => x.Balance);
            e.HasMany(x => x.Lines).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<SalesReturn>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne(x => x.SalesReturn).HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<CustomerPayment>(e => { e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.CustomerId); e.HasIndex(x => x.Date); });

        m.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne(x => x.PurchaseOrder).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<PurchaseReceipt>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne(x => x.Receipt).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<SupplierInvoice>(e => { e.HasIndex(x => x.Number).IsUnique(); e.Ignore(x => x.Balance); });
        m.Entity<SupplierPayment>().HasIndex(x => x.Number).IsUnique();
        m.Entity<PurchaseReturn>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Expense>(e => { e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.Date); e.HasIndex(x => x.JobId); e.Ignore(x => x.Total); });

        m.Entity<Account>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.SystemKey).IsUnique();
            e.Ignore(x => x.IsDebitNormal);
        });
        m.Entity<JournalEntry>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.Ignore(x => x.TotalDebit);
            e.Ignore(x => x.TotalCredit);
            e.HasMany(x => x.Lines).WithOne(x => x.JournalEntry).HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Description).HasMaxLength(1000);
        });
        m.Entity<JournalLine>(e => { e.HasIndex(x => x.AccountId); e.HasIndex(x => x.JobId); e.HasIndex(x => x.CustomerId); e.HasIndex(x => x.SupplierId); });

        base.OnModelCreating(m);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var now = _clock.Now;
        var user = _user?.Username ?? "system";
        EnforceIntegrityRules();

        var pendingAudit = new List<(EntityEntry Entry, AuditAction Action, string? Details)>();
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditableEntity a)
            {
                if (entry.State == EntityState.Added)
                {
                    a.CreatedAt = now;
                    a.CreatedBy ??= user;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAt = now;
                    a.UpdatedBy = user;
                }
            }
            if (entry.Entity is InventoryTransaction it && entry.State == EntityState.Added)
            {
                it.CreatedAt = now;
                it.CreatedBy ??= user;
            }
            if (entry.Entity is JobCostEntry jc && entry.State == EntityState.Added)
            {
                jc.CreatedAt = now;
                jc.CreatedBy ??= user;
            }
            if (entry.Entity is JournalEntry je && entry.State == EntityState.Added)
            {
                je.CreatedAt = now;
                je.CreatedBy ??= user;
            }

            if (entry.Entity is IAudited && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var action = entry.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Updated
                };
                string? details = null;
                if (entry.State == EntityState.Modified)
                {
                    var changed = entry.Properties
                        .Where(p => p.IsModified && p.Metadata.Name is not (nameof(AuditableEntity.UpdatedAt) or nameof(AuditableEntity.UpdatedBy)))
                        .Where(p => !Equals(p.OriginalValue, p.CurrentValue))
                        .Take(20)
                        .ToDictionary(p => p.Metadata.Name, p => $"{p.OriginalValue} → {p.CurrentValue}");
                    if (changed.Count == 0) continue;
                    details = JsonSerializer.Serialize(changed, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    if (details.Length > 3900) details = details[..3900];
                }
                pendingAudit.Add((entry, action, details));
            }
        }

        var deletedIds = pendingAudit.Where(p => p.Action == AuditAction.Deleted)
            .ToDictionary(p => p.Entry, p => (long?)((Entity)p.Entry.Entity).Id);

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (pendingAudit.Count > 0)
        {
            foreach (var (entry, action, details) in pendingAudit)
            {
                AuditLogs.Add(new AuditLog
                {
                    Timestamp = now,
                    Username = user,
                    Action = action,
                    Entity = entry.Entity.GetType().Name,
                    RecordId = action == AuditAction.Deleted ? deletedIds[entry] : ((Entity)entry.Entity).Id,
                    Details = details
                });
            }
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        return result;
    }

    /// <summary>
    /// Guards that hold regardless of which service writes the data:
    ///  - append-only ledgers (inventory transactions, job cost entries, audit log) cannot be edited or deleted;
    ///  - posted journal entries cannot be edited or deleted (only marked as reversed);
    ///  - a posted journal entry must balance.
    /// </summary>
    private void EnforceIntegrityRules()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IImmutableRecord && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new DomainException("Err.ImmutableRecord", entry.Entity.GetType().Name);

            if (entry.Entity is JournalEntry je)
            {
                if (entry.State == EntityState.Deleted && (JournalStatus)entry.OriginalValues[nameof(JournalEntry.Status)]! != JournalStatus.Draft)
                    throw new DomainException("Err.PostedEntryImmutable", je.Number);
                if (entry.State == EntityState.Modified)
                {
                    var original = (JournalStatus)entry.OriginalValues[nameof(JournalEntry.Status)]!;
                    if (original != JournalStatus.Draft)
                    {
                        var allowed = new[] { nameof(JournalEntry.Status), nameof(JournalEntry.ReversedById) };
                        var illegal = entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) && !allowed.Contains(p.Metadata.Name)).ToList();
                        if (illegal.Count > 0 || (original == JournalStatus.Posted && je.Status != JournalStatus.Reversed && je.Status != JournalStatus.Posted) || original == JournalStatus.Reversed && je.Status != JournalStatus.Reversed)
                            throw new DomainException("Err.PostedEntryImmutable", je.Number);
                    }
                }
                if (entry.State is EntityState.Added or EntityState.Modified && je.Status == JournalStatus.Posted)
                {
                    var lines = ChangeTracker.Entries<JournalLine>()
                        .Where(l => l.State != EntityState.Deleted && (ReferenceEquals(l.Entity.JournalEntry, je) || (je.Id != 0 && l.Entity.JournalEntryId == je.Id)))
                        .Select(l => l.Entity).ToList();
                    if (entry.State == EntityState.Added || lines.Count > 0)
                    {
                        var dr = lines.Sum(l => l.Debit);
                        var cr = lines.Sum(l => l.Credit);
                        if (dr != cr || dr == 0) throw new DomainException("Err.UnbalancedEntry", dr, cr);
                        if (lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit != 0 && l.Credit != 0)))
                            throw new DomainException("Err.InvalidJournalLine");
                    }
                }
            }

            if (entry.Entity is JournalLine jl && entry.State is EntityState.Modified or EntityState.Deleted or EntityState.Added)
            {
                var parent = jl.JournalEntry ?? JournalEntries.Local.FirstOrDefault(x => x.Id == jl.JournalEntryId);
                JournalStatus? parentOriginal = null;
                if (parent != null)
                {
                    var pe = Entry(parent);
                    parentOriginal = pe.State == EntityState.Added ? JournalStatus.Draft : (JournalStatus)pe.OriginalValues[nameof(JournalEntry.Status)]!;
                }
                else if (jl.JournalEntryId != 0)
                {
                    parentOriginal = JournalEntries.AsNoTracking().Where(x => x.Id == jl.JournalEntryId).Select(x => (JournalStatus?)x.Status).FirstOrDefault();
                }
                if (parentOriginal is JournalStatus.Posted or JournalStatus.Reversed)
                    throw new DomainException("Err.PostedEntryImmutable", parent?.Number ?? jl.JournalEntryId.ToString());
            }
        }
    }
}
