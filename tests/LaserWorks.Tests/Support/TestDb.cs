using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Files;
using LaserWorks.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LaserWorks.Tests.Support;

/// <summary>A real SQLite database in a private temp folder, wired with the production service registrations.</summary>
public sealed class TestDb : IAsyncDisposable
{
    private TestDb(string folder, ServiceProvider sp, MutableClock clock)
    {
        Folder = folder; Services = sp; Clock = clock;
    }

    public string Folder { get; }
    public ServiceProvider Services { get; }
    public MutableClock Clock { get; }
    public AppPaths Paths => (AppPaths)Services.GetRequiredService<IAppPaths>();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public static string NewFolder(string prefix = "lw")
    {
        var folder = Path.Combine(Path.GetTempPath(), "laserworks-tests", prefix + "-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Creates a database; optionally runs the first-run setup (and demo data).</summary>
    public static async Task<TestDb> CreateAsync(bool setup = true, bool demo = false, DateTime? now = null, string? folder = null)
    {
        folder ??= NewFolder();
        var clock = new MutableClock();
        clock.Set(now ?? new DateTime(2026, 3, 1, 9, 0, 0));
        var sp = new ServiceCollection().AddLaserWorks(new AppPaths(folder), clock).AddLaserWorksReporting().BuildServiceProvider();
        await sp.InitializeDatabaseAsync();
        var db = new TestDb(folder, sp, clock);
        if (setup) await sp.GetRequiredService<SetupService>().CompleteAsync(DefaultSetup(demo, clock.Now), null);
        if (setup) await db.LoginAsync("admin", "Admin@2026");
        return db;
    }

    public static SetupInput DefaultSetup(bool demo, DateTime now) => new()
    {
        Company = new CompanySettings
        {
            CompanyName = "LaserWorks Test Workshop", CurrencyCode = "SAR", CurrencySymbol = "SAR", DefaultTaxRate = 15, Language = "en",
            Address = "Riyadh", Phone = "+966 11 000 0000", TaxNumber = "300000000000003"
        },
        FiscalYearStart = new DateTime(now.Year, 1, 1),
        Administrator = new SetupUser("admin", "System Administrator", UserRole.Administrator, "Admin@2026"),
        Users =
        {
            new SetupUser("manager", "Workshop Manager", UserRole.Manager, "Manager@2026"),
            new SetupUser("accountant", "Accountant", UserRole.Accountant, "Account@2026"),
            new SetupUser("sales", "Sales Rep", UserRole.Sales, "Sales@2026"),
            new SetupUser("production", "Production Lead", UserRole.Production, "Prod@2026"),
            new SetupUser("store", "Storekeeper", UserRole.Storekeeper, "Store@2026")
        },
        Machines = demo ? new() : new() { new SetupMachine("CO2 Laser 1390", "CO2 flatbed", 130, 1.8m, 60000, 5, 5000, 2000, 3000) },
        Materials = demo ? new() : new()
        {
            new SetupMaterial("Acrylic Clear 3mm 122x244", "Acrylic", 3, 244, 122, "SHEET", 145, 20),
            new SetupMaterial("Wooden Coaster Set", "Wood", 4, 10, 10, "PCS", 12, 50, MaterialKind.FinishedGood)
        },
        OpeningCash = demo ? 0 : 10000,
        OpeningBank = demo ? 0 : 100000,
        LoadDemoData = demo
    };

    public async Task LoginAsync(string user, string password)
    {
        var (result, _) = await Get<AuthService>().LoginAsync(user, password);
        if (result != LoginResult.Success) throw new InvalidOperationException("login failed: " + result);
        await Get<PermissionService>().LoadAsync();
    }

    /// <summary>Keep the data folder on dispose (to "close" the application and open it again on the same data).</summary>
    public bool KeepFolder { get; set; }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (KeepFolder) return;
        try { Directory.Delete(Folder, true); } catch { /* best effort */ }
    }
}
