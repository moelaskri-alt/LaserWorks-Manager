using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

// ================================================================== materials
public sealed partial class MaterialsViewModel : ListPageViewModel<MaterialRow>
{
    public override string TitleKey => "Nav.Materials";
    protected override AppModule Module => AppModule.Inventory;

    public List<Option<MaterialKind>> Kinds { get; } = Options.ForEnum<MaterialKind>();
    [ObservableProperty] private Option<MaterialKind>? _kind;
    [ObservableProperty] private bool _lowOnly;

    public MaterialsViewModel(bool lowOnly = false)
    {
        _kind = Kinds[0];
        _lowOnly = lowOnly;
    }

    partial void OnKindChanged(Option<MaterialKind>? value) => _ = LoadAsync();
    partial void OnLowOnlyChanged(bool value) => _ = LoadAsync();

    protected override Task<PagedResult<MaterialRow>> FetchAsync(PageRequest r) => Get<MaterialService>().ListAsync(r, kind: Kind?.Value, lowOnly: LowOnly);

    public override string? RowClass(object row) => row is MaterialRow { IsLow: true } m ? m.QuantityOnHand <= m.MinimumStock ? "negative" : "warning" : null;

    protected override ReportTable BuildExport(IReadOnlyList<MaterialRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("name", "Col.Name", width: 2).Col("cat", "Col.Category").Col("kind", "Col.Kind").Col("type", "Col.Type").Col("thk", "Col.Thickness", K.Number)
            .Col("size", "Col.Dimensions").Col("unit", "Col.Unit").Col("qty", "Col.OnHand", K.Number).Col("avg", "Col.AverageCost", K.Money).Col("value", "Col.StockValue", K.Money, total: true).Col("reorder", "Col.ReorderLevel", K.Number);
        foreach (var m in rows) t.Add(m.Code, m.Name, m.Category, m.Kind, m.MaterialType, m.Thickness, m.Length > 0 ? $"{m.Length:0.#}×{m.Width:0.#}" : "", m.Unit, m.QuantityOnHand, m.AverageCost, m.StockValue, m.ReorderLevel);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new MaterialEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(MaterialRow? row) { if (row != null && await Dialogs.ShowAsync(new MaterialEditorViewModel(row.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(MaterialRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Name), danger: true)) return;
        if (await RunAsync(() => Get<MaterialService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }

    [RelayCommand]
    private async Task Movement(string action)
    {
        if (!Enum.TryParse<StockAction>(action, out var a)) return;
        if (await Dialogs.ShowAsync(new StockMovementDialogViewModel(a, null, Selected?.Id))) await LoadAsync();
    }
}

public sealed partial class MaterialEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public MaterialEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Material.New" : "Material.Edit";
    public override double DialogWidth => 820;

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private MaterialCategory? _category;
    [ObservableProperty] private MaterialKind _kind = MaterialKind.RawMaterial;
    [ObservableProperty] private string? _materialType;
    [ObservableProperty] private decimal _thickness;
    [ObservableProperty] private decimal _length;
    [ObservableProperty] private decimal _width;
    [ObservableProperty] private UnitOfMeasure? _unit;
    [ObservableProperty] private decimal _purchaseCost;
    [ObservableProperty] private decimal _salesPrice;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private decimal _minimumStock;
    [ObservableProperty] private decimal _reorderLevel;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private decimal _quantityOnHand;
    [ObservableProperty] private decimal _averageCost;
    [ObservableProperty] private decimal _stockValue;
    public List<MaterialCategory> Categories { get; private set; } = new();
    public List<UnitOfMeasure> Units { get; private set; } = new();
    public List<Lookup> Suppliers { get; private set; } = new();
    public IReadOnlyList<MaterialKind> KindsList { get; } = Enum.GetValues<MaterialKind>();
    public ObservableCollection<string> StockByWarehouse { get; } = new();
    public bool IsSaved => _id != 0;

    public override async Task InitializeAsync()
    {
        Categories = await Get<LookupService>().CategoriesAsync();
        Units = await Get<LookupService>().UnitsAsync();
        Suppliers = await Get<SupplierService>().LookupAsync();
        foreach (var p in new[] { nameof(Categories), nameof(Units), nameof(Suppliers) }) OnPropertyChanged(p);
        Unit = Units.FirstOrDefault();
        if (_id == 0) return;
        var m = await Get<MaterialService>().GetAsync(_id);
        if (m == null) return;
        Code = m.Code; Name = m.Name; Category = Categories.FirstOrDefault(c => c.Id == m.CategoryId); Kind = m.Kind; MaterialType = m.MaterialType; Thickness = m.Thickness;
        Length = m.Length; Width = m.Width; Unit = Units.FirstOrDefault(u => u.Id == m.UnitId); PurchaseCost = m.PurchaseCost; SalesPrice = m.SalesPrice;
        Supplier = Suppliers.FirstOrDefault(s => s.Id == m.SupplierId); MinimumStock = m.MinimumStock; ReorderLevel = m.ReorderLevel; IsActive = m.IsActive; Notes = m.Notes;
        QuantityOnHand = m.QuantityOnHand; AverageCost = m.AverageCost; StockValue = m.StockValue;
        foreach (var (w, q) in await Get<MaterialService>().StockByWarehouseAsync(_id)) StockByWarehouse.Add($"{w}: {L.Number(q)}");
    }

    protected override Task SaveAsync() => Get<MaterialService>().SaveAsync(new Material
    {
        Id = _id, Code = Code ?? "", Name = Name ?? "", CategoryId = Category?.Id, Kind = Kind, MaterialType = MaterialType, Thickness = Thickness, Length = Length, Width = Width,
        UnitId = Unit?.Id ?? 0, PurchaseCost = PurchaseCost, SalesPrice = SalesPrice, SupplierId = Supplier?.Id, MinimumStock = MinimumStock, ReorderLevel = ReorderLevel, IsActive = IsActive, Notes = Notes
    });
}

// ================================================================== stock movements
public enum StockAction { Opening, Adjust, IssueToJob, ReturnFromJob, Transfer, Scrap }

public sealed partial class StockMovementDialogViewModel : DialogViewModel
{
    private readonly long? _jobId;
    private readonly long? _materialId;

    public StockMovementDialogViewModel(StockAction action, long? jobId, long? materialId = null)
    {
        Action = action;
        _jobId = jobId;
        _materialId = materialId;
    }

    public StockAction Action { get; }
    public override string TitleKey => "Stock." + Action;
    public override double DialogWidth => 640;
    public override string SaveKey => "Common.Post";

    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private Warehouse? _warehouse;
    [ObservableProperty] private Warehouse? _toWarehouse;
    [ObservableProperty] private Lookup? _job;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal? _unitCost;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _increase = true;
    [ObservableProperty] private decimal _available;
    [ObservableProperty] private string? _hint;

    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Warehouse> Warehouses { get; private set; } = new();
    public List<Lookup> Jobs { get; private set; } = new();
    public bool NeedsJob => Action is StockAction.IssueToJob or StockAction.ReturnFromJob;
    public bool NeedsCost => Action == StockAction.Opening || (Action == StockAction.Adjust && Increase);
    public bool IsTransfer => Action == StockAction.Transfer;
    public bool IsAdjust => Action == StockAction.Adjust;
    public bool JobFixed => _jobId != null;

    public bool Decrease { get => !Increase; set => Increase = !value; }

    partial void OnIncreaseChanged(bool value) { OnPropertyChanged(nameof(NeedsCost)); OnPropertyChanged(nameof(Decrease)); }
    partial void OnMaterialChanged(MaterialLookup? value) => _ = RefreshAvailableAsync();
    partial void OnWarehouseChanged(Warehouse? value) => _ = RefreshAvailableAsync();

    private async Task RefreshAvailableAsync()
    {
        if (Material == null || Warehouse == null) return;
        Available = await Get<MaterialService>().StockInWarehouseAsync(Material.Id, Warehouse.Id);
        UnitCost ??= Action == StockAction.Opening ? Material.AverageCost : null;
        Hint = L.Format("Stock.AvailableHint", L.Number(Available), Material.Unit, L.Money(Material.AverageCost));
        if (Action == StockAction.ReturnFromJob && _jobId is { } jid)
        {
            var issued = (await Get<InventoryService>().JobMaterialsAsync(jid)).FirstOrDefault(m => m.MaterialId == Material.Id);
            Hint = L.Format("Stock.IssuedToJobHint", L.Number(issued?.NetQuantity ?? 0), L.Money(issued?.AverageIssueCost ?? 0));
        }
    }

    public override async Task InitializeAsync()
    {
        Materials = await Get<MaterialService>().LookupAsync();
        Warehouses = await Get<LookupService>().WarehousesAsync(activeOnly: true);
        Jobs = await Get<JobService>().LookupAsync();
        foreach (var p in new[] { nameof(Materials), nameof(Warehouses), nameof(Jobs) }) OnPropertyChanged(p);
        var s = await Get<SettingsService>().GetAsync();
        Warehouse = Warehouses.FirstOrDefault(w => w.Id == s.DefaultWarehouseId) ?? Warehouses.FirstOrDefault();
        ToWarehouse = Warehouses.FirstOrDefault(w => w.Id != Warehouse?.Id);
        Job = Jobs.FirstOrDefault(j => j.Id == _jobId);
        if (_jobId is { } jid && Action == StockAction.ReturnFromJob)
        {
            var issued = await Get<InventoryService>().JobMaterialsAsync(jid);
            Material = Materials.FirstOrDefault(m => issued.Any(i => i.MaterialId == m.Id));
        }
        else Material = Materials.FirstOrDefault(m => m.Id == _materialId) ?? (Action == StockAction.IssueToJob ? await DefaultForJobAsync() : null);
    }

    private async Task<MaterialLookup?> DefaultForJobAsync()
    {
        if (_jobId is not { } jid) return null;
        var job = await Get<JobService>().GetAsync(jid);
        if (job?.EstimateId is not { } eid) return null;
        var est = await Get<EstimateService>().GetAsync(eid);
        var line = est?.MaterialLines.FirstOrDefault();
        if (line == null) return null;
        Quantity = line.SheetBased ? line.SheetsRequired : line.TotalQuantity;
        return Materials.FirstOrDefault(m => m.Id == line.MaterialId);
    }

    protected override async Task SaveAsync()
    {
        var inv = Get<InventoryService>();
        if (Material == null) throw new DomainException("Err.Required", L["Col.Material"]);
        if (Warehouse == null) throw new DomainException("Err.Required", L["Col.Warehouse"]);
        var date = Date?.Date ?? Today;
        switch (Action)
        {
            case StockAction.Opening:
                await inv.OpeningBalanceAsync(Material.Id, Warehouse.Id, Quantity, UnitCost ?? 0, date); break;
            case StockAction.Adjust:
                await inv.AdjustAsync(Material.Id, Warehouse.Id, Increase ? Quantity : -Quantity, Increase ? UnitCost : null, date, Notes ?? ""); break;
            case StockAction.IssueToJob:
                await inv.IssueToJobAsync(Job?.Id ?? throw new DomainException("Err.Required", L["Col.Job"]), Material.Id, Warehouse.Id, Quantity, date, Notes); break;
            case StockAction.ReturnFromJob:
                await inv.ReturnFromJobAsync(Job?.Id ?? throw new DomainException("Err.Required", L["Col.Job"]), Material.Id, Warehouse.Id, Quantity, date, Notes); break;
            case StockAction.Transfer:
                await inv.TransferAsync(Material.Id, Warehouse.Id, ToWarehouse?.Id ?? 0, Quantity, date, Notes); break;
            case StockAction.Scrap:
                await inv.ScrapStockAsync(Material.Id, Warehouse.Id, Quantity, date, Notes ?? ""); break;
        }
    }
}

// ================================================================== remnants
public enum RemnantAction { CreateFromJob, CreateFromStock, Consume, Adjust }

public sealed partial class RemnantDialogViewModel : DialogViewModel
{
    private readonly long? _jobId;
    private readonly long? _remnantId;

    public RemnantDialogViewModel(RemnantAction action, long? jobId = null, long? remnantId = null)
    {
        Action = action;
        _jobId = jobId;
        _remnantId = remnantId;
    }

    public RemnantAction Action { get; }
    public override string TitleKey => "Remnant." + Action;
    public override double DialogWidth => 640;
    public override string SaveKey => "Common.Post";

    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private Warehouse? _warehouse;
    [ObservableProperty] private Lookup? _job;
    [ObservableProperty] private RemnantRow? _remnant;
    [ObservableProperty] private decimal _length;
    [ObservableProperty] private decimal _width;
    [ObservableProperty] private decimal? _cost;
    [ObservableProperty] private bool _scrap;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _costPreview;

    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Warehouse> Warehouses { get; private set; } = new();
    public List<Lookup> Jobs { get; private set; } = new();
    public ObservableCollection<RemnantRow> Remnants { get; } = new();
    public bool IsCreate => Action is RemnantAction.CreateFromJob or RemnantAction.CreateFromStock;
    public bool IsConsume => Action == RemnantAction.Consume;
    public bool IsAdjust => Action == RemnantAction.Adjust;
    public bool NeedsJob => Action is RemnantAction.CreateFromJob or RemnantAction.Consume;

    partial void OnLengthChanged(decimal value) => Preview();
    partial void OnWidthChanged(decimal value) => Preview();
    partial void OnMaterialChanged(MaterialLookup? value) { Preview(); if (IsConsume) _ = LoadRemnantsAsync(); }

    private void Preview()
    {
        if (Material == null || Material.Length <= 0 || Material.Width <= 0 || Length <= 0 || Width <= 0) { CostPreview = null; return; }
        try
        {
            var c = MaterialUtilizationCalculator.RemnantCost(Material.Length * Material.Width, Material.AverageCost, Length, Width);
            CostPreview = L.Format("Remnant.CostPreview", L.Money(c));
        }
        catch (DomainException ex) { CostPreview = Errors.Describe(ex); }
    }

    private async Task LoadRemnantsAsync()
    {
        Remnants.Clear();
        var list = (await Get<InventoryService>().ListRemnantsAsync(new PageRequest(PageSize: 500), RemnantStatus.Available, Material?.Id)).Items;
        foreach (var r in list) Remnants.Add(r);
        Remnant = Remnants.FirstOrDefault();
    }

    partial void OnRemnantChanged(RemnantRow? value)
    {
        if (value == null || !IsAdjust) return;
        Length = value.Length; Width = value.Width; Cost = value.Cost;
    }

    public override async Task InitializeAsync()
    {
        Materials = await Get<MaterialService>().LookupAsync();
        Warehouses = await Get<LookupService>().WarehousesAsync(activeOnly: true);
        Jobs = await Get<JobService>().LookupAsync();
        foreach (var p in new[] { nameof(Materials), nameof(Warehouses), nameof(Jobs) }) OnPropertyChanged(p);
        var s = await Get<SettingsService>().GetAsync();
        Warehouse = Warehouses.FirstOrDefault(w => w.Id == s.DefaultWarehouseId) ?? Warehouses.FirstOrDefault();
        Job = Jobs.FirstOrDefault(j => j.Id == _jobId);
        if (_jobId is { } jid && Action == RemnantAction.CreateFromJob)
        {
            var issued = await Get<InventoryService>().JobMaterialsAsync(jid);
            Material = Materials.FirstOrDefault(m => issued.Any(i => i.MaterialId == m.Id));
        }
        if (IsConsume || IsAdjust)
        {
            await LoadRemnantsAsync();
            if (_remnantId is { } rid) Remnant = Remnants.FirstOrDefault(r => r.Id == rid);
        }
    }

    protected override async Task SaveAsync()
    {
        var inv = Get<InventoryService>();
        var date = Date?.Date ?? Today;
        switch (Action)
        {
            case RemnantAction.CreateFromJob:
            case RemnantAction.CreateFromStock:
                if (Material == null) throw new DomainException("Err.Required", L["Col.Material"]);
                await inv.CreateRemnantAsync(new RemnantInput(Material.Id, Warehouse?.Id ?? 0, Length, Width, null, Action == RemnantAction.CreateFromJob ? Job?.Id : null, Cost, date, Notes));
                break;
            case RemnantAction.Consume:
                await inv.ConsumeRemnantAsync(Remnant?.Id ?? throw new DomainException("Err.Required", L["Col.Remnant"]), Job?.Id ?? throw new DomainException("Err.Required", L["Col.Job"]), date);
                break;
            case RemnantAction.Adjust:
                await inv.AdjustRemnantAsync(Remnant?.Id ?? throw new DomainException("Err.Required", L["Col.Remnant"]), Length, Width, Cost ?? 0, Scrap, date, Notes ?? "");
                break;
        }
    }
}

// ================================================================== inventory page (tabs)
public sealed partial class StockTransactionsViewModel : ListPageViewModel<InventoryTxRow>
{
    public override string TitleKey => "Inventory.Transactions";
    protected override AppModule Module => AppModule.Inventory;
    public List<Option<InventoryTxType>> Types { get; } = Options.ForEnum<InventoryTxType>();
    [ObservableProperty] private Option<InventoryTxType>? _type;
    [ObservableProperty] private DateTime? _from = DateTime.Today.AddMonths(-3);
    [ObservableProperty] private DateTime? _to = DateTime.Today;

    public StockTransactionsViewModel() => _type = Types[0];

    partial void OnTypeChanged(Option<InventoryTxType>? value) => _ = LoadAsync();
    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    protected override Task<PagedResult<InventoryTxRow>> FetchAsync(PageRequest r) =>
        Get<InventoryService>().ListTransactionsAsync(r, new DateRange(From?.Date ?? Today.AddYears(-10), To?.Date ?? Today), type: Type?.Value);

    protected override ReportTable BuildExport(IReadOnlyList<InventoryTxRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("type", "Col.Type").Col("material", "Col.Material", width: 1.8f).Col("wh", "Col.Warehouse")
            .Col("qty", "Col.Quantity", K.Number).Col("cost", "Col.UnitCost", K.Money).Col("value", "Col.Value", K.Money, total: true).Col("job", "Col.Job").Col("ref", "Col.Reference").Col("je", "Col.Entry");
        foreach (var r in rows) t.Add(r.Number, r.Date, r.Type, r.MaterialName, r.Warehouse, r.Quantity, r.UnitCost, r.TotalCost, r.JobNumber, r.Reference, r.JournalNumber);
        return t;
    }

    [RelayCommand]
    private async Task Movement(string action)
    {
        if (Enum.TryParse<StockAction>(action, out var a) && await Dialogs.ShowAsync(new StockMovementDialogViewModel(a, null))) await LoadAsync();
    }
}

public sealed partial class RemnantsViewModel : ListPageViewModel<RemnantRow>
{
    public override string TitleKey => "Nav.Remnants";
    protected override AppModule Module => AppModule.Inventory;
    public List<Option<RemnantStatus>> Statuses { get; } = Options.ForEnum<RemnantStatus>();
    [ObservableProperty] private Option<RemnantStatus>? _status;

    public RemnantsViewModel() => _status = Statuses[1];

    partial void OnStatusChanged(Option<RemnantStatus>? value) => _ = LoadAsync();

    protected override Task<PagedResult<RemnantRow>> FetchAsync(PageRequest r) => Get<InventoryService>().ListRemnantsAsync(r, Status?.Value);

    protected override ReportTable BuildExport(IReadOnlyList<RemnantRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("material", "Col.Material", width: 2).Col("thk", "Col.Thickness", K.Number).Col("len", "Col.Length", K.Number).Col("wid", "Col.Width", K.Number)
            .Col("wh", "Col.Warehouse").Col("job", "Col.SourceJob").Col("date", "Col.Date", K.Date).Col("status", "Col.Status").Col("cost", "Col.Cost", K.Money, total: true);
        foreach (var r in rows) t.Add(r.Code, r.MaterialName, r.Thickness, r.Length, r.Width, r.Warehouse, r.SourceJob, r.Date, r.Status, r.Cost);
        return t;
    }

    [RelayCommand] private async Task Create() { if (await Dialogs.ShowAsync(new RemnantDialogViewModel(RemnantAction.CreateFromStock))) await LoadAsync(); }
    [RelayCommand] private async Task Consume(RemnantRow? r) { if (await Dialogs.ShowAsync(new RemnantDialogViewModel(RemnantAction.Consume, null, r?.Id))) await LoadAsync(); }
    [RelayCommand] private async Task Adjust(RemnantRow? r) { if (await Dialogs.ShowAsync(new RemnantDialogViewModel(RemnantAction.Adjust, null, r?.Id))) await LoadAsync(); }
}

public sealed partial class UtilizationCalculatorViewModel : ViewModelBase
{
    public UtilizationCalculatorViewModel()
    {
        Pieces.Add(Track(new PieceVm { Name = "A", Length = 80, Width = 40, QuantityPerUnit = 1 }));
        Pieces.Add(Track(new PieceVm { Name = "B", Length = 30, Width = 20, QuantityPerUnit = 1 }));
        Pieces.Add(Track(new PieceVm { Name = "C", Length = 50, Width = 30, QuantityPerUnit = 1 }));
        Calculate();
    }

    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private decimal _sheetLength = 244;
    [ObservableProperty] private decimal _sheetWidth = 122;
    [ObservableProperty] private decimal _spacing = 0.5m;
    [ObservableProperty] private decimal _efficiency = 85;
    [ObservableProperty] private decimal _costPerSheet = 28;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private bool _chargeFullSheets = true;
    [ObservableProperty] private decimal _sheetsOverride;
    [ObservableProperty] private SheetLayoutResult? _result;
    [ObservableProperty] private PieceVm? _selectedPiece;
    public ObservableCollection<PieceVm> Pieces { get; } = new();
    public ObservableCollection<PieceFit> Fits { get; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();

    private PieceVm Track(PieceVm p) { p.PropertyChanged += (_, _) => Calculate(); return p; }

    public async Task LoadAsync()
    {
        Materials = (await Get<MaterialService>().LookupAsync()).Where(m => m.Length > 0 && m.Width > 0).ToList();
        OnPropertyChanged(nameof(Materials));
    }

    partial void OnMaterialChanged(MaterialLookup? value)
    {
        if (value == null) return;
        SheetLength = value.Length; SheetWidth = value.Width; CostPerSheet = value.AverageCost;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(SheetLength) or nameof(SheetWidth) or nameof(Spacing) or nameof(Efficiency) or nameof(CostPerSheet) or nameof(Quantity) or nameof(ChargeFullSheets) or nameof(SheetsOverride))
            Calculate();
    }

    [RelayCommand] private void AddPiece() { Pieces.Add(Track(new PieceVm { Name = ((char)('A' + Pieces.Count % 26)).ToString(), Length = 10, Width = 10 })); Calculate(); }
    [RelayCommand] private void RemovePiece(PieceVm? p) { if (p != null) { Pieces.Remove(p); Calculate(); } }

    private void Calculate()
    {
        try
        {
            ErrorMessage = null;
            Result = MaterialUtilizationCalculator.Calculate(new SheetLayoutInput(SheetLength, SheetWidth,
                Pieces.Select(p => new PieceSpec(p.Name, p.Length, p.Width, p.QuantityPerUnit * Quantity)).ToList(), Spacing, Efficiency, CostPerSheet, ChargeFullSheets, SheetsOverride));
            Fits.Clear();
            foreach (var f in Result.Fits) Fits.Add(f);
        }
        catch (Exception ex)
        {
            Result = null;
            ErrorMessage = Errors.Describe(ex);
        }
    }
}

public sealed partial class LaserParametersViewModel : ViewModelBase
{
    [ObservableProperty] private string? _search;
    [ObservableProperty] private LookupService.LaserParameterRow? _selected;
    public ObservableCollection<LookupService.LaserParameterRow> Items { get; } = new();

    partial void OnSearchChanged(string? value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Items.Clear();
            foreach (var r in await Get<LookupService>().LaserParametersAsync(Search)) Items.Add(r);
        });
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new LaserParameterEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(LookupService.LaserParameterRow? r) { if (r != null && await Dialogs.ShowAsync(new LaserParameterEditorViewModel(r.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(LookupService.LaserParameterRow? r)
    {
        if (r == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", r.Material), danger: true)) return;
        if (await RunAsync(() => Get<LookupService>().DeleteLaserParameterAsync(r.Id))) await LoadAsync();
    }
}

public sealed partial class LaserParameterEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public LaserParameterEditorViewModel(long id) => _id = id;
    public override string TitleKey => "LaserParam.Title";
    public override double DialogWidth => 620;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private decimal _thickness;
    [ObservableProperty] private OperationType _operationType = OperationType.Cutting;
    [ObservableProperty] private decimal _powerPercent = 50;
    [ObservableProperty] private decimal _speed = 20;
    [ObservableProperty] private decimal? _frequency;
    [ObservableProperty] private int _passes = 1;
    [ObservableProperty] private string? _notes;
    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public IReadOnlyList<OperationType> Operations { get; } = new[] { OperationType.Cutting, OperationType.Engraving };

    partial void OnMaterialChanged(MaterialLookup? value) { if (value != null && Thickness == 0) Thickness = value.Thickness; }

    public override async Task InitializeAsync()
    {
        Materials = await Get<MaterialService>().LookupAsync();
        Machines = await Get<MachineService>().LookupAsync();
        OnPropertyChanged(nameof(Materials)); OnPropertyChanged(nameof(Machines));
        if (_id == 0) return;
        var p = await Get<LookupService>().GetLaserParameterAsync(_id);
        if (p == null) return;
        Material = Materials.FirstOrDefault(m => m.Id == p.MaterialId); Machine = Machines.FirstOrDefault(m => m.Id == p.MachineId); Thickness = p.Thickness; OperationType = p.OperationType;
        PowerPercent = p.PowerPercent; Speed = p.SpeedMmPerSec; Frequency = p.FrequencyHz; Passes = p.PassCount; Notes = p.Notes;
    }

    protected override Task SaveAsync() => Get<LookupService>().SaveLaserParameterAsync(new LaserParameter
    {
        Id = _id, MaterialId = Material?.Id ?? 0, MachineId = Machine?.Id, Thickness = Thickness, OperationType = OperationType, PowerPercent = PowerPercent, SpeedMmPerSec = Speed,
        FrequencyHz = Frequency, PassCount = Passes, Notes = Notes
    });
}

public sealed partial class InventoryViewModel : PageViewModel
{
    public InventoryViewModel(string? tab = null)
    {
        SelectedTab = tab switch { "Remnants" => 1, "Calculator" => 2, "Parameters" => 3, _ => 0 };
    }

    public override string TitleKey => "Nav.Inventory";
    public StockTransactionsViewModel Transactions { get; } = new();
    public RemnantsViewModel Remnants { get; } = new();
    public UtilizationCalculatorViewModel Calculator { get; } = new();
    public LaserParametersViewModel Parameters { get; } = new();
    [ObservableProperty] private int _selectedTab;

    public override async Task LoadAsync()
    {
        await Transactions.LoadAsync();
        await Remnants.LoadAsync();
        await Calculator.LoadAsync();
        await Parameters.LoadAsync();
    }
}
