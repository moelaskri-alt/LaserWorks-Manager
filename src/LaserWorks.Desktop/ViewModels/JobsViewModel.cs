using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class JobsViewModel : ListPageViewModel<JobRow>
{
    public override string TitleKey => "Nav.Jobs";
    protected override AppModule Module => AppModule.Jobs;

    public List<Option<JobStatus>> Statuses { get; } = Options.ForEnum<JobStatus>();
    public IReadOnlyList<string> Views { get; } = new[] { "All", "Open", "DueSoon", "Overdue" };
    [ObservableProperty] private Option<JobStatus>? _status;
    [ObservableProperty] private string _view = "All";

    public JobsViewModel(JobFilter? filter = null)
    {
        _status = filter?.Status is { } st ? Statuses.First(s => Equals(s.Value, st)) : Statuses[0];
        _view = filter switch { { Overdue: true } => "Overdue", { DueSoon: true } => "DueSoon", { OpenOnly: true } => "Open", _ => "All" };
    }

    partial void OnStatusChanged(Option<JobStatus>? value) => _ = LoadAsync();
    partial void OnViewChanged(string value) => _ = LoadAsync();

    protected override Task<PagedResult<JobRow>> FetchAsync(PageRequest r) =>
        Get<JobService>().ListAsync(r, new JobFilter(Status?.Value, OpenOnly: View == "Open", DueSoon: View == "DueSoon", Overdue: View == "Overdue"));

    public override string? RowClass(object row) => row switch
    {
        JobRow j when j.IsOverdue(Today) => "negative",
        JobRow { ActualCost: > 0 } j when j.ActualCost > j.SellingPrice => "warning",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<JobRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Job").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.5f).Col("title", "Col.Description", width: 2.2f)
            .Col("qty", "Col.Quantity", K.Number).Col("due", "Col.DueDate", K.Date).Col("priority", "Col.Priority").Col("status", "Col.Status").Col("est", "Col.EstimatedCost", K.Money, total: true)
            .Col("act", "Col.ActualCost", K.Money, total: true).Col("price", "Col.SellingPrice", K.Money, total: true).Col("margin", "Col.Margin", K.Percent);
        foreach (var j in rows) t.Add(j.Number, j.OrderDate, j.Customer, j.Title, j.Quantity, j.DueDate, j.Priority, j.Status, j.EstimatedCost, j.ActualCost, j.SellingPrice, j.MarginPercent);
        return t;
    }

    [RelayCommand] private void Open(JobRow? row) { if (row != null) Nav.Navigate(new JobDetailViewModel(row.Id)); }

    [RelayCommand]
    private async Task New()
    {
        if (await Dialogs.ShowAsync(new JobEditorViewModel(0))) await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete(JobRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Number), danger: true)) return;
        if (await RunAsync(() => Get<JobService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

/// <summary>Create a direct job (walk-in work without a quotation) or edit job planning fields.</summary>
public sealed partial class JobEditorViewModel : DialogViewModel
{
    private readonly long _id;

    public JobEditorViewModel(long id) => _id = id;

    public override string TitleKey => _id == 0 ? "Job.New" : "Job.Edit";
    public override double DialogWidth => 760;

    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private string? _title;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private DateTime? _orderDate = DateTime.Today;
    [ObservableProperty] private DateTime? _dueDate = DateTime.Today.AddDays(7);
    [ObservableProperty] private JobPriority _priority = JobPriority.Normal;
    [ObservableProperty] private decimal _estimatedCost;
    [ObservableProperty] private decimal _sellingPrice;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private Lookup? _operator;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _fromQuotation;
    [ObservableProperty] private bool _isInvoiced;

    public List<Lookup> Customers { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public List<Lookup> Employees { get; private set; } = new();
    public IReadOnlyList<JobPriority> Priorities { get; } = Enum.GetValues<JobPriority>();
    public bool CostEditable => !FromQuotation;

    partial void OnFromQuotationChanged(bool value) => OnPropertyChanged(nameof(CostEditable));

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync(activeOnly: false);
        Machines = await Get<MachineService>().LookupAsync();
        Employees = await Get<EmployeeService>().LookupAsync();
        OnPropertyChanged(nameof(Customers)); OnPropertyChanged(nameof(Machines)); OnPropertyChanged(nameof(Employees));
        if (_id == 0) return;
        var j = await Get<JobService>().GetAsync(_id);
        if (j == null) return;
        Customer = Customers.FirstOrDefault(c => c.Id == j.CustomerId); Title = j.Title; Description = j.Description; Quantity = j.Quantity; OrderDate = j.OrderDate; DueDate = j.DueDate;
        Priority = j.Priority; EstimatedCost = j.EstimatedCost; SellingPrice = j.SellingPrice; Machine = Machines.FirstOrDefault(m => m.Id == j.MachineId);
        Operator = Employees.FirstOrDefault(e => e.Id == j.OperatorId); Notes = j.Notes; FromQuotation = j.QuotationId != null || j.EstimateId != null; IsInvoiced = j.Status >= JobStatus.Invoiced;
    }

    protected override async Task SaveAsync()
    {
        await Get<JobService>().SaveAsync(new Job
        {
            Id = _id, CustomerId = Customer?.Id ?? 0, Title = Title ?? "", Description = Description, Quantity = Quantity, OrderDate = OrderDate?.Date ?? Today, DueDate = DueDate?.Date,
            Priority = Priority, EstimatedCost = EstimatedCost, SellingPrice = SellingPrice, MachineId = Machine?.Id, OperatorId = Operator?.Id, Notes = Notes
        });
    }
}
