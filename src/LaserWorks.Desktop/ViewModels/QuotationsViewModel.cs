using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class QuotationsViewModel : ListPageViewModel<QuotationRow>
{
    public override string TitleKey => "Nav.Quotations";
    protected override AppModule Module => AppModule.Quotations;

    public List<Option<QuotationStatus>> Statuses { get; } = Options.ForEnum<QuotationStatus>();
    [ObservableProperty] private Option<QuotationStatus>? _status;
    [ObservableProperty] private bool _pendingOnly;
    [ObservableProperty] private bool _showAllVersions;

    public QuotationsViewModel(bool pendingOnly = false)
    {
        _status = Statuses[0];
        _pendingOnly = pendingOnly;
    }

    partial void OnStatusChanged(Option<QuotationStatus>? value) => _ = LoadAsync();
    partial void OnPendingOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnShowAllVersionsChanged(bool value) => _ = LoadAsync();

    protected override async Task<PagedResult<QuotationRow>> FetchAsync(PageRequest r)
    {
        if (!PendingOnly) return await Get<QuotationService>().ListAsync(r, Status?.Value, latestOnly: !ShowAllVersions);
        var all = await Get<QuotationService>().ListAsync(r with { Page = 1, PageSize = 100_000 });
        var items = all.Items.Where(q => q.Status is QuotationStatus.Draft or QuotationStatus.Sent).ToList();
        return new PagedResult<QuotationRow>(items.Skip(r.Skip).Take(r.PageSize).ToList(), items.Count, r.Page, r.PageSize);
    }

    public override string? RowClass(object row) => row switch
    {
        QuotationRow { Status: QuotationStatus.Approved } => "subtotal",
        QuotationRow { Status: QuotationStatus.Rejected or QuotationStatus.Expired } => "warning",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<QuotationRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.5f).Col("desc", "Col.Description", width: 2.2f)
            .Col("cost", "Col.EstimatedCost", K.Money, total: true).Col("net", "Col.NetAmount", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true).Col("margin", "Col.Margin", K.Percent)
            .Col("valid", "Col.ValidUntil", K.Date).Col("status", "Col.Status").Col("job", "Col.Job");
        foreach (var q in rows) t.Add(q.DisplayNumber, q.Date, q.Customer, q.Description, q.EstimatedCost, q.NetAmount, q.Total, q.MarginPercent, q.ValidUntil, q.Status, q.JobNumber);
        return t;
    }

    [RelayCommand]
    private async Task Open(QuotationRow? row)
    {
        if (row == null) return;
        await Dialogs.ShowAsync(new QuotationEditorViewModel(row.Id));
        await LoadAsync();
    }

    [RelayCommand]
    private async Task New()
    {
        if (await Dialogs.ShowAsync(new QuotationEditorViewModel(0))) await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete(QuotationRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.DisplayNumber), danger: true)) return;
        if (await RunAsync(() => Get<QuotationService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

public sealed partial class QuotationEditorViewModel : DialogViewModel
{
    private long _id;

    public QuotationEditorViewModel(long id) => _id = id;

    public override string TitleKey => _id == 0 ? "Quotation.New" : "Quotation.Title";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} {Number} v{VersionNo}";
    public override double DialogWidth => 940;
    public override bool ShowSave => IsDraft;

    [ObservableProperty] private string? _number;
    [ObservableProperty] private int _versionNo = 1;
    [ObservableProperty] private QuotationStatus _status = QuotationStatus.Draft;
    [ObservableProperty] private bool _isLatest = true;
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private long? _requestId;
    [ObservableProperty] private long? _estimateId;
    [ObservableProperty] private string? _estimateNumber;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private decimal _estimatedCost;
    [ObservableProperty] private decimal _sellingPrice;
    [ObservableProperty] private decimal _discountAmount;
    [ObservableProperty] private decimal _taxRate;
    [ObservableProperty] private DateTime? _validUntil;
    [ObservableProperty] private int _deliveryDays = 7;
    [ObservableProperty] private string? _paymentTerms;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _jobNumber;
    [ObservableProperty] private long? _jobId;
    // job creation options
    [ObservableProperty] private DateTime? _jobDueDate;
    [ObservableProperty] private JobPriority _jobPriority = JobPriority.Normal;
    [ObservableProperty] private Lookup? _jobMachine;
    [ObservableProperty] private Lookup? _jobOperator;

    public List<Lookup> Customers { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public List<Lookup> Employees { get; private set; } = new();
    public IReadOnlyList<JobPriority> Priorities { get; } = Enum.GetValues<JobPriority>();
    public ObservableCollection<QuotationRow> Versions { get; } = new();

    public bool IsDraft => Status == QuotationStatus.Draft && IsLatest;
    public bool CanSend => _id != 0 && Status == QuotationStatus.Draft && IsLatest;
    public bool CanApprove => _id != 0 && Status is QuotationStatus.Draft or QuotationStatus.Sent && IsLatest && Can(AppModule.Quotations, Permission.Approve);
    public bool CanReject => _id != 0 && Status is QuotationStatus.Draft or QuotationStatus.Sent && IsLatest;
    public bool CanNewVersion => _id != 0 && IsLatest && JobId == null && Status != QuotationStatus.Draft;
    public bool CanCreateJob => Status == QuotationStatus.Approved && JobId == null && Can(AppModule.Jobs, Permission.Create);
    public bool HasJob => JobId != null;
    public bool IsSaved => _id != 0;
    public decimal NetAmount => SellingPrice - DiscountAmount;
    public decimal TaxAmount => Math.Round(Math.Max(0, NetAmount) * TaxRate / 100m, Decimals, MidpointRounding.AwayFromZero);
    public decimal Total => NetAmount + TaxAmount;
    public decimal Profit => NetAmount - EstimatedCost;
    public decimal MarginPercent => NetAmount == 0 ? 0 : Math.Round(Profit / NetAmount * 100m, 2);

    partial void OnSellingPriceChanged(decimal value) => RaiseTotals();
    partial void OnDiscountAmountChanged(decimal value) => RaiseTotals();
    partial void OnTaxRateChanged(decimal value) => RaiseTotals();
    partial void OnEstimatedCostChanged(decimal value) => RaiseTotals();

    private void RaiseTotals()
    {
        foreach (var p in new[] { nameof(NetAmount), nameof(TaxAmount), nameof(Total), nameof(Profit), nameof(MarginPercent) }) OnPropertyChanged(p);
    }

    private void RaiseState()
    {
        foreach (var p in new[] { nameof(IsDraft), nameof(CanSend), nameof(CanApprove), nameof(CanReject), nameof(CanNewVersion), nameof(CanCreateJob), nameof(HasJob), nameof(IsSaved), nameof(ShowSave), nameof(Title) })
            OnPropertyChanged(p);
    }

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync(activeOnly: false);
        Machines = await Get<MachineService>().LookupAsync();
        Employees = await Get<EmployeeService>().LookupAsync();
        OnPropertyChanged(nameof(Customers)); OnPropertyChanged(nameof(Machines)); OnPropertyChanged(nameof(Employees));
        if (_id == 0)
        {
            var s = await Get<SettingsService>().GetAsync();
            TaxRate = s.DefaultTaxRate;
            ValidUntil = Today.AddDays(s.QuotationValidityDays);
            PaymentTerms = s.DefaultPaymentTerms;
            RaiseState();
            return;
        }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var q = await Get<QuotationService>().GetAsync(_id);
        if (q == null) return;
        Number = q.Number; VersionNo = q.VersionNo; Status = q.Status; IsLatest = q.IsLatestVersion; Customer = Customers.FirstOrDefault(c => c.Id == q.CustomerId);
        RequestId = q.RequestId; EstimateId = q.EstimateId; EstimateNumber = q.Estimate?.Number; Date = q.Date; Description = q.Description; Quantity = q.Quantity;
        EstimatedCost = q.EstimatedCost; SellingPrice = q.SellingPrice; DiscountAmount = q.DiscountAmount; TaxRate = q.TaxRate; ValidUntil = q.ValidUntil;
        DeliveryDays = q.DeliveryDays; PaymentTerms = q.PaymentTerms; Notes = q.Notes;
        JobDueDate ??= Today.AddDays(Math.Max(1, q.DeliveryDays));
        var versions = await Get<QuotationService>().VersionsAsync(_id);
        Versions.Clear();
        foreach (var v in versions) Versions.Add(v);
        var job = await Get<QuotationService>().JobForAsync(_id);
        JobNumber = job?.Code;
        JobId = job?.Id;
        RaiseState();
    }

    protected override async Task SaveAsync()
    {
        _id = await Get<QuotationService>().SaveAsync(new Quotation
        {
            Id = _id, CustomerId = Customer?.Id ?? 0, RequestId = RequestId, EstimateId = EstimateId, Date = Date?.Date ?? Today, Description = Description ?? "", Quantity = Quantity,
            EstimatedCost = EstimatedCost, SellingPrice = SellingPrice, DiscountAmount = DiscountAmount, TaxRate = TaxRate, ValidUntil = ValidUntil?.Date ?? Today,
            DeliveryDays = DeliveryDays, PaymentTerms = PaymentTerms, Notes = Notes
        });
    }

    private async Task StepAsync(Func<Task> action, string success)
    {
        if (IsDraft && !await RunAsync(SaveAsync)) return;
        if (await RunAsync(action, success)) await ReloadAsync();
    }

    [RelayCommand] private Task MarkSent() => StepAsync(() => Get<QuotationService>().MarkSentAsync(_id), "Msg.QuotationSent");
    [RelayCommand] private Task Approve() => StepAsync(() => Get<QuotationService>().ApproveAsync(_id), "Msg.Approved");
    [RelayCommand] private Task Reject() => StepAsync(() => Get<QuotationService>().RejectAsync(_id), "Msg.Saved");

    [RelayCommand]
    private async Task NewVersion()
    {
        long nid = 0;
        if (await RunAsync(async () => nid = await Get<QuotationService>().NewVersionAsync(_id), "Msg.NewVersion"))
        {
            _id = nid;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task CreateJob()
    {
        long jid = 0;
        if (await RunAsync(async () => jid = await Get<QuotationService>().CreateJobAsync(_id,
                new JobCreationOptions(JobDueDate?.Date, JobPriority, JobMachine?.Id, JobOperator?.Id)), "Msg.JobCreated"))
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private void OpenJob()
    {
        if (JobId is not { } id) return;
        Close(true);
        Nav.Navigate(new JobDetailViewModel(id));
    }

    [RelayCommand]
    private async Task OpenVersion(QuotationRow? v)
    {
        if (v == null || v.Id == _id) return;
        _id = v.Id;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task Pdf(string mode)
    {
        await RunAsync(async () =>
        {
            if (IsDraft) await SaveAsync();
            var q = await Get<QuotationService>().GetAsync(_id) ?? throw new LaserWorks.Domain.Common.DomainException("Err.NotFound");
            var company = await Get<SettingsService>().GetAsync();
            var bytes = await Task.Run(() => DocumentRenderer.Quotation(q, company));
            if (mode == "print") { await ReportFiles.PrintBytesAsync(bytes, $"Quotation_{q.Number}"); return; }
            var path = await Dialogs.SaveFileAsync($"{L["Doc.Quotation"]}_{q.Number}_v{q.VersionNo}", "pdf");
            if (path == null) return;
            await File.WriteAllBytesAsync(path, bytes);
            InfoMessage = L.Format("Msg.Exported", Path.GetFileName(path));
            Dialogs.OpenFile(path);
        });
    }
}
