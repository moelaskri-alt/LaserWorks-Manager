using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Enums;
using LaserWorks.Localization;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    public NavItem(string key, string titleKey, string iconKey, AppModule module, Func<PageViewModel> factory)
    {
        Key = key; TitleKey = titleKey; IconKey = iconKey; Module = module; Factory = factory;
    }

    public string Key { get; }
    public string TitleKey { get; }
    public string IconKey { get; }
    public AppModule Module { get; }
    public Func<PageViewModel> Factory { get; }
    public Geometry? Icon => Avalonia.Application.Current!.TryGetResource(IconKey, null, out var g) ? g as Geometry : null;
    public string Title => Loc.Instance[TitleKey];
    [ObservableProperty] private bool _isActive;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed class NavGroup : ObservableObject
{
    public NavGroup(string titleKey, IEnumerable<NavItem> items) { TitleKey = titleKey; Items = items.ToList(); }
    public string TitleKey { get; }
    public string Title => Loc.Instance[TitleKey];
    public List<NavItem> Items { get; }
    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly List<NavItem> _all;

    public ShellViewModel(MainWindowViewModel main)
    {
        _main = main;
        _all = new List<NavItem>
        {
            new("Dashboard", "Nav.Dashboard", "IcoDashboard", AppModule.Dashboard, () => new DashboardViewModel()),
            new("Customers", "Nav.Customers", "IcoCustomers", AppModule.Customers, () => new CustomersViewModel()),
            new("Requests", "Nav.Requests", "IcoRequest", AppModule.Requests, () => new RequestsViewModel()),
            new("Design", "Nav.Design", "IcoDesign", AppModule.Design, () => new DesignsViewModel()),
            new("Estimates", "Nav.Estimates", "IcoEstimate", AppModule.Estimates, () => new EstimatesViewModel()),
            new("Quotations", "Nav.Quotations", "IcoQuotation", AppModule.Quotations, () => new QuotationsViewModel()),
            new("Jobs", "Nav.Jobs", "IcoJobs", AppModule.Jobs, () => new JobsViewModel()),
            new("Production", "Nav.Production", "IcoProduction", AppModule.Production, () => new ProductionViewModel()),
            new("Machines", "Nav.Machines", "IcoMachines", AppModule.Machines, () => new MachinesViewModel()),
            new("Employees", "Nav.Employees", "IcoEmployees", AppModule.Employees, () => new EmployeesViewModel()),
            new("Materials", "Nav.Materials", "IcoMaterials", AppModule.Inventory, () => new MaterialsViewModel()),
            new("Inventory", "Nav.Inventory", "IcoInventory", AppModule.Inventory, () => new InventoryViewModel()),
            new("Purchases", "Nav.Purchases", "IcoPurchases", AppModule.Purchases, () => new PurchasesViewModel()),
            new("Sales", "Nav.Sales", "IcoSales", AppModule.Sales, () => new SalesViewModel()),
            new("Expenses", "Nav.Expenses", "IcoExpenses", AppModule.Expenses, () => new ExpensesViewModel()),
            new("Accounting", "Nav.Accounting", "IcoAccounting", AppModule.Accounting, () => new AccountingViewModel()),
            new("Profitability", "Nav.Profitability", "IcoProfit", AppModule.Profitability, () => new ProfitabilityViewModel()),
            new("Reports", "Nav.Reports", "IcoReports", AppModule.Reports, () => new ReportsViewModel()),
            new("Settings", "Nav.Settings", "IcoSettings", AppModule.Settings, () => new SettingsViewModel()),
            new("Backup", "Nav.Backup", "IcoBackup", AppModule.Backup, () => new BackupViewModel()),
            new("Users", "Nav.Users", "IcoUsers", AppModule.Users, () => new UsersViewModel()),
        };
        var groups = new (string Title, string[] Keys)[]
        {
            ("NavGroup.Overview", new[] { "Dashboard" }),
            ("NavGroup.Sales", new[] { "Customers", "Requests", "Design", "Estimates", "Quotations", "Sales" }),
            ("NavGroup.Production", new[] { "Jobs", "Production", "Machines", "Employees" }),
            ("NavGroup.Inventory", new[] { "Materials", "Inventory", "Purchases" }),
            ("NavGroup.Finance", new[] { "Expenses", "Accounting", "Profitability", "Reports" }),
            ("NavGroup.Admin", new[] { "Settings", "Backup", "Users" }),
        };
        foreach (var (title, keys) in groups)
        {
            var items = keys.Select(k => _all.First(n => n.Key == k)).Where(n => Can(n.Module, Permission.View)).ToList();
            if (items.Count > 0) Groups.Add(new NavGroup(title, items));
        }
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<NavGroup> Groups { get; } = new();
    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private string? _currentKey;

    public string UserDisplay => Get<UserSession>().FullName;
    public string RoleDisplay => Get<UserSession>().Role is { } r ? L.Enum(r) : "";
    public string CompanyName => Get<SettingsService>().Current.CompanyName;
    public string LanguageLabel => L.IsRightToLeft ? "English" : "العربية";
    public string ThemeLabel => L["Theme." + Get<SettingsService>().Current.Theme];

    public async Task InitializeAsync()
    {
        await Get<PermissionService>().LoadAsync();
        NavigateTo(Groups.SelectMany(g => g.Items).FirstOrDefault()?.Key ?? "Dashboard");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var n in _all) n.RefreshTitle();
        foreach (var g in Groups) g.RefreshTitle();
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(RoleDisplay));
        OnPropertyChanged(nameof(Groups));
        // rebuild the current page so converted values (enums, formats) re-render in the new language
        if (CurrentKey != null && _all.Any(n => n.Key == CurrentKey)) NavigateTo(CurrentKey);
        else if (CurrentPage != null) _ = CurrentPage.LoadAsync();
    }

    [RelayCommand]
    public void NavigateTo(string key)
    {
        var item = _all.FirstOrDefault(n => n.Key == key);
        if (item == null || !Can(item.Module, Permission.View)) return;
        foreach (var n in _all) n.IsActive = n.Key == key;
        CurrentKey = key;
        Show(item.Factory(), keepKey: true);
    }

    public void Show(PageViewModel page, bool keepKey = false)
    {
        if (!keepKey)
        {
            CurrentKey = null;
            foreach (var n in _all) n.IsActive = false;
        }
        CurrentPage = page;
        _ = page.LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleLanguage()
    {
        var settings = Get<SettingsService>();
        var s = await settings.GetAsync();
        var lang = L.IsRightToLeft ? "en" : "ar";
        s.Language = lang;
        await RunAsync(() => settings.SaveAsync(s));
        App.ApplyLanguage(lang);
    }

    [RelayCommand]
    private async Task CycleTheme()
    {
        var settings = Get<SettingsService>();
        var s = await settings.GetAsync();
        s.Theme = s.Theme switch { "Light" => "Dark", "Dark" => "System", _ => "Light" };
        await RunAsync(() => settings.SaveAsync(s));
        App.ApplyTheme(s.Theme);
        OnPropertyChanged(nameof(ThemeLabel));
    }

    [RelayCommand]
    private async Task Logout()
    {
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        await Get<AuthService>().LogoutAsync();
        Get<Navigator>().Shell = null;
        _main.Content = new LoginViewModel(_main);
    }

    [RelayCommand]
    private async Task ChangePassword() => await Dialogs.ShowAsync(new ChangePasswordViewModel());
}
