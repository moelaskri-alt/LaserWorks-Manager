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

/// <summary>Option for filter combo boxes: null value = "all".</summary>
public sealed record Option<T>(T? Value, string Label) where T : struct
{
    public override string ToString() => Label;
}

public sealed record TextOption(string? Value, string Label)
{
    public override string ToString() => Label;
}

public static class Options
{
    public static List<Option<T>> ForEnum<T>(bool includeAll = true) where T : struct, Enum
    {
        var list = new List<Option<T>>();
        if (includeAll) list.Add(new Option<T>(null, LaserWorks.Localization.Loc.Instance["Filter.All"]));
        list.AddRange(Enum.GetValues<T>().Select(v => new Option<T>(v, LaserWorks.Localization.Loc.Instance.Enum(v))));
        return list;
    }

    public static List<Lookup> WithAll(IEnumerable<Lookup> items) =>
        new List<Lookup> { new(0, "", LaserWorks.Localization.Loc.Instance["Filter.All"]) }.Concat(items).ToList();
}

public sealed partial class RequestsViewModel : ListPageViewModel<RequestRow>
{
    public override string TitleKey => "Nav.Requests";
    protected override AppModule Module => AppModule.Requests;

    public List<Option<RequestStatus>> Statuses { get; } = Options.ForEnum<RequestStatus>();
    [ObservableProperty] private Option<RequestStatus>? _status;

    public RequestsViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<RequestStatus>? value) => _ = LoadAsync();

    protected override Task<PagedResult<RequestRow>> FetchAsync(PageRequest r) => Get<RequestService>().ListAsync(r, Status?.Value);

    protected override ReportTable BuildExport(IReadOnlyList<RequestRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.6f).Col("desc", "Col.Description", width: 2.5f)
            .Col("items", "Col.RequestedItems", width: 1.6f).Col("qty", "Col.Quantity", K.Number).Col("req", "Col.RequiredDate", K.Date).Col("status", "Col.Status");
        foreach (var r in rows) t.Add(r.Number, r.RequestDate, r.Customer, r.Description, r.Items, r.Quantity, r.RequiredDate, r.Status);
        return t;
    }

    [RelayCommand]
    private async Task New()
    {
        if (await Dialogs.ShowAsync(new RequestEditorViewModel(0))) await LoadAsync();
    }

    [RelayCommand]
    private async Task Open(RequestRow? row)
    {
        if (row == null) return;
        await Dialogs.ShowAsync(new RequestEditorViewModel(row.Id));
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete(RequestRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Number), danger: true)) return;
        if (await RunAsync(() => Get<RequestService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

public sealed partial class RequestEditorViewModel : DialogViewModel
{
    private long _id;
    private readonly long? _customerId;

    public RequestEditorViewModel(long id, long? customerId = null)
    {
        _id = id;
        _customerId = customerId;
    }

    public override string TitleKey => _id == 0 ? "Request.New" : "Request.Edit";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} — {Number}";
    public override double DialogWidth => 1120;

    [ObservableProperty] private string? _number;
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private DateTime? _requestDate = DateTime.Today;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _dimensions;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private DateTime? _requiredDate;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private RequestStatus _status;
    [ObservableProperty] private RequestItemVm? _selectedItem;
    [ObservableProperty] private int _selectedTab;
    /// <summary>Requested materials, purchased components, consumables, packaging and services (unlimited lines).</summary>
    public ObservableCollection<RequestItemVm> Items { get; } = new();
    public IReadOnlyList<ComponentCategory> Categories { get; } = Enum.GetValues<ComponentCategory>();
    [ObservableProperty] private AttachmentRow? _selectedAttachment;
    [ObservableProperty] private RevisionRow? _selectedRevision;

    public List<Lookup> Customers { get; private set; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public ObservableCollection<AttachmentRow> Attachments { get; } = new();
    public ObservableCollection<RevisionRow> Revisions { get; } = new();
    public List<RequestStatus> ManualStatuses { get; } = new() { RequestStatus.New, RequestStatus.UnderReview, RequestStatus.Designing, RequestStatus.Estimating, RequestStatus.Rejected };
    public bool IsSaved => _id != 0;
    public bool IsNew => _id == 0;

    [RelayCommand]
    private void AddItem()
    {
        var item = new RequestItemVm(this) { Quantity = 1 };
        Items.Add(item);
        SelectedItem = item;
    }

    [RelayCommand]
    private void DuplicateItem(RequestItemVm? item)
    {
        item ??= SelectedItem;
        if (item == null) return;
        var copy = new RequestItemVm(this) { Material = item.Material, Category = item.Category, Description = item.Description, Quantity = item.Quantity, Unit = item.Unit, Notes = item.Notes };
        Items.Insert(Items.IndexOf(item) + 1, copy);
        SelectedItem = copy;
    }

    [RelayCommand]
    private void RemoveItem(RequestItemVm? item)
    {
        item ??= SelectedItem;
        if (item != null) Items.Remove(item);
    }

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync();
        Materials = await Get<MaterialService>().LookupAsync();
        OnPropertyChanged(nameof(Customers));
        OnPropertyChanged(nameof(Materials));
        if (_id == 0)
        {
            Customer = Customers.FirstOrDefault(c => c.Id == _customerId);
            return;
        }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var r = await Get<RequestService>().GetAsync(_id);
        if (r == null) return;
        Number = r.Number;
        Customer = Customers.FirstOrDefault(c => c.Id == r.CustomerId) ?? new Lookup(r.CustomerId, r.Customer!.Code, r.Customer.Name);
        RequestDate = r.RequestDate; Description = r.Description; Dimensions = r.Dimensions;
        Quantity = r.Quantity; RequiredDate = r.RequiredDate; Notes = r.Notes; Status = r.Status;
        Items.Clear();
        foreach (var i in r.Items.OrderBy(i => i.LineNo))
            Items.Add(new RequestItemVm(this)
            {
                Material = Materials.FirstOrDefault(m => m.Id == i.MaterialId), Category = i.Category, Description = i.Description, Quantity = i.Quantity, Unit = i.Unit, Notes = i.Notes
            });
        await ReloadChildrenAsync();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(IsNew));
    }

    private async Task ReloadChildrenAsync()
    {
        Attachments.Clear();
        foreach (var a in await Get<RequestService>().AttachmentsAsync(AttachmentOwner.Request, _id)) Attachments.Add(a);
        Revisions.Clear();
        foreach (var d in (await Get<DesignService>().ListAsync(new PageRequest(PageSize: 200), _id)).Items) Revisions.Add(d);
    }

    private async Task SaveCoreAsync()
    {
        _id = await Get<RequestService>().SaveAsync(new CustomerRequest
        {
            Id = _id, CustomerId = Customer?.Id ?? 0, RequestDate = RequestDate?.Date ?? Today, Description = Description ?? "", Dimensions = Dimensions,
            Quantity = Quantity, RequiredDate = RequiredDate?.Date, Notes = Notes,
            Items = Items.Where(i => i.Material != null || !string.IsNullOrWhiteSpace(i.Description))
                .Select(i => new RequestItem { MaterialId = i.Material?.Id, Category = i.Category, Description = i.Description, Quantity = i.Quantity, Unit = i.Unit, Notes = i.Notes }).ToList()
        });
    }

    protected override Task SaveAsync() => SaveCoreAsync();

    /// <summary>Save and keep the dialog open (so attachments and revisions can be added).</summary>
    [RelayCommand]
    private async Task Apply()
    {
        if (await RunAsync(SaveCoreAsync, "Msg.Saved")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        if (_id == 0) return;
        var file = await Dialogs.OpenFileAsync();
        if (file == null) return;
        if (await RunAsync(() => Get<RequestService>().AddAttachmentAsync(AttachmentOwner.Request, _id, file, Path.GetExtension(file).ToLowerInvariant() is ".svg" or ".dxf" or ".ai" or ".cdr" or ".pdf" ? "Design" : "Reference")))
            await ReloadChildrenAsync();
    }

    [RelayCommand]
    private void OpenAttachment(AttachmentRow? a)
    {
        if (a != null) Dialogs.OpenFile(Get<RequestService>().ResolveAttachment(a.StoredPath));
    }

    [RelayCommand]
    private async Task RemoveAttachment(AttachmentRow? a)
    {
        if (a == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", a.FileName), danger: true)) return;
        if (await RunAsync(() => Get<RequestService>().DeleteAttachmentAsync(a.Id))) await ReloadChildrenAsync();
    }

    [RelayCommand]
    private async Task NewRevision()
    {
        if (_id == 0) return;
        if (await Dialogs.ShowAsync(new RevisionEditorViewModel(0, _id))) await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenRevision(RevisionRow? r)
    {
        if (r == null) return;
        if (await Dialogs.ShowAsync(new RevisionEditorViewModel(r.Id, _id))) await ReloadAsync();
    }

    [RelayCommand]
    private async Task ApproveRevision(RevisionRow? r)
    {
        if (r == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmApproveRevision", r.RevisionLabel))) return;
        if (await RunAsync(() => Get<DesignService>().ApproveAsync(r.Id), "Msg.Approved")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task SetStatus(RequestStatus status)
    {
        if (await RunAsync(() => Get<RequestService>().SetStatusAsync(_id, status), "Msg.Saved")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task CreateEstimate()
    {
        if (_id == 0) await Apply();
        if (_id == 0 || Customer == null) return;
        var customerId = Customer.Id;
        var requestId = _id;
        Close(true);
        Nav.Navigate(new EstimateEditorViewModel(0, customerId, requestId));
    }
}

public sealed partial class RevisionEditorViewModel : DialogViewModel
{
    private long _id;
    private readonly long _requestId;

    public RevisionEditorViewModel(long id, long requestId)
    {
        _id = id;
        _requestId = requestId;
    }

    public override string TitleKey => _id == 0 ? "Revision.New" : "Revision.Edit";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} — {Label}";
    public override double DialogWidth => 760;
    public override bool ShowSave => !IsLocked;

    [ObservableProperty] private string? _label;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private Lookup? _designer;
    [ObservableProperty] private decimal _width;
    [ObservableProperty] private decimal _height;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private decimal _thickness;
    [ObservableProperty] private decimal _cuttingLengthM;
    [ObservableProperty] private decimal _engravingAreaCm2;
    [ObservableProperty] private decimal _estimatedMachineMinutes;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _submitForReview;
    [ObservableProperty] private RevisionStatus _status;

    public List<Lookup> Designers { get; private set; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public ObservableCollection<AttachmentRow> Files { get; } = new();
    public bool IsLocked => Status is RevisionStatus.Approved or RevisionStatus.Superseded or RevisionStatus.Rejected;
    public bool CanApprove => _id != 0 && !IsLocked && Can(AppModule.Design, Permission.Approve);
    public bool IsSaved => _id != 0;

    partial void OnStatusChanged(RevisionStatus value)
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(ShowSave));
    }

    public override async Task InitializeAsync()
    {
        Designers = await Get<EmployeeService>().LookupAsync();
        Materials = await Get<MaterialService>().LookupAsync();
        OnPropertyChanged(nameof(Designers));
        OnPropertyChanged(nameof(Materials));
        if (_id == 0)
        {
            // the design's main sheet material defaults to the first raw material the customer asked for
            var req = await Get<RequestService>().GetAsync(_requestId);
            var main = req?.Items.OrderBy(i => i.LineNo).FirstOrDefault(i => i.Material is { Kind: MaterialKind.RawMaterial });
            Material = Materials.FirstOrDefault(m => m.Id == main?.MaterialId);
            Thickness = Material?.Thickness ?? 0;
            return;
        }
        var d = await Get<DesignService>().GetAsync(_id);
        if (d == null) return;
        Label = d.RevisionLabel; Date = d.Date; Designer = Designers.FirstOrDefault(x => x.Id == d.DesignerId); Width = d.Width; Height = d.Height;
        Material = Materials.FirstOrDefault(m => m.Id == d.MaterialId); Thickness = d.Thickness; CuttingLengthM = d.CuttingLengthM; EngravingAreaCm2 = d.EngravingAreaCm2;
        EstimatedMachineMinutes = d.EstimatedMachineMinutes; Notes = d.Notes; Status = d.Status;
        await LoadFilesAsync();
        OnPropertyChanged(nameof(IsSaved));
    }

    private async Task LoadFilesAsync()
    {
        Files.Clear();
        foreach (var a in await Get<RequestService>().AttachmentsAsync(AttachmentOwner.DesignRevision, _id)) Files.Add(a);
    }

    protected override async Task SaveAsync()
    {
        _id = await Get<DesignService>().SaveAsync(new DesignRevision
        {
            Id = _id, RequestId = _requestId, Date = Date?.Date ?? Today, DesignerId = Designer?.Id, Width = Width, Height = Height, MaterialId = Material?.Id, Thickness = Thickness,
            CuttingLengthM = CuttingLengthM, EngravingAreaCm2 = EngravingAreaCm2, EstimatedMachineMinutes = EstimatedMachineMinutes, Notes = Notes,
            Status = SubmitForReview ? RevisionStatus.UnderReview : RevisionStatus.Draft
        });
    }

    [RelayCommand]
    private async Task AttachFile()
    {
        if (_id == 0 && !await RunAsync(SaveAsync)) return;
        OnPropertyChanged(nameof(IsSaved));
        var file = await Dialogs.OpenFileAsync();
        if (file == null) return;
        if (await RunAsync(() => Get<RequestService>().AddAttachmentAsync(AttachmentOwner.DesignRevision, _id, file, "Design"))) await LoadFilesAsync();
    }

    [RelayCommand]
    private void OpenFile(AttachmentRow? a)
    {
        if (a != null) Dialogs.OpenFile(Get<RequestService>().ResolveAttachment(a.StoredPath));
    }

    [RelayCommand]
    private async Task Approve()
    {
        if (!await RunAsync(SaveAsync)) return;
        if (await RunAsync(() => Get<DesignService>().ApproveAsync(_id))) Close(true);
    }

    [RelayCommand]
    private async Task Reject()
    {
        if (await RunAsync(() => Get<DesignService>().RejectAsync(_id))) Close(true);
    }
}

public sealed partial class DesignsViewModel : ListPageViewModel<RevisionRow>
{
    public override string TitleKey => "Nav.Design";
    protected override AppModule Module => AppModule.Design;

    public List<Option<RevisionStatus>> Statuses { get; } = Options.ForEnum<RevisionStatus>();
    [ObservableProperty] private Option<RevisionStatus>? _status;

    public DesignsViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<RevisionStatus>? value) => _ = LoadAsync();

    protected override Task<PagedResult<RevisionRow>> FetchAsync(PageRequest r) => Get<DesignService>().ListAsync(r, status: Status?.Value);

    public override string? RowClass(object row) => row is RevisionRow { Status: RevisionStatus.Approved } ? "subtotal" : null;

    protected override ReportTable BuildExport(IReadOnlyList<RevisionRow> rows)
    {
        var t = new ReportTable().Col("req", "Col.Request").Col("customer", "Col.Customer", width: 1.5f).Col("rev", "Col.Revision").Col("date", "Col.Date", K.Date).Col("designer", "Col.Designer")
            .Col("size", "Col.Dimensions").Col("material", "Col.Material", width: 1.4f).Col("cut", "Col.CuttingLength", K.Number).Col("eng", "Col.EngravingArea", K.Number).Col("min", "Col.MachineMinutes", K.Number).Col("status", "Col.Status");
        foreach (var r in rows) t.Add(r.RequestNumber, r.Customer, r.RevisionLabel, r.Date, r.Designer, $"{r.Width:0.#}×{r.Height:0.#}", r.Material, r.CuttingLengthM, r.EngravingAreaCm2, r.EstimatedMachineMinutes, r.Status);
        return t;
    }

    [RelayCommand]
    private async Task Open(RevisionRow? row)
    {
        if (row == null) return;
        if (await Dialogs.ShowAsync(new RevisionEditorViewModel(row.Id, row.RequestId))) await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenRequest(RevisionRow? row)
    {
        if (row == null) return;
        await Dialogs.ShowAsync(new RequestEditorViewModel(row.RequestId));
        await LoadAsync();
    }
}


/// <summary>One requested item line in the request editor.</summary>
public sealed partial class RequestItemVm : ObservableObject
{
    public RequestItemVm(RequestEditorViewModel owner) => Owner = owner;
    public RequestEditorViewModel Owner { get; }
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private ComponentCategory _category = ComponentCategory.RawMaterial;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private string? _unit;
    [ObservableProperty] private string? _notes;
    public bool IsFreeText => Material == null;

    partial void OnMaterialChanged(MaterialLookup? value)
    {
        if (value != null)
        {
            Category = LaserWorks.Domain.Costing.ComponentRules.CategoryOf(value.Kind);
            Unit = value.Unit;
        }
        OnPropertyChanged(nameof(IsFreeText));
    }
}
