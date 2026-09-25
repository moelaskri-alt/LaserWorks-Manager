using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class OperationsBoardViewModel : ListPageViewModel<OperationRow>
{
    public override string TitleKey => "Production.Board";
    protected override AppModule Module => AppModule.Production;
    public List<Option<OperationStatus>> Statuses { get; } = Options.ForEnum<OperationStatus>();
    [ObservableProperty] private Option<OperationStatus>? _status;
    [ObservableProperty] private bool _openJobsOnly = true;

    public OperationsBoardViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<OperationStatus>? value) => _ = LoadAsync();
    partial void OnOpenJobsOnlyChanged(bool value) => _ = LoadAsync();

    protected override Task<PagedResult<OperationRow>> FetchAsync(PageRequest r) => Get<ProductionService>().ListOperationsAsync(r, status: Status?.Value, openJobsOnly: OpenJobsOnly);

    public override string? RowClass(object row) => row switch
    {
        OperationRow { Status: OperationStatus.InProgress } => "subtotal",
        OperationRow { Status: not OperationStatus.Done, DueDate: { } d } when d.Date < Today => "negative",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<OperationRow> rows)
    {
        var t = new ReportTable().Col("job", "Col.Job").Col("customer", "Col.Customer", width: 1.5f).Col("op", "Col.Operation").Col("emp", "Col.Employee").Col("machine", "Col.Machine")
            .Col("planned", "Col.PlannedHours", K.Number, total: true).Col("actual", "Col.ActualHours", K.Number, total: true).Col("status", "Col.Status").Col("due", "Col.DueDate", K.Date).Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var o in rows) t.Add(o.JobNumber, o.Customer, o.OperationType, o.Employee, o.Machine, o.PlannedHours, o.ActualHours, o.Status, o.DueDate, o.TotalCost);
        return t;
    }

    [RelayCommand] private async Task Start(OperationRow? o) { if (o != null && await RunAsync(() => Get<ProductionService>().StartOperationAsync(o.Id))) await LoadAsync(); }
    [RelayCommand] private async Task Complete(OperationRow? o) { if (o != null && await Dialogs.ShowAsync(new CompleteOperationDialogViewModel(o))) await LoadAsync(); }
    [RelayCommand] private void OpenJob(OperationRow? o) { if (o != null) Nav.Navigate(new JobDetailViewModel(o.JobId)); }
}

public sealed partial class ScrapLogViewModel : ListPageViewModel<ScrapRow>
{
    public override string TitleKey => "Nav.ScrapRework";
    protected override AppModule Module => AppModule.Production;
    public List<Option<ScrapType>> Types { get; } = Options.ForEnum<ScrapType>();
    [ObservableProperty] private Option<ScrapType>? _type;

    public ScrapLogViewModel() => _type = Types[0];

    partial void OnTypeChanged(Option<ScrapType>? value) => _ = LoadAsync();

    protected override Task<PagedResult<ScrapRow>> FetchAsync(PageRequest r) => Get<ProductionService>().ListScrapAsync(r, type: Type?.Value);

    public override string? RowClass(object row) => row is ScrapRow { Type: ScrapType.AbnormalScrap } ? "warning" : null;

    protected override ReportTable BuildExport(IReadOnlyList<ScrapRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("type", "Col.Type").Col("job", "Col.Job").Col("material", "Col.Material", width: 1.5f)
            .Col("machine", "Col.Machine").Col("qty", "Col.Quantity", K.Number).Col("hours", "Col.Hours", K.Number).Col("reason", "Col.Reason", width: 2).Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var s in rows) t.Add(s.Number, s.Date, s.Type, s.JobNumber, s.Material, s.Machine, s.Quantity, s.Hours, s.Reason, s.Cost);
        return t;
    }

    [RelayCommand] private void OpenJob(ScrapRow? s) { if (s != null) Nav.Navigate(new JobDetailViewModel(s.JobId)); }
}

public sealed partial class QualityLogViewModel : ListPageViewModel<QualityRow>
{
    public override string TitleKey => "Nav.Quality";
    protected override AppModule Module => AppModule.Production;
    public List<Option<QualityStatus>> Statuses { get; } = Options.ForEnum<QualityStatus>();
    [ObservableProperty] private Option<QualityStatus>? _status;

    public QualityLogViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<QualityStatus>? value) => _ = LoadAsync();

    protected override Task<PagedResult<QualityRow>> FetchAsync(PageRequest r) => Get<ProductionService>().ListQualityAsync(r, status: Status?.Value);

    public override string? RowClass(object row) => row switch
    {
        QualityRow { Status: QualityStatus.Failed } => "negative",
        QualityRow { Status: QualityStatus.ReworkRequired } => "warning",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<QualityRow> rows)
    {
        var t = new ReportTable().Col("date", "Col.Date", K.Date).Col("job", "Col.Job").Col("customer", "Col.Customer", width: 1.5f).Col("inspector", "Col.Inspector").Col("produced", "Col.Produced", K.Number, total: true)
            .Col("accepted", "Col.Accepted", K.Number, total: true).Col("rejected", "Col.Rejected", K.Number, total: true).Col("rate", "Col.AcceptanceRate", K.Percent).Col("status", "Col.Status");
        foreach (var q in rows) t.Add(q.Date, q.JobNumber, q.Customer, q.Inspector, q.QuantityProduced, q.QuantityAccepted, q.QuantityRejected, q.AcceptanceRate, q.Status);
        return t;
    }

    [RelayCommand] private void OpenJob(QualityRow? q) { if (q != null) Nav.Navigate(new JobDetailViewModel(q.JobId)); }
}

public sealed partial class ProductionViewModel : PageViewModel
{
    public ProductionViewModel(string? tab = null) => SelectedTab = tab switch { "Scrap" => 1, "Quality" => 2, _ => 0 };

    public override string TitleKey => "Nav.Production";
    public OperationsBoardViewModel Board { get; } = new();
    public ScrapLogViewModel ScrapLog { get; } = new();
    public QualityLogViewModel QualityLog { get; } = new();
    [ObservableProperty] private int _selectedTab;

    public override async Task LoadAsync()
    {
        await Board.LoadAsync();
        await ScrapLog.LoadAsync();
        await QualityLog.LoadAsync();
    }
}
