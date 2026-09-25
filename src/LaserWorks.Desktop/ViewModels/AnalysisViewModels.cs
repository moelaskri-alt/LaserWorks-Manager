using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed record JobProfitItem(JobProfitRow Row, ProfitFlags Flags, string FlagText)
{
    public string RowClass => Flags.NegativeMargin ? "negative" : Flags.Any ? "warning" : "";
}

public sealed partial class ProfitabilityViewModel : PageViewModel, IRowClassifier
{
    public override string TitleKey => "Nav.Profitability";

    [ObservableProperty] private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _to = DateTime.Today;
    [ObservableProperty] private bool _completedOnly = true;
    [ObservableProperty] private bool _flaggedOnly;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private JobProfitItem? _selectedJob;
    [ObservableProperty] private decimal _totalRevenue;
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _totalProfit;
    [ObservableProperty] private decimal _totalMargin;
    [ObservableProperty] private int _lossJobs;
    [ObservableProperty] private int _flaggedJobs;

    public ObservableCollection<JobProfitItem> Jobs { get; } = new();
    public ObservableCollection<GroupProfitRow> Customers { get; } = new();
    public ObservableCollection<GroupProfitRow> Machines { get; } = new();
    public ObservableCollection<GroupProfitRow> Materials { get; } = new();
    public ObservableCollection<GroupProfitRow> Months { get; } = new();
    public ObservableCollection<ChartPoint> MonthChart { get; } = new();

    private DateRange Range => new(From?.Date ?? Today.AddYears(-1), To?.Date ?? Today);

    partial void OnCompletedOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnFlaggedOnlyChanged(bool value) => _ = LoadAsync();

    public string? RowClass(object row) => row is JobProfitItem i && i.RowClass.Length > 0 ? i.RowClass : row is GroupProfitRow { GrossProfit: < 0 } ? "negative" : null;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var svc = Get<JobCostingService>();
            var rows = await svc.JobProfitabilityAsync(Range, CompletedOnly);
            Jobs.Clear();
            foreach (var r in rows)
            {
                var f = svc.Flags(r);
                if (FlaggedOnly && !f.Any) continue;
                var text = string.Join(", ", new[] { f.NegativeMargin ? L["Flag.NegativeMargin"] : null, f.LowMargin ? L["Flag.LowMargin"] : null, f.HighMaterialVariance ? L["Flag.HighMaterialVariance"] : null,
                    f.HighScrap ? L["Flag.HighScrap"] : null, f.HighMachineVariance ? L["Flag.HighMachineVariance"] : null }.Where(x => x != null));
                Jobs.Add(new JobProfitItem(r, f, text));
            }
            var withCost = rows.Where(r => r.ActualCost != 0).ToList();
            TotalRevenue = withCost.Sum(r => r.Revenue); TotalCost = withCost.Sum(r => r.ActualCost); TotalProfit = TotalRevenue - TotalCost;
            TotalMargin = Money.Percent(TotalProfit, TotalRevenue);
            LossJobs = withCost.Count(r => r.GrossProfit < 0);
            FlaggedJobs = rows.Count(r => svc.Flags(r).Any);
            Fill(Customers, await svc.ByCustomerAsync(Range));
            Fill(Machines, await svc.ByMachineAsync(Range));
            Fill(Materials, await svc.ByMaterialAsync(Range));
            var months = await svc.ByMonthAsync(Range);
            Fill(Months, months);
            MonthChart.Clear();
            foreach (var m in months) MonthChart.Add(new ChartPoint(m.Key, m.GrossProfit));
        });
    }

    private static void Fill<T>(ObservableCollection<T> c, IEnumerable<T> items) { c.Clear(); foreach (var i in items) c.Add(i); }

    [RelayCommand] private Task Refresh() => LoadAsync();
    [RelayCommand] private void OpenJob(JobProfitItem? j) { if (j != null) Nav.Navigate(new JobDetailViewModel(j.Row.JobId)); }

    [RelayCommand]
    private void Report(string key) => Nav.Navigate(new ReportsViewModel(key, Range));
}

public sealed class ReportGroup
{
    public ReportGroup(string titleKey, List<ReportDefinition> reports) { TitleKey = titleKey; Reports = reports; }
    public string TitleKey { get; }
    public string Title => LaserWorks.Localization.Loc.Instance[TitleKey];
    public List<ReportDefinition> Reports { get; }
}

public sealed record ReportItem(ReportDefinition Definition)
{
    public string Title => LaserWorks.Localization.Loc.Instance[Definition.TitleKey];
}

public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly string? _initialKey;

    public ReportsViewModel(string? key = null, DateRange? range = null, long? customerId = null, long? accountId = null)
    {
        _initialKey = key;
        var t = Today;
        _from = range?.From ?? new DateTime(t.Year, 1, 1);
        _to = range?.To ?? t;
        _initialCustomerId = customerId;
        _initialAccountId = accountId;
    }

    private readonly long? _initialCustomerId;
    private readonly long? _initialAccountId;

    public override string TitleKey => "Nav.Reports";

    public ObservableCollection<ReportGroup> Groups { get; } = new();
    [ObservableProperty] private ReportItem? _selectedReport;
    [ObservableProperty] private ReportDefinition? _definition;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private DateTime? _asOf = DateTime.Today;
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private Lookup? _job;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private Account? _account;
    [ObservableProperty] private TextOption? _status;
    [ObservableProperty] private ReportTable? _table;
    [ObservableProperty] private string? _summary;

    public List<ReportItem> AllReports { get; private set; } = new();
    public List<Lookup> CustomerList { get; private set; } = new();
    public List<Lookup> SupplierList { get; private set; } = new();
    public List<Lookup> JobList { get; private set; } = new();
    public List<Lookup> MachineList { get; private set; } = new();
    public List<MaterialLookup> MaterialList { get; private set; } = new();
    public List<Account> AccountList { get; private set; } = new();
    public ObservableCollection<TextOption> StatusOptions { get; } = new();

    public bool ShowRange => Has(ReportFilterKind.DateRange);
    public bool ShowAsOf => Has(ReportFilterKind.AsOfDate);
    public bool ShowCustomer => Has(ReportFilterKind.Customer);
    public bool ShowSupplier => Has(ReportFilterKind.Supplier);
    public bool ShowJob => Has(ReportFilterKind.Job);
    public bool ShowMachine => Has(ReportFilterKind.Machine);
    public bool ShowMaterial => Has(ReportFilterKind.Material);
    public bool ShowAccount => Has(ReportFilterKind.Account);
    public bool ShowStatus => Has(ReportFilterKind.Status);
    public bool HasTable => Table != null;
    public int RowCount => Table?.Rows.Count ?? 0;

    private bool Has(ReportFilterKind k) => Definition?.Filters.HasFlag(k) == true;

    partial void OnSelectedReportChanged(ReportItem? value)
    {
        Definition = value?.Definition;
        StatusOptions.Clear();
        if (Definition?.StatusOptions is { } opts)
        {
            StatusOptions.Add(new TextOption(null, L["Filter.All"]));
            foreach (var (v, k) in opts) StatusOptions.Add(new TextOption(v, L[k]));
        }
        Status = StatusOptions.FirstOrDefault();
        foreach (var p in new[] { nameof(ShowRange), nameof(ShowAsOf), nameof(ShowCustomer), nameof(ShowSupplier), nameof(ShowJob), nameof(ShowMachine), nameof(ShowMaterial), nameof(ShowAccount), nameof(ShowStatus) })
            OnPropertyChanged(p);
        Table = null;
    }

    partial void OnTableChanged(ReportTable? value)
    {
        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(RowCount));
        TableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised so the view can rebuild the dynamic grid columns.</summary>
    public event EventHandler? TableChanged;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var catalog = Get<ReportCatalog>();
            Groups.Clear();
            foreach (var g in catalog.Reports.GroupBy(r => r.CategoryKey)) Groups.Add(new ReportGroup(g.Key, g.ToList()));
            AllReports = catalog.Reports.Select(r => new ReportItem(r)).ToList();
            CustomerList = await Get<CustomerService>().LookupAsync(activeOnly: false);
            SupplierList = await Get<SupplierService>().LookupAsync();
            JobList = await Get<JobService>().LookupAsync(openOnly: false);
            MachineList = await Get<MachineService>().LookupAsync();
            MaterialList = await Get<MaterialService>().LookupAsync();
            AccountList = await Get<AccountingService>().PostableAccountsAsync();
            foreach (var p in new[] { nameof(AllReports), nameof(CustomerList), nameof(SupplierList), nameof(JobList), nameof(MachineList), nameof(MaterialList), nameof(AccountList) }) OnPropertyChanged(p);
            Customer = CustomerList.FirstOrDefault(c => c.Id == _initialCustomerId);
            Account = AccountList.FirstOrDefault(a => a.Id == _initialAccountId);
            SelectedReport = AllReports.FirstOrDefault(r => r.Definition.Key == _initialKey) ?? AllReports.FirstOrDefault();
        });
        if (_initialKey != null) await Run();
    }

    private ReportFilter Filter() => new(new DateRange(From?.Date ?? Today.AddYears(-1), To?.Date ?? Today), AsOf?.Date ?? Today,
        ShowCustomer ? Customer?.Id : null, ShowJob ? Job?.Id : null, ShowMachine ? Machine?.Id : null, ShowMaterial ? Material?.Id : null,
        ShowStatus ? Status?.Value : null, ShowAccount ? Account?.Id : null, ShowSupplier ? Supplier?.Id : null);

    [RelayCommand]
    public async Task Run()
    {
        if (Definition == null) return;
        await RunAsync(async () =>
        {
            if (ShowRange && From > To) throw new DomainException("Err.DateRangeInvalid");
            Table = await Get<ReportCatalog>().RunAsync(Definition.Key, Filter());
            Summary = L.Format("Report.RowsSummary", Table.Rows.Count);
        });
    }

    [RelayCommand]
    private async Task Export(string format)
    {
        if (Table == null) await Run();
        if (Table == null) return;
        await RunAsync(async () =>
        {
            if (!Can(AppModule.Reports, Permission.Export)) throw new DomainException("Err.AccessDenied", AppModule.Reports, Permission.Export);
            var path = await Dialogs.SaveFileAsync(Table.Title, format);
            if (path == null) return;
            await ReportFiles.SaveAsync(Table, format, path);
            InfoMessage = L.Format("Msg.Exported", Path.GetFileName(path));
        });
    }

    [RelayCommand]
    private async Task Print()
    {
        if (Table == null) await Run();
        if (Table == null) return;
        await RunAsync(async () =>
        {
            if (!Can(AppModule.Reports, Permission.Print)) throw new DomainException("Err.AccessDenied", AppModule.Reports, Permission.Print);
            await ReportFiles.PrintAsync(Table);
        });
    }

    [RelayCommand] private async Task OpenRow(ReportRow? row) { if (row != null) await Nav.OpenAsync(row.SourceType, row.SourceId); }
}
