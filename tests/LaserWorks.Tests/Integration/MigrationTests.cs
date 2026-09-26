using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Enums;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Files;
using LaserWorks.Reporting;
using LaserWorks.Reporting.Core;
using LaserWorks.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Spec §31: the 1.0.0-rc1 demo database (single material per request, no job component lines) is upgraded by the
/// MultiComponentJobs migration. Nothing may be lost or re-priced; everything must be linked to component lines.
/// </summary>
public class MigrationTests
{
    private readonly ITestOutputHelper _out;
    public MigrationTests(ITestOutputHelper output) => _out = output;

    private static long Scalar(SqliteConnection c, string sql) => Convert.ToInt64(new SqliteCommand(sql, c).ExecuteScalar() ?? 0);
    private static List<(long, decimal)> Pairs(SqliteConnection c, string sql)
    {
        var list = new List<(long, decimal)>();
        using var r = new SqliteCommand(sql, c).ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.IsDBNull(1) ? 0 : Convert.ToDecimal(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture)));
        return list;
    }

    [Fact]
    public async Task Rc1_database_upgrades_without_losing_or_repricing_data()
    {
        var folder = TestDb.NewFolder("migrate");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rc1-demo.db"), Path.Combine(folder, "laserworks.db"));

        // ---- before
        List<(long Id, decimal MaterialId)> requestMaterials;
        List<(long, decimal)> jobActual;
        long journalLines, stockTx, costEntries, requestsWithMaterial;
        await using (var c = new SqliteConnection($"Data Source={Path.Combine(folder, "laserworks.db")}"))
        {
            c.Open();
            Assert.Equal(0, Scalar(c, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%MultiComponentJobs'"));
            requestsWithMaterial = Scalar(c, "SELECT COUNT(*) FROM CustomerRequests WHERE MaterialId IS NOT NULL");
            requestMaterials = new List<(long, decimal)>();
            using (var r = new SqliteCommand("SELECT Id, MaterialId FROM CustomerRequests WHERE MaterialId IS NOT NULL", c).ExecuteReader())
                while (r.Read()) requestMaterials.Add((r.GetInt64(0), r.GetInt64(1)));
            jobActual = Pairs(c, "SELECT Id, ActualCost FROM Jobs ORDER BY Id");
            journalLines = Scalar(c, "SELECT COUNT(*) FROM JournalLines");
            stockTx = Scalar(c, "SELECT COUNT(*) FROM InventoryTransactions");
            costEntries = Scalar(c, "SELECT COUNT(*) FROM JobCostEntries");
        }
        SqliteConnection.ClearAllPools();
        Assert.True(requestsWithMaterial > 10 && jobActual.Count > 10, "fixture should contain real demo data");

        // ---- upgrade with the production start-up path
        await using var sp = new ServiceCollection().AddLaserWorks(new AppPaths(folder)).AddLaserWorksReporting().BuildServiceProvider();
        await sp.InitializeDatabaseAsync();
        var (result, _) = await sp.GetRequiredService<AuthService>().LoginAsync("admin", "Admin@2026");
        Assert.Equal(LoginResult.Success, result);
        await sp.GetRequiredService<PermissionService>().LoadAsync();

        await using var db = sp.GetRequiredService<IAppDbFactory>().Create();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("MultiComponentJobs"));
        // nothing lost, nothing re-priced
        Assert.Equal(journalLines, await db.JournalLines.CountAsync());
        Assert.Equal(stockTx, await db.InventoryTransactions.CountAsync());
        Assert.Equal(costEntries, await db.JobCostEntries.CountAsync());
        foreach (var (id, actual) in jobActual)
        {
            var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id);
            Assert.Equal(actual, job.ActualCost);
            Assert.Equal(actual, (await db.JobCostEntries.Where(e => e.JobId == id).Select(e => e.Amount).ToListAsync()).Sum());
        }
        // each request's single material became its first requested item
        foreach (var (reqId, materialId) in requestMaterials)
        {
            var items = await db.RequestItems.AsNoTracking().Where(i => i.RequestId == reqId).ToListAsync();
            var item = Assert.Single(items);
            Assert.Equal((long)materialId, item.MaterialId);
            Assert.Equal(1m, item.Quantity);
        }
        // every job from an estimate has its estimate lines as components; every movement / item cost is linked
        foreach (var job in await db.Jobs.AsNoTracking().Where(j => j.EstimateId != null).ToListAsync())
        {
            var estLines = await db.EstimateMaterialLines.CountAsync(l => l.EstimateId == job.EstimateId);
            Assert.True(await db.JobComponents.CountAsync(c => c.JobId == job.Id && c.EstimateLineId != null) == estLines, job.Number);
        }
        // every stock movement of a job (issue, return, remnant saved from or used on the job) belongs to a component line
        Assert.Equal(0, await db.InventoryTransactions.CountAsync(t => t.JobId != null && t.MaterialId != null && t.JobComponentId == null));
        Assert.Equal(0, await db.JobCostEntries.CountAsync(e => e.MaterialId != null && e.Component == CostComponent.Material && e.JobComponentId == null));
        Assert.Equal(0, await db.EstimateMaterialLines.CountAsync(l => l.Category == 0 || l.Source == 0));

        // old jobs still open, and component rows add up to the job's estimated and actual totals
        var costing = sp.GetRequiredService<JobCostingService>();
        foreach (var (id, actual) in jobActual)
        {
            var sheet = await costing.CostSheetAsync(id);
            Assert.Equal(actual, sheet.Components.Sum(c => c.Actual));
            Assert.Equal(sheet.Variance.EstimatedTotal, sheet.Components.Sum(c => c.Estimated));
        }
        // books still reconcile and reports still run
        foreach (var check in await sp.GetRequiredService<ReconciliationService>().RunAsync())
            Assert.True(check.Passed, $"{check.Name}: {check.Expected} vs {check.Actual}");
        var catalog = sp.GetRequiredService<ReportCatalog>();
        var range = new LaserWorks.Application.Common.DateRange(DateTime.Today.AddYears(-2), DateTime.Today.AddYears(1));
        foreach (var r in catalog.Reports.Where(r => r.Required == ReportFilterKind.None))
            await catalog.RunAsync(r.Key, new ReportFilter(range, DateTime.Today));
        _out.WriteLine($"{jobActual.Count} jobs, {requestMaterials.Count} requests migrated; {await db.JobComponents.CountAsync()} component lines created");
        await db.DisposeAsync();
        await sp.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
