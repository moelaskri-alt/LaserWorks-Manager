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

// ================================================================== expenses
public sealed partial class ExpensesViewModel : ListPageViewModel<ExpenseRow>
{
    public override string TitleKey => "Nav.Expenses";
    protected override AppModule Module => AppModule.Expenses;
    public List<Option<DocumentStatus>> Statuses { get; } = Options.ForEnum<DocumentStatus>();
    [ObservableProperty] private Option<DocumentStatus>? _status;
    [ObservableProperty] private DateTime? _from = DateTime.Today.AddMonths(-3);
    [ObservableProperty] private DateTime? _to = DateTime.Today;

    public ExpensesViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<DocumentStatus>? value) => _ = LoadAsync();
    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    protected override Task<PagedResult<ExpenseRow>> FetchAsync(PageRequest r) =>
        Get<ExpenseService>().ListAsync(r, new DateRange(From?.Date ?? Today.AddYears(-10), To?.Date ?? Today), status: Status?.Value);

    public override string? RowClass(object row) => row switch
    {
        ExpenseRow { Status: DocumentStatus.Draft } => "warning",
        ExpenseRow { Status: DocumentStatus.Cancelled } => "negative",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<ExpenseRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("cat", "Col.Category").Col("desc", "Col.Description", width: 2).Col("amount", "Col.Amount", K.Money, total: true)
            .Col("tax", "Col.Tax", K.Money, total: true).Col("method", "Col.Method").Col("job", "Col.Job").Col("machine", "Col.Machine").Col("cc", "Col.CostCenter").Col("status", "Col.Status");
        foreach (var e in rows) t.Add(e.Number, e.Date, e.Category, e.Description, e.Amount, e.TaxAmount, e.PaymentMethod, e.JobNumber, e.Machine, e.CostCenter, e.Status);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new ExpenseEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(ExpenseRow? r) { if (r != null) { await Dialogs.ShowAsync(new ExpenseEditorViewModel(r.Id)); await LoadAsync(); } }

    [RelayCommand]
    private async Task Post(ExpenseRow? r)
    {
        if (r == null) return;
        if (await RunAsync(() => Get<ExpenseService>().PostAsync(r.Id), "Msg.Posted")) await LoadAsync();
    }

    [RelayCommand]
    private async Task CancelExpense(ExpenseRow? r)
    {
        if (r == null) return;
        var reason = await Dialogs.PromptAsync("Expense.Cancel", "Col.Reason");
        if (reason != null && await RunAsync(() => Get<ExpenseService>().CancelAsync(r.Id, reason), "Msg.Reversed")) await LoadAsync();
    }
}

public sealed partial class ExpenseEditorViewModel : DialogViewModel
{
    private long _id;
    private readonly long? _jobId;

    public ExpenseEditorViewModel(long id, long? jobId = null) { _id = id; _jobId = jobId; }

    public override string TitleKey => _id == 0 ? "Expense.New" : "Expense.Title";
    public override double DialogWidth => 720;
    public override bool ShowSave => IsDraft;
    public override string SaveKey => "Expense.SaveAndPost";
    [ObservableProperty] private DocumentStatus _status;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private ExpenseCategory? _category;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private decimal _taxAmount;
    [ObservableProperty] private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private Lookup? _job;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private CostCenter? _costCenter;
    [ObservableProperty] private string? _reference;
    [ObservableProperty] private bool _postNow = true;
    public List<ExpenseCategory> Categories { get; private set; } = new();
    public List<Lookup> Suppliers { get; private set; } = new();
    public List<Lookup> Jobs { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public List<CostCenter> CostCenters { get; private set; } = new();
    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>();
    public bool IsDraft => Status == DocumentStatus.Draft;
    public string? JobHint => Job == null ? null : L.Format("Expense.JobHint", Category == null ? "" : L.Enum(Category.JobComponent));

    partial void OnStatusChanged(DocumentStatus value) { OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(ShowSave)); }
    partial void OnJobChanged(Lookup? value) => OnPropertyChanged(nameof(JobHint));
    partial void OnCategoryChanged(ExpenseCategory? value) => OnPropertyChanged(nameof(JobHint));

    public override async Task InitializeAsync()
    {
        Categories = (await Get<LookupService>().ExpenseCategoriesAsync()).Where(c => c.IsActive).ToList();
        Suppliers = await Get<SupplierService>().LookupAsync();
        Jobs = await Get<JobService>().LookupAsync();
        Machines = await Get<MachineService>().LookupAsync();
        CostCenters = await Get<LookupService>().CostCentersAsync();
        foreach (var p in new[] { nameof(Categories), nameof(Suppliers), nameof(Jobs), nameof(Machines), nameof(CostCenters) }) OnPropertyChanged(p);
        Job = Jobs.FirstOrDefault(j => j.Id == _jobId);
        if (_id == 0) { Category = Categories.FirstOrDefault(); return; }
        var e = await Get<ExpenseService>().GetAsync(_id);
        if (e == null) return;
        Status = e.Status; Date = e.Date; Category = Categories.FirstOrDefault(c => c.Id == e.CategoryId); Description = e.Description; Amount = e.Amount; TaxAmount = e.TaxAmount;
        PaymentMethod = e.PaymentMethod; Supplier = Suppliers.FirstOrDefault(s => s.Id == e.SupplierId); Job = Jobs.FirstOrDefault(j => j.Id == e.JobId);
        Machine = Machines.FirstOrDefault(m => m.Id == e.MachineId); CostCenter = CostCenters.FirstOrDefault(c => c.Id == e.CostCenterId); Reference = e.Reference;
    }

    [RelayCommand] private void CalcTax() => TaxAmount = Math.Round(Amount * Get<SettingsService>().Current.DefaultTaxRate / 100m, Decimals, MidpointRounding.AwayFromZero);

    protected override async Task SaveAsync()
    {
        _id = await Get<ExpenseService>().SaveAsync(new Expense
        {
            Id = _id, Date = Date?.Date ?? Today, CategoryId = Category?.Id ?? 0, Description = Description ?? "", Amount = Amount, TaxAmount = TaxAmount, PaymentMethod = PaymentMethod,
            SupplierId = Supplier?.Id, JobId = Job?.Id, MachineId = Machine?.Id, CostCenterId = CostCenter?.Id, Reference = Reference
        });
        if (PostNow) await Get<ExpenseService>().PostAsync(_id);
    }
}

// ================================================================== accounting
public sealed partial class ChartOfAccountsViewModel : ViewModelBase
{
    public ObservableCollection<AccountRow> Items { get; } = new();
    [ObservableProperty] private AccountRow? _selected;
    [ObservableProperty] private string? _search;
    private List<AccountRow> _all = new();

    partial void OnSearchChanged(string? value) => Filter();

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _all = await Get<AccountingService>().AccountsAsync();
            Filter();
        });
    }

    private void Filter()
    {
        Items.Clear();
        foreach (var a in _all.Where(a => string.IsNullOrWhiteSpace(Search) || a.Code.Contains(Search) || a.NameEn.Contains(Search, StringComparison.OrdinalIgnoreCase) || a.NameAr.Contains(Search)))
            Items.Add(a);
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new AccountEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(AccountRow? a) { if (a != null && await Dialogs.ShowAsync(new AccountEditorViewModel(a.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(AccountRow? a)
    {
        if (a == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", a.Code), danger: true)) return;
        if (await RunAsync(() => Get<AccountingService>().DeleteAccountAsync(a.Id), "Msg.Deleted")) await LoadAsync();
    }

    [RelayCommand] private void Ledger(AccountRow? a) { if (a != null) Nav.Navigate(new AccountingViewModel("Ledger", accountId: a.Id)); }

    [RelayCommand]
    private async Task OpeningBalance(AccountRow? a)
    {
        if (a == null) return;
        var s = await Dialogs.PromptAsync("Accounting.OpeningBalance", "Accounting.OpeningAmountHint");
        if (s == null || !decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount)) return;
        if (await RunAsync(() => Get<AccountingService>().PostAccountOpeningBalanceAsync(a.Id, amount, Today), "Msg.Posted")) await LoadAsync();
    }

    public string AccountName(AccountRow a) => L.IsRightToLeft ? a.NameAr : a.NameEn;
}

public sealed partial class AccountEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public AccountEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Account.New" : "Account.Edit";
    public override double DialogWidth => 560;
    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _nameEn;
    [ObservableProperty] private string? _nameAr;
    [ObservableProperty] private AccountType _type = AccountType.Expense;
    [ObservableProperty] private Account? _parent;
    [ObservableProperty] private bool _isPostable = true;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _isCashOrBank;
    [ObservableProperty] private bool _isSystem;
    public IReadOnlyList<AccountType> Types { get; } = Enum.GetValues<AccountType>();
    public ObservableCollection<Account> Parents { get; } = new();

    partial void OnTypeChanged(AccountType value) => _ = LoadParentsAsync();

    private async Task LoadParentsAsync()
    {
        var all = await Get<AccountingService>().AccountsAsync();
        Parents.Clear();
        foreach (var a in all.Where(a => !a.IsPostable && a.Type == Type)) Parents.Add(new Account { Id = a.Id, Code = a.Code, NameEn = a.NameEn, NameAr = a.NameAr });
    }

    public override async Task InitializeAsync()
    {
        await LoadParentsAsync();
        if (_id == 0) return;
        var a = (await Get<AccountingService>().AccountsAsync()).FirstOrDefault(x => x.Id == _id);
        if (a == null) return;
        Code = a.Code; NameEn = a.NameEn; NameAr = a.NameAr; Type = a.Type; IsPostable = a.IsPostable; IsActive = a.IsActive; IsSystem = a.IsSystem;
        await LoadParentsAsync();
        Parent = Parents.FirstOrDefault(p => p.Code == a.ParentCode);
    }

    protected override Task SaveAsync() => Get<AccountingService>().SaveAccountAsync(new Account
    {
        Id = _id, Code = Code ?? "", NameEn = NameEn ?? "", NameAr = NameAr ?? "", Type = Type, ParentId = Parent?.Id, IsPostable = IsPostable, IsActive = IsActive, IsCashOrBank = IsCashOrBank
    });
}

public sealed partial class JournalsViewModel : ListPageViewModel<JournalRow>
{
    public override string TitleKey => "Accounting.Journals";
    protected override AppModule Module => AppModule.Accounting;
    [ObservableProperty] private bool _manualOnly;
    [ObservableProperty] private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _to = DateTime.Today;
    partial void OnManualOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    protected override Task<PagedResult<JournalRow>> FetchAsync(PageRequest r) =>
        Get<AccountingService>().ListEntriesAsync(r, new DateRange(From?.Date ?? Today.AddYears(-10), To?.Date ?? Today), manualOnly: ManualOnly);

    public override string? RowClass(object row) => row switch
    {
        JournalRow { Status: JournalStatus.Draft } => "warning",
        JournalRow { Status: JournalStatus.Reversed } => "negative",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<JournalRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Entry").Col("date", "Col.Date", K.Date).Col("desc", "Col.Description", width: 3).Col("src", "Col.Source").Col("doc", "Col.Document")
            .Col("dr", "Col.Debit", K.Money, total: true).Col("cr", "Col.Credit", K.Money, total: true).Col("status", "Col.Status");
        foreach (var j in rows) t.Add(j.Number, j.Date, j.Description, L.Source(j.SourceType), j.SourceNumber, j.TotalDebit, j.TotalCredit, j.Status);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new JournalEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(JournalRow? r) { if (r != null) { await Dialogs.ShowAsync(new JournalEditorViewModel(r.Id)); await LoadAsync(); } }
}

public sealed partial class JournalLineVm : ObservableObject
{
    [ObservableProperty] private Account? _account;
    [ObservableProperty] private decimal _debit;
    [ObservableProperty] private decimal _credit;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _tags;
}

public sealed partial class JournalEditorViewModel : DialogViewModel
{
    private long _id;
    public JournalEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Journal.New" : "Journal.Title";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} {Number}";
    public override double DialogWidth => 980;
    public override bool ShowSave => IsDraft;
    [ObservableProperty] private string? _number;
    [ObservableProperty] private JournalStatus _status;
    [ObservableProperty] private bool _isManual = true;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _source;
    [ObservableProperty] private string? _reversalInfo;
    public ObservableCollection<JournalLineVm> Lines { get; } = new();
    public List<Account> Accounts { get; private set; } = new();
    public bool IsDraft => Status == JournalStatus.Draft && IsManual;
    public bool CanPost => _id != 0 && Status == JournalStatus.Draft;
    public bool CanReverse => Status == JournalStatus.Posted && IsManual;
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public decimal Difference => TotalDebit - TotalCredit;

    private void Raise()
    {
        foreach (var p in new[] { nameof(IsDraft), nameof(CanPost), nameof(CanReverse), nameof(TotalDebit), nameof(TotalCredit), nameof(Difference), nameof(ShowSave), nameof(Title) }) OnPropertyChanged(p);
    }

    public override async Task InitializeAsync()
    {
        Accounts = await Get<AccountingService>().PostableAccountsAsync();
        OnPropertyChanged(nameof(Accounts));
        if (_id == 0) { AddLine(); AddLine(); Raise(); return; }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var e = await Get<AccountingService>().GetEntryAsync(_id);
        if (e == null) return;
        Number = e.Number; Status = e.Status; IsManual = e.IsManual; Date = e.Date; Description = e.Description; Source = $"{L.Source(e.SourceType)} {e.SourceNumber}".Trim();
        ReversalInfo = e.ReversalOfId != null ? L["Journal.IsReversal"] : e.ReversedById != null ? L["Journal.WasReversed"] : null;
        Lines.Clear();
        var rows = await Get<AccountingService>().EntryLinesAsync(_id);
        foreach (var l in e.Lines.OrderBy(x => x.Id))
        {
            var info = rows.FirstOrDefault(r => r.Id == l.Id);
            var vm = new JournalLineVm { Account = Accounts.FirstOrDefault(a => a.Id == l.AccountId) ?? l.Account, Debit = l.Debit, Credit = l.Credit, Description = l.Description,
                Tags = string.Join(" · ", new[] { info?.Job, info?.Customer, info?.Supplier, info?.CostCenter }.Where(x => !string.IsNullOrEmpty(x))) };
            vm.PropertyChanged += (_, _) => Raise();
            Lines.Add(vm);
        }
        Raise();
    }

    [RelayCommand]
    private void AddLine()
    {
        var vm = new JournalLineVm();
        vm.PropertyChanged += (_, _) => Raise();
        Lines.Add(vm);
        Raise();
    }

    [RelayCommand] private void RemoveLine(JournalLineVm? l) { if (l != null) { Lines.Remove(l); Raise(); } }

    protected override async Task SaveAsync()
    {
        _id = await Get<AccountingService>().SaveManualDraftAsync(_id, Date?.Date ?? Today, Description ?? "",
            Lines.Where(l => l.Account != null || l.Debit != 0 || l.Credit != 0).Select(l => new ManualJournalLine(l.Account?.Id ?? 0, l.Debit, l.Credit, l.Description)).ToList());
    }

    [RelayCommand]
    private async Task Post()
    {
        if (IsDraft && !await RunAsync(SaveAsync)) return;
        if (await RunAsync(() => Get<AccountingService>().PostManualAsync(_id), "Msg.Posted")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task Reverse()
    {
        var reason = await Dialogs.PromptAsync("Journal.Reverse", "Col.Reason");
        if (reason == null) return;
        if (await RunAsync(() => Get<AccountingService>().ReverseAsync(_id, Today, reason), "Msg.Reversed")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeleteDraft()
    {
        if (!await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", Number ?? ""), danger: true)) return;
        if (await RunAsync(() => Get<AccountingService>().DeleteDraftAsync(_id))) Cancel();
    }
}

public sealed partial class LedgerViewModel : ViewModelBase
{
    private readonly long? _accountId;
    private readonly string? _accountKey;

    public LedgerViewModel(long? accountId = null, string? accountKey = null)
    {
        _accountId = accountId;
        _accountKey = accountKey;
    }

    [ObservableProperty] private Account? _account;
    [ObservableProperty] private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _to = DateTime.Today;
    [ObservableProperty] private LedgerRow? _selected;
    public List<Account> Accounts { get; private set; } = new();
    public ObservableCollection<LedgerRow> Rows { get; } = new();
    public decimal ClosingBalance => Rows.LastOrDefault()?.Balance ?? 0;

    partial void OnAccountChanged(Account? value) => _ = RefreshAsync();
    partial void OnFromChanged(DateTime? value) => _ = RefreshAsync();
    partial void OnToChanged(DateTime? value) => _ = RefreshAsync();

    public async Task LoadAsync()
    {
        Accounts = await Get<AccountingService>().PostableAccountsAsync();
        OnPropertyChanged(nameof(Accounts));
        Account = Accounts.FirstOrDefault(a => a.Id == _accountId) ?? Accounts.FirstOrDefault(a => a.SystemKey == (_accountKey ?? SystemAccounts.Cash));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Account == null) return;
        await RunAsync(async () =>
        {
            var rows = await Get<AccountingService>().GeneralLedgerAsync(Account.Id, new DateRange(From?.Date ?? Today.AddYears(-1), To?.Date ?? Today));
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            OnPropertyChanged(nameof(ClosingBalance));
        });
    }

    [RelayCommand] private async Task OpenEntry(LedgerRow? r) { if (r is { EntryId: > 0 }) await Dialogs.ShowAsync(new JournalEditorViewModel(r.EntryId)); }

    [RelayCommand]
    private void Report() { if (Account != null) Nav.Navigate(new ReportsViewModel("GeneralLedger", new DateRange(From?.Date ?? Today, To?.Date ?? Today), accountId: Account.Id)); }
}

public sealed partial class TrialBalanceViewModel : ViewModelBase
{
    [ObservableProperty] private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _to = DateTime.Today;
    public ObservableCollection<TrialBalanceRow> Rows { get; } = new();
    public decimal TotalDebit => Rows.Sum(r => r.ClosingDebit);
    public decimal TotalCredit => Rows.Sum(r => r.ClosingCredit);
    public bool Balanced => TotalDebit == TotalCredit;

    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var rows = await Get<AccountingService>().TrialBalanceAsync(new DateRange(From?.Date ?? Today.AddYears(-1), To?.Date ?? Today));
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            OnPropertyChanged(nameof(TotalDebit)); OnPropertyChanged(nameof(TotalCredit)); OnPropertyChanged(nameof(Balanced));
        });
    }
}

public sealed partial class FiscalPeriodsViewModel : ViewModelBase
{
    public ObservableCollection<FiscalPeriod> Periods { get; } = new();
    [ObservableProperty] private FiscalPeriod? _selected;
    [ObservableProperty] private DateTime? _newYearStart = new DateTime(DateTime.Today.Year + 1, 1, 1);

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Periods.Clear();
            foreach (var y in await Get<AccountingService>().FiscalYearsAsync())
                foreach (var p in y.Periods.OrderBy(p => p.PeriodNo)) { p.FiscalYear = y; Periods.Add(p); }
        });
    }

    [RelayCommand] private async Task Close(FiscalPeriod? p) { if (p != null && await RunAsync(() => Get<AccountingService>().SetPeriodClosedAsync(p.Id, true), "Msg.Saved")) await LoadAsync(); }
    [RelayCommand] private async Task Reopen(FiscalPeriod? p) { if (p != null && await RunAsync(() => Get<AccountingService>().SetPeriodClosedAsync(p.Id, false), "Msg.Saved")) await LoadAsync(); }
    [RelayCommand] private async Task CreateYear() { if (await RunAsync(() => Get<AccountingService>().CreateFiscalYearAsync(NewYearStart?.Date ?? Today), "Msg.Saved")) await LoadAsync(); }
}

public sealed partial class ReconciliationViewModel : ViewModelBase
{
    public ObservableCollection<ReconciliationCheck> Checks { get; } = new();
    [ObservableProperty] private bool _allPassed;

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Checks.Clear();
            foreach (var c in await Get<ReconciliationService>().RunAsync()) Checks.Add(c);
            AllPassed = Checks.All(c => c.Passed);
        });
    }
}

public sealed partial class AccountingViewModel : PageViewModel
{
    public AccountingViewModel(string? tab = null, string? accountKey = null, long? accountId = null)
    {
        Ledger = new LedgerViewModel(accountId, accountKey);
        SelectedTab = tab switch { "Journals" => 1, "Ledger" => 2, "TrialBalance" => 3, "Periods" => 4, "Reconciliation" => 5, _ => 0 };
    }

    public override string TitleKey => "Nav.Accounting";
    public ChartOfAccountsViewModel Chart { get; } = new();
    public JournalsViewModel Journals { get; } = new();
    public LedgerViewModel Ledger { get; }
    public TrialBalanceViewModel TrialBalance { get; } = new();
    public FiscalPeriodsViewModel Periods { get; } = new();
    public ReconciliationViewModel Reconciliation { get; } = new();
    [ObservableProperty] private int _selectedTab;

    public override async Task LoadAsync()
    {
        await Chart.LoadAsync();
        await Journals.LoadAsync();
        await Ledger.LoadAsync();
        await TrialBalance.LoadAsync();
        await Periods.LoadAsync();
        await Reconciliation.LoadAsync();
    }
}
