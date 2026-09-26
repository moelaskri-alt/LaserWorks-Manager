using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Files;
using LaserWorks.Reporting;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using Microsoft.Extensions.DependencyInjection;

// LaserWorks command-line tools (used by the release pipeline and QA):
//   demo <folder>                          create a fully set-up demo database (admin / Admin@2026)
//   reports <dataFolder> <outFolder> [ar|en]  run all reports and export PDF / Excel / CSV + sample documents
return args.FirstOrDefault() switch
{
    "demo" when args.Length >= 2 => await Tools.DemoAsync(args[1]),
    "reports" when args.Length >= 3 => await Tools.ReportsAsync(args[1], args[2], args.Length > 3 ? args[3] : "ar"),
    _ => Tools.Usage()
};

internal static class Tools
{
    public static int Usage()
    {
        Console.WriteLine("usage: LaserWorks.Tools demo <folder> | reports <dataFolder> <outFolder> [ar|en]");
        return 1;
    }

    public static async Task<int> DemoAsync(string folderArg)
    {
        var folder = Path.GetFullPath(folderArg);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        var paths = new AppPaths(folder);
        var sp = new ServiceCollection().AddLaserWorks(paths).BuildServiceProvider();
        await sp.InitializeDatabaseAsync();
        var setup = sp.GetRequiredService<SetupService>();
        var input = new SetupInput
        {
            Company = new CompanySettings
            {
                CompanyName = "ورشة ليزر ووركس التجريبية — LaserWorks Demo Workshop", Address = "Riyadh, Saudi Arabia", Phone = "+966 11 000 0000", Email = "info@laserworks.demo",
                TaxNumber = "300000000000003", CurrencyCode = "SAR", CurrencySymbol = "ر.س", DefaultTaxRate = 15, Language = "ar"
            },
            FiscalYearStart = new DateTime(DateTime.Today.AddDays(-120).Year, 1, 1),
            Administrator = new SetupUser("admin", "System Administrator", UserRole.Administrator, "Admin@2026"),
            Users =
            {
                new SetupUser("manager", "Workshop Manager", UserRole.Manager, "Manager@2026"), new SetupUser("accountant", "Accountant", UserRole.Accountant, "Account@2026"),
                new SetupUser("sales", "Sales Rep", UserRole.Sales, "Sales@2026"), new SetupUser("production", "Production Lead", UserRole.Production, "Prod@2026"),
                new SetupUser("store", "Storekeeper", UserRole.Storekeeper, "Store@2026")
            },
            LoadDemoData = true
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await setup.CompleteAsync(input, new Progress<string>(Console.WriteLine));
        Console.WriteLine($"Demo created in {sw.Elapsed.TotalSeconds:0.0}s at {paths.DatabasePath}");
        var checks = await sp.GetRequiredService<ReconciliationService>().RunAsync();
        foreach (var c in checks) Console.WriteLine($"{(c.Passed ? "PASS" : "FAIL")}  {c.Name}  expected={c.Expected} actual={c.Actual}");
        // a restorable backup of the demo company (Backup & Restore → Restore from file…)
        var backupFile = Path.Combine(folder, "LaserWorksDemo" + LaserWorks.Infrastructure.Backup.BackupService.Extension);
        var backup = await sp.GetRequiredService<LaserWorks.Infrastructure.Backup.BackupService>().CreateBackupAsync(backupFile, "LaserWorks Manager demo company", "Demo");
        Console.WriteLine($"Demo backup: {backup.FilePath}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return checks.All(c => c.Passed) ? 0 : 2;
    }

    public static async Task<int> ReportsAsync(string dataFolder, string outFolder, string lang)
    {
        var paths = new AppPaths(Path.GetFullPath(dataFolder));
        var sp = new ServiceCollection().AddLaserWorks(paths).AddLaserWorksReporting().BuildServiceProvider();
        await sp.InitializeDatabaseAsync();
        LaserWorks.Localization.Loc.Instance.SetLanguage(lang);
        var outDir = Directory.CreateDirectory(outFolder).FullName;
        var catalog = sp.GetRequiredService<ReportCatalog>();
        var company = await sp.GetRequiredService<SettingsService>().GetAsync();
        var today = DateTime.Today;
        var jobs = await sp.GetRequiredService<JobService>().LookupAsync(openOnly: false);
        var customers = await sp.GetRequiredService<CustomerService>().LookupAsync();
        var ar = (await sp.GetRequiredService<AccountingService>().PostableAccountsAsync()).First(a => a.SystemKey == SystemAccounts.AR);
        var failures = 0;
        foreach (var def in catalog.Reports)
        {
            try
            {
                var f = new ReportFilter(new DateRange(today.AddDays(-365), today), today,
                    CustomerId: def.Required.HasFlag(ReportFilterKind.Customer) ? customers[0].Id : null,
                    JobId: def.Required.HasFlag(ReportFilterKind.Job) ? jobs.Last().Id : null,
                    AccountId: def.Required.HasFlag(ReportFilterKind.Account) ? ar.Id : null);
                var t = await catalog.RunAsync(def.Key, f);
                PdfExporter.Export(t, company, Path.Combine(outDir, def.Key + ".pdf"));
                ExcelExporter.Export(t, company, Path.Combine(outDir, def.Key + ".xlsx"));
                CsvExporter.Export(t, Path.Combine(outDir, def.Key + ".csv"));
                Console.WriteLine($"OK   {def.Key,-24} rows={t.Rows.Count}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {def.Key}: {ex.GetType().Name} {ex.Message}");
                Console.WriteLine(ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LaserWorks")));
            }
        }
        var sheet = await sp.GetRequiredService<JobCostingService>().CostSheetAsync(jobs.Last().Id);
        File.WriteAllBytes(Path.Combine(outDir, "doc_jobcostsheet.pdf"), DocumentRenderer.JobCostSheet(sheet, company));
        var quotes = sp.GetRequiredService<QuotationService>();
        var q = await quotes.GetAsync((await quotes.ListAsync(new PageRequest(PageSize: 5))).Items[0].Id);
        File.WriteAllBytes(Path.Combine(outDir, "doc_quotation.pdf"), DocumentRenderer.Quotation(q!, company));
        var sales = sp.GetRequiredService<SalesService>();
        var inv = await sales.GetInvoiceAsync((await sales.ListInvoicesAsync(new PageRequest(PageSize: 5))).Items[0].Id);
        File.WriteAllBytes(Path.Combine(outDir, "doc_invoice.pdf"), DocumentRenderer.Invoice(inv!, company));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Console.WriteLine(failures == 0 ? "All reports generated." : $"{failures} report(s) failed.");
        return failures == 0 ? 0 : 3;
    }
}
