using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

public sealed record SetupUser(string Username, string FullName, UserRole Role, string Password);
public sealed record SetupMachine(string Name, string? Type, decimal PowerWatts, decimal LoadKw, decimal PurchaseCost, decimal LifeYears, decimal ResidualValue, decimal AnnualHours, decimal MaintenancePerYear);
public sealed record SetupMaterial(string Name, string? MaterialType, decimal Thickness, decimal Length, decimal Width, string UnitCode, decimal UnitCost, decimal OpeningQuantity, MaterialKind Kind = MaterialKind.RawMaterial);

public sealed class SetupInput
{
    public CompanySettings Company { get; set; } = new();
    public DateTime FiscalYearStart { get; set; } = new(DateTime.Today.Year, 1, 1);
    public List<string> Warehouses { get; set; } = new() { "Main Warehouse" };
    public SetupUser Administrator { get; set; } = new("admin", "Administrator", UserRole.Administrator, "");
    public List<SetupUser> Users { get; set; } = new();
    public List<SetupMachine> Machines { get; set; } = new();
    public List<SetupMaterial> Materials { get; set; } = new();
    public decimal OpeningCash { get; set; }
    public decimal OpeningBank { get; set; }
    public bool LoadDemoData { get; set; }
}

/// <summary>First-run setup: company, currency, tax, fiscal year, warehouses, machines, materials, users, opening balances, optional demo data.</summary>
public sealed class SetupService : ServiceBase
{
    private readonly AuthService _auth;
    private readonly MachineService _machines;
    private readonly MaterialService _materials;
    private readonly InventoryService _inventory;
    private readonly AccountingService _accounting;
    private readonly DemoDataService _demo;

    public SetupService(ServiceContext ctx, AuthService auth, MachineService machines, MaterialService materials, InventoryService inventory, AccountingService accounting, DemoDataService demo) : base(ctx)
    {
        _auth = auth; _machines = machines; _materials = materials; _inventory = inventory; _accounting = accounting; _demo = demo;
    }

    public async Task<bool> IsSetupCompletedAsync()
    {
        Ctx.Settings.Invalidate();
        var s = await Ctx.Settings.GetAsync();
        return s.SetupCompleted && await _auth.AnyUserAsync();
    }

    public async Task CompleteAsync(SetupInput input, IProgress<string>? progress = null)
    {
        SettingsService.Validate(input.Company);
        if (!PasswordHasher.MeetsPolicy(input.Administrator.Password)) throw new DomainException("Err.PasswordPolicy");
        if (input.Warehouses.Count == 0 || input.Warehouses.Any(string.IsNullOrWhiteSpace)) throw new DomainException("Err.Required", "Warehouse");
        if (await _auth.AnyUserAsync()) throw new DomainException("Err.SetupAlreadyDone");

        progress?.Report("Company");
        var current = await Ctx.Settings.GetAsync();
        var s = input.Company;
        s.SetupCompleted = false;
        s.DefaultWarehouseId = current.DefaultWarehouseId;
        await Ctx.Settings.SaveAsync(s);

        progress?.Report("FiscalYear");
        await using (var db = Factory.Create())
        {
            if (!await db.FiscalYears.AnyAsync()) { await AccountingEngine.CreateFiscalYearAsync(db, input.FiscalYearStart); await db.SaveChangesAsync(); }
            // Align the first fiscal year to the requested start
            var fy = await db.FiscalYears.Include(y => y.Periods).OrderBy(y => y.StartDate).FirstAsync();
            if (fy.StartDate != input.FiscalYearStart.Date && !await db.JournalEntries.AnyAsync())
            {
                db.FiscalYears.Remove(fy);
                await db.SaveChangesAsync();
                var start = new DateTime(input.FiscalYearStart.Year, input.FiscalYearStart.Month, 1);
                await AccountingEngine.CreateFiscalYearAsync(db, start);
                await db.SaveChangesAsync();
            }

            progress?.Report("Warehouses");
            var warehouses = await db.Warehouses.OrderBy(w => w.Id).ToListAsync();
            for (int i = 0; i < input.Warehouses.Count; i++)
            {
                var name = input.Warehouses[i].Trim();
                if (i < warehouses.Count) warehouses[i].Name = name;
                else db.Warehouses.Add(new Warehouse { Code = $"WH{i + 1}", Name = name });
            }
            await db.SaveChangesAsync();
        }

        progress?.Report("Users");
        await _auth.CreateUserAsync(input.Administrator.Username, input.Administrator.FullName, UserRole.Administrator, input.Administrator.Password);
        foreach (var u in input.Users) await _auth.CreateUserAsync(u.Username, u.FullName, u.Role, u.Password, mustChange: true);

        var s2 = await Ctx.Settings.GetAsync();
        var warehouse = s2.DefaultWarehouseId!.Value;
        var openingDate = input.FiscalYearStart.Date > Now.Date ? input.FiscalYearStart.Date : Now.Date;

        progress?.Report("Machines");
        foreach (var m in input.Machines)
            await _machines.SaveAsync(new Machine
            {
                Name = m.Name, MachineType = m.Type, LaserPowerWatts = m.PowerWatts, ElectricalLoadKw = m.LoadKw, PurchaseCost = m.PurchaseCost, UsefulLifeYears = m.LifeYears,
                ResidualValue = m.ResidualValue, AnnualWorkingHours = m.AnnualHours, MaintenanceCostPerYear = m.MaintenancePerYear, ElectricityPricePerKwh = s.DefaultElectricityPricePerKwh
            });

        progress?.Report("Materials");
        var units = await ReadAsync(db => db.Units.AsNoTracking().ToListAsync());
        foreach (var m in input.Materials)
        {
            var unit = units.FirstOrDefault(u => u.Code == m.UnitCode) ?? units.First();
            var id = await _materials.SaveAsync(new Material { Name = m.Name, MaterialType = m.MaterialType, Thickness = m.Thickness, Length = m.Length, Width = m.Width, UnitId = unit.Id, PurchaseCost = m.UnitCost, Kind = m.Kind });
            if (m.OpeningQuantity > 0) await _inventory.OpeningBalanceAsync(id, warehouse, m.OpeningQuantity, m.UnitCost, openingDate);
        }

        progress?.Report("OpeningBalances");
        var accounts = await _accounting.PostableAccountsAsync();
        if (input.OpeningCash != 0) await _accounting.PostAccountOpeningBalanceAsync(accounts.First(a => a.SystemKey == SystemAccounts.Cash).Id, input.OpeningCash, openingDate);
        if (input.OpeningBank != 0) await _accounting.PostAccountOpeningBalanceAsync(accounts.First(a => a.SystemKey == SystemAccounts.Bank).Id, input.OpeningBank, openingDate);

        if (input.LoadDemoData)
        {
            progress?.Report("DemoData");
            await _demo.LoadAsync(progress);
        }

        var final = await Ctx.Settings.GetAsync();
        final.SetupCompleted = true;
        await Ctx.Settings.SaveAsync(final);
        Ctx.Settings.Invalidate();
    }
}
