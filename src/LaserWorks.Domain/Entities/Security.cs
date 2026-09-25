using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Entities;

public class User : AuditableEntity, IAudited
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }
}

public class RolePermission : Entity
{
    public UserRole Role { get; set; }
    public AppModule Module { get; set; }
    public Permission Permissions { get; set; }
}

public class AuditLog : Entity, IImmutableRecord
{
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = "";
    public AuditAction Action { get; set; }
    public string Entity { get; set; } = "";
    public long? RecordId { get; set; }
    public string? Details { get; set; }
}
