using LaserWorks.Domain.Common;

namespace LaserWorks.Domain.Costing;

public sealed record StockPosition(decimal Quantity, decimal Value)
{
    public decimal AverageCost => Quantity > 0 ? Math.Round(Value / Quantity, 6, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>Moving weighted average cost. Receipts change the average; issues are valued at the current average.</summary>
public static class InventoryMath
{
    public const int CostDecimals = 6;

    /// <summary>Receipt at a known unit cost (purchase, return at original cost, remnant, opening).</summary>
    public static StockPosition Receive(StockPosition current, decimal qty, decimal unitCost)
    {
        if (qty < 0) throw new DomainException("Err.QuantityPositive");
        if (unitCost < 0) throw new DomainException("Err.NegativeValue");
        var value = current.Value + Money.Round(qty * unitCost);
        return new StockPosition(current.Quantity + qty, value);
    }

    /// <summary>Issue at the current average cost. Returns the new position and the issued value.</summary>
    public static (StockPosition Position, decimal IssuedValue, decimal UnitCost) Issue(StockPosition current, decimal qty, bool allowNegative = false)
    {
        if (qty < 0) throw new DomainException("Err.QuantityPositive");
        if (!allowNegative && qty > current.Quantity) throw new DomainException("Err.InsufficientStock", current.Quantity, qty);
        var avg = current.AverageCost;
        decimal value;
        if (qty == current.Quantity)
            value = current.Value; // clear out exactly so no rounding residue stays in stock
        else
            value = Money.Round(qty * avg);
        return (new StockPosition(current.Quantity - qty, current.Value - value), value, avg);
    }

    /// <summary>Issue at a specific cost (e.g. purchase return at original receipt cost).</summary>
    public static (StockPosition Position, decimal IssuedValue) IssueAtCost(StockPosition current, decimal qty, decimal unitCost)
    {
        if (qty > current.Quantity) throw new DomainException("Err.InsufficientStock", current.Quantity, qty);
        var value = Money.Round(qty * unitCost);
        if (qty == current.Quantity || value > current.Value) value = current.Value;
        return (new StockPosition(current.Quantity - qty, current.Value - value), value);
    }
}
