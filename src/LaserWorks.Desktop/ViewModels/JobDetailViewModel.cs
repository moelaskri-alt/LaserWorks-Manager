using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

/// <summary>Everything about one job: production, materials, scrap, quality, estimated vs actual cost and profitability.</summary>
public sealed partial class JobDetailViewModel : PageViewModel
{
    private readonly long _id;

    public JobDetailViewModel(long id) => _id = id;

    public long JobId => _id;
    public override string TitleKey => "Job.Title";

    [ObservableProperty] private Job? _job;
    [ObservableProperty] private JobCostSheet? _sheet;
    [ObservableProperty] private OperationRow? _selectedOperation;
    [ObservableProperty] private int _selectedTab;
    public ObservableCollection<OperationRow> Operations { get; } = new();
    public ObservableCollection<JobMaterialLine> Materials { get; } = new();
    public ObservableCollection<ScrapRow> Scrap { get; } = new();
    public ObservableCollection<QualityRow> Quality { get; } = new();
    public ObservableCollection<CostSheetEntry> Entries { get; } = new();
    public ObservableCollection<VarianceLine> Variance { get; } = new();
    public ObservableCollection<InventoryTxRow> Movements { get; } = new();
    public ObservableCollection<AttachmentRow> Attachments { get; } = new();

    public string Header => Job == null ? "" : $"{Job.Number} — {Job.Title}";
    public JobStatus Status => Job?.Status ?? JobStatus.New;
    public bool IsOpen => Job != null && Status is not (JobStatus.Closed or JobStatus.Cancelled);
    public bool CanPlan => Status == JobStatus.New;
    public bool CanStart => Status is JobStatus.New or JobStatus.Planned;
    public bool CanCompleteProduction => Status is JobStatus.New or JobStatus.Planned or JobStatus.InProduction;
    public bool CanQualityCheck => Status is JobStatus.QualityCheck or JobStatus.InProduction or JobStatus.Completed;
    public bool CanDeliver => Status == JobStatus.Completed;
    public bool CanInvoice => Status is JobStatus.Completed or JobStatus.Delivered;
    public bool CanClose => Status == JobStatus.Invoiced;
    public bool CanCancel => Status is JobStatus.New or JobStatus.Planned;
    public bool CanRecordCost => IsOpen && Status != JobStatus.Invoiced || Status == JobStatus.Invoiced;
    public string? MainDriver => Sheet?.Variance.MainDriver is { } d ? L.Enum(d) : null;

    partial void OnJobChanged(Job? value)
    {
        foreach (var p in new[] { nameof(Header), nameof(Status), nameof(IsOpen), nameof(CanPlan), nameof(CanStart), nameof(CanCompleteProduction), nameof(CanQualityCheck),
                     nameof(CanDeliver), nameof(CanInvoice), nameof(CanClose), nameof(CanCancel), nameof(CanRecordCost) })
            OnPropertyChanged(p);
    }

    partial void OnSheetChanged(JobCostSheet? value) => OnPropertyChanged(nameof(MainDriver));

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Job = await Get<JobService>().GetAsync(_id);
            Sheet = await Get<JobCostingService>().CostSheetAsync(_id);
            Fill(Operations, Sheet.Operations);
            Fill(Materials, Sheet.Materials);
            Fill(Scrap, Sheet.Scrap);
            Fill(Quality, Sheet.Quality);
            Fill(Entries, Sheet.Entries);
            Fill(Variance, Sheet.Variance.Lines);
            Fill(Movements, (await Get<InventoryService>().ListTransactionsAsync(new PageRequest(PageSize: 1000), jobId: _id)).Items);
            Fill(Attachments, await Get<RequestService>().AttachmentsAsync(AttachmentOwner.Job, _id));
        });
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    private async Task Do(Func<Task> action, string? success = "Msg.Saved")
    {
        if (await RunAsync(action, success)) await LoadAsync();
    }

    private async Task Show(DialogViewModel dialog)
    {
        if (await Dialogs.ShowAsync(dialog)) await LoadAsync();
    }

    [RelayCommand] private void Back() => Nav.Go("Jobs");
    [RelayCommand] private Task Edit() => Show(new JobEditorViewModel(_id));
    [RelayCommand] private Task Plan() => Do(() => Get<JobService>().SetStatusAsync(_id, JobStatus.Planned));
    [RelayCommand] private Task Start() => Do(() => Get<JobService>().SetStatusAsync(_id, JobStatus.InProduction));
    [RelayCommand] private Task CompleteProduction() => Do(() => Get<ProductionService>().CompleteProductionAsync(_id), "Msg.ProductionCompleted");
    [RelayCommand] private Task QualityCheck() => Show(new QualityCheckDialogViewModel(_id, Job?.Quantity ?? 0));
    [RelayCommand] private Task Deliver() => Show(new DeliverDialogViewModel(_id));
    [RelayCommand] private Task Close() => Do(() => Get<JobService>().CloseAsync(_id), "Msg.JobClosed");

    [RelayCommand]
    private async Task Cancel()
    {
        if (!await Dialogs.ConfirmAsync(L["Msg.ConfirmCancelJob"], danger: true)) return;
        await Do(() => Get<JobService>().SetStatusAsync(_id, JobStatus.Cancelled));
    }

    [RelayCommand]
    private async Task CreateInvoice()
    {
        long invoiceId = 0;
        if (!await RunAsync(async () => invoiceId = await Get<SalesService>().CreateInvoiceFromJobAsync(_id))) return;
        await Dialogs.ShowAsync(new InvoiceEditorViewModel(invoiceId));
        await LoadAsync();
    }

    // materials
    [RelayCommand] private Task IssueMaterial() => Show(new StockMovementDialogViewModel(StockAction.IssueToJob, _id));
    [RelayCommand] private Task ReturnMaterial() => Show(new StockMovementDialogViewModel(StockAction.ReturnFromJob, _id));
    [RelayCommand] private Task UseRemnant() => Show(new RemnantDialogViewModel(RemnantAction.Consume, _id));
    [RelayCommand] private Task CreateRemnant() => Show(new RemnantDialogViewModel(RemnantAction.CreateFromJob, _id));

    // operations
    [RelayCommand] private Task AddOperation() => Show(new OperationEditorViewModel(0, _id));
    [RelayCommand] private async Task EditOperation(OperationRow? o) { if (o != null) await Show(new OperationEditorViewModel(o.Id, _id)); }
    [RelayCommand] private async Task StartOperation(OperationRow? o) { if (o != null) await Do(() => Get<ProductionService>().StartOperationAsync(o.Id), null); }
    [RelayCommand] private async Task CompleteOperation(OperationRow? o) { if (o != null) await Show(new CompleteOperationDialogViewModel(o)); }

    [RelayCommand]
    private async Task ReopenOperation(OperationRow? o)
    {
        if (o == null) return;
        var reason = await Dialogs.PromptAsync("Operation.Reopen", "Col.Reason");
        if (reason != null) await Do(() => Get<ProductionService>().ReopenOperationAsync(o.Id, reason), "Msg.Reversed");
    }

    [RelayCommand]
    private async Task DeleteOperation(OperationRow? o)
    {
        if (o == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", L.Enum(o.OperationType)), danger: true)) return;
        await Do(() => Get<ProductionService>().DeleteOperationAsync(o.Id), "Msg.Deleted");
    }

    // scrap / rework
    [RelayCommand] private Task RecordScrap() => Show(new ScrapDialogViewModel(_id, false));
    [RelayCommand] private Task RecordRework() => Show(new ScrapDialogViewModel(_id, true));
    [RelayCommand] private Task AddExpense() => Show(new ExpenseEditorViewModel(0, _id));

    // attachments
    [RelayCommand]
    private async Task AddAttachment()
    {
        var file = await Dialogs.OpenFileAsync();
        if (file != null) await Do(() => Get<RequestService>().AddAttachmentAsync(AttachmentOwner.Job, _id, file), null);
    }

    [RelayCommand] private void OpenAttachment(AttachmentRow? a) { if (a != null) Dialogs.OpenFile(Get<RequestService>().ResolveAttachment(a.StoredPath)); }

    [RelayCommand]
    private async Task OpenSource(string what)
    {
        if (Job == null) return;
        switch (what)
        {
            case "Customer": Nav.Navigate(new CustomerProfileViewModel(Job.CustomerId)); break;
            case "Quotation" when Job.QuotationId is { } q: await Dialogs.ShowAsync(new QuotationEditorViewModel(q)); break;
            case "Estimate" when Job.EstimateId is { } e: Nav.Navigate(new EstimateEditorViewModel(e)); break;
            case "Request" when Job.RequestId is { } r: await Dialogs.ShowAsync(new RequestEditorViewModel(r)); break;
        }
    }

    [RelayCommand]
    private async Task CostSheet(string format)
    {
        await RunAsync(async () =>
        {
            var sheet = await Get<JobCostingService>().CostSheetAsync(_id);
            var company = await Get<SettingsService>().GetAsync();
            if (format == "xlsx" || format == "csv")
            {
                var table = await Get<ReportCatalog>().RunAsync("JobCostSheet", new ReportFilter(new DateRange(Today, Today), Today, JobId: _id));
                var p = await Dialogs.SaveFileAsync($"{L["Rpt.JobCostSheet"]}_{sheet.Job.Number}", format);
                if (p == null) return;
                await ReportFiles.SaveAsync(table, format, p);
                InfoMessage = L.Format("Msg.Exported", Path.GetFileName(p));
                return;
            }
            var bytes = await Task.Run(() => DocumentRenderer.JobCostSheet(sheet, company));
            if (format == "print") { await ReportFiles.PrintBytesAsync(bytes, $"JobCostSheet_{sheet.Job.Number}"); return; }
            var path = await Dialogs.SaveFileAsync($"{L["Rpt.JobCostSheet"]}_{sheet.Job.Number}", "pdf");
            if (path == null) return;
            await File.WriteAllBytesAsync(path, bytes);
            InfoMessage = L.Format("Msg.Exported", Path.GetFileName(path));
            Dialogs.OpenFile(path);
        });
    }
}

// ================================================================== job action dialogs

public sealed partial class DeliverDialogViewModel : DialogViewModel
{
    private readonly long _jobId;
    public DeliverDialogViewModel(long jobId) => _jobId = jobId;
    public override string TitleKey => "Job.Deliver";
    public override double DialogWidth => 460;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _note;
    protected override Task SaveAsync() => Get<JobService>().DeliverAsync(_jobId, Date?.Date ?? Today, Note);
}

public sealed partial class QualityCheckDialogViewModel : DialogViewModel
{
    private readonly long _jobId;

    public QualityCheckDialogViewModel(long jobId, decimal quantity)
    {
        _jobId = jobId;
        _quantityProduced = quantity;
        _quantityAccepted = quantity;
    }

    public override string TitleKey => "Quality.Record";
    public override double DialogWidth => 560;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private Lookup? _inspector;
    [ObservableProperty] private decimal _quantityProduced;
    [ObservableProperty] private decimal _quantityAccepted;
    [ObservableProperty] private decimal _quantityRejected;
    [ObservableProperty] private decimal _reworkQuantity;
    [ObservableProperty] private QualityStatus _status = QualityStatus.Passed;
    [ObservableProperty] private string? _notes;
    public List<Lookup> Inspectors { get; private set; } = new();
    public IReadOnlyList<QualityStatus> Statuses { get; } = new[] { QualityStatus.Passed, QualityStatus.ReworkRequired, QualityStatus.Failed, QualityStatus.Pending };

    public override async Task InitializeAsync()
    {
        Inspectors = await Get<EmployeeService>().LookupAsync();
        OnPropertyChanged(nameof(Inspectors));
    }

    protected override Task SaveAsync() => Get<ProductionService>().RecordQualityCheckAsync(new QualityCheck
    {
        JobId = _jobId, Date = Date?.Date ?? Today, InspectorId = Inspector?.Id, QuantityProduced = QuantityProduced, QuantityAccepted = QuantityAccepted,
        QuantityRejected = QuantityRejected, ReworkQuantity = ReworkQuantity, Status = Status, Notes = Notes
    });
}

public sealed partial class OperationEditorViewModel : DialogViewModel
{
    private readonly long _id;
    private readonly long _jobId;

    public OperationEditorViewModel(long id, long jobId) { _id = id; _jobId = jobId; }

    public override string TitleKey => _id == 0 ? "Operation.New" : "Operation.Edit";
    public override double DialogWidth => 600;
    [ObservableProperty] private OperationType _operationType = OperationType.Cutting;
    [ObservableProperty] private Lookup? _employee;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private decimal _plannedHours;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private bool _isRework;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private int _sequence;
    public List<Lookup> Employees { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public IReadOnlyList<OperationType> Types { get; } = Enum.GetValues<OperationType>();

    public override async Task InitializeAsync()
    {
        Employees = await Get<EmployeeService>().LookupAsync();
        Machines = await Get<MachineService>().LookupAsync();
        OnPropertyChanged(nameof(Employees)); OnPropertyChanged(nameof(Machines));
        if (_id == 0)
        {
            var job = await Get<JobService>().GetAsync(_jobId);
            Quantity = job?.Quantity ?? 0;
            Machine = Machines.FirstOrDefault(m => m.Id == job?.MachineId);
            Employee = Employees.FirstOrDefault(e => e.Id == job?.OperatorId);
            return;
        }
        var o = await Get<ProductionService>().GetOperationAsync(_id);
        if (o == null) return;
        OperationType = o.OperationType; Employee = Employees.FirstOrDefault(e => e.Id == o.EmployeeId); Machine = Machines.FirstOrDefault(m => m.Id == o.MachineId);
        PlannedHours = o.PlannedHours; Quantity = o.Quantity; IsRework = o.IsRework; Notes = o.Notes; Sequence = o.Sequence;
    }

    protected override Task SaveAsync() => Get<ProductionService>().SaveOperationAsync(new JobOperation
    {
        Id = _id, JobId = _jobId, OperationType = OperationType, EmployeeId = Employee?.Id, MachineId = Machine?.Id, PlannedHours = PlannedHours, Quantity = Quantity,
        IsRework = IsRework, Notes = Notes, Sequence = Sequence
    });
}

public sealed partial class CompleteOperationDialogViewModel : DialogViewModel
{
    private readonly OperationRow _op;

    public CompleteOperationDialogViewModel(OperationRow op)
    {
        _op = op;
        _quantity = op.Quantity;
        _laborHours = op.PlannedHours;
        _machineHours = op.Machine != null ? op.PlannedHours : 0;
        _startTime = op.StartTime ?? DateTime.Now.AddHours(-(double)Math.Max(op.PlannedHours, 0.25m));
        _endTime = DateTime.Now;
    }

    public override string TitleKey => "Operation.Complete";
    public override string Title => $"{L[TitleKey]} — {_op.JobNumber} / {L.Enum(_op.OperationType)}";
    public override double DialogWidth => 640;
    public override string SaveKey => "Operation.PostCost";
    [ObservableProperty] private decimal _laborHours;
    [ObservableProperty] private decimal _machineHours;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _scrapQuantity;
    [ObservableProperty] private decimal _reworkQuantity;
    [ObservableProperty] private DateTime? _startTime;
    [ObservableProperty] private DateTime? _endTime;
    [ObservableProperty] private Lookup? _employee;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _costPreview;
    public List<Lookup> Employees { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public decimal PlannedHours => _op.PlannedHours;

    private Dictionary<long, MachineRateBreakdown> _rates = new();
    private Dictionary<long, decimal> _empRates = new();

    public override async Task InitializeAsync()
    {
        Employees = await Get<EmployeeService>().LookupAsync();
        Machines = await Get<MachineService>().LookupAsync();
        _rates = await Get<EstimateService>().MachineRatesAsync();
        _empRates = (await Get<EmployeeService>().ListAsync(new PageRequest(PageSize: 5000))).Items.ToDictionary(e => e.Id, e => e.HourlyCost);
        OnPropertyChanged(nameof(Employees)); OnPropertyChanged(nameof(Machines));
        Employee = Employees.FirstOrDefault(e => e.Name == _op.Employee);
        Machine = Machines.FirstOrDefault(m => m.Name == _op.Machine);
        UpdatePreview();
    }

    partial void OnLaborHoursChanged(decimal value) => UpdatePreview();
    partial void OnMachineHoursChanged(decimal value) => UpdatePreview();
    partial void OnEmployeeChanged(Lookup? value) => UpdatePreview();
    partial void OnMachineChanged(Lookup? value) => UpdatePreview();

    private void UpdatePreview()
    {
        var mr = Machine != null && _rates.TryGetValue(Machine.Id, out var r) ? r.EffectiveRate : 0;
        var lr = Employee != null && _empRates.TryGetValue(Employee.Id, out var e) ? e : Get<SettingsService>().Current.DefaultLaborRate;
        CostPreview = L.Format("Operation.CostPreview", L.Money(MachineHours * mr), L.Money(LaborHours * lr), L.Money(MachineHours * mr + LaborHours * lr));
    }

    protected override Task SaveAsync() => Get<ProductionService>().CompleteOperationAsync(_op.Id,
        new OperationCompletion(LaborHours, MachineHours, Quantity, ScrapQuantity, ReworkQuantity, StartTime, EndTime, Employee?.Id, Machine?.Id, Notes));
}

public sealed partial class ScrapDialogViewModel : DialogViewModel
{
    private readonly long _jobId;

    public ScrapDialogViewModel(long jobId, bool rework)
    {
        _jobId = jobId;
        _type = rework ? ScrapType.Rework : ScrapType.NormalScrap;
    }

    public override string TitleKey => IsRework ? "Scrap.RecordRework" : "Scrap.RecordScrap";
    public override double DialogWidth => 620;
    [ObservableProperty] private ScrapType _type;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private Lookup? _employee;
    [ObservableProperty] private OperationRow? _operation;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _hours;
    [ObservableProperty] private decimal _cost;
    [ObservableProperty] private string? _reason;
    [ObservableProperty] private string? _notes;
    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public List<Lookup> Employees { get; private set; } = new();
    public List<OperationRow> Operations { get; private set; } = new();
    public IReadOnlyList<ScrapType> Types { get; } = Enum.GetValues<ScrapType>();
    public bool IsRework => Type == ScrapType.Rework;

    partial void OnTypeChanged(ScrapType value) { OnPropertyChanged(nameof(IsRework)); OnPropertyChanged(nameof(Title)); }

    public override async Task InitializeAsync()
    {
        var issued = await Get<InventoryService>().JobMaterialsAsync(_jobId);
        var all = await Get<MaterialService>().LookupAsync();
        Materials = all.Where(m => issued.Any(i => i.MaterialId == m.Id)).Concat(all.Where(m => issued.All(i => i.MaterialId != m.Id))).ToList();
        Machines = await Get<MachineService>().LookupAsync();
        Employees = await Get<EmployeeService>().LookupAsync();
        Operations = (await Get<ProductionService>().ListOperationsAsync(new PageRequest(PageSize: 200), _jobId)).Items.ToList();
        Material = Materials.FirstOrDefault(m => issued.Any(i => i.MaterialId == m.Id));
        foreach (var p in new[] { nameof(Materials), nameof(Machines), nameof(Employees), nameof(Operations) }) OnPropertyChanged(p);
    }

    protected override Task SaveAsync() => Get<ProductionService>().RecordScrapAsync(new ScrapRecord
    {
        JobId = _jobId, Type = Type, Date = Date?.Date ?? Today, MaterialId = IsRework ? null : Material?.Id, MachineId = Machine?.Id, EmployeeId = Employee?.Id,
        OperationId = Operation?.Id, Quantity = Quantity, Hours = Hours, Cost = Cost, Reason = Reason ?? "", Notes = Notes
    });
}
