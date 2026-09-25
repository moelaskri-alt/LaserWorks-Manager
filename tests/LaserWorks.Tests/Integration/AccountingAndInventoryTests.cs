using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Tests.Integration;

public class AccountingIntegrityTests
{
    private static async Task<(long Cash, long Rent)> Accounts(TestDb t)
    {
        var acc = await t.Get<AccountingService>().PostableAccountsAsync();
        return (acc.Single(a => a.SystemKey == SystemAccounts.Cash).Id, acc.Single(a => a.SystemKey == SystemAccounts.Rent).Id);
    }

    [Fact]
    public async Task Manual_entry_must_balance_and_posts_both_sides()
    {
        await using var t = await TestDb.CreateAsync();
        var svc = t.Get<AccountingService>();
        var (cash, rent) = await Accounts(t);
        var day = t.Clock.Now.Date;
        var bad = await svc.SaveManualDraftAsync(0, day, "unbalanced", new[] { new ManualJournalLine(rent, 100, 0, null), new ManualJournalLine(cash, 0, 90, null) });
        Assert.Equal("Err.UnbalancedEntry", (await Assert.ThrowsAsync<DomainException>(() => svc.PostManualAsync(bad))).Code);

        var id = await svc.SaveManualDraftAsync(0, day, "rent", new[] { new ManualJournalLine(rent, 500, 0, null), new ManualJournalLine(cash, 0, 500, null) });
        var cashBefore = await Ledger.BalanceAsync(t, SystemAccounts.Cash);
        await svc.PostManualAsync(id);
        Assert.Equal(cashBefore - 500, await Ledger.BalanceAsync(t, SystemAccounts.Cash));
        Assert.Equal(500m, await Ledger.BalanceAsync(t, SystemAccounts.Rent));
        await Ledger.AssertBooksBalanceAsync(t);
    }

    [Fact]
    public async Task Posted_entries_are_immutable_and_corrected_by_reversal()
    {
        await using var t = await TestDb.CreateAsync();
        var svc = t.Get<AccountingService>();
        var (cash, rent) = await Accounts(t);
        var day = t.Clock.Now.Date;
        var id = await svc.SaveManualDraftAsync(0, day, "rent", new[] { new ManualJournalLine(rent, 300, 0, null), new ManualJournalLine(cash, 0, 300, null) });
        await svc.PostManualAsync(id);

        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            var line = await db.JournalLines.FirstAsync(l => l.JournalEntryId == id);
            line.Debit = 1;
            await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync());
        }
        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            var e = await db.JournalEntries.FirstAsync(x => x.Id == id);
            db.JournalEntries.Remove(e);
            await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync());
        }

        var reversal = await svc.ReverseAsync(id, day, "wrong account");
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.Rent));
        var original = (await svc.GetEntryAsync(id))!;
        Assert.Equal(JournalStatus.Reversed, original.Status);
        Assert.Equal(reversal, original.ReversedById);
        Assert.Equal("Err.OnlyPostedCanBeReversed", (await Assert.ThrowsAsync<DomainException>(() => svc.ReverseAsync(id, day, "again"))).Code);
        await Ledger.AssertBooksBalanceAsync(t);
    }

    [Fact]
    public async Task Closed_period_rejects_postings()
    {
        await using var t = await TestDb.CreateAsync();
        var svc = t.Get<AccountingService>();
        var (cash, rent) = await Accounts(t);
        var day = t.Clock.Now.Date;
        var period = (await svc.FiscalYearsAsync()).SelectMany(y => y.Periods).Single(p => p.StartDate <= day && p.EndDate >= day);
        await svc.SetPeriodClosedAsync(period.Id, true);
        var id = await svc.SaveManualDraftAsync(0, day, "rent", new[] { new ManualJournalLine(rent, 10, 0, null), new ManualJournalLine(cash, 0, 10, null) });
        Assert.Equal("Err.PeriodClosed", (await Assert.ThrowsAsync<DomainException>(() => svc.PostManualAsync(id))).Code);
        await svc.SetPeriodClosedAsync(period.Id, false);
        await svc.PostManualAsync(id);
    }

    [Fact]
    public async Task Inventory_transactions_and_audit_log_cannot_be_changed()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Get<IAppDbFactory>().Create();
        var tx = await db.InventoryTransactions.FirstAsync();
        tx.Quantity += 1;
        Assert.Equal("Err.ImmutableRecord", (await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync())).Code);
        db.ChangeTracker.Clear();
        var log = await db.AuditLogs.FirstAsync();
        db.AuditLogs.Remove(log);
        Assert.Equal("Err.ImmutableRecord", (await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync())).Code);
    }

    [Fact]
    public async Task Demo_company_books_reconcile_and_trial_balance_balances()
    {
        await using var t = await TestDb.CreateAsync(demo: true, now: DateTime.Today.AddHours(9));
        await Ledger.AssertBooksBalanceAsync(t);
        var tb = await t.Get<AccountingService>().TrialBalanceAsync(new DateRange(new DateTime(2000, 1, 1), DateTime.Today));
        Assert.Equal(tb.Sum(r => r.ClosingDebit), tb.Sum(r => r.ClosingCredit));
        Assert.Equal(tb.Sum(r => r.PeriodDebit), tb.Sum(r => r.PeriodCredit));
    }
}

public class InventoryCostingTests
{
    [Fact]
    public async Task Receipts_update_moving_average_and_issues_charge_the_job_at_average()
    {
        await using var t = await TestDb.CreateAsync();
        var inv = t.Get<InventoryService>();
        var mat = (await t.Get<MaterialService>().LookupAsync()).First(m => m.Name.StartsWith("Acrylic"));
        var wh = (await t.Get<SettingsService>().GetAsync()).DefaultWarehouseId!.Value;
        var day = t.Clock.Now.Date;
        await inv.AdjustAsync(mat.Id, wh, 10, 175, day, "stock count gain at new price"); // 20 @145 + 10 @175
        var m = (await t.Get<MaterialService>().GetAsync(mat.Id))!;
        Assert.Equal(30m, m.QuantityOnHand);
        Assert.Equal(4650m, m.StockValue);
        Assert.Equal(155m, m.AverageCost);

        var customer = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Direct customer" });
        var jobId = await t.Get<JobService>().SaveAsync(new Job { CustomerId = customer, Title = "Direct job", Quantity = 1, OrderDate = day, DueDate = day.AddDays(3), SellingPrice = 1000 });
        await inv.IssueToJobAsync(jobId, mat.Id, wh, 2, day);
        Assert.Equal(310m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        await inv.ReturnFromJobAsync(jobId, mat.Id, wh, 1, day);
        Assert.Equal(155m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        Assert.Equal("Err.ReturnExceedsIssued", (await Assert.ThrowsAsync<DomainException>(() => inv.ReturnFromJobAsync(jobId, mat.Id, wh, 5, day))).Code);
        Assert.Equal("Err.InsufficientStockWarehouse", (await Assert.ThrowsAsync<DomainException>(() => inv.IssueToJobAsync(jobId, mat.Id, wh, 500, day))).Code);

        m = (await t.Get<MaterialService>().GetAsync(mat.Id))!;
        Assert.Equal(29m, m.QuantityOnHand);
        Assert.Equal(m.StockValue, await Ledger.BalanceAsync(t, SystemAccounts.Inventory));
        await Ledger.AssertBooksBalanceAsync(t);
    }

    [Fact]
    public async Task Remnant_takes_area_proportional_value_out_of_the_job_and_can_be_reused()
    {
        await using var t = await TestDb.CreateAsync();
        var inv = t.Get<InventoryService>();
        var mat = (await t.Get<MaterialService>().LookupAsync()).First(m => m.Name.StartsWith("Acrylic"));
        var wh = (await t.Get<SettingsService>().GetAsync()).DefaultWarehouseId!.Value;
        var day = t.Clock.Now.Date;
        var customer = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Remnant customer" });
        var job = await t.Get<JobService>().SaveAsync(new Job { CustomerId = customer, Title = "Job A", Quantity = 1, OrderDate = day, DueDate = day.AddDays(3), SellingPrice = 500 });
        await inv.IssueToJobAsync(job, mat.Id, wh, 1, day); // one 244×122 sheet at 145
        var remnantId = await inv.CreateRemnantAsync(new RemnantInput(mat.Id, wh, 122, 61, null, job, null, day, "offcut"));
        var expected = MaterialUtilizationCalculator_RemnantCost(244 * 122, 145, 122, 61); // a quarter of the sheet
        Assert.Equal(36.25m, expected);
        Assert.Equal(145m - expected, await Ledger.BalanceAsync(t, SystemAccounts.WIP, job));
        Assert.Equal(expected, await Ledger.BalanceAsync(t, SystemAccounts.InventoryRemnants));

        var job2 = await t.Get<JobService>().SaveAsync(new Job { CustomerId = customer, Title = "Job B", Quantity = 1, OrderDate = day, DueDate = day.AddDays(3), SellingPrice = 200 });
        await inv.ConsumeRemnantAsync(remnantId, job2, day);
        Assert.Equal(expected, await Ledger.BalanceAsync(t, SystemAccounts.WIP, job2));
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.InventoryRemnants));
        Assert.Equal("Err.RemnantNotAvailable", (await Assert.ThrowsAsync<DomainException>(() => inv.ConsumeRemnantAsync(remnantId, job, day))).Code);
        await Ledger.AssertBooksBalanceAsync(t);
    }

    private static decimal MaterialUtilizationCalculator_RemnantCost(decimal area, decimal cost, decimal l, decimal w) =>
        LaserWorks.Domain.Costing.MaterialUtilizationCalculator.RemnantCost(area, cost, l, w);
}

public class SalesReturnTests
{
    /// <summary>Spec §44: sale → COGS → return → inventory receipt at ORIGINAL cost → COGS reversal; all ledgers reconcile.</summary>
    [Fact]
    public async Task Return_restocks_at_original_cost_and_reverses_revenue_tax_receivable_and_cogs()
    {
        await using var t = await TestDb.CreateAsync();
        var sales = t.Get<SalesService>();
        var inv = t.Get<InventoryService>();
        var fg = (await t.Get<MaterialService>().LookupAsync()).First(m => m.Name.StartsWith("Wooden Coaster"));
        var wh = (await t.Get<SettingsService>().GetAsync()).DefaultWarehouseId!.Value;
        var day = t.Clock.Now.Date;
        var customer = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Gift shop", CreditLimit = 10000 });
        var fgValueBefore = await Ledger.BalanceAsync(t, SystemAccounts.InventoryFG);
        Assert.Equal(600m, fgValueBefore); // 50 @ 12

        // sale of 12 @ 35 + 15 % VAT
        var invoice = new SalesInvoice { CustomerId = customer, Date = day, DueDate = day.AddDays(30) };
        invoice.Lines.Add(new SalesInvoiceLine { LineType = InvoiceLineType.StockItem, MaterialId = fg.Id, WarehouseId = wh, Description = "Coaster set", Quantity = 12, UnitPrice = 35, TaxRate = 15 });
        var invoiceId = await sales.SaveInvoiceAsync(invoice);
        await sales.PostInvoiceAsync(invoiceId);
        Assert.Equal(144m, await Ledger.BalanceAsync(t, SystemAccounts.COGS));      // 12 × 12
        Assert.Equal(-420m, await Ledger.BalanceAsync(t, SystemAccounts.Sales));
        Assert.Equal(483m, await Ledger.BalanceAsync(t, SystemAccounts.AR, customerId: customer));

        // later purchases at a higher cost move the average away from the original 12
        t.Clock.Set(day.AddDays(2).AddHours(10));
        await inv.AdjustAsync(fg.Id, wh, 10, 20, day.AddDays(2), "new batch");
        var avgNow = (await t.Get<MaterialService>().GetAsync(fg.Id))!.AverageCost;
        Assert.True(avgNow > 12m);

        // customer returns 2
        t.Clock.Set(day.AddDays(3).AddHours(10));
        var line = (await sales.GetInvoiceAsync(invoiceId))!.Lines.Single();
        var returnId = await sales.CreateReturnAsync(invoiceId, new[] { new ReturnLineInput(line.Id, 2, true, wh) }, day.AddDays(3), "wrong colour");
        var ret = (await sales.ListReturnsAsync(new PageRequest())).Items.Single(r => r.Id == returnId);
        Assert.Equal(24m, ret.CogsReversed);                                            // original cost 2 × 12, not 2 × average
        Assert.Equal(70m, ret.Subtotal);
        Assert.Equal(10.5m, ret.TaxAmount);

        var restock = (await inv.ListTransactionsAsync(new PageRequest(PageSize: 100), materialId: fg.Id, type: InventoryTxType.SalesReturn)).Items.Single();
        Assert.Equal(2m, restock.Quantity);
        Assert.Equal(12m, restock.UnitCost);

        Assert.Equal(120m, await Ledger.BalanceAsync(t, SystemAccounts.COGS));
        Assert.Equal(-420m, await Ledger.BalanceAsync(t, SystemAccounts.Sales));
        Assert.Equal(70m, await Ledger.BalanceAsync(t, SystemAccounts.SalesReturns));
        Assert.Equal(-(63m - 10.5m), await Ledger.BalanceAsync(t, SystemAccounts.OutputTax));
        Assert.Equal(483m - 80.5m, await Ledger.BalanceAsync(t, SystemAccounts.AR, customerId: customer));
        Assert.Equal(483m - 80.5m, (await sales.GetInvoiceAsync(invoiceId))!.Balance);
        var m = (await t.Get<MaterialService>().GetAsync(fg.Id))!;
        Assert.Equal(50m - 12 + 10 + 2, m.QuantityOnHand);
        Assert.Equal(m.StockValue, await Ledger.BalanceAsync(t, SystemAccounts.InventoryFG));
        Assert.Equal(600m - 144m + 200m + 24m, m.StockValue);
        Assert.Equal("Err.ReturnExceedsSold", (await Assert.ThrowsAsync<DomainException>(() =>
            sales.CreateReturnAsync(invoiceId, new[] { new ReturnLineInput(line.Id, 11, true, wh) }, day.AddDays(3), "too many"))).Code);
        await Ledger.AssertBooksBalanceAsync(t);
    }
}
