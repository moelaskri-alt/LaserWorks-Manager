using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Costing;

public sealed record VarianceLine(CostComponent Component, decimal Estimated, decimal Actual)
{
    /// <summary>Positive = over budget (unfavourable).</summary>
    public decimal Variance => Actual - Estimated;
    public decimal VariancePercent => Estimated == 0 ? (Actual == 0 ? 0 : 100m) : Money.Round(Variance / Estimated * 100m);
    public bool IsUnfavourable => Variance > 0;
}

public sealed record VarianceReport(IReadOnlyList<VarianceLine> Lines, decimal EstimatedTotal, decimal ActualTotal, CostComponent? MainDriver)
{
    public decimal Variance => ActualTotal - EstimatedTotal;
    public decimal VariancePercent => EstimatedTotal == 0 ? 0 : Money.Round(Variance / EstimatedTotal * 100m);
}

public static class VarianceCalculator
{
    public static VarianceReport Compare(IReadOnlyDictionary<CostComponent, decimal> estimated, IReadOnlyDictionary<CostComponent, decimal> actual)
    {
        var comps = estimated.Keys.Union(actual.Keys).Distinct().OrderBy(c => (int)c).ToList();
        var lines = comps.Select(c => new VarianceLine(c, estimated.GetValueOrDefault(c), actual.GetValueOrDefault(c)))
            .Where(l => l.Estimated != 0 || l.Actual != 0).ToList();
        var driver = lines.Where(l => l.Variance != 0).OrderByDescending(l => Math.Abs(l.Variance)).Select(l => (CostComponent?)l.Component).FirstOrDefault();
        return new VarianceReport(lines, lines.Sum(l => l.Estimated), lines.Sum(l => l.Actual), driver);
    }
}
