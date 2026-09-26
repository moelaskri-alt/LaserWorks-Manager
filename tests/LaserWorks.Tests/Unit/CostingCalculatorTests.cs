using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Tests.Unit;

public class MachineCostCalculatorTests
{
    private static MachineRateInput Input(bool overrideRate = false, decimal overrideValue = 0, decimal residual = 5000) =>
        new(PurchaseCost: 60000, UsefulLifeYears: 5, ResidualValue: residual, AnnualWorkingHours: 2000, ElectricalLoadKw: 1.8m, ElectricityPricePerKwh: 0.18m,
            MaintenanceCostPerYear: 3000, OperatorCostPerHour: 0, OtherOverheadPerYear: 2000, UseOverride: overrideRate, OverrideRate: overrideValue);

    [Fact]
    public void Hourly_rate_is_depreciation_plus_electricity_plus_maintenance_plus_overhead()
    {
        var r = MachineCostCalculator.Calculate(Input());
        Assert.Equal(5.5m, r.DepreciationPerHour);     // (60,000 − 5,000) / (5 × 2,000)
        Assert.Equal(0.324m, r.ElectricityPerHour);    // 1.8 kW × 0.18
        Assert.Equal(1.5m, r.MaintenancePerHour);      // 3,000 / 2,000
        Assert.Equal(1.0m, r.OverheadPerHour);         // 2,000 / 2,000
        Assert.Equal(8.324m, r.CalculatedRate);
        Assert.Equal(8.324m, r.EffectiveRate);
        Assert.Equal(6.824m, r.EffectiveRateExcludingMaintenance);
        Assert.False(r.IsOverridden);
    }

    [Fact]
    public void Manual_override_replaces_the_rate_and_is_not_split_into_maintenance()
    {
        var r = MachineCostCalculator.Calculate(Input(true, 20));
        Assert.Equal(20m, r.EffectiveRate);
        Assert.Equal(8.324m, r.CalculatedRate);
        Assert.Equal(0m, r.EffectiveMaintenanceRate);
        Assert.True(r.IsOverridden);
    }

    [Fact]
    public void Residual_value_above_purchase_cost_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => MachineCostCalculator.Calculate(Input(residual: 70000)));
        Assert.Equal("Err.ResidualExceedsCost", ex.Code);
    }
}

public class MaterialUtilizationCalculatorTests
{
    [Fact]
    public void Area_method_rounds_up_to_whole_sheets_and_reports_utilization()
    {
        var r = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("tile", 10, 10, 30) }, Spacing: 0, NestingEfficiencyPercent: 100, CostPerSheet: 80));
        Assert.Equal(5000m, r.SheetArea);
        Assert.Equal(3000m, r.PiecesArea);
        Assert.Equal(1, r.SheetsByArea);
        Assert.Equal(1, r.SheetsByGrid);
        Assert.Equal(1m, r.SheetsRequired);
        Assert.Equal(60m, r.UtilizationPercent);
        Assert.Equal(2000m, r.WasteArea);
        Assert.Equal(80m, r.MaterialCost);
    }

    [Fact]
    public void Nesting_efficiency_increases_the_sheet_count()
    {
        var r = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("tile", 10, 10, 30) }, Spacing: 0, NestingEfficiencyPercent: 50, CostPerSheet: 80));
        Assert.Equal(2m, r.SheetsRequired);
        Assert.Equal(30m, r.UtilizationPercent);
        Assert.Equal(160m, r.MaterialCost);
    }

    [Fact]
    public void Charging_consumed_fraction_costs_only_the_used_part_of_the_sheet()
    {
        var r = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("tile", 10, 10, 30) }, Spacing: 0, NestingEfficiencyPercent: 100, CostPerSheet: 80, ChargeFullSheets: false));
        Assert.Equal(48m, r.MaterialCost); // 0.6 sheet × 80
    }

    [Fact]
    public void Grid_fit_counts_spacing_and_rotation()
    {
        Assert.Equal(16, MaterialUtilizationCalculator.FitCount(100, 50, 20, 10, 1)); // floor(101/21)=4 × floor(51/11)=4
        var r = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("strip", 45, 95, 1) }, Spacing: 0));
        Assert.True(r.Fits[0].Rotated);
    }

    [Fact]
    public void Invalid_layouts_are_rejected()
    {
        Assert.Equal("Err.PieceLargerThanSheet", Assert.Throws<DomainException>(() =>
            MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("big", 120, 60, 1) }))).Code);
        Assert.Equal("Err.OverrideTooLow", Assert.Throws<DomainException>(() =>
            MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(100, 50, new[] { new PieceSpec("t", 10, 10, 60) }, Spacing: 0, SheetsOverride: 1))).Code);
        Assert.Equal("Err.SheetSizeRequired", Assert.Throws<DomainException>(() =>
            MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(0, 50, Array.Empty<PieceSpec>()))).Code);
    }

    [Fact]
    public void Remnant_cost_is_area_proportional()
    {
        Assert.Equal(40m, MaterialUtilizationCalculator.RemnantCost(5000, 80, 50, 50));
    }
}

public class InventoryMathTests
{
    [Fact]
    public void Moving_weighted_average_updates_on_receipt_and_issues_at_average()
    {
        var p = InventoryMath.Receive(new StockPosition(0, 0), 10, 5);
        p = InventoryMath.Receive(p, 10, 7);
        Assert.Equal(20m, p.Quantity);
        Assert.Equal(120m, p.Value);
        Assert.Equal(6m, p.AverageCost);
        var (after, value, unit) = InventoryMath.Issue(p, 5);
        Assert.Equal(30m, value);
        Assert.Equal(6m, unit);
        Assert.Equal(15m, after.Quantity);
        Assert.Equal(90m, after.Value);
        // a later receipt at a different price moves the average
        after = InventoryMath.Receive(after, 5, 10);
        Assert.Equal(7m, after.AverageCost); // (90 + 50) / 20
    }

    [Fact]
    public void Issuing_the_last_units_clears_the_value_without_rounding_residue()
    {
        var p = InventoryMath.Receive(new StockPosition(0, 0), 3, 3.3333m);
        var (after, value, _) = InventoryMath.Issue(p, 3);
        Assert.Equal(0m, after.Quantity);
        Assert.Equal(0m, after.Value);
        Assert.Equal(p.Value, value);
    }

    [Fact]
    public void Issue_beyond_stock_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => InventoryMath.Issue(new StockPosition(2, 20), 3));
        Assert.Equal("Err.InsufficientStock", ex.Code);
    }

    [Fact]
    public void Issue_at_specific_cost_uses_that_cost()
    {
        var p = new StockPosition(10, 100);
        var (after, value) = InventoryMath.IssueAtCost(p, 2, 12);
        Assert.Equal(24m, value);
        Assert.Equal(76m, after.Value);
    }
}

public class PricingAndEstimateTests
{
    [Fact]
    public void Margin_and_markup_are_distinct()
    {
        Assert.Equal(100m, PricingCalculator.PriceFromMargin(70, 30));
        Assert.Equal(140m, PricingCalculator.PriceFromMarkup(100, 40));
        Assert.Equal(20m, PricingCalculator.MarginFromMarkup(25));
        var a = PricingCalculator.Analyze(80, 100);
        Assert.Equal(20m, a.Profit);
        Assert.Equal(20m, a.MarginPercent);
        Assert.Equal(25m, a.MarkupPercent);
    }

    private static EstimateInput Input(OverheadMethod method = OverheadMethod.PercentOfDirectCost, decimal overhead = 10, Dictionary<CostComponent, decimal>? manual = null) => new()
    {
        Quantity = 10,
        ItemCosts = new[] { new ItemCost(CostComponent.Material, 100m) },
        Machines = new[] { new MachineTimeInput(6, 8, 1.5m) },
        Labor = new[] { new LaborTimeInput(3, 30) },
        DesignHours = 2, DesignRate = 45, SetupHours = 0.5m, SetupRate = 30,
        FinishingPerUnit = 1, PackagingPerUnit = 0.5m, ConsumablesPerUnit = 0.2m,
        ScrapAllowancePercent = 5, OverheadMethod = method, OverheadRate = overhead,
        Manual = manual ?? new Dictionary<CostComponent, decimal>()
    };

    [Fact]
    public void Estimate_builds_up_all_components()
    {
        var r = EstimateCalculator.Calculate(Input());
        Assert.Equal(100m, r[CostComponent.Material]);
        Assert.Equal(1m, r.TotalMachineHours);           // 6 min × 10 units
        Assert.Equal(8m, r[CostComponent.Machine]);
        Assert.Equal(1.5m, r[CostComponent.Maintenance]);
        Assert.Equal(15m, r[CostComponent.Labor]);        // 0.5 h × 30
        Assert.Equal(90m, r[CostComponent.Design]);
        Assert.Equal(15m, r[CostComponent.Setup]);
        Assert.Equal(10m, r[CostComponent.Finishing]);
        Assert.Equal(5m, r[CostComponent.Packaging]);
        Assert.Equal(2m, r[CostComponent.Consumables]);
        Assert.Equal(5m, r[CostComponent.Scrap]);         // 5 % of material
        Assert.Equal(251.5m, r.DirectCost);
        Assert.Equal(25.15m, r.Overhead);
        Assert.Equal(276.65m, r.TotalCost);
        Assert.Equal(27.665m, r.UnitCost); // unit cost keeps 4 decimals
    }

    [Fact]
    public void Manual_component_replaces_the_calculated_value()
    {
        var r = EstimateCalculator.Calculate(Input(manual: new() { [CostComponent.Design] = 50 }));
        Assert.Equal(90m, r.Calculated[CostComponent.Design]);
        Assert.Equal(50m, r[CostComponent.Design]);
        Assert.Equal(211.5m, r.DirectCost);
    }

    [Fact]
    public void Overhead_per_machine_hour()
    {
        var r = EstimateCalculator.Calculate(Input(OverheadMethod.PerMachineHour, 12));
        Assert.Equal(12m, r.Overhead);
        Assert.Equal(263.5m, r.TotalCost);
    }

    [Fact]
    public void Variance_identifies_the_main_driver()
    {
        var v = VarianceCalculator.Compare(
            new Dictionary<CostComponent, decimal> { [CostComponent.Material] = 100, [CostComponent.Machine] = 50 },
            new Dictionary<CostComponent, decimal> { [CostComponent.Material] = 130, [CostComponent.Machine] = 45, [CostComponent.Rework] = 20 });
        Assert.Equal(150m, v.EstimatedTotal);
        Assert.Equal(195m, v.ActualTotal);
        Assert.Equal(45m, v.Variance);
        Assert.Equal(30m, v.VariancePercent);
        Assert.Equal(CostComponent.Material, v.MainDriver);
    }
}

public class PasswordTests
{
    [Fact]
    public void Passwords_are_salted_hashes_never_plaintext()
    {
        var h1 = PasswordHasher.Hash("Secret123");
        var h2 = PasswordHasher.Hash("Secret123");
        Assert.DoesNotContain("Secret123", h1);
        Assert.NotEqual(h1, h2);
        Assert.True(PasswordHasher.Verify("Secret123", h1));
        Assert.False(PasswordHasher.Verify("secret123", h1));
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("longpassword", false)]
    [InlineData("12345678", false)]
    [InlineData("Laser2026", true)]
    public void Password_policy(string password, bool ok) => Assert.Equal(ok, PasswordHasher.MeetsPolicy(password));

    [Fact]
    public void Money_rounding_is_away_from_zero()
    {
        Assert.Equal(2.35m, Money.Round(2.345m));
        Assert.Equal(-2.35m, Money.Round(-2.345m));
        Assert.Equal(0m, Money.Percent(5, 0));
    }
}
