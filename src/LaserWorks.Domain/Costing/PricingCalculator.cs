using LaserWorks.Domain.Common;

namespace LaserWorks.Domain.Costing;

public sealed record PriceAnalysis(decimal Cost, decimal Price, decimal Profit, decimal MarginPercent, decimal MarkupPercent);

/// <summary>
/// Margin is profit as a percentage of the selling price; markup is profit as a percentage of cost.
///   price from margin = cost / (1 − margin%)
///   price from markup = cost × (1 + markup%)
/// </summary>
public static class PricingCalculator
{
    public static decimal PriceFromMargin(decimal cost, decimal marginPercent, int decimals = 2)
    {
        if (marginPercent >= 100m) throw new DomainException("Err.MarginBelow100");
        if (marginPercent < -100m) throw new DomainException("Err.MarginRange");
        return Money.Round(cost / (1m - marginPercent / 100m), decimals);
    }

    public static decimal PriceFromMarkup(decimal cost, decimal markupPercent, int decimals = 2)
        => Money.Round(cost * (1m + markupPercent / 100m), decimals);

    public static decimal MarginFromMarkup(decimal markupPercent) => markupPercent <= -100m ? 0 : Money.Round(markupPercent / (100m + markupPercent) * 100m);

    public static PriceAnalysis Analyze(decimal cost, decimal price)
    {
        var profit = price - cost;
        return new PriceAnalysis(cost, price, profit, Money.Percent(profit, price), Money.Percent(profit, cost));
    }
}
