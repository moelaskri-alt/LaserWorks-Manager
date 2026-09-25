using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class CustomersViewModel : ListPageViewModel<CustomerRow>
{
    public override string TitleKey => "Nav.Customers";
    protected override AppModule Module => AppModule.Customers;

    public IReadOnlyList<string> ActiveFilters { get; } = new[] { "All", "Active", "Inactive" };
    [ObservableProperty] private string _activeFilter = "Active";

    partial void OnActiveFilterChanged(string value) => _ = LoadAsync();

    protected override Task<PagedResult<CustomerRow>> FetchAsync(PageRequest r) =>
        Get<CustomerService>().ListAsync(r, ActiveFilter == "All" ? null : ActiveFilter == "Active");

    public override string? RowClass(object row) => row is CustomerRow { CreditLimit: > 0 } c && c.Balance > c.CreditLimit ? "warning" : null;

    protected override ReportTable BuildExport(IReadOnlyList<CustomerRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("name", "Col.Name", width: 2).Col("phone", "Col.Phone").Col("email", "Col.Email", width: 1.5f)
            .Col("tax", "Col.TaxNumber").Col("limit", "Col.CreditLimit", K.Money).Col("balance", "Col.Balance", K.Money, total: true).Col("active", "Col.Active");
        foreach (var c in rows) t.Add(c.Code, c.Name, c.Phone, c.Email, c.TaxNumber, c.CreditLimit, c.Balance, c.IsActive);
        return t;
    }

    [RelayCommand]
    private async Task New()
    {
        if (await Dialogs.ShowAsync(new CustomerEditorViewModel(0))) await LoadAsync();
    }

    [RelayCommand]
    private async Task Edit(CustomerRow? row)
    {
        if (row == null) return;
        if (await Dialogs.ShowAsync(new CustomerEditorViewModel(row.Id))) await LoadAsync();
    }

    [RelayCommand]
    private void Open(CustomerRow? row)
    {
        if (row != null) Nav.Navigate(new CustomerProfileViewModel(row.Id));
    }

    [RelayCommand]
    private async Task Delete(CustomerRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Name), danger: true)) return;
        if (await RunAsync(() => Get<CustomerService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

public sealed partial class CustomerEditorViewModel : DialogViewModel
{
    private readonly long _id;

    public CustomerEditorViewModel(long id) => _id = id;

    public override string TitleKey => _id == 0 ? "Customer.New" : "Customer.Edit";

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private decimal _creditLimit;
    [ObservableProperty] private int _paymentTermsDays;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private decimal _openingBalance;

    public bool IsNew => _id == 0;
    public bool CanPostOpening => IsNew && Can(AppModule.Accounting, Permission.Post);

    public override async Task InitializeAsync()
    {
        if (_id == 0) return;
        var c = await Get<CustomerService>().GetAsync(_id);
        if (c == null) return;
        Code = c.Code; Name = c.Name; Phone = c.Phone; Email = c.Email; Address = c.Address; TaxNumber = c.TaxNumber; Notes = c.Notes;
        CreditLimit = c.CreditLimit; PaymentTermsDays = c.PaymentTermsDays; IsActive = c.IsActive;
    }

    protected override async Task SaveAsync()
    {
        var id = await Get<CustomerService>().SaveAsync(new Customer
        {
            Id = _id, Code = Code ?? "", Name = Name ?? "", Phone = Phone, Email = Email, Address = Address, TaxNumber = TaxNumber, Notes = Notes,
            CreditLimit = CreditLimit, PaymentTermsDays = PaymentTermsDays, IsActive = IsActive
        });
        if (IsNew && OpeningBalance != 0) await Get<AccountingService>().PostPartyOpeningBalanceAsync(id, null, OpeningBalance, Today);
    }
}

public sealed record ProfileDocument(string Type, long Id, string Number, DateTime Date, string Status, decimal Amount)
{
    public string TypeLabel => LaserWorks.Localization.Loc.Instance["Doc." + Type];

    public string StatusLabel
    {
        get
        {
            var L = LaserWorks.Localization.Loc.Instance;
            var key = Type switch
            {
                "Request" => "Enum.RequestStatus." + Status,
                "Quotation" => "Enum.QuotationStatus." + Status,
                "Job" => "Enum.JobStatus." + Status,
                "Payment" => "Enum.PaymentMethod." + Status,
                _ => "Common." + Status
            };
            return L.Has(key) ? L[key] : Status;
        }
    }
}

public sealed partial class CustomerProfileViewModel : PageViewModel
{
    private readonly long _id;

    public CustomerProfileViewModel(long id) => _id = id;

    public override string TitleKey => "Customer.Profile";

    [ObservableProperty] private CustomerProfile? _profile;
    public ObservableCollection<ProfileDocument> Documents { get; } = new();
    public IReadOnlyList<string> DocTypes { get; } = new[] { "All", "Request", "Quotation", "Job", "Invoice", "Payment" };
    [ObservableProperty] private string _docType = "All";

    partial void OnDocTypeChanged(string value) => Fill();

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Profile = await Get<CustomerService>().ProfileAsync(_id);
            Fill();
        });
    }

    private void Fill()
    {
        Documents.Clear();
        if (Profile == null) return;
        foreach (var d in Profile.Documents.Where(d => DocType == "All" || d.Type == DocType))
            Documents.Add(new ProfileDocument(d.Type, d.Id, d.Number, d.Date, d.Status, d.Amount));
    }

    [RelayCommand]
    private async Task OpenDocument(ProfileDocument? d)
    {
        if (d == null) return;
        if (d.Type == "Payment") return;
        await Nav.OpenAsync(d.Type, d.Id);
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (await Dialogs.ShowAsync(new CustomerEditorViewModel(_id))) await LoadAsync();
    }

    [RelayCommand] private void Back() => Nav.Go("Customers");

    [RelayCommand]
    private async Task NewRequest()
    {
        if (await Dialogs.ShowAsync(new RequestEditorViewModel(0, _id))) await LoadAsync();
    }

    [RelayCommand]
    private async Task RecordPayment()
    {
        if (await Dialogs.ShowAsync(new PaymentEditorViewModel(_id, null))) await LoadAsync();
    }

    [RelayCommand]
    private void Statement() => Nav.Navigate(new ReportsViewModel("CustomerStatement", new DateRange(Today.AddYears(-1), Today), customerId: _id));
}
