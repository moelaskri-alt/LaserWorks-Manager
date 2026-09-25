using System.Diagnostics;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Spec §40: 3,000 customers, 10,000 jobs, 10,000 invoices, 20,000 inventory transactions, 50,000 journal lines.
/// Volume is bulk-inserted (the posting services would take far longer); every entry is balanced and posted.
/// The test then times the screens and reports a user actually opens.
/// </summary>
[Trait("Category", "Performance")]
public class PerformanceTests
{
    private readonly ITestOutputHelper _out;
    public PerformanceTests(ITestOutputHelper output) => _out = output;

    public const int Customers = 3000, Jobs = 10000, Invoices = 10000, InventoryTx = 20000, JournalEntries = 25000; // 2 lines each = 50,000 lines

    [Fact]
    public async Task Large_database_stays_responsive()
    {
        await using var t = await TestDb.CreateAsync(now: DateTime.Today.AddHours(9));
        var seed = Stopwatch.StartNew();
        await SeedAsync(t);
        _out.WriteLine($"seeded in {seed.Elapsed.TotalSeconds:0.0}s");

        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            Assert.True(await db.Customers.CountAsync() >= Customers);
            Assert.True(await db.Jobs.CountAsync() >= Jobs);
            Assert.True(await db.SalesInvoices.CountAsync() >= Invoices);
            Assert.True(await db.InventoryTransactions.CountAsync() >= InventoryTx);
            Assert.True(await db.JournalLines.CountAsync() >= JournalEntries * 2);
        }

        var today = DateTime.Today;
        var year = new DateRange(today.AddYears(-1), today);
        var page = new PageRequest(PageSize: 50);
        var timings = new List<(string Name, double Ms)>();
        async Task Time(string name, Func<Task> action)
        {
            await action(); // warm-up (first query compiles the EF model/query)
            var sw = Stopwatch.StartNew();
            await action();
            timings.Add((name, sw.Elapsed.TotalMilliseconds));
        }

        await Time("Customers page (balance per customer)", () => t.Get<CustomerService>().ListAsync(page));
        await Time("Customers search", () => t.Get<CustomerService>().ListAsync(page with { Search = "Customer 29" }));
        await Time("Jobs page sorted by customer", () => t.Get<JobService>().ListAsync(page with { SortBy = "Customer.Name", Descending = true }));
        await Time("Jobs last page", () => t.Get<JobService>().ListAsync(page with { Page = Jobs / 50 }));
        await Time("Invoices page, open only", () => t.Get<SalesService>().ListInvoicesAsync(page, openOnly: true));
        await Time("Inventory movements page", () => t.Get<InventoryService>().ListTransactionsAsync(page));
        await Time("Journal entries page", () => t.Get<AccountingService>().ListEntriesAsync(page));
        await Time("Trial balance", () => t.Get<AccountingService>().TrialBalanceAsync(year));
        await Time("Dashboard", () => t.Get<DashboardService>().LoadAsync(year));
        await Time("AR aging report", () => t.Get<ReportCatalog>().RunAsync("AccountsReceivable", new ReportFilter(year, today)));
        await Time("Sales register report (1 year)", () => t.Get<ReportCatalog>().RunAsync("SalesRegister", new ReportFilter(year, today)));

        foreach (var (name, ms) in timings) _out.WriteLine($"{ms,8:0} ms  {name}");
        var slow = timings.Where(x => x.Ms > 3000).ToList();
        Assert.True(slow.Count == 0, "slow: " + string.Join(", ", slow.Select(s => $"{s.Name} {s.Ms:0}ms")));
    }

    private static async Task SeedAsync(TestDb t)
    {
        var rnd = new Random(7);
        var today = DateTime.Today;
        long wh, materialId, arId, salesId, cashId;
        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            wh = await db.Warehouses.Select(w => w.Id).FirstAsync();
            materialId = await db.Materials.Where(m => m.Kind == MaterialKind.RawMaterial).Select(m => m.Id).FirstAsync();
            arId = await db.Accounts.Where(a => a.SystemKey == SystemAccounts.AR).Select(a => a.Id).FirstAsync();
            salesId = await db.Accounts.Where(a => a.SystemKey == SystemAccounts.Sales).Select(a => a.Id).FirstAsync();
            cashId = await db.Accounts.Where(a => a.SystemKey == SystemAccounts.Cash).Select(a => a.Id).FirstAsync();
        }

        async Task Batch<T>(int count, int size, Func<int, T> make) where T : class
        {
            for (var start = 0; start < count; start += size)
            {
                await using var db = (DbContext)t.Get<IAppDbFactory>().Create();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Set<T>().AddRange(Enumerable.Range(start, Math.Min(size, count - start)).Select(make));
                await db.SaveChangesAsync();
            }
        }

        await Batch(Customers, 1000, i => new Customer { Code = $"PC-{i:00000}", Name = $"Customer {i}", Phone = $"05{i:00000000}", PaymentTermsDays = 30, CreditLimit = 50000 });
        List<long> customers;
        await using (var db = t.Get<IAppDbFactory>().Create()) customers = await db.Customers.Select(c => c.Id).ToListAsync();

        var statuses = new[] { JobStatus.Closed, JobStatus.Invoiced, JobStatus.Delivered, JobStatus.InProduction, JobStatus.Planned };
        await Batch(Jobs, 2000, i => new Job
        {
            Number = $"PJ-{i:000000}", CustomerId = customers[i % customers.Count], Title = $"Laser job {i}", Quantity = 1 + i % 50,
            OrderDate = today.AddDays(-(i % 360)), DueDate = today.AddDays(-(i % 360) + 7), Status = statuses[i % statuses.Length],
            EstimatedCost = 100 + i % 400, ActualCost = 110 + i % 420, SellingPrice = 180 + i % 600, InvoicedRevenue = i % 5 < 2 ? 180 + i % 600 : 0
        });
        List<long> jobs;
        await using (var db = t.Get<IAppDbFactory>().Create()) jobs = await db.Jobs.Select(j => j.Id).ToListAsync();

        await Batch(Invoices, 2000, i =>
        {
            var net = 100m + i % 900;
            var inv = new SalesInvoice
            {
                Number = $"PI-{i:000000}", CustomerId = customers[i % customers.Count], JobId = jobs[i % jobs.Count], Date = today.AddDays(-(i % 360)), DueDate = today.AddDays(-(i % 360) + 30),
                Status = DocumentStatus.Posted, Subtotal = net, TaxAmount = net * 0.15m, Total = net * 1.15m, PaidAmount = i % 3 == 0 ? 0 : net * 1.15m, PostedAt = today
            };
            inv.Lines.Add(new SalesInvoiceLine { LineType = InvoiceLineType.Service, Description = "Laser cutting service", Quantity = 1, UnitPrice = net, NetAmount = net, TaxRate = 15, TaxAmount = net * 0.15m, LineTotal = net * 1.15m });
            return inv;
        });

        await Batch(InventoryTx, 4000, i => new InventoryTransaction
        {
            Number = $"PT-{i:000000}", Date = today.AddDays(-(i % 360)), Type = i % 2 == 0 ? InventoryTxType.PurchaseReceipt : InventoryTxType.MaterialIssue,
            MaterialId = materialId, WarehouseId = wh, Quantity = i % 2 == 0 ? 5 : -5, UnitCost = 145, TotalCost = i % 2 == 0 ? 725 : -725,
            QuantityAfter = 20, AverageCostAfter = 145, JobId = i % 2 == 0 ? null : jobs[i % jobs.Count], CreatedAt = today, CreatedBy = "perf"
        });

        await Batch(JournalEntries, 2500, i =>
        {
            var amount = 50m + i % 700;
            var sale = i % 2 == 0;
            var e = new JournalEntry
            {
                Number = $"PJE-{i:000000}", Date = today.AddDays(-(i % 360)), Description = sale ? "Synthetic sale" : "Synthetic receipt", SourceType = "Perf",
                Status = JournalStatus.Posted, CreatedAt = today, PostedAt = today, PostedBy = "perf"
            };
            var customer = customers[i % customers.Count];
            e.Lines.Add(new JournalLine { AccountId = sale ? arId : cashId, Debit = amount, CustomerId = sale ? customer : null });
            e.Lines.Add(new JournalLine { AccountId = sale ? salesId : arId, Credit = amount, CustomerId = sale ? null : customer });
            return e;
        });
    }
}
