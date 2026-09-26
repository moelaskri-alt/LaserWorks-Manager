using CommunityToolkit.Mvvm.ComponentModel;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Desktop.ViewModels;

/// <summary>Adds or edits one job component line (item, category, source, planned quantity and estimated unit cost).</summary>
public sealed partial class JobComponentEditorViewModel : DialogViewModel
{
    private readonly long _jobId;
    private readonly long _id;
    private readonly ComponentSource _initialSource;

    public JobComponentEditorViewModel(long jobId, long id = 0, ComponentSource source = ComponentSource.Inventory)
    {
        _jobId = jobId;
        _id = id;
        _initialSource = source;
    }

    public override string TitleKey => _id == 0 ? "Component.New" : "Component.Edit";
    public override double DialogWidth => 720;

    [ObservableProperty] private ComponentCategory _category = ComponentCategory.RawMaterial;
    [ObservableProperty] private ComponentSource _source = ComponentSource.Inventory;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _unit;
    [ObservableProperty] private decimal _plannedQuantity = 1;
    [ObservableProperty] private decimal _estimatedUnitCost;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _fromEstimate;

    public IReadOnlyList<ComponentCategory> Categories { get; } = Enum.GetValues<ComponentCategory>();
    public IReadOnlyList<ComponentSource> Sources { get; } = Enum.GetValues<ComponentSource>();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Lookup> Suppliers { get; private set; } = new();
    public decimal EstimatedCost => Money.Round(PlannedQuantity * EstimatedUnitCost);
    public bool IsDirect => !ComponentRules.IsStocked(Source);
    public bool CanChangeIdentity => !Locked;

    public IEnumerable<MaterialLookup> ItemChoices => Source switch
    {
        ComponentSource.Remnant => Materials.Where(m => m.Kind == MaterialKind.RawMaterial),
        ComponentSource.Inventory => Materials.Where(m => m.Kind != MaterialKind.Service),
        _ => Materials
    };

    partial void OnPlannedQuantityChanged(decimal value) => OnPropertyChanged(nameof(EstimatedCost));
    partial void OnEstimatedUnitCostChanged(decimal value) => OnPropertyChanged(nameof(EstimatedCost));
    partial void OnLockedChanged(bool value) => OnPropertyChanged(nameof(CanChangeIdentity));

    private bool _loading;

    partial void OnSourceChanged(ComponentSource value)
    {
        OnPropertyChanged(nameof(IsDirect));
        OnPropertyChanged(nameof(ItemChoices));
        if (_loading) return;
        if (value == ComponentSource.ExternalService) Category = ComponentCategory.ExternalService;
        if (value == ComponentSource.Remnant && Material is { Kind: not MaterialKind.RawMaterial }) Material = null;
        if (value == ComponentSource.Inventory && Material is { Kind: MaterialKind.Service }) Material = null;
    }

    partial void OnMaterialChanged(MaterialLookup? value)
    {
        if (_loading || value == null) return;
        Category = ComponentRules.CategoryOf(value.Kind);
        Unit = value.Unit;
        EstimatedUnitCost = value.AverageCost;
        if (value.Kind == MaterialKind.Service && ComponentRules.IsStocked(Source)) Source = ComponentSource.ExternalService;
    }

    public override async Task InitializeAsync()
    {
        Materials = await Get<MaterialService>().LookupAsync();
        Suppliers = await Get<SupplierService>().LookupAsync();
        OnPropertyChanged(nameof(Materials)); OnPropertyChanged(nameof(Suppliers)); OnPropertyChanged(nameof(ItemChoices));
        if (_id == 0)
        {
            Source = _initialSource;
            if (_initialSource == ComponentSource.ManualCost) Category = ComponentCategory.OtherDirect;
            return;
        }
        var c = await Get<JobComponentService>().GetAsync(_id) ?? throw new DomainException("Err.NotFound");
        var row = (await Get<JobComponentService>().ListAsync(c.JobId)).FirstOrDefault(r => r.Id == _id);
        _loading = true;
        try
        {
            Source = c.Source; Category = c.Category; Material = Materials.FirstOrDefault(m => m.Id == c.MaterialId);
            Description = c.Description; Unit = c.Unit; PlannedQuantity = c.PlannedQuantity; EstimatedUnitCost = c.EstimatedUnitCost;
            Supplier = Suppliers.FirstOrDefault(s => s.Id == c.SupplierId); Notes = c.Notes;
            Locked = row?.HasCost == true || row?.UsedQuantity != 0;
            FromEstimate = c.EstimateLineId != null;
        }
        finally { _loading = false; }
        OnPropertyChanged(nameof(ItemChoices));
    }

    /// <summary>Creates a new item in the item master (kind from the line's type) and selects it.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task NewItem()
    {
        var editor = MaterialEditorViewModel.NewOfKind(ComponentRules.KindOf(Category));
        if (!await Dialogs.ShowAsync(editor) || editor.SavedId == 0) return;
        Materials = await Get<MaterialService>().LookupAsync();
        OnPropertyChanged(nameof(Materials));
        OnPropertyChanged(nameof(ItemChoices));
        var item = Materials.FirstOrDefault(m => m.Id == editor.SavedId);
        if (item == null) return;
        if (!ComponentRules.IsStockable(item.Kind) && ComponentRules.IsStocked(Source)) Source = ComponentSource.ExternalService;
        Material = item;
    }

    protected override Task SaveAsync() => Get<JobComponentService>().SaveAsync(new JobComponent
    {
        Id = _id, JobId = _jobId, Category = Category, Source = Source, MaterialId = Material?.Id, Description = Description, Unit = Unit,
        PlannedQuantity = PlannedQuantity, EstimatedUnitCost = EstimatedUnitCost, SupplierId = Supplier?.Id, Notes = Notes
    });
}

/// <summary>
/// Charges a direct purchase, outsourced service or manual cost to a component line (posted to WIP against the supplier or cash/bank).
/// It never passes through stock, so it can't double count with an issue.
/// </summary>
public sealed partial class DirectCostDialogViewModel : DialogViewModel
{
    public DirectCostDialogViewModel(JobComponentRow line)
    {
        Line = line;
        _quantity = line.RemainingQuantity > 0 ? line.RemainingQuantity : line.PlannedQuantity;
        _amount = Math.Max(0, line.EstimatedCost - line.ActualCost);
        _method = line.SupplierId != null ? PaymentMethod.OnCredit : PaymentMethod.Cash;
    }

    public JobComponentRow Line { get; }
    public override string TitleKey => "Component.RecordCost";
    public override double DialogWidth => 600;
    public override string SaveKey => "Common.Post";

    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private decimal _taxAmount;
    [ObservableProperty] private PaymentMethod _method;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private string? _reference;
    [ObservableProperty] private string? _notes;

    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>();
    public List<Lookup> Suppliers { get; private set; } = new();
    public string LineText => $"#{Line.LineNo} · {L.Enum(Line.Category)} · {Line.Display}";
    public decimal Total => Money.Round(Amount + TaxAmount);

    partial void OnAmountChanged(decimal value) => OnPropertyChanged(nameof(Total));
    partial void OnTaxAmountChanged(decimal value) => OnPropertyChanged(nameof(Total));

    public override async Task InitializeAsync()
    {
        Suppliers = await Get<SupplierService>().LookupAsync();
        OnPropertyChanged(nameof(Suppliers));
        Supplier = Suppliers.FirstOrDefault(s => s.Id == Line.SupplierId);
    }

    protected override Task SaveAsync() => Get<JobComponentService>().RecordDirectCostAsync(
        new DirectCostInput(Line.Id, Date?.Date ?? Today, Quantity, Amount, TaxAmount, Method, Supplier?.Id, Reference, Notes));
}

/// <summary>Starts a new estimate from a saved product template (current item costs and machine rates); templates can be removed here too.</summary>
public sealed partial class TemplatePickerViewModel : DialogViewModel
{
    public override string TitleKey => "Template.NewEstimate";
    public override double DialogWidth => 820;
    public override string SaveKey => "Template.Use";

    [ObservableProperty] private ProductTemplateRow? _selected;
    [ObservableProperty] private Lookup? _customer;
    public System.Collections.ObjectModel.ObservableCollection<ProductTemplateRow> Templates { get; } = new();
    public List<Lookup> Customers { get; private set; } = new();
    public long CreatedEstimateId { get; private set; }

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync();
        OnPropertyChanged(nameof(Customers));
        Customer = Customers.FirstOrDefault();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        Templates.Clear();
        foreach (var t in await Get<ProductTemplateService>().ListAsync()) Templates.Add(t);
        Selected = Templates.FirstOrDefault();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task DeleteTemplate()
    {
        if (Selected is not { } t || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", t.Name), danger: true)) return;
        if (await RunAsync(() => Get<ProductTemplateService>().DeleteAsync(t.Id), "Msg.Deleted")) await ReloadAsync();
    }

    protected override async Task SaveAsync()
    {
        if (Selected == null) throw new DomainException("Err.Required", L["Template.Title"]);
        if (Customer == null) throw new DomainException("Err.Required", L["Col.Customer"]);
        var est = Get<EstimateService>();
        var draft = await Get<ProductTemplateService>().NewEstimateAsync(Selected.Id, Customer.Id, est);
        CreatedEstimateId = await est.SaveAsync(draft);
    }
}
