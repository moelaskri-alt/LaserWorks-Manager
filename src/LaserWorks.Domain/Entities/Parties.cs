using LaserWorks.Domain.Common;

namespace LaserWorks.Domain.Entities;

public class Customer : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public string? Notes { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Supplier : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Employee : AuditableEntity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public decimal HourlyCost { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
