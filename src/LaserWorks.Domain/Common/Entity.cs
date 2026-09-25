namespace LaserWorks.Domain.Common;

/// <summary>Base type for all persisted entities.</summary>
public abstract class Entity
{
    public long Id { get; set; }
}

/// <summary>Entity with creation/modification stamps.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Marker: create/update/delete of this entity is written to the audit trail automatically.</summary>
public interface IAudited
{
}

/// <summary>Marker: rows are append-only once saved (ledger style). Updates and deletes are rejected.</summary>
public interface IImmutableRecord
{
}

/// <summary>Business rule violation that should be shown to the user.</summary>
public class DomainException : Exception
{
    public string Code { get; }
    public object[] Args { get; }

    public DomainException(string code, params object[] args) : base(code)
    {
        Code = code;
        Args = args;
    }

    public override string Message => Args.Length == 0 ? Code : $"{Code}: {string.Join(", ", Args)}";
}

public static class Money
{
    /// <summary>Rounds a monetary amount using banker-safe away-from-zero rounding.</summary>
    public static decimal Round(decimal value, int decimals = 2) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    public static decimal SafeDivide(decimal numerator, decimal denominator) => denominator == 0 ? 0 : numerator / denominator;

    public static decimal Percent(decimal part, decimal whole) => whole == 0 ? 0 : Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);
}
