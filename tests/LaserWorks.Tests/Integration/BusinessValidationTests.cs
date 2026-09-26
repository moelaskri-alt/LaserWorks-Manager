using LaserWorks.Application.Abstractions;
using LaserWorks.Infrastructure.Backup;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Business validations with hand-calculated expected values:
/// the reference job (MDF 500 + acrylic 350 + LED 180 + wire 75 + glue 25 + packaging 60 + labor 300 + machine 450 + scrap 50 = 1,990)
/// sold at a profit and at a loss, checked on every screen/report that shows it; the inventory roll-forward
/// (opening + receipts − issues + returns ± adjustments − scrap = closing, in quantity and value); and backup → changes → restore
/// → close → reopen.
/// </summary>
public class BusinessValidationTests
{
    private readonly ITestOutputHelper _out;
    public BusinessValidationTests(ITestOutputHelper output) => _out = output;

    private sealed record Setup(long Customer, long Machine, long Employee, long Wh, long Mdf, long Acrylic, long Led, long Wire, long Glue, long Box);

    private static async Task<Setup> ArrangeAsync(TestDb t)
    {
        var day = t.Clock.Now.Date;
        // the reference job has no overhead: actual overhead follows the company rate, so it is set to 0
        var settings = await t.Get<SettingsService>().GetAsync();
        settings.OverheadRate = 0;
        await t.Get<SettingsService>().SaveAsync(settings);
        var wh = settings.DefaultWarehouseId!.Value;
        await using var db = t.Get<IAppDbFactory>().Create();
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id);
        var mat = t.Get<MaterialService>();
        var inv = t.Get<InventoryService>();
        async Task<long> Item(string name, MaterialKind kind, string unit, decimal cost, decimal qty)
        {
            var id = await mat.SaveAsync(new Material { Name = name, Kind = kind, UnitId = units[unit], PurchaseCost = cost });
            await inv.OpeningBalanceAsync(id, wh, qty, cost, day);
            return id;
        }
        return new Setup(
            await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Reference Customer", CreditLimit = 100000 }),
            await t.Get<MachineService>().SaveAsync(new Machine { Name = "CO2 150W", UseHourlyCostOverride = true, HourlyCostOverride = 45 }),
            await t.Get<EmployeeService>().SaveAsync(new Employee { Name = "Operator", Role = "Operator", HourlyCost = 30 }),
            wh,
            await Item("MDF 4mm sheet", MaterialKind.RawMaterial, "SHEET", 250, 10),
            await Item("Acrylic 3mm sheet", MaterialKind.RawMaterial, "SHEET", 350, 5),
            await Item("LED lamp", MaterialKind.PurchasedComponent, "PCS", 90, 20),
            await Item("Electrical wire", MaterialKind.PurchasedComponent, "M", 15, 100),
            await Item("Glue", MaterialKind.Consumable, "L", 25, 10),
            await Item("Packaging box", MaterialKind.Packaging, "PCS", 60, 10));
    }

    /// <summary>Spec §8 and §12: the 1,990 job sold for 3,000 (profit 1,010, 33.67 %) and for 1,500 (loss 490, −32.67 %).</summary>
    /// Third case — the demo script's variance job: 2.44 MDF sheets issued (0.2 scrapped) and 12 machine hours instead of 10:
    /// MDF 560 (+60), machine 540 (+90), actual 2,140, profit 860 (28.67 %), main variance driver = machine time.
    [Theory]
    [InlineData(3000, 1010, 33.67, 2.2, 10)]
    [InlineData(1500, -490, -32.67, 2.2, 10)]
    [InlineData(3000, 860, 28.67, 2.44, 12)]
    public async Task Reference_job_costs_1990_and_profit_is_the_same_everywhere(decimal price, decimal expectedProfit, decimal expectedMargin, decimal mdfIssued, decimal machineHours)
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var s = await ArrangeAsync(t);

        // estimate: every line priced so the expected amounts are exact
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(s.Customer);
        est.Description = "Illuminated MDF/acrylic sign"; est.Quantity = 1;
        est.MaterialLines.Clear();
        void Line(ComponentCategory cat, long item, decimal qty, decimal cost) =>
            est.MaterialLines.Add(new EstimateMaterialLine { Category = cat, Source = ComponentSource.Inventory, MaterialId = item, SheetBased = false, QuantityPerUnit = qty, UnitCost = cost });
        Line(ComponentCategory.RawMaterial, s.Mdf, 2, 250);
        Line(ComponentCategory.RawMaterial, s.Acrylic, 1, 350);
        Line(ComponentCategory.PurchasedComponent, s.Led, 2, 90);
        Line(ComponentCategory.PurchasedComponent, s.Wire, 5, 15);
        Line(ComponentCategory.Consumable, s.Glue, 1, 25);
        Line(ComponentCategory.Packaging, s.Box, 1, 60);
        est.MachineLines.Clear(); est.LaborLines.Clear();
        est.MachineLines.Add(new EstimateMachineLine { MachineId = s.Machine, Operation = OperationType.Cutting, MinutesPerUnit = 600 });
        est.LaborLines.Add(new EstimateLaborLine { EmployeeId = s.Employee, Operation = OperationType.Assembly, MinutesPerUnit = 600, HourlyRate = 30 });
        est.DesignHours = 0; est.SetupHours = 0; est.FinishingPerUnit = 0; est.PackagingPerUnit = 0; est.ConsumablesPerUnit = 0;
        est.OverheadRate = 0; est.ScrapAllowancePercent = 0; est.ReworkAllowancePercent = 0;
        est.Components.Clear();
        est.Components.Add(new EstimateComponentLine { Component = CostComponent.Scrap, Mode = ComponentMode.Manual, ManualAmount = 50 });
        est.SellingPrice = price;
        var calc = await estSvc.CalculateAsync(est);
        Assert.Equal(850m, calc[CostComponent.Material]);
        Assert.Equal(255m, calc[CostComponent.PurchasedComponents]);
        Assert.Equal(25m, calc[CostComponent.Consumables]);
        Assert.Equal(60m, calc[CostComponent.Packaging]);
        Assert.Equal(300m, calc[CostComponent.Labor]);
        Assert.Equal(450m, calc[CostComponent.Machine]);
        Assert.Equal(0m, calc[CostComponent.Maintenance]);
        Assert.Equal(50m, calc[CostComponent.Scrap]);
        Assert.Equal(0m, calc.Overhead);
        Assert.Equal(1990m, calc.TotalCost);
        var estimateId = await estSvc.SaveAsync(est);
        Assert.Equal(1990m, (await estSvc.GetAsync(estimateId))!.TotalCost);

        // quotation → job
        var quotes = t.Get<QuotationService>();
        var q = await quotes.CreateFromEstimateAsync(estimateId);
        await quotes.MarkSentAsync(q);
        await quotes.ApproveAsync(q);
        var jobId = await quotes.CreateJobAsync(q, new JobCreationOptions(day.AddDays(7), JobPriority.Normal, s.Machine, s.Employee));
        Assert.Equal(price, (await t.Get<JobService>().GetAsync(jobId))!.SellingPrice);

        // actual: every line issued; MDF 2.2 sheets of which 0.2 scrapped (50) → MDF 500 + scrap 50
        var jc = t.Get<JobComponentService>();
        foreach (var l in await jc.ListAsync(jobId))
            await jc.IssueAsync(l.Id, s.Wh, l.MaterialId == s.Mdf ? mdfIssued : l.PlannedQuantity, day);
        var prod = t.Get<ProductionService>();
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.NormalScrap, MaterialId = s.Mdf, Quantity = 0.2m, Reason = "burnt edge" });
        foreach (var o in (await prod.ListOperationsAsync(new PageRequest(PageSize: 50), jobId)).Items)
        {
            var machine = o.OperationType == OperationType.Cutting ? machineHours : 0;
            var labor = o.OperationType == OperationType.Assembly ? 10m : 0;
            await prod.CompleteOperationAsync(o.Id, new OperationCompletion(labor, machine, 1, EmployeeId: s.Employee, MachineId: machine > 0 ? s.Machine : null));
        }
        await prod.CompleteProductionAsync(jobId);
        await prod.RecordQualityCheckAsync(new QualityCheck { JobId = jobId, Date = day, QuantityProduced = 1, QuantityAccepted = 1, Status = QualityStatus.Passed });
        await t.Get<JobService>().DeliverAsync(jobId, day, null);
        var sales = t.Get<SalesService>();
        var invoiceId = await sales.CreateInvoiceFromJobAsync(jobId);
        await sales.PostInvoiceAsync(invoiceId);
        var invoice = (await sales.GetInvoiceAsync(invoiceId))!;
        Assert.Equal(price, invoice.Subtotal - invoice.DiscountAmount);
        await sales.RecordPaymentAsync(s.Customer, invoiceId, invoice.Total, PaymentMethod.Bank, day, "TRF");

        // the job
        var costing = t.Get<JobCostingService>();
        var cs = await costing.CostSheetAsync(jobId);
        decimal A(CostComponent c) => cs.Entries.Where(e => e.Component == c).Sum(e => e.Amount);
        _out.WriteLine(string.Join(", ", cs.Entries.GroupBy(e => e.Component).Select(g => $"{g.Key} {g.Sum(e => e.Amount)}")));
        var mdfActual = Money.Round((mdfIssued - 0.2m) * 250);
        var machineActual = machineHours * 45;
        var expectedActual = 1990m + (mdfActual - 500) + (machineActual - 450);
        Assert.Equal(350m + mdfActual, A(CostComponent.Material));
        Assert.Equal(255m, A(CostComponent.PurchasedComponents));
        Assert.Equal(25m, A(CostComponent.Consumables));
        Assert.Equal(60m, A(CostComponent.Packaging));
        Assert.Equal(300m, A(CostComponent.Labor));
        Assert.Equal(machineActual, A(CostComponent.Machine) + A(CostComponent.Maintenance));
        Assert.Equal(50m, A(CostComponent.Scrap));
        Assert.Equal(0m, A(CostComponent.Overhead));
        Assert.Equal(expectedActual, cs.ActualCost);
        Assert.Equal(1990m, cs.Variance.EstimatedTotal);
        Assert.Equal(expectedActual - 1990m, cs.Variance.ActualTotal - cs.Variance.EstimatedTotal);
        var mdfLine = cs.Components.Single(c => c.IsLine && c.Item.Contains("MDF"));
        Assert.Equal((500m, mdfActual), (mdfLine.Estimated, mdfLine.Actual));
        if (machineHours != 10) Assert.Equal(CostComponent.Machine, cs.Variance.MainDriver);
        Assert.Equal(price, cs.Revenue);
        Assert.Equal(expectedProfit, cs.GrossProfit);
        Assert.Equal(expectedMargin, cs.MarginPercent);

        // job profitability (list and flags: a loss is flagged, never hidden)
        var row = (await costing.JobProfitabilityAsync()).Single(r => r.JobId == jobId);
        Assert.Equal(expectedActual, row.ActualCost);
        Assert.Equal(expectedProfit, row.GrossProfit);
        Assert.Equal(expectedMargin, row.MarginPercent);
        Assert.Equal(expectedProfit < 0, costing.Flags(row).NegativeMargin);

        // reports: job cost sheet, job profitability (loss row styled), income statement
        var catalog = t.Get<ReportCatalog>();
        var range = new DateRange(day.AddDays(-1), day);
        var sheet = await catalog.RunAsync("JobCostSheet", new ReportFilter(range, day, JobId: jobId));
        var amountCol = sheet.Columns.FindIndex(c => c.Key == "amount");
        Assert.Equal(expectedActual, sheet.Totals()[amountCol]);
        var prof = await catalog.RunAsync("JobProfitability", new ReportFilter(range, day));
        var profRow = prof.Rows.Single(r => r.SourceId == jobId);
        Assert.Contains(expectedProfit, profRow.Cells.OfType<decimal>());
        if (expectedProfit < 0) Assert.Equal(RowStyle.Negative, profRow.Style);
        var income = await t.Get<AccountingService>().IncomeStatementAsync(range);
        Assert.Equal(price, income.Single(l => l.Name == "Net revenue").Amount);
        Assert.Equal(expectedActual, income.Single(l => l.Name == "Total cost of sales").Amount);
        Assert.Equal(expectedProfit, income.Single(l => l.Code == "GP").Amount);

        // dashboard for the same day
        var dash = await t.Get<DashboardService>().LoadAsync(range);
        Assert.Equal(price, dash.SalesInRange);
        Assert.Equal(expectedProfit, dash.GrossProfit);
        Assert.Equal(expectedMargin, dash.GrossMarginPercent);
        await Ledger.AssertBooksBalanceAsync(t);
        _out.WriteLine($"price {price}: cost {cs.ActualCost}, profit {cs.GrossProfit}, margin {cs.MarginPercent}% — job, profitability, report, income statement and dashboard agree");
    }

    /// <summary>
    /// Spec §9: opening + receipts − issues + returns ± adjustments − scrap = closing for one material,
    /// in quantity and value, with the moving weighted average recomputed by hand; transfers move stock between
    /// warehouses without changing the total; the GL inventory account equals the stock value.
    /// </summary>
    [Fact]
    public async Task Inventory_roll_forward_and_moving_average()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var s = await ArrangeAsync(t);   // MDF: opening 10 @ 250 = 2,500
        var inv = t.Get<InventoryService>();
        var mat = t.Get<MaterialService>();
        var pur = t.Get<PurchaseService>();
        var supplier = await t.Get<SupplierService>().SaveAsync(new Supplier { Name = "Wood Supplier" });

        // receipt 10 @ 280 → average (2,500 + 2,800) / 20 = 265
        var po = new PurchaseOrder { SupplierId = supplier, Date = day, ExpectedDate = day };
        po.Lines.Add(new PurchaseOrderLine { MaterialId = s.Mdf, Quantity = 10, UnitCost = 280, TaxRate = 15 });
        var poId = await pur.SaveOrderAsync(po);
        await pur.SetOrderStatusAsync(poId, PurchaseOrderStatus.Approved);
        var rec = await pur.ReceiptFromOrderAsync(poId);
        rec.WarehouseId = s.Wh; rec.Date = day;
        await pur.PostReceiptAsync(rec);
        var m = (await mat.GetAsync(s.Mdf))!;
        Assert.Equal(20m, m.QuantityOnHand);
        Assert.Equal(265m, m.AverageCost);
        Assert.Equal(5300m, m.StockValue);

        // issue 6 to a job @ 265 = 1,590; return 1 at its issue cost 265
        var jobId = await t.Get<JobService>().SaveAsync(new Job { CustomerId = s.Customer, Title = "Stock test", Quantity = 1, OrderDate = day });
        await inv.IssueToJobAsync(jobId, s.Mdf, s.Wh, 6, day, null);
        await inv.ReturnFromJobAsync(jobId, s.Mdf, s.Wh, 1, day, null);
        // adjustment +2 @ 300 → (15 × 265 + 600) / 17; adjustment −1; scrap 1 from stock
        await inv.AdjustAsync(s.Mdf, s.Wh, 2, 300, day, "count difference");

        await inv.AdjustAsync(s.Mdf, s.Wh, -1, null, day, "damaged in storage");
        await inv.ScrapStockAsync(s.Mdf, s.Wh, 1, day, "water damage");
        // transfer 3 to a second warehouse — total unchanged
        var wh2 = await t.Get<LookupService>().SaveWarehouseAsync(new Warehouse { Code = "WH2", Name = "Second store", IsActive = true });
        await inv.TransferAsync(s.Mdf, s.Wh, wh2, 3, day, null);

        m = (await mat.GetAsync(s.Mdf))!;
        var expectedQty = 10m + 10 - 6 + 1 + 2 - 1 - 1;            // = 15
        Assert.Equal(expectedQty, m.QuantityOnHand);
        // value is the source of truth: each removal takes out qty × average rounded to money, and the average is value ÷ quantity
        //   after the receipt 5,300 / 20 = 265; issue 6 → 3,710 / 14; return 1 at 265 → 3,975 / 15; +2 @ 300 → 4,575 / 17 = 269.1176
        //   −1 at 269.12 → 4,305.88 / 16; scrap 1 at 269.12 → 4,036.76 / 15 = 269.1173
        Assert.Equal(4036.76m, m.StockValue);
        Assert.Equal(Money.Round(4036.76m / 15, 4), Money.Round(m.AverageCost, 4));
        Assert.Equal(3m, await mat.StockInWarehouseAsync(s.Mdf, wh2));
        Assert.Equal(12m, await mat.StockInWarehouseAsync(s.Mdf, s.Wh));

        // value roll-forward from the stock ledger: opening + receipts − issues + returns ± adjustments − scrap = closing value
        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            var txs = await db.InventoryTransactions.AsNoTracking().Where(x => x.MaterialId == s.Mdf).OrderBy(x => x.Id).ToListAsync();
            decimal Val(InventoryTxType type) => txs.Where(x => x.Type == type).Sum(x => x.TotalCost);
            decimal Qty(InventoryTxType type) => txs.Where(x => x.Type == type).Sum(x => x.Quantity);
            Assert.Equal(expectedQty, txs.Sum(x => x.Quantity));
            Assert.Equal(0m, Qty(InventoryTxType.Transfer));                     // out and in cancel
            Assert.Equal(2500m, Val(InventoryTxType.OpeningBalance));
            Assert.Equal(2800m, Val(InventoryTxType.PurchaseReceipt));
            Assert.Equal(-1590m, Val(InventoryTxType.MaterialIssue));
            Assert.Equal(265m, Val(InventoryTxType.MaterialReturn));
            var closing = Val(InventoryTxType.OpeningBalance) + Val(InventoryTxType.PurchaseReceipt) + Val(InventoryTxType.MaterialIssue) + Val(InventoryTxType.MaterialReturn)
                          + Val(InventoryTxType.Adjustment) + Val(InventoryTxType.Scrap) + Val(InventoryTxType.Transfer);
            Assert.Equal(m.StockValue, closing);
            Assert.Equal(txs.Last().QuantityAfter, m.QuantityOnHand);
        }
        // the whole inventory in the GL equals the stock value of all raw materials
        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            var raw = await db.Materials.Where(x => x.Kind == MaterialKind.RawMaterial).SumAsync(x => x.StockValue);
            Assert.Equal(raw, await Ledger.BalanceAsync(t, SystemAccounts.Inventory));
        }
        await Ledger.AssertBooksBalanceAsync(t);
    }

    /// <summary>
    /// Spec §15: backup → more transactions → restore → the application is closed and started again on the same data folder →
    /// the restored data is what was backed up, the later transactions are gone, and the books still reconcile.
    /// </summary>
    [Fact]
    public async Task Backup_restore_then_close_and_reopen()
    {
        var folder = TestDb.NewFolder("reopen");
        string backup;
        int customersAtBackup; decimal stockAtBackup; long mdf;
        await using (var t = await TestDb.CreateAsync(folder: folder))
        {
            t.KeepFolder = true;
            var s = await ArrangeAsync(t);
            mdf = s.Mdf;
            await using (var db = t.Get<IAppDbFactory>().Create()) customersAtBackup = await db.Customers.CountAsync();
            stockAtBackup = (await t.Get<MaterialService>().GetAsync(mdf))!.QuantityOnHand;
            backup = (await t.Get<BackupService>().CreateBackupAsync(null, "before changes")).FilePath;
            // more work after the backup
            await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Created after backup" });
            var job = await t.Get<JobService>().SaveAsync(new Job { CustomerId = s.Customer, Title = "After backup", Quantity = 1, OrderDate = t.Clock.Now.Date });
            await t.Get<InventoryService>().IssueToJobAsync(job, mdf, s.Wh, 3, t.Clock.Now.Date, null);
            Assert.Equal(stockAtBackup - 3, (await t.Get<MaterialService>().GetAsync(mdf))!.QuantityOnHand);
            await t.Get<BackupService>().RestoreAsync(backup);
        }
        // "close" the application (the service provider and its connections are gone) and "reopen" it on the same folder
        await using (var t = await TestDb.CreateAsync(setup: false, folder: folder))
        {
            await t.LoginAsync("admin", "Admin@2026");
            await using (var db = t.Get<IAppDbFactory>().Create())
            {
                Assert.Equal(customersAtBackup, await db.Customers.CountAsync());
                Assert.False(await db.Customers.AnyAsync(c => c.Name == "Created after backup"));
                Assert.False(await db.Jobs.AnyAsync(j => j.Title == "After backup"));
            }
            Assert.Equal(stockAtBackup, (await t.Get<MaterialService>().GetAsync(mdf))!.QuantityOnHand);
            await Ledger.AssertBooksBalanceAsync(t);
        }
    }
}

/// <summary>Spec §18: duplicate codes, required fields and records in use are refused with a message, never a database error.</summary>
public class DataIntegrityTests
{
    [Fact]
    public async Task Duplicates_required_fields_and_records_in_use_are_refused()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var customers = t.Get<CustomerService>();
        var c1 = await customers.SaveAsync(new Customer { Name = "First" });
        var code = (await customers.GetAsync(c1))!.Code;
        var dup = await Assert.ThrowsAsync<DomainException>(() => customers.SaveAsync(new Customer { Name = "Second", Code = code }));
        Assert.StartsWith("Err.", dup.Code);
        await Assert.ThrowsAsync<DomainException>(() => customers.SaveAsync(new Customer { Name = "" }));
        await Assert.ThrowsAsync<DomainException>(() => t.Get<JobService>().SaveAsync(new Job { CustomerId = c1, Title = "", Quantity = 1, OrderDate = day }));
        await Assert.ThrowsAsync<DomainException>(() => t.Get<JobService>().SaveAsync(new Job { CustomerId = c1, Title = "x", Quantity = 0, OrderDate = day }));

        var mats = t.Get<MaterialService>();
        var acrylic = (await mats.LookupAsync()).First(m => m.Name.StartsWith("Acrylic"));
        var mdup = await Assert.ThrowsAsync<DomainException>(() => mats.SaveAsync(new Material { Name = "Copy", Code = acrylic.Code, UnitId = 1 }));
        Assert.StartsWith("Err.", mdup.Code);

        // a customer with a job and a material with stock movements cannot be deleted
        await t.Get<JobService>().SaveAsync(new Job { CustomerId = c1, Title = "In use", Quantity = 1, OrderDate = day });
        Assert.StartsWith("Err.", (await Assert.ThrowsAsync<DomainException>(() => customers.DeleteAsync(c1))).Code);
        Assert.StartsWith("Err.", (await Assert.ThrowsAsync<DomainException>(() => mats.DeleteAsync(acrylic.Id))).Code);
    }
}
