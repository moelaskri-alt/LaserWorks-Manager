using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    public override string TitleKey => "Nav.Dashboard";

    public IReadOnlyList<string> Presets { get; } = new[] { "Today", "Last7", "ThisMonth", "Last30", "ThisQuarter", "ThisYear", "Last12Months", "Custom" };

    [ObservableProperty] private string _preset = "ThisMonth";
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private DashboardData? _data;

    public DashboardViewModel()
    {
        ApplyPreset();
    }

    public bool IsCustom => Preset == "Custom";
    public string Currency => Get<SettingsService>().Current.CurrencyCode;

    partial void OnPresetChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustom));
        if (value != "Custom") { ApplyPreset(); _ = LoadAsync(); }
    }

    private void ApplyPreset()
    {
        var t = Today;
        (DateTime f, DateTime to) = Preset switch
        {
            "Today" => (t, t),
            "Last7" => (t.AddDays(-6), t),
            "Last30" => (t.AddDays(-29), t),
            "ThisQuarter" => (new DateTime(t.Year, (t.Month - 1) / 3 * 3 + 1, 1), t),
            "ThisYear" => (new DateTime(t.Year, 1, 1), t),
            "Last12Months" => (new DateTime(t.Year, t.Month, 1).AddMonths(-11), t),
            "Custom" => (From?.Date ?? t.AddDays(-30), To?.Date ?? t),
            _ => (new DateTime(t.Year, t.Month, 1), t)
        };
        From = f;
        To = to;
    }

    public DateRange Range => new((From ?? DateTime.Today).Date, (To ?? DateTime.Today).Date);

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            if (Range.From > Range.To) throw new LaserWorks.Domain.Common.DomainException("Err.DateRangeInvalid");
            Data = await Get<DashboardService>().LoadAsync(Range);
        });
    }

    [RelayCommand] private Task Refresh() => LoadAsync();

    /// <summary>KPI tile drill-down to the underlying list.</summary>
    [RelayCommand]
    private void Drill(string target)
    {
        switch (target)
        {
            case "Sales": Nav.Navigate(new SalesViewModel(range: Range)); break;
            case "OpenJobs": Nav.Navigate(new JobsViewModel(new JobFilter(OpenOnly: true))); break;
            case "DueSoon": Nav.Navigate(new JobsViewModel(new JobFilter(DueSoon: true))); break;
            case "Overdue": Nav.Navigate(new JobsViewModel(new JobFilter(Overdue: true))); break;
            case "Quotations": Nav.Navigate(new QuotationsViewModel(pendingOnly: true)); break;
            case "Receivables": Nav.Navigate(new SalesViewModel(openOnly: true)); break;
            case "Cash": Nav.Navigate(new AccountingViewModel("Ledger", SystemAccounts.Bank)); break;
            case "Scrap": Nav.Navigate(new ProductionViewModel("Scrap")); break;
            case "Profit": Nav.Navigate(new ProfitabilityViewModel()); break;
            case "Material": Nav.Navigate(new ReportsViewModel("MaterialConsumption", Range)); break;
            case "Machine": Nav.Navigate(new ReportsViewModel("MachineCost", Range)); break;
            case "LowStock": Nav.Navigate(new MaterialsViewModel(lowOnly: true)); break;
        }
    }

    [RelayCommand]
    private void OpenPoint(ChartPoint? p)
    {
        if (p == null) return;
        _ = p.Key switch
        {
            not null when Enum.TryParse<JobStatus>(p.Key, out var st) && p.Id == null => Go(() => Nav.Navigate(new JobsViewModel(new JobFilter(Status: st)))),
            not null when Enum.TryParse<CostComponent>(p.Key, out _) => Go(() => Nav.Navigate(new ReportsViewModel("JobCostVariance", Range))),
            not null when DateTime.TryParse(p.Key, out var d) => Go(() => Nav.Navigate(new SalesViewModel(range: (Range.To - Range.From).TotalDays <= 62 ? new DateRange(d, d) : new DateRange(d, d.AddMonths(1).AddDays(-1))))),
            _ => Task.CompletedTask
        };
    }

    [RelayCommand] private void OpenCustomer(ChartPoint? p) { if (p?.Id is { } id) Nav.Navigate(new CustomerProfileViewModel(id)); }
    [RelayCommand] private void OpenJob(ChartPoint? p) { if (p?.Id is { } id) Nav.Navigate(new JobDetailViewModel(id)); }
    [RelayCommand] private async Task OpenMaterial(ChartPoint? p) { if (p?.Id is { } id) await Dialogs.ShowAsync(new MaterialEditorViewModel(id)); }

    private static Task Go(Action a) { a(); return Task.CompletedTask; }
}
