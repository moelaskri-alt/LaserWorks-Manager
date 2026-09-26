using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Backup;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

// ================================================================== settings
public sealed partial class SettingsViewModel : PageViewModel
{
    public override string TitleKey => "Nav.Settings";

    [ObservableProperty] private CompanySettings _company = new();
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _canLoadDemo;
    public ObservableCollection<NumberSequence> Sequences { get; } = new();
    public ObservableCollection<Warehouse> Warehouses { get; } = new();
    public ObservableCollection<UnitOfMeasure> Units { get; } = new();
    public ObservableCollection<MaterialCategory> Categories { get; } = new();
    public ObservableCollection<CostCenter> CostCenters { get; } = new();
    public ObservableCollection<ExpenseCategory> ExpenseCategories { get; } = new();
    public List<Account> ExpenseAccounts { get; private set; } = new();
    public IReadOnlyList<string> Languages { get; } = new[] { "ar", "en" };
    public IReadOnlyList<string> Themes { get; } = new[] { "System", "Light", "Dark" };
    public IReadOnlyList<OverheadMethod> OverheadMethods { get; } = Enum.GetValues<OverheadMethod>();
    public IReadOnlyList<CostComponent> Components { get; } = Enum.GetValues<CostComponent>();
    public IReadOnlyList<string> CostingMethods { get; } = new[] { "WeightedAverage" };
    public bool CanEdit => Can(AppModule.Settings, Permission.Edit);

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var settings = Get<SettingsService>();
            settings.Invalidate();
            var s = await settings.GetAsync();
            Company = new CompanySettings();
            var copy = new CompanySettings();
            foreach (var p in typeof(CompanySettings).GetProperties().Where(p => p.CanWrite)) p.SetValue(copy, p.GetValue(s));
            Company = copy;
            Fill(Sequences, await settings.GetSequencesAsync());
            var lookup = Get<LookupService>();
            Fill(Warehouses, await lookup.WarehousesAsync());
            Fill(Units, await lookup.UnitsAsync());
            Fill(Categories, await lookup.CategoriesAsync());
            Fill(CostCenters, await lookup.CostCentersAsync());
            Fill(ExpenseCategories, await lookup.ExpenseCategoriesAsync());
            ExpenseAccounts = await Get<AccountingService>().PostableAccountsAsync(AccountType.Expense);
            OnPropertyChanged(nameof(ExpenseAccounts));
            CanLoadDemo = !await Get<DemoDataService>().HasBusinessDataAsync();
        });
    }

    private static void Fill<T>(ObservableCollection<T> c, IEnumerable<T> items) { c.Clear(); foreach (var i in items) c.Add(i); }

    [RelayCommand]
    private async Task SaveCompany()
    {
        if (await RunAsync(() => Get<SettingsService>().SaveAsync(Company), "Msg.Saved"))
        {
            App.ApplyTheme(Company.Theme);
            if (Company.Language != L.Language) App.ApplyLanguage(Company.Language);
        }
    }

    [RelayCommand] private async Task SaveSequences() { if (await RunAsync(() => Get<SettingsService>().SaveSequencesAsync(Sequences), "Msg.Saved")) await LoadAsync(); }

    [RelayCommand]
    private async Task SaveLists()
    {
        await RunAsync(async () =>
        {
            var lookup = Get<LookupService>();
            foreach (var w in Warehouses.Where(w => !string.IsNullOrWhiteSpace(w.Name))) await lookup.SaveWarehouseAsync(w);
            foreach (var u in Units.Where(u => !string.IsNullOrWhiteSpace(u.Code))) await lookup.SaveUnitAsync(u);
            foreach (var c in Categories.Where(c => !string.IsNullOrWhiteSpace(c.Name))) await lookup.SaveCategoryAsync(c);
            foreach (var c in CostCenters.Where(c => !string.IsNullOrWhiteSpace(c.Code))) await lookup.SaveCostCenterAsync(c);
            foreach (var c in ExpenseCategories.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            {
                if (c.Account != null) c.AccountId = c.Account.Id;
                await lookup.SaveExpenseCategoryAsync(c);
            }
        }, "Msg.Saved");
        await LoadAsync();
    }

    [RelayCommand] private void AddWarehouse() => Warehouses.Add(new Warehouse { Code = $"WH{Warehouses.Count + 1}", Name = "", IsActive = true });
    [RelayCommand] private void AddUnit() => Units.Add(new UnitOfMeasure { Code = "", NameEn = "", NameAr = "" });
    [RelayCommand] private void AddCategory() => Categories.Add(new MaterialCategory { Name = "", IsActive = true });
    [RelayCommand] private void AddCostCenter() => CostCenters.Add(new CostCenter { Code = "", Name = "", IsActive = true });
    [RelayCommand] private void AddExpenseCategory() => ExpenseCategories.Add(new ExpenseCategory { Name = "", Account = ExpenseAccounts.FirstOrDefault(), IsActive = true });

    [RelayCommand]
    private async Task LoadDemo()
    {
        if (!await Dialogs.ConfirmAsync(L["Settings.ConfirmDemo"])) return;
        if (await RunAsync(() => Task.Run(() => Get<DemoDataService>().LoadAsync()), "Msg.DemoLoaded")) await LoadAsync();
    }
}

// ================================================================== backup & restore
public sealed partial class BackupViewModel : PageViewModel
{
    public override string TitleKey => "Nav.Backup";

    public ObservableCollection<BackupInfo> History { get; } = new();
    public ObservableCollection<string> IntegrityResults { get; } = new();
    [ObservableProperty] private BackupInfo? _selected;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string? _validationResult;
    [ObservableProperty] private bool _integrityOk;
    public string DataFolder => Get<IAppPaths>().DataDirectory;
    public string BackupFolder => Get<IAppPaths>().BackupsDirectory;
    public bool CanRestore => Can(AppModule.Backup, Permission.Edit) || Get<UserSession>().Role == UserRole.Administrator;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            History.Clear();
            foreach (var h in await Get<BackupService>().HistoryAsync()) History.Add(h);
        });
    }

    [RelayCommand]
    private async Task BackupNow()
    {
        await RunAsync(async () =>
        {
            if (!Can(AppModule.Backup, Permission.Create)) throw new DomainException("Err.AccessDenied", AppModule.Backup, Permission.Create);
            var info = await Task.Run(() => Get<BackupService>().CreateBackupAsync(note: Note));
            InfoMessage = L.Format("Backup.Created", info.FilePath);
        });
        await LoadAsync();
    }

    [RelayCommand]
    private async Task BackupTo()
    {
        var path = await Dialogs.SaveFileAsync("LaserWorks_Backup", "lwbak");
        if (path == null) return;
        await RunAsync(async () =>
        {
            var info = await Task.Run(() => Get<BackupService>().CreateBackupAsync(path, Note));
            InfoMessage = L.Format("Backup.Created", info.FilePath);
        });
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Validate()
    {
        var path = Selected?.FilePath ?? await Dialogs.OpenFileAsync("LaserWorks backup", "*.lwbak");
        if (path == null) return;
        await RunAsync(async () =>
        {
            var v = await Get<BackupService>().ValidateAsync(path);
            ValidationResult = v.IsValid
                ? L.Format("Backup.Valid", Path.GetFileName(path), v.Manifest?.CreatedAt.ToString("yyyy-MM-dd HH:mm"), v.Manifest?.AppVersion)
                : L["Backup.Invalid"] + ": " + string.Join("; ", v.Problems);
        });
    }

    [RelayCommand]
    private async Task Restore()
    {
        var path = await Dialogs.OpenFileAsync("LaserWorks backup", "*.lwbak");
        await RestoreFromAsync(path);
    }

    [RelayCommand]
    private async Task RestoreSelected() => await RestoreFromAsync(Selected?.FilePath);

    private async Task RestoreFromAsync(string? path)
    {
        if (path == null) return;
        if (!await Dialogs.ConfirmAsync(L.Format("Backup.ConfirmRestore", Path.GetFileName(path)), danger: true)) return;
        var ok = await RunAsync(async () =>
        {
            if (!CanRestore) throw new DomainException("Err.AccessDenied", AppModule.Backup, Permission.Edit);
            await Task.Run(() => Get<BackupService>().RestoreAsync(path));
            await Task.Run(() => App.Services.InitializeDatabaseAsync());
        });
        if (!ok) return;
        await Dialogs.AlertAsync(L["Backup.Restored"]);
        await Get<AuthService>().LogoutAsync();
        Get<SettingsService>().Invalidate();
        var main = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as MainWindowViewModel;
        if (main != null) await main.ShowStartScreenAsync();
    }

    [RelayCommand]
    private async Task CheckIntegrity()
    {
        await RunAsync(async () =>
        {
            IntegrityResults.Clear();
            var problems = await Task.Run(() => Get<BackupService>().CheckLiveDatabase());
            IntegrityResults.Add(problems.Count == 0 ? "✔ " + L["Backup.SqliteOk"] : "✘ " + string.Join("; ", problems));
            var checks = await Get<ReconciliationService>().RunAsync();
            foreach (var c in checks) IntegrityResults.Add($"{(c.Passed ? "✔" : "✘")} {c.Name}");
            IntegrityOk = problems.Count == 0 && checks.All(c => c.Passed);
        });
    }

    [RelayCommand] private void OpenFolder() => Dialogs.OpenFile(BackupFolder);
}

// ================================================================== users, permissions, audit
public sealed partial class PermissionRowVm : ObservableObject
{
    public PermissionRowVm(AppModule module, Permission p) { Module = module; Set(p); }
    public AppModule Module { get; }
    [ObservableProperty] private bool _view;
    [ObservableProperty] private bool _create;
    [ObservableProperty] private bool _edit;
    [ObservableProperty] private bool _delete;
    [ObservableProperty] private bool _post;
    [ObservableProperty] private bool _approve;
    [ObservableProperty] private bool _print;
    [ObservableProperty] private bool _export;

    private void Set(Permission p)
    {
        View = p.HasFlag(Permission.View); Create = p.HasFlag(Permission.Create); Edit = p.HasFlag(Permission.Edit); Delete = p.HasFlag(Permission.Delete);
        Post = p.HasFlag(Permission.Post); Approve = p.HasFlag(Permission.Approve); Print = p.HasFlag(Permission.Print); Export = p.HasFlag(Permission.Export);
    }

    public Permission Value =>
        (View ? Permission.View : 0) | (Create ? Permission.Create : 0) | (Edit ? Permission.Edit : 0) | (Delete ? Permission.Delete : 0) |
        (Post ? Permission.Post : 0) | (Approve ? Permission.Approve : 0) | (Print ? Permission.Print : 0) | (Export ? Permission.Export : 0);
}

public sealed partial class AuditLogViewModel : ListPageViewModel<AuditRow>
{
    public override string TitleKey => "Users.Audit";
    protected override AppModule Module => AppModule.Users;
    public List<Option<AuditAction>> Actions { get; } = Options.ForEnum<AuditAction>();
    [ObservableProperty] private Option<AuditAction>? _action;
    [ObservableProperty] private DateTime? _from = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime? _to = DateTime.Today;

    public AuditLogViewModel() => _action = Actions[0];

    partial void OnActionChanged(Option<AuditAction>? value) => _ = LoadAsync();
    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    protected override Task<PagedResult<AuditRow>> FetchAsync(PageRequest r) => Get<AuditService>().ListAsync(r, From?.Date, To?.Date, action: Action?.Value);

    protected override ReportTable BuildExport(IReadOnlyList<AuditRow> rows)
    {
        var t = new ReportTable().Col("time", "Col.Date", K.Date).Col("user", "Col.User").Col("action", "Col.Action").Col("entity", "Col.Entity").Col("id", "Col.RecordId", K.Text, 0.6f).Col("details", "Col.Details", width: 3);
        foreach (var a in rows) t.Add(a.Timestamp, a.Username, a.Action, L.EntityName(a.Entity), a.RecordId?.ToString(), L.AuditDetails(a.Details));
        return t;
    }
}

public sealed partial class UsersViewModel : PageViewModel
{
    public override string TitleKey => "Nav.Users";
    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<PermissionRowVm> Matrix { get; } = new();
    public AuditLogViewModel Audit { get; } = new();
    public IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>().Where(r => r != UserRole.Administrator).ToList();
    [ObservableProperty] private UserRow? _selectedUser;
    [ObservableProperty] private UserRole _matrixRole = UserRole.Manager;
    [ObservableProperty] private int _selectedTab;

    partial void OnMatrixRoleChanged(UserRole value) => LoadMatrix();

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Users.Clear();
            foreach (var u in await Get<AuthService>().ListUsersAsync()) Users.Add(u);
            await Get<PermissionService>().LoadAsync();
            LoadMatrix();
        });
        await Audit.LoadAsync();
    }

    private void LoadMatrix()
    {
        Matrix.Clear();
        var perms = Get<PermissionService>();
        foreach (var m in Enum.GetValues<AppModule>()) Matrix.Add(new PermissionRowVm(m, perms.For(MatrixRole, m)));
    }

    [RelayCommand] private async Task NewUser() { if (await Dialogs.ShowAsync(new UserEditorViewModel(null))) await LoadAsync(); }
    [RelayCommand] private async Task EditUser(UserRow? u) { if (u != null && await Dialogs.ShowAsync(new UserEditorViewModel(u))) await LoadAsync(); }

    [RelayCommand]
    private async Task ResetPassword(UserRow? u)
    {
        if (u == null) return;
        var pwd = await Dialogs.PromptAsync("Users.ResetPassword", "Users.NewPassword");
        if (pwd != null && await RunAsync(() => Get<AuthService>().ResetPasswordAsync(u.Id, pwd), "Msg.Saved")) await LoadAsync();
    }

    [RelayCommand] private async Task Unlock(UserRow? u) { if (u != null && await RunAsync(() => Get<AuthService>().UnlockAsync(u.Id), "Msg.Saved")) await LoadAsync(); }

    [RelayCommand]
    private async Task SaveMatrix()
    {
        await RunAsync(() => Get<PermissionService>().SaveMatrixAsync(Matrix.Select(r => new RolePermission { Role = MatrixRole, Module = r.Module, Permissions = r.Value })), "Msg.Saved");
    }
}

public sealed partial class UserEditorViewModel : DialogViewModel
{
    private readonly UserRow? _user;
    public UserEditorViewModel(UserRow? user)
    {
        _user = user;
        if (user != null) { _username = user.Username; _fullName = user.FullName; _role = user.Role; _isActive = user.IsActive; }
    }

    public override string TitleKey => _user == null ? "Users.New" : "Users.Edit";
    public override double DialogWidth => 520;
    [ObservableProperty] private string? _username;
    [ObservableProperty] private string? _fullName;
    [ObservableProperty] private UserRole _role = UserRole.Sales;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _password;
    public bool IsNew => _user == null;
    public IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>();

    protected override async Task SaveAsync()
    {
        if (_user == null) await Get<AuthService>().CreateUserAsync(Username ?? "", FullName ?? "", Role, Password ?? "", mustChange: true);
        else await Get<AuthService>().UpdateUserAsync(_user.Id, FullName ?? "", Role, IsActive);
    }
}
