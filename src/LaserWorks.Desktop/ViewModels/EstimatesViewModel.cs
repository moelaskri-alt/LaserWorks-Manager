using System.Collections.ObjectModel;
using System.ComponentModel;
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

public sealed partial class EstimatesViewModel : ListPageViewModel<EstimateRow>
{
    public override string TitleKey => "Nav.Estimates";
    protected override AppModule Module => AppModule.Estimates;

    public List<Option<EstimateStatus>> Statuses { get; } = Options.ForEnum<EstimateStatus>();
    [ObservableProperty] private Option<EstimateStatus>? _status;

    public EstimatesViewModel() => _status = Statuses[0];

    partial void OnStatusChanged(Option<EstimateStatus>? value) => _ = LoadAsync();

    protected override Task<PagedResult<EstimateRow>> FetchAsync(PageRequest r) => Get<EstimateService>().ListAsync(r, Status?.Value);

    protected override ReportTable BuildExport(IReadOnlyList<EstimateRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.5f).Col("req", "Col.Request").Col("desc", "Col.Description", width: 2.2f)
            .Col("qty", "Col.Quantity", K.Number).Col("cost", "Col.TotalCost", K.Money, total: true).Col("suggested", "Col.SuggestedPrice", K.Money, total: true).Col("price", "Col.SellingPrice", K.Money, total: true).Col("status", "Col.Status");
        foreach (var e in rows) t.Add(e.Number, e.Date, e.Customer, e.RequestNumber, e.Description, e.Quantity, e.TotalCost, e.SuggestedPrice, e.SellingPrice, e.Status);
        return t;
    }

    [RelayCommand]
    private async Task New()
    {
        var customers = await Get<CustomerService>().LookupAsync();
        if (customers.Count == 0) { ErrorMessage = L["Err.NoCustomers"]; return; }
        Nav.Navigate(new EstimateEditorViewModel(0, customers[0].Id, null));
    }

    [RelayCommand] private void Open(EstimateRow? row) { if (row != null) Nav.Navigate(new EstimateEditorViewModel(row.Id)); }

    [RelayCommand]
    private async Task Delete(EstimateRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Number), danger: true)) return;
        if (await RunAsync(() => Get<EstimateService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

// ------------------------------------------------------------------ row view models for the estimate editor
public sealed partial class PieceVm : ObservableObject
{
    [ObservableProperty] private string? _name;
    [ObservableProperty] private decimal _length;
    [ObservableProperty] private decimal _width;
    [ObservableProperty] private decimal _quantityPerUnit = 1;
}

public sealed partial class MaterialLineVm : ObservableObject
{
    public MaterialLineVm(EstimateEditorViewModel owner) => Owner = owner;
    public EstimateEditorViewModel Owner { get; }

    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private bool _sheetBased = true;
    [ObservableProperty] private decimal _sheetLength;
    [ObservableProperty] private decimal _sheetWidth;
    [ObservableProperty] private decimal _spacing = 0.5m;
    [ObservableProperty] private decimal _nestingEfficiency = 85;
    [ObservableProperty] private bool _chargeFullSheets = true;
    [ObservableProperty] private decimal _sheetsOverride;
    [ObservableProperty] private decimal _quantityPerUnit;
    [ObservableProperty] private decimal _unitCost;
    [ObservableProperty] private decimal _sheetsRequired;
    [ObservableProperty] private decimal _utilizationPercent;
    [ObservableProperty] private decimal _wasteArea;
    [ObservableProperty] private decimal _totalQuantity;
    [ObservableProperty] private decimal _cost;
    [ObservableProperty] private PieceVm? _selectedPiece;
    public ObservableCollection<PieceVm> Pieces { get; } = new();

    partial void OnMaterialChanged(MaterialLookup? value)
    {
        if (value == null || Owner.Suspended) return;
        SheetBased = value.IsSheet || (value.Length > 0 && value.Width > 0);
        SheetLength = value.Length;
        SheetWidth = value.Width;
        UnitCost = value.AverageCost;
    }

    [RelayCommand] private void AddPiece() => Owner.Track(AddPieceCore(new PieceVm { Name = L.Get("Estimate.Piece") + " " + (Pieces.Count + 1), Length = 10, Width = 10 }));

    public PieceVm AddPieceCore(PieceVm p) { Pieces.Add(p); return p; }

    [RelayCommand] private void RemovePiece(PieceVm? p) { if (p != null) { Pieces.Remove(p); Owner.ScheduleRecalc(); } }

    [RelayCommand] private void Remove() => Owner.RemoveMaterial(this);

    [RelayCommand]
    private async Task FindRemnants()
    {
        if (Material == null) return;
        var maxL = Pieces.Count > 0 ? Pieces.Max(p => Math.Max(p.Length, p.Width)) : 0;
        var maxW = Pieces.Count > 0 ? Pieces.Max(p => Math.Min(p.Length, p.Width)) : 0;
        var found = await ViewModelBase.Get<InventoryService>().FindRemnantsAsync(Material.Id, maxL, maxW);
        Owner.RemnantHint = found.Count == 0 ? L.Get("Estimate.NoRemnants") : L.Format("Estimate.RemnantsFound", found.Count, string.Join(", ", found.Take(4).Select(r => $"{r.Code} {r.Length:0.#}×{r.Width:0.#}")));
    }

    private static LaserWorks.Localization.Loc L => LaserWorks.Localization.Loc.Instance;
}

public sealed partial class MachineLineVm : ObservableObject
{
    public MachineLineVm(EstimateEditorViewModel owner) => Owner = owner;
    public EstimateEditorViewModel Owner { get; }
    [ObservableProperty] private Lookup? _machine;
    [ObservableProperty] private OperationType _operation = OperationType.Cutting;
    [ObservableProperty] private decimal _minutesPerUnit;
    [ObservableProperty] private decimal _hours;
    [ObservableProperty] private decimal _hourlyRate;
    [ObservableProperty] private decimal _maintenanceRate;
    [ObservableProperty] private decimal _cost;
    [RelayCommand] private void Remove() => Owner.RemoveMachine(this);
}

public sealed partial class LaborLineVm : ObservableObject
{
    public LaborLineVm(EstimateEditorViewModel owner) => Owner = owner;
    public EstimateEditorViewModel Owner { get; }
    [ObservableProperty] private Lookup? _employee;
    [ObservableProperty] private OperationType _operation = OperationType.Assembly;
    [ObservableProperty] private decimal _minutesPerUnit;
    [ObservableProperty] private decimal _hourlyRate;
    [ObservableProperty] private decimal _hours;
    [ObservableProperty] private decimal _cost;

    partial void OnEmployeeChanged(Lookup? value)
    {
        if (value != null && !Owner.Suspended && Owner.EmployeeRates.TryGetValue(value.Id, out var r)) HourlyRate = r;
    }

    [RelayCommand] private void Remove() => Owner.RemoveLabor(this);
}

public sealed partial class ComponentVm : ObservableObject
{
    public ComponentVm(CostComponent c) => Component = c;
    public CostComponent Component { get; }
    public string Name => LaserWorks.Localization.Loc.Instance.Enum(Component);
    [ObservableProperty] private bool _isManual;
    [ObservableProperty] private decimal _manualAmount;
    [ObservableProperty] private decimal _calculatedAmount;
    [ObservableProperty] private decimal _amount;
}

/// <summary>
/// Full cost estimate editor with live recalculation. Every change recalculates material utilisation, machine, labor,
/// overhead and scrap allowance, suggested/minimum price and the what-if scenario.
/// </summary>
public sealed partial class EstimateEditorViewModel : PageViewModel
{
    private long _id;
    private readonly long? _customerId;
    private readonly long? _requestId;
    private CancellationTokenSource? _recalcDelay;
    private Dictionary<long, MachineRateBreakdown> _rates = new();

    public EstimateEditorViewModel(long id, long? customerId = null, long? requestId = null)
    {
        _id = id;
        _customerId = customerId;
        _requestId = requestId;
        foreach (var c in EstimateCalculator.EstimateComponents) Track(new ComponentVm(c), Components);
    }

    public override string TitleKey => "Estimate.Title";
    public bool Suspended { get; private set; }
    public Dictionary<long, decimal> EmployeeRates { get; private set; } = new();

    [ObservableProperty] private string? _number;
    [ObservableProperty] private EstimateStatus _status;
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private Lookup? _request;
    [ObservableProperty] private long? _designRevisionId;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private decimal _designHours;
    [ObservableProperty] private decimal _designRate;
    [ObservableProperty] private decimal _setupHours;
    [ObservableProperty] private decimal _setupRate;
    [ObservableProperty] private decimal _finishingPerUnit;
    [ObservableProperty] private decimal _packagingPerUnit;
    [ObservableProperty] private decimal _consumablesPerUnit;
    [ObservableProperty] private OverheadMethod _overheadMethod;
    [ObservableProperty] private decimal _overheadRate;
    [ObservableProperty] private decimal _scrapAllowancePercent;
    [ObservableProperty] private decimal _targetMarginPercent;
    [ObservableProperty] private decimal _minimumMarginPercent;
    [ObservableProperty] private decimal _sellingPrice;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _calcError;
    [ObservableProperty] private string? _remnantHint;

    // results
    [ObservableProperty] private decimal _directCost;
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _unitCost;
    [ObservableProperty] private decimal _suggestedPrice;
    [ObservableProperty] private decimal _minimumPrice;
    [ObservableProperty] private decimal _totalMachineHours;
    [ObservableProperty] private decimal _profit;
    [ObservableProperty] private decimal _marginAtPrice;
    [ObservableProperty] private decimal _markupAtPrice;
    [ObservableProperty] private bool _belowMinimum;

    // what-if
    [ObservableProperty] private decimal? _wiQuantity;
    [ObservableProperty] private decimal? _wiMaterialCost;
    [ObservableProperty] private decimal? _wiMachineHours;
    [ObservableProperty] private decimal? _wiMargin;
    [ObservableProperty] private decimal? _wiPrice;
    [ObservableProperty] private WhatIfResult? _whatIf;

    public ObservableCollection<MaterialLineVm> MaterialLines { get; } = new();
    public ObservableCollection<MachineLineVm> MachineLines { get; } = new();
    public ObservableCollection<LaborLineVm> LaborLines { get; } = new();
    public ObservableCollection<ComponentVm> Components { get; } = new();
    public List<Lookup> Customers { get; private set; } = new();
    public List<Lookup> Requests { get; private set; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public List<Lookup> Machines { get; private set; } = new();
    public List<Lookup> Employees { get; private set; } = new();
    public IReadOnlyList<OperationType> Operations { get; } = Enum.GetValues<OperationType>();
    public IReadOnlyList<OverheadMethod> OverheadMethods { get; } = Enum.GetValues<OverheadMethod>();
    public bool IsDraft => Status == EstimateStatus.Draft;
    public bool IsFinal => Status == EstimateStatus.Final;
    public bool IsSaved => _id != 0;
    public string Header => _id == 0 ? L["Estimate.New"] : $"{L["Estimate.Title"]} {Number}";

    partial void OnStatusChanged(EstimateStatus value) { OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsFinal)); }

    partial void OnCustomerChanged(Lookup? value)
    {
        if (Suspended || value == null) return;
        _ = LoadRequestsAsync(value.Id);
    }

    private async Task LoadRequestsAsync(long customerId)
    {
        Requests = await Get<RequestService>().LookupAsync(customerId);
        OnPropertyChanged(nameof(Requests));
    }

    public T Track<T>(T item, System.Collections.IList? into = null) where T : INotifyPropertyChanged
    {
        item.PropertyChanged += OnChildChanged;
        into?.Add(item);
        return item;
    }

    private static readonly HashSet<string> ResultProps = new()
    {
        nameof(MaterialLineVm.SheetsRequired), nameof(MaterialLineVm.UtilizationPercent), nameof(MaterialLineVm.WasteArea), nameof(MaterialLineVm.TotalQuantity), nameof(MaterialLineVm.Cost),
        nameof(MachineLineVm.Hours), nameof(MachineLineVm.HourlyRate), nameof(MachineLineVm.MaintenanceRate), nameof(ComponentVm.CalculatedAmount), nameof(ComponentVm.Amount), nameof(MaterialLineVm.SelectedPiece)
    };

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Suspended || ResultProps.Contains(e.PropertyName ?? "")) return;
        if (sender is LaborLineVm && e.PropertyName is nameof(LaborLineVm.Hours) or nameof(LaborLineVm.Cost)) return;
        if (sender is MachineLineVm && e.PropertyName is nameof(MachineLineVm.Cost)) return;
        ScheduleRecalc();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (Suspended) return;
        switch (e.PropertyName)
        {
            case nameof(Quantity): case nameof(DesignHours): case nameof(DesignRate): case nameof(SetupHours): case nameof(SetupRate): case nameof(FinishingPerUnit):
            case nameof(PackagingPerUnit): case nameof(ConsumablesPerUnit): case nameof(OverheadMethod): case nameof(OverheadRate): case nameof(ScrapAllowancePercent):
            case nameof(TargetMarginPercent): case nameof(MinimumMarginPercent): case nameof(SellingPrice):
                ScheduleRecalc();
                break;
            case nameof(WiQuantity): case nameof(WiMaterialCost): case nameof(WiMachineHours): case nameof(WiMargin): case nameof(WiPrice):
                RecalcWhatIf();
                break;
        }
    }

    public void ScheduleRecalc()
    {
        _recalcDelay?.Cancel();
        var cts = _recalcDelay = new CancellationTokenSource();
        _ = Task.Delay(150, cts.Token).ContinueWith(t => { if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(Recalculate); }, TaskScheduler.Default);
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Customers = await Get<CustomerService>().LookupAsync(activeOnly: false);
            Materials = await Get<MaterialService>().LookupAsync();
            Machines = await Get<MachineService>().LookupAsync();
            Employees = await Get<EmployeeService>().LookupAsync();
            EmployeeRates = (await Get<EmployeeService>().ListAsync(new PageRequest(PageSize: 5000))).Items.ToDictionary(e => e.Id, e => e.HourlyCost);
            _rates = await Get<EstimateService>().MachineRatesAsync();
            foreach (var p in new[] { nameof(Customers), nameof(Materials), nameof(Machines), nameof(Employees) }) OnPropertyChanged(p);
            var e = _id == 0 ? await Get<EstimateService>().NewDraftAsync(_customerId ?? Customers.First().Id, _requestId) : await Get<EstimateService>().GetAsync(_id) ?? throw new DomainException("Err.NotFound");
            Requests = await Get<RequestService>().LookupAsync(e.CustomerId);
            OnPropertyChanged(nameof(Requests));
            FromEntity(e);
        });
        Recalculate();
    }

    private void FromEntity(CostEstimate e)
    {
        Suspended = true;
        try
        {
            Number = e.Number; Status = e.Status; Customer = Customers.FirstOrDefault(c => c.Id == e.CustomerId); Request = Requests.FirstOrDefault(r => r.Id == e.RequestId);
            DesignRevisionId = e.DesignRevisionId; Date = e.Date; Description = e.Description; Quantity = e.Quantity; DesignHours = e.DesignHours; DesignRate = e.DesignRate;
            SetupHours = e.SetupHours; SetupRate = e.SetupRate; FinishingPerUnit = e.FinishingPerUnit; PackagingPerUnit = e.PackagingPerUnit; ConsumablesPerUnit = e.ConsumablesPerUnit;
            OverheadMethod = e.OverheadMethod; OverheadRate = e.OverheadRate; ScrapAllowancePercent = e.ScrapAllowancePercent; TargetMarginPercent = e.TargetMarginPercent;
            MinimumMarginPercent = e.MinimumMarginPercent; Notes = e.Notes;
            _settingPrice = true;
            SellingPrice = e.SellingPrice;
            _settingPrice = false;
            // a new estimate (or one still priced at its suggestion) keeps following the suggested price until the user types a price
            _priceFollowsSuggestion = _id == 0 || e.SellingPrice == 0 || e.SellingPrice == e.SuggestedPrice;
            MaterialLines.Clear();
            foreach (var l in e.MaterialLines)
            {
                var vm = Track(new MaterialLineVm(this)
                {
                    Material = Materials.FirstOrDefault(m => m.Id == l.MaterialId) ?? (l.Material is { } mm ? new MaterialLookup(mm.Id, mm.Code, mm.Name, mm.Thickness, mm.Length, mm.Width, mm.AverageCost, "", true, mm.Kind, mm.QuantityOnHand, mm.SalesPrice) : null),
                    SheetBased = l.SheetBased, SheetLength = l.SheetLength, SheetWidth = l.SheetWidth, Spacing = l.Spacing, NestingEfficiency = l.NestingEfficiency,
                    ChargeFullSheets = l.ChargeFullSheets, SheetsOverride = l.SheetsOverride, QuantityPerUnit = l.QuantityPerUnit, UnitCost = l.UnitCost
                }, MaterialLines);
                foreach (var p in l.Pieces) Track(vm.AddPieceCore(new PieceVm { Name = p.Name, Length = p.Length, Width = p.Width, QuantityPerUnit = p.QuantityPerUnit }));
            }
            MachineLines.Clear();
            foreach (var m in e.MachineLines)
                Track(new MachineLineVm(this) { Machine = Machines.FirstOrDefault(x => x.Id == m.MachineId), Operation = m.Operation, MinutesPerUnit = m.MinutesPerUnit, HourlyRate = m.HourlyRate, MaintenanceRate = m.MaintenanceRate }, MachineLines);
            LaborLines.Clear();
            foreach (var l in e.LaborLines)
                Track(new LaborLineVm(this) { Employee = Employees.FirstOrDefault(x => x.Id == l.EmployeeId), Operation = l.Operation, MinutesPerUnit = l.MinutesPerUnit, HourlyRate = l.HourlyRate }, LaborLines);
            foreach (var c in Components)
            {
                var src = e.Components.FirstOrDefault(x => x.Component == c.Component);
                c.IsManual = src?.Mode == ComponentMode.Manual;
                c.ManualAmount = src?.ManualAmount ?? 0;
            }
        }
        finally { Suspended = false; }
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsSaved));
    }

    private CostEstimate ToEntity()
    {
        var e = new CostEstimate
        {
            Id = _id, Number = Number ?? "", Status = Status, CustomerId = Customer?.Id ?? 0, RequestId = Request?.Id, DesignRevisionId = DesignRevisionId, Date = Date?.Date ?? Today,
            Description = Description ?? "", Quantity = Quantity, DesignHours = DesignHours, DesignRate = DesignRate, SetupHours = SetupHours, SetupRate = SetupRate,
            FinishingPerUnit = FinishingPerUnit, PackagingPerUnit = PackagingPerUnit, ConsumablesPerUnit = ConsumablesPerUnit, OverheadMethod = OverheadMethod, OverheadRate = OverheadRate,
            ScrapAllowancePercent = ScrapAllowancePercent, TargetMarginPercent = TargetMarginPercent, MinimumMarginPercent = MinimumMarginPercent, SellingPrice = SellingPrice, Notes = Notes
        };
        foreach (var l in MaterialLines)
        {
            var ml = new EstimateMaterialLine
            {
                MaterialId = l.Material?.Id ?? 0, SheetBased = l.SheetBased, SheetLength = l.SheetLength, SheetWidth = l.SheetWidth, Spacing = l.Spacing, NestingEfficiency = l.NestingEfficiency,
                ChargeFullSheets = l.ChargeFullSheets, SheetsOverride = l.SheetsOverride, QuantityPerUnit = l.QuantityPerUnit, UnitCost = l.UnitCost
            };
            foreach (var p in l.Pieces) ml.Pieces.Add(new EstimatePiece { Name = p.Name, Length = p.Length, Width = p.Width, QuantityPerUnit = p.QuantityPerUnit });
            e.MaterialLines.Add(ml);
        }
        foreach (var m in MachineLines)
            e.MachineLines.Add(new EstimateMachineLine { MachineId = m.Machine?.Id ?? 0, Operation = m.Operation, MinutesPerUnit = m.MinutesPerUnit, HourlyRate = m.HourlyRate, MaintenanceRate = m.MaintenanceRate });
        foreach (var l in LaborLines)
            e.LaborLines.Add(new EstimateLaborLine { EmployeeId = l.Employee?.Id, Operation = l.Operation, MinutesPerUnit = l.MinutesPerUnit, HourlyRate = l.HourlyRate });
        foreach (var c in Components)
            e.Components.Add(new EstimateComponentLine { Component = c.Component, Mode = c.IsManual ? ComponentMode.Manual : ComponentMode.Auto, ManualAmount = c.ManualAmount });
        return e;
    }

    public void Recalculate()
    {
        try
        {
            var e = ToEntity();
            var result = EstimateBuilder.Compute(e, Decimals, IsDraft ? _rates : null);
            Suspended = true;
            for (var i = 0; i < MaterialLines.Count; i++)
            {
                var src = e.MaterialLines[i];
                var vm = MaterialLines[i];
                vm.SheetsRequired = src.SheetsRequired; vm.UtilizationPercent = src.UtilizationPercent; vm.WasteArea = src.WasteArea; vm.TotalQuantity = src.TotalQuantity; vm.Cost = src.Cost;
            }
            for (var i = 0; i < MachineLines.Count; i++)
            {
                var src = e.MachineLines[i];
                var vm = MachineLines[i];
                vm.Hours = src.Hours; vm.HourlyRate = src.HourlyRate; vm.MaintenanceRate = src.MaintenanceRate; vm.Cost = src.MachineCost + src.MaintenanceCost;
            }
            for (var i = 0; i < LaborLines.Count; i++) { LaborLines[i].Hours = e.LaborLines[i].Hours; LaborLines[i].Cost = e.LaborLines[i].Cost; }
            foreach (var c in Components) { c.CalculatedAmount = result.Calculated.GetValueOrDefault(c.Component); c.Amount = result[c.Component]; }
            DirectCost = e.DirectCost; TotalCost = e.TotalCost; UnitCost = e.UnitCost; SuggestedPrice = e.SuggestedPrice; MinimumPrice = e.MinimumPrice; TotalMachineHours = e.TotalMachineHours;
            if (SellingPrice <= 0 || _priceFollowsSuggestion)
            {
                _settingPrice = true;
                SellingPrice = e.SuggestedPrice;
                _settingPrice = false;
            }
            var a = PricingCalculator.Analyze(TotalCost, SellingPrice);
            Profit = a.Profit; MarginAtPrice = a.MarginPercent; MarkupAtPrice = a.MarkupPercent; BelowMinimum = SellingPrice < MinimumPrice;
            CalcError = null;
        }
        catch (Exception ex)
        {
            CalcError = Errors.Describe(ex);
        }
        finally
        {
            Suspended = false;
        }
        RecalcWhatIf();
    }

    private void RecalcWhatIf()
    {
        try
        {
            WhatIf = EstimateBuilder.WhatIf(ToEntityWithLines(), new WhatIfInput(WiQuantity, WiMaterialCost, WiMachineHours, WiMargin, WiPrice), Decimals);
        }
        catch (Exception ex)
        {
            WhatIf = null;
            CalcError = Errors.Describe(ex);
        }
    }

    private CostEstimate ToEntityWithLines()
    {
        var e = ToEntity();
        EstimateBuilder.Compute(e, Decimals, IsDraft ? _rates : null);
        return e;
    }

    [RelayCommand] private void ResetWhatIf() { WiQuantity = null; WiMaterialCost = null; WiMachineHours = null; WiMargin = null; WiPrice = null; }
    [RelayCommand]
    private void UseSuggestedPrice()
    {
        SellingPrice = SuggestedPrice;
        _priceFollowsSuggestion = true;
    }

    private bool _priceFollowsSuggestion;
    private bool _settingPrice;

    partial void OnSellingPriceChanged(decimal value)
    {
        if (!_settingPrice && !Suspended) _priceFollowsSuggestion = false;
    }
    [RelayCommand] private void ApplyWhatIfPrice() { if (WhatIf != null && WiQuantity == null) SellingPrice = WhatIf.SellingPrice; }

    [RelayCommand]
    private void AddMaterial()
    {
        var m = Track(new MaterialLineVm(this), MaterialLines);
        m.Material = Materials.FirstOrDefault();
        Track(m.AddPieceCore(new PieceVm { Name = L["Estimate.Piece"] + " 1", Length = 10, Width = 10 }));
        ScheduleRecalc();
    }

    [RelayCommand]
    private void AddMachine()
    {
        var m = Track(new MachineLineVm(this), MachineLines);
        m.Machine = Machines.FirstOrDefault();
        m.MinutesPerUnit = 5;
        ScheduleRecalc();
    }

    [RelayCommand]
    private void AddLabor()
    {
        var l = Track(new LaborLineVm(this), LaborLines);
        l.Employee = Employees.FirstOrDefault();
        l.MinutesPerUnit = 5;
        ScheduleRecalc();
    }

    public void RemoveMaterial(MaterialLineVm vm) { MaterialLines.Remove(vm); ScheduleRecalc(); }
    public void RemoveMachine(MachineLineVm vm) { MachineLines.Remove(vm); ScheduleRecalc(); }
    public void RemoveLabor(LaborLineVm vm) { LaborLines.Remove(vm); ScheduleRecalc(); }

    [RelayCommand]
    public async Task Save()
    {
        Recalculate();
        if (await RunAsync(async () => _id = await Get<EstimateService>().SaveAsync(ToEntity()), "Msg.Saved"))
        {
            var e = await Get<EstimateService>().GetAsync(_id);
            if (e != null) { Number = e.Number; Status = e.Status; }
            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(IsSaved));
        }
    }

    [RelayCommand]
    private async Task Finalize()
    {
        await Save();
        if (ErrorMessage != null || _id == 0) return;
        if (await RunAsync(() => Get<EstimateService>().FinalizeAsync(_id), "Msg.EstimateFinalized")) Status = EstimateStatus.Final;
    }

    [RelayCommand]
    private async Task CreateQuotation()
    {
        if (IsDraft) await Save();
        if (ErrorMessage != null || _id == 0) return;
        long qid = 0;
        if (await RunAsync(async () => qid = await Get<QuotationService>().CreateFromEstimateAsync(_id)))
        {
            Status = EstimateStatus.Final;
            await Dialogs.ShowAsync(new QuotationEditorViewModel(qid));
        }
    }

    [RelayCommand]
    private async Task Duplicate()
    {
        long id = 0;
        if (await RunAsync(async () => id = await Get<EstimateService>().DuplicateAsync(_id))) Nav.Navigate(new EstimateEditorViewModel(id));
    }

    [RelayCommand] private void Back() => Nav.Go("Estimates");
}
