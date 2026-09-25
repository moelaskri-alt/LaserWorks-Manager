using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class SetupMachineRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _type = "CO2";
    [ObservableProperty] private decimal _powerWatts = 100;
    [ObservableProperty] private decimal _loadKw = 2.5m;
    [ObservableProperty] private decimal _purchaseCost;
    [ObservableProperty] private decimal _lifeYears = 5;
    [ObservableProperty] private decimal _residualValue;
    [ObservableProperty] private decimal _annualHours = 2000;
    [ObservableProperty] private decimal _maintenancePerYear;
}

public sealed partial class SetupMaterialRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _materialType = "MDF";
    [ObservableProperty] private decimal _thickness = 3;
    [ObservableProperty] private decimal _length = 244;
    [ObservableProperty] private decimal _width = 122;
    [ObservableProperty] private string _unitCode = "SHEET";
    [ObservableProperty] private decimal _unitCost;
    [ObservableProperty] private decimal _openingQuantity;
}

public sealed partial class SetupUserRow : ObservableObject
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private UserRole _role = UserRole.Sales;
    [ObservableProperty] private string _password = "";
}

/// <summary>First-run wizard: company → currency/tax → fiscal year → warehouses → machines → materials → users → opening balances → demo data.</summary>
public sealed partial class SetupWizardViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    public const int StepCount = 9;

    public SetupWizardViewModel(MainWindowViewModel main)
    {
        _main = main;
        Machines.Add(new SetupMachineRow { Name = "CO2 Laser 1390 100W", PurchaseCost = 60000, MaintenancePerYear = 6000, ResidualValue = 6000 });
        Materials.Add(new SetupMaterialRow { Name = "MDF 3mm 122×244", UnitCost = 28 });
        Materials.Add(new SetupMaterialRow { Name = "Acrylic Clear 3mm 122×244", MaterialType = "Acrylic", UnitCost = 140 });
    }

    [ObservableProperty] private int _step = 1;
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private string _currencyCode = "SAR";
    [ObservableProperty] private string _currencySymbol = "ر.س";
    [ObservableProperty] private decimal _taxRate = 15;
    [ObservableProperty] private int _decimalPlaces = 2;
    [ObservableProperty] private decimal _marginPercent = 30;
    [ObservableProperty] private decimal _overheadPercent = 10;
    [ObservableProperty] private decimal _laborRate = 25;
    [ObservableProperty] private decimal _electricityPrice = 0.18m;
    [ObservableProperty] private DateTime? _fiscalYearStart = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private string _warehouseNames = "Main Warehouse";
    [ObservableProperty] private string _adminUsername = "admin";
    [ObservableProperty] private string _adminFullName = "Administrator";
    [ObservableProperty] private string? _adminPassword;
    [ObservableProperty] private string? _adminPasswordConfirm;
    [ObservableProperty] private decimal _openingCash;
    [ObservableProperty] private decimal _openingBank;
    [ObservableProperty] private bool _loadDemoData;
    [ObservableProperty] private string _language = "ar";
    [ObservableProperty] private string? _progressText;

    public ObservableCollection<SetupMachineRow> Machines { get; } = new();
    public ObservableCollection<SetupMaterialRow> Materials { get; } = new();
    public ObservableCollection<SetupUserRow> Users { get; } = new();
    public IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>().Where(r => r != UserRole.Administrator).ToList();
    public IReadOnlyList<string> UnitCodes { get; } = new[] { "SHEET", "PCS", "M", "M2", "KG", "L", "ROLL", "BOX" };
    public IReadOnlyList<string> Languages { get; } = new[] { "ar", "en" };

    public string StepTitle => L[$"Setup.Step{Step}"];
    public string StepText => L.Format("Setup.StepOf", Step, StepCount);
    public bool IsFirst => Step == 1;
    public bool IsLast => Step == StepCount;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool IsStep5 => Step == 5;
    public bool IsStep6 => Step == 6;
    public bool IsStep7 => Step == 7;
    public bool IsStep8 => Step == 8;
    public bool IsStep9 => Step == 9;

    partial void OnStepChanged(int value)
    {
        foreach (var p in new[] { nameof(StepTitle), nameof(StepText), nameof(IsFirst), nameof(IsLast), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5),
                     nameof(IsStep6), nameof(IsStep7), nameof(IsStep8), nameof(IsStep9) })
            OnPropertyChanged(p);
    }

    partial void OnLanguageChanged(string value)
    {
        App.ApplyLanguage(value);
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepText));
    }

    private void ValidateStep()
    {
        switch (Step)
        {
            case 1: if (string.IsNullOrWhiteSpace(CompanyName)) throw new DomainException("Err.Required", L["Company.Name"]); break;
            case 2:
                if (string.IsNullOrWhiteSpace(CurrencyCode)) throw new DomainException("Err.Required", L["Company.Currency"]);
                if (TaxRate is < 0 or > 100) throw new DomainException("Err.PercentRange", L["Company.TaxRate"]);
                if (MarginPercent is < 0 or >= 100) throw new DomainException("Err.MarginBelow100");
                break;
            case 3: if (FiscalYearStart == null) throw new DomainException("Err.Required", L["Setup.FiscalYearStart"]); break;
            case 4: if (WarehouseList().Count == 0) throw new DomainException("Err.Required", L["Nav.Warehouses"]); break;
            case 5:
                foreach (var m in Machines)
                {
                    if (string.IsNullOrWhiteSpace(m.Name)) throw new DomainException("Err.Required", L["Col.Machine"]);
                    if (m.ResidualValue > m.PurchaseCost) throw new DomainException("Err.ResidualExceedsCost");
                }
                break;
            case 6:
                foreach (var m in Materials) if (string.IsNullOrWhiteSpace(m.Name)) throw new DomainException("Err.Required", L["Col.Material"]);
                break;
            case 7:
                if (string.IsNullOrWhiteSpace(AdminUsername)) throw new DomainException("Err.Required", L["Login.Username"]);
                if (AdminPassword != AdminPasswordConfirm) throw new DomainException("Err.PasswordMismatch");
                if (!PasswordHasher.MeetsPolicy(AdminPassword ?? "")) throw new DomainException("Err.PasswordPolicy");
                foreach (var u in Users) if (!PasswordHasher.MeetsPolicy(u.Password)) throw new DomainException("Err.PasswordPolicy");
                break;
        }
    }

    private List<string> WarehouseList() => WarehouseNames.Split('\n', ',', ';').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

    [RelayCommand]
    private void Next()
    {
        try
        {
            ErrorMessage = null;
            ValidateStep();
            if (Step < StepCount) Step++;
        }
        catch (Exception ex) { ErrorMessage = Errors.Describe(ex); }
    }

    [RelayCommand] private void Back() { ErrorMessage = null; if (Step > 1) Step--; }
    [RelayCommand] private void AddMachine() => Machines.Add(new SetupMachineRow { Name = "" });
    [RelayCommand] private void RemoveMachine(SetupMachineRow row) => Machines.Remove(row);
    [RelayCommand] private void AddMaterial() => Materials.Add(new SetupMaterialRow { Name = "" });
    [RelayCommand] private void RemoveMaterial(SetupMaterialRow row) => Materials.Remove(row);
    [RelayCommand] private void AddUser() => Users.Add(new SetupUserRow());
    [RelayCommand] private void RemoveUser(SetupUserRow row) => Users.Remove(row);

    [RelayCommand]
    private async Task Finish()
    {
        for (var s = 1; s <= StepCount; s++)
        {
            Step = s;
            try { ValidateStep(); }
            catch (Exception ex) { ErrorMessage = Errors.Describe(ex); return; }
        }
        var input = new SetupInput
        {
            Company = new CompanySettings
            {
                CompanyName = CompanyName.Trim(), Address = Address, Phone = Phone, Email = Email, TaxNumber = TaxNumber, CurrencyCode = CurrencyCode.Trim().ToUpperInvariant(),
                CurrencySymbol = CurrencySymbol, DefaultTaxRate = TaxRate, DecimalPlaces = DecimalPlaces, DefaultMarginPercent = MarginPercent, OverheadRate = OverheadPercent,
                DefaultLaborRate = LaborRate, DefaultElectricityPricePerKwh = ElectricityPrice, Language = Language, Theme = "System"
            },
            FiscalYearStart = FiscalYearStart!.Value.Date,
            Warehouses = WarehouseList(),
            Administrator = new SetupUser(AdminUsername, AdminFullName, UserRole.Administrator, AdminPassword!),
            Users = Users.Select(u => new SetupUser(u.Username, u.FullName, u.Role, u.Password)).ToList(),
            Machines = Machines.Select(m => new SetupMachine(m.Name, m.Type, m.PowerWatts, m.LoadKw, m.PurchaseCost, m.LifeYears, m.ResidualValue, m.AnnualHours, m.MaintenancePerYear)).ToList(),
            Materials = Materials.Select(m => new SetupMaterial(m.Name, m.MaterialType, m.Thickness, m.Length, m.Width, m.UnitCode, m.UnitCost, m.OpeningQuantity)).ToList(),
            OpeningCash = OpeningCash,
            OpeningBank = OpeningBank,
            LoadDemoData = LoadDemoData
        };
        var ok = await RunAsync(async () =>
        {
            var progress = new Progress<string>(p => ProgressText = L.Has("Setup.Progress." + p) ? L["Setup.Progress." + p] : p);
            await Task.Run(() => Get<SetupService>().CompleteAsync(input, progress));
        });
        if (ok) _main.Content = new LoginViewModel(_main) { Username = AdminUsername };
    }
}
