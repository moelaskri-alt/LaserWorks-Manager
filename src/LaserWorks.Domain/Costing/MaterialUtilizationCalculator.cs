using LaserWorks.Domain.Common;

namespace LaserWorks.Domain.Costing;

public sealed record PieceSpec(string? Name, decimal Length, decimal Width, decimal Quantity);

public sealed record SheetLayoutInput(
    decimal SheetLength,
    decimal SheetWidth,
    IReadOnlyList<PieceSpec> Pieces,
    decimal Spacing = 0.5m,
    decimal NestingEfficiencyPercent = 85m,
    decimal CostPerSheet = 0m,
    bool ChargeFullSheets = true,
    decimal SheetsOverride = 0m);

public sealed record PieceFit(string? Name, decimal Length, decimal Width, decimal Quantity, int FitPerSheet, int SheetsIfAlone, bool Rotated);

public sealed record SheetLayoutResult(
    decimal SheetArea,
    decimal PiecesArea,
    int SheetsByArea,
    int SheetsByGrid,
    decimal SheetsRequired,
    decimal UtilizationPercent,
    decimal WasteArea,
    decimal WastePercent,
    decimal MaterialCost,
    decimal SheetsConsumedExact,
    IReadOnlyList<PieceFit> Fits);

/// <summary>
/// Practical manual utilisation calculator (not a nesting engine).
/// - Area method: sheets = ceil(total piece area / (sheet area × nesting efficiency)).
/// - Grid method: each piece size laid out alone in a grid (best orientation), summed — a safe upper bound.
/// Required sheets = the larger of the area method and the minimum physically possible, unless overridden.
/// The user can always override the sheet count after checking the layout manually.
/// </summary>
public static class MaterialUtilizationCalculator
{
    public static SheetLayoutResult Calculate(SheetLayoutInput input)
    {
        if (input.SheetLength <= 0 || input.SheetWidth <= 0) throw new DomainException("Err.SheetSizeRequired");
        if (input.NestingEfficiencyPercent <= 0 || input.NestingEfficiencyPercent > 100) throw new DomainException("Err.EfficiencyRange");
        if (input.Spacing < 0) throw new DomainException("Err.NegativeValue");

        var sheetArea = input.SheetLength * input.SheetWidth;
        var fits = new List<PieceFit>();
        decimal piecesArea = 0;
        int gridSheets = 0;
        foreach (var p in input.Pieces)
        {
            if (p.Length <= 0 || p.Width <= 0 || p.Quantity < 0) throw new DomainException("Err.PieceSizeInvalid", p.Name ?? "");
            var normal = FitCount(input.SheetLength, input.SheetWidth, p.Length, p.Width, input.Spacing);
            var rotated = FitCount(input.SheetLength, input.SheetWidth, p.Width, p.Length, input.Spacing);
            var best = Math.Max(normal, rotated);
            if (best == 0) throw new DomainException("Err.PieceLargerThanSheet", p.Name ?? $"{p.Length}x{p.Width}");
            var qty = Math.Ceiling(p.Quantity);
            var sheetsAlone = (int)Math.Ceiling(qty / best);
            gridSheets += sheetsAlone;
            piecesArea += p.Length * p.Width * p.Quantity;
            fits.Add(new PieceFit(p.Name, p.Length, p.Width, p.Quantity, best, sheetsAlone, rotated > normal));
        }

        var effectiveArea = sheetArea * input.NestingEfficiencyPercent / 100m;
        var exact = effectiveArea > 0 ? piecesArea / effectiveArea : 0;
        var areaSheets = (int)Math.Ceiling(exact);
        if (piecesArea > 0 && areaSheets == 0) areaSheets = 1;

        decimal required = input.SheetsOverride > 0 ? input.SheetsOverride : areaSheets;
        var utilization = required > 0 ? Money.Round(piecesArea / (required * sheetArea) * 100m) : 0;
        if (utilization > 100m) throw new DomainException("Err.OverrideTooLow");
        var waste = required * sheetArea - piecesArea;

        // Cost: full sheets, or only the fraction consumed (remainder expected to become a remnant).
        var charged = input.ChargeFullSheets ? required : Math.Min(required, exact);
        var cost = Money.Round(charged * input.CostPerSheet);
        return new SheetLayoutResult(sheetArea, piecesArea, areaSheets, gridSheets, required, utilization, Money.Round(waste),
            Money.Round(100m - utilization), cost, Math.Round(exact, 4), fits);
    }

    /// <summary>Number of pieces of size l×w that fit on an L×W sheet in a simple grid, with spacing between pieces.</summary>
    public static int FitCount(decimal sheetL, decimal sheetW, decimal l, decimal w, decimal spacing)
    {
        if (l > sheetL || w > sheetW) return 0;
        var along = (int)Math.Floor((sheetL + spacing) / (l + spacing));
        var across = (int)Math.Floor((sheetW + spacing) / (w + spacing));
        return Math.Max(0, along) * Math.Max(0, across);
    }

    /// <summary>Cost of a remnant as the area-proportional share of the parent sheet cost.</summary>
    public static decimal RemnantCost(decimal sheetArea, decimal sheetCost, decimal remnantLength, decimal remnantWidth)
    {
        if (sheetArea <= 0) return 0;
        var area = remnantLength * remnantWidth;
        if (area > sheetArea) throw new DomainException("Err.RemnantLargerThanSheet");
        return Money.Round(sheetCost * area / sheetArea);
    }
}
