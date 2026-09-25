using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class MachinesViewModel : ListPageViewModel<MachineRow>
{
    public override string TitleKey => "Nav.Machines";
    protected override AppModule Module => AppModule.Machines;

    protected override Task<PagedResult<MachineRow>> FetchAsync(PageRequest r) => Get<MachineService>().ListAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<MachineRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("name", "Col.Name", width: 2).Col("type", "Col.Type", width: 1.5f).Col("power", "Machine.PowerWatts", K.Number).Col("rate", "Col.HourlyRate", K.Money).Col("active", "Col.Active");
        foreach (var m in rows) t.Add(m.Code, m.Name, m.MachineType, m.LaserPowerWatts, m.HourlyRate, m.IsActive);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new MachineEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(MachineRow? row) { if (row != null && await Dialogs.ShowAsync(new MachineEditorViewModel(row.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(MachineRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Name), danger: true)) return;
        if (await RunAsync(() => Get<MachineService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

public sealed partial class MachineEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public MachineEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Machine.New" : "Machine.Edit";
    public override double DialogWidth => 900;

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _machineType;
    [ObservableProperty] private decimal _laserPowerWatts;
    [ObservableProperty] private decimal _electricalLoadKw;
    [ObservableProperty] private decimal _purchaseCost;
    [ObservableProperty] private decimal _usefulLifeYears = 5;
    [ObservableProperty] private decimal _residualValue;
    [ObservableProperty] private decimal _annualWorkingHours = 2000;
    [ObservableProperty] private decimal _electricityPricePerKwh;
    [ObservableProperty] private decimal _maintenanceCostPerYear;
    [ObservableProperty] private decimal _operatorCostPerHour;
    [ObservableProperty] private decimal _otherOverheadPerYear;
    [ObservableProperty] private bool _useHourlyCostOverride;
    [ObservableProperty] private decimal _hourlyCostOverride;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private MachineRateBreakdown? _rate;
    [ObservableProperty] private string? _rateError;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Rate) or nameof(RateError) or nameof(IsBusy) or nameof(ErrorMessage) or nameof(InfoMessage)) return;
        try
        {
            Rate = MachineCostCalculator.Calculate(ToEntity().ToInput());
            RateError = null;
        }
        catch (Exception ex)
        {
            Rate = null;
            RateError = Errors.Describe(ex);
        }
    }

    private Machine ToEntity() => new()
    {
        Id = _id, Code = Code ?? "", Name = Name ?? "", MachineType = MachineType, LaserPowerWatts = LaserPowerWatts, ElectricalLoadKw = ElectricalLoadKw, PurchaseCost = PurchaseCost,
        UsefulLifeYears = UsefulLifeYears, ResidualValue = ResidualValue, AnnualWorkingHours = AnnualWorkingHours, ElectricityPricePerKwh = ElectricityPricePerKwh,
        MaintenanceCostPerYear = MaintenanceCostPerYear, OperatorCostPerHour = OperatorCostPerHour, OtherOverheadPerYear = OtherOverheadPerYear,
        UseHourlyCostOverride = UseHourlyCostOverride, HourlyCostOverride = HourlyCostOverride, IsActive = IsActive, Notes = Notes
    };

    public override async Task InitializeAsync()
    {
        if (_id == 0)
        {
            ElectricityPricePerKwh = (await Get<SettingsService>().GetAsync()).DefaultElectricityPricePerKwh;
            return;
        }
        var m = await Get<MachineService>().GetAsync(_id);
        if (m == null) return;
        Code = m.Code; Name = m.Name; MachineType = m.MachineType; LaserPowerWatts = m.LaserPowerWatts; ElectricalLoadKw = m.ElectricalLoadKw; PurchaseCost = m.PurchaseCost;
        UsefulLifeYears = m.UsefulLifeYears; ResidualValue = m.ResidualValue; AnnualWorkingHours = m.AnnualWorkingHours; ElectricityPricePerKwh = m.ElectricityPricePerKwh;
        MaintenanceCostPerYear = m.MaintenanceCostPerYear; OperatorCostPerHour = m.OperatorCostPerHour; OtherOverheadPerYear = m.OtherOverheadPerYear;
        UseHourlyCostOverride = m.UseHourlyCostOverride; HourlyCostOverride = m.HourlyCostOverride; IsActive = m.IsActive; Notes = m.Notes;
    }

    protected override Task SaveAsync() => Get<MachineService>().SaveAsync(ToEntity());
}

public sealed partial class EmployeesViewModel : ListPageViewModel<EmployeeRow>
{
    public override string TitleKey => "Nav.Employees";
    protected override AppModule Module => AppModule.Employees;

    protected override Task<PagedResult<EmployeeRow>> FetchAsync(PageRequest r) => Get<EmployeeService>().ListAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<EmployeeRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("name", "Col.Name", width: 2).Col("role", "Col.Role", width: 1.5f).Col("rate", "Col.HourlyCost", K.Money).Col("phone", "Col.Phone").Col("active", "Col.Active");
        foreach (var e in rows) t.Add(e.Code, e.Name, e.Role, e.HourlyCost, e.Phone, e.IsActive);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new EmployeeEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(EmployeeRow? row) { if (row != null && await Dialogs.ShowAsync(new EmployeeEditorViewModel(row.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(EmployeeRow? row)
    {
        if (row == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", row.Name), danger: true)) return;
        if (await RunAsync(() => Get<EmployeeService>().DeleteAsync(row.Id), "Msg.Deleted")) await LoadAsync();
    }
}

public sealed partial class EmployeeEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public EmployeeEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Employee.New" : "Employee.Edit";
    public override double DialogWidth => 560;
    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _role;
    [ObservableProperty] private decimal _hourlyCost;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private bool _isActive = true;

    public override async Task InitializeAsync()
    {
        if (_id == 0) { HourlyCost = (await Get<SettingsService>().GetAsync()).DefaultLaborRate; return; }
        var e = await Get<EmployeeService>().GetAsync(_id);
        if (e == null) return;
        Code = e.Code; Name = e.Name; Role = e.Role; HourlyCost = e.HourlyCost; Phone = e.Phone; IsActive = e.IsActive;
    }

    protected override Task SaveAsync() => Get<EmployeeService>().SaveAsync(new Employee { Id = _id, Code = Code ?? "", Name = Name ?? "", Role = Role, HourlyCost = HourlyCost, Phone = Phone, IsActive = IsActive });
}
