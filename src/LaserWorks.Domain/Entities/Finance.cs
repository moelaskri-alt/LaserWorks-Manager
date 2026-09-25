using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class Expense : AuditableEntity, IAudited
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public long CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public long? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long? JobId { get; set; }
    public Job? Job { get; set; }
    public long? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public long? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }
    public string? Reference { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public long? JournalEntryId { get; set; }

    public decimal Total => Amount + TaxAmount;
}

public class Account : Entity
{
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public AccountType Type { get; set; }
    public long? ParentId { get; set; }
    public Account? Parent { get; set; }
    /// <summary>Header accounts group others and cannot receive postings.</summary>
    public bool IsPostable { get; set; } = true;
    public bool IsSystem { get; set; }
    /// <summary>Stable key used by posting rules (e.g. "AR", "Inventory").</summary>
    public string? SystemKey { get; set; }
    public bool IsCashOrBank { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Debit-normal accounts: assets and expenses.</summary>
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;
}

public class JournalEntry : Entity
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public JournalStatus Status { get; set; } = JournalStatus.Draft;
    public long? ReversalOfId { get; set; }
    public long? ReversedById { get; set; }
    public bool IsManual { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public List<JournalLine> Lines { get; set; } = new();

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
}

public class JournalLine : Entity
{
    public long JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public long AccountId { get; set; }
    public Account? Account { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
    public long? CostCenterId { get; set; }
    public long? JobId { get; set; }
    public long? CustomerId { get; set; }
    public long? SupplierId { get; set; }
    public long? MachineId { get; set; }
}
