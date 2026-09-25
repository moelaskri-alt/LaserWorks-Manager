using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;

// LaserWorks tools:
//   demo <folder>   create a fully set-up demo database (admin / Admin@2026) in <folder>
if (args.Length >= 2 && args[0] == "demo")
{
    var folder = Path.GetFullPath(args[1]);
    if (Directory.Exists(folder)) Directory.Delete(folder, true);
    var paths = new AppPaths(folder);
    var sp = new ServiceCollection().AddLaserWorks(paths).BuildServiceProvider();
    await sp.InitializeDatabaseAsync();
    var setup = sp.GetRequiredService<SetupService>();
    var input = new SetupInput
    {
        Company = new CompanySettings { CompanyName = "ورشة ليزر ووركس التجريبية — LaserWorks Demo Workshop", Address = "Riyadh, Saudi Arabia", Phone = "+966 11 000 0000", Email = "info@laserworks.demo", TaxNumber = "300000000000003", CurrencyCode = "SAR", CurrencySymbol = "ر.س", DefaultTaxRate = 15, Language = "ar" },
        FiscalYearStart = new DateTime(DateTime.Today.AddDays(-120).Year, 1, 1),
        Administrator = new SetupUser("admin", "System Administrator", UserRole.Administrator, "Admin@2026"),
        Users = { new SetupUser("manager", "Workshop Manager", UserRole.Manager, "Manager@2026"), new SetupUser("accountant", "Accountant", UserRole.Accountant, "Account@2026"),
                  new SetupUser("sales", "Sales Rep", UserRole.Sales, "Sales@2026"), new SetupUser("production", "Production Lead", UserRole.Production, "Prod@2026"), new SetupUser("store", "Storekeeper", UserRole.Storekeeper, "Store@2026") },
        LoadDemoData = true
    };
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await setup.CompleteAsync(input, new Progress<string>(Console.WriteLine));
    Console.WriteLine($"Demo created in {sw.Elapsed.TotalSeconds:0.0}s at {paths.DatabasePath}");
    var checks = await sp.GetRequiredService<ReconciliationService>().RunAsync();
    foreach (var c in checks) Console.WriteLine($"{(c.Passed ? "PASS" : "FAIL")}  {c.Name}  expected={c.Expected} actual={c.Actual}");
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    return checks.All(c => c.Passed) ? 0 : 2;
}
Console.WriteLine("usage: LaserWorks.Tools demo <folder>");
return 1;
