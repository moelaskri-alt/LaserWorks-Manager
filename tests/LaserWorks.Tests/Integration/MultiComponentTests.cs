using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Costing;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Multi-component jobs: request items → estimate component lines → job component lines → stock issues, remnants,
/// direct purchases and external services → estimated vs actual per line → accounting and profitability.
/// </summary>
public class MultiComponentTests
{
    private readonly ITestOutputHelper _out;
    public MultiComponentTests(ITestOutputHelper output) => _out = output;

    private sealed record Items(MaterialLookup Acrylic, long Led, long Adapter, long Screws, long Tape, long Box, long UvService, long Supplier, long Wh, long MachineId, long OperatorId);

    /// <summary>Stock items of every kind, with opening stock, plus a supplier.</summary>
    private static async Task<Items> SetupItemsAsync(TestDb t)
    {
        var day = t.Clock.Now.Date;
        var wh = (await t.Get<SettingsService>().GetAsync()).DefaultWarehouseId!.Value;
        await using var db = t.Get<IAppDbFactory>().Create();
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id);
        var mat = t.Get<MaterialService>();
        var inv = t.Get<InventoryService>();
        async Task<long> Item(string name, MaterialKind kind, string unit, decimal cost, decimal opening)
        {
            var id = await mat.SaveAsync(new Material { Name = name, Kind = kind, UnitId = units[unit], PurchaseCost = cost });
            if (opening > 0) await inv.OpeningBalanceAsync(id, wh, opening, cost, day);
            return id;
        }
        var led = await Item("LED strip 12V warm white", MaterialKind.PurchasedComponent, "M", 18, 100);
        var adapter = await Item("Power adapter 12V 5A", MaterialKind.PurchasedComponent, "PCS", 45, 20);
        var screws = await Item("Stainless standoff screw", MaterialKind.PurchasedComponent, "PCS", 2.5m, 200);
        var tape = await Item("Double-sided VHB tape", MaterialKind.Consumable, "ROLL", 22, 10);
        var box = await Item("Carton box 60x40", MaterialKind.Packaging, "PCS", 9, 50);
        var uv = await Item("UV printing service", MaterialKind.Service, "PCS", 120, 0);
        var supplier = await t.Get<SupplierService>().SaveAsync(new Supplier { Name = "Bright LED Trading", PaymentTermsDays = 30 });
        var acrylic = (await mat.LookupAsync()).First(m => m.Name.StartsWith("Acrylic"));
        var machine = (await t.Get<MachineService>().LookupAsync()).First().Id;
        var op = await t.Get<EmployeeService>().SaveAsync(new Employee { Name = "Khalid Operator", Role = "Laser operator", HourlyCost = 30 });
        return new Items(acrylic, led, adapter, screws, tape, box, uv, supplier, wh, machine, op);
    }

    private static async Task<int> CountAsync<T>(TestDb t, Func<IAppDb, IQueryable<T>> q)
    {
        await using var db = t.Get<IAppDbFactory>().Create();
        return await q(db).CountAsync();
    }

    /// <summary>
    /// The restaurant sign: an illuminated acrylic sign (sheet material, LED strip, adapter, screws, tape, box, UV printing service,
    /// a mounting kit bought directly for the job and an installation cost), two design revisions, remnant use, scrap, rework,
    /// QC, delivery, invoice, payment and profitability — every figure reconciled to the ledger.
    /// </summary>
    [Fact]
    public async Task Restaurant_sign_multi_component_end_to_end()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Al Bait Restaurant", Phone = "0551234567", CreditLimit = 50000, PaymentTermsDays = 30 });

        // request with every requested item (quantity per finished sign)
        var reqSvc = t.Get<RequestService>();
        var requestId = await reqSvc.SaveAsync(new CustomerRequest
        {
            CustomerId = customerId, RequestDate = day, Description = "Illuminated restaurant sign 120×40 cm", Quantity = 2, RequiredDate = day.AddDays(20),
            Items =
            {
                new RequestItem { MaterialId = it.Acrylic.Id, Quantity = 1 },
                new RequestItem { MaterialId = it.Led, Quantity = 3 },
                new RequestItem { MaterialId = it.Adapter, Quantity = 1 },
                new RequestItem { MaterialId = it.Screws, Quantity = 4 },
                new RequestItem { MaterialId = it.Tape, Quantity = 0.5m },
                new RequestItem { MaterialId = it.Box, Quantity = 1 },
                new RequestItem { MaterialId = it.UvService, Quantity = 1 },
                new RequestItem { Category = ComponentCategory.PurchasedComponent, Description = "Brass wall-mounting kit", Quantity = 1 }
            }
        });
        var request = (await reqSvc.GetAsync(requestId))!;
        Assert.Equal(8, request.Items.Count);
        Assert.Equal(new[] { ComponentCategory.RawMaterial, ComponentCategory.PurchasedComponent, ComponentCategory.PurchasedComponent, ComponentCategory.PurchasedComponent,
            ComponentCategory.Consumable, ComponentCategory.Packaging, ComponentCategory.ExternalService, ComponentCategory.PurchasedComponent },
            request.Items.OrderBy(i => i.LineNo).Select(i => i.Category));

        // design V1 (rejected) and V2 (approved)
        var design = t.Get<DesignService>();
        var v1 = await design.SaveAsync(new DesignRevision { RequestId = requestId, Date = day, Width = 110, Height = 40, MaterialId = it.Acrylic.Id, Thickness = 3, CuttingLengthM = 6, EstimatedMachineMinutes = 20 });
        await design.RejectAsync(v1);
        var v2 = await design.SaveAsync(new DesignRevision { RequestId = requestId, Date = day, Width = 120, Height = 40, MaterialId = it.Acrylic.Id, Thickness = 3, CuttingLengthM = 7, EstimatedMachineMinutes = 25 });
        await design.ApproveAsync(v2);
        Assert.NotEqual(RevisionStatus.Approved, (await design.GetAsync(v1))!.Status);
        Assert.Equal(v2, (await design.ApprovedForRequestAsync(requestId))!.Id);

        // estimate: one line per requested item, then the mounting kit priced and an installation cost added
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(customerId, requestId);
        Assert.Equal(8, est.MaterialLines.Count);
        var sheet = est.MaterialLines.Single(l => l.MaterialId == it.Acrylic.Id);
        Assert.True(sheet.SheetBased);
        sheet.Pieces.Clear();
        sheet.Pieces.Add(new EstimatePiece { Name = "Face", Length = 120, Width = 40, QuantityPerUnit = 1 });
        var kit = est.MaterialLines.Single(l => l.MaterialId == null && l.Category == ComponentCategory.PurchasedComponent);
        Assert.Equal(ComponentSource.DirectPurchase, kit.Source);
        kit.UnitCost = 25;
        var uvLine = est.MaterialLines.Single(l => l.MaterialId == it.UvService);
        Assert.Equal(ComponentSource.ExternalService, uvLine.Source);
        Assert.Equal(ComponentCategory.ExternalService, uvLine.Category);
        est.MaterialLines.Add(new EstimateMaterialLine { Category = ComponentCategory.OtherDirect, Source = ComponentSource.ManualCost, Description = "Installation transport", QuantityPerUnit = 0.5m, UnitCost = 80, SheetBased = false });
        est.MachineLines.Clear();
        est.MachineLines.Add(new EstimateMachineLine { MachineId = it.MachineId, Operation = OperationType.Cutting, MinutesPerUnit = 25 });
        est.LaborLines.Add(new EstimateLaborLine { EmployeeId = it.OperatorId, Operation = OperationType.Assembly, MinutesPerUnit = 45, HourlyRate = 30 });
        est.ReworkAllowancePercent = 5;
        est.PackagingPerUnit = 0; est.ConsumablesPerUnit = 0; est.FinishingPerUnit = 0;
        var calc = await estSvc.CalculateAsync(est);
        foreach (var cat in Enum.GetValues<ComponentCategory>())
        {
            var comp = ComponentRules.CostComponentOf(cat);
            var catLines = est.MaterialLines.Where(l => ComponentRules.CostComponentOf(l.Category) == comp).Sum(l => l.Cost);
            Assert.Equal(catLines, calc[comp]);
        }
        Assert.Equal(2 * 3 * 18m, est.MaterialLines.Single(l => l.MaterialId == it.Led).Cost);          // 6 m of LED strip
        Assert.Equal(2 * 120m, calc[CostComponent.ExternalServices]);
        Assert.Equal(Money.Round(0.05m * (calc[CostComponent.Machine] + calc[CostComponent.Maintenance] + calc[CostComponent.Labor])), calc[CostComponent.Rework]);
        Assert.Equal(calc.DirectCost + calc.Overhead, calc.TotalCost);
        var estimateId = await estSvc.SaveAsync(est);
        var saved = (await estSvc.GetAsync(estimateId))!;
        Assert.Equal(9, saved.MaterialLines.Count);
        Assert.Equal(Enumerable.Range(1, 9), saved.MaterialLines.OrderBy(l => l.LineNo).Select(l => l.LineNo));
        Assert.Equal(calc.TotalCost, saved.TotalCost);

        // quotation → job: one job component per estimate line, estimated figures snapshotted
        var quotes = t.Get<QuotationService>();
        var quoteId = await quotes.CreateFromEstimateAsync(estimateId);
        await quotes.MarkSentAsync(quoteId);
        await quotes.ApproveAsync(quoteId);
        var jobId = await quotes.CreateJobAsync(quoteId, new JobCreationOptions(day.AddDays(10), JobPriority.High, it.MachineId, it.OperatorId));
        var jc = t.Get<JobComponentService>();
        var lines0 = await jc.ListAsync(jobId);
        Assert.Equal(9, lines0.Count);
        Assert.Equal(saved.MaterialLines.Sum(l => l.Cost), lines0.Sum(l => l.EstimatedCost));
        Assert.All(lines0, l => Assert.True(l.FromEstimate));

        // issue every stocked line in full
        foreach (var l in lines0.Where(l => l.Source == ComponentSource.Inventory))
            await jc.IssueAsync(l.Id, it.Wh, l.PlannedQuantity, day);
        // direct purchase on credit (actual 52 vs estimate 50), external service paid in cash, installation paid in cash
        var kitLine = lines0.Single(l => l.Source == ComponentSource.DirectPurchase);
        var apBefore = await Ledger.BalanceAsync(t, SystemAccounts.AP);
        await jc.RecordDirectCostAsync(new DirectCostInput(kitLine.Id, day, 2, 52, 7.8m, PaymentMethod.OnCredit, it.Supplier, "BLT-7781", null));
        Assert.Equal(-59.8m, await Ledger.BalanceAsync(t, SystemAccounts.AP) - apBefore);
        var uvJobLine = lines0.Single(l => l.Source == ComponentSource.ExternalService);
        await jc.RecordDirectCostAsync(new DirectCostInput(uvJobLine.Id, day, 2, 230, 34.5m, PaymentMethod.Cash, null, "UV-19", null));
        var install = lines0.Single(l => l.Source == ComponentSource.ManualCost);
        await jc.RecordDirectCostAsync(new DirectCostInput(install.Id, day, 1, 40, 0, PaymentMethod.Cash, null, null, "fuel"));
        // no double charge: direct lines never touch stock, stocked lines refuse direct costs
        Assert.Equal(0, await CountAsync(t, db => db.InventoryTransactions.Where(x => x.JobComponentId == kitLine.Id || x.JobComponentId == uvJobLine.Id || x.JobComponentId == install.Id)));
        var ledLine = lines0.Single(l => l.MaterialId == it.Led);
        var ex = await Assert.ThrowsAsync<DomainException>(() => jc.RecordDirectCostAsync(new DirectCostInput(ledLine.Id, day, 1, 10, 0, PaymentMethod.Cash, null, null, null)));
        Assert.Equal("Err.ComponentIsStocked", ex.Code);
        ex = await Assert.ThrowsAsync<DomainException>(() => jc.IssueAsync(kitLine.Id, it.Wh, 1, day));
        Assert.Equal("Err.ComponentNotStocked", ex.Code);

        // a stored offcut used on its own line; a second sign face cut from it
        var remnantId = await t.Get<InventoryService>().CreateRemnantAsync(new RemnantInput(it.Acrylic.Id, it.Wh, 60, 40, null, null, 18, day, "storage"));
        var remLineId = await jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.Remnant, MaterialId = it.Acrylic.Id, PlannedQuantity = 1, Notes = "back plate" });
        await jc.UseRemnantAsync(remLineId, remnantId, day);

        var lines = await jc.ListAsync(jobId);
        Assert.Equal(10, lines.Count);
        foreach (var l in lines.Where(l => l.Source == ComponentSource.Inventory)) Assert.Equal(l.PlannedQuantity, l.UsedQuantity);
        Assert.Equal(6m * 18, lines.Single(l => l.MaterialId == it.Led).ActualCost);
        Assert.Equal(52m, lines.Single(l => l.Id == kitLine.Id).ActualCost);
        Assert.Equal(2m, lines.Single(l => l.Id == kitLine.Id).Variance);
        Assert.Equal(230m, lines.Single(l => l.Id == uvJobLine.Id).ActualCost);
        Assert.Equal(18m, lines.Single(l => l.Id == remLineId).ActualCost);
        Assert.Equal(RemnantStatus.Consumed, (await t.Get<InventoryService>().ListRemnantsAsync(new PageRequest(PageSize: 50), null)).Items.Single(r => r.Id == remnantId).Status);

        // production, scrap on the sheet line (reclassified out of it), rework, QC, delivery
        var prod = t.Get<ProductionService>();
        foreach (var o in (await prod.ListOperationsAsync(new PageRequest(PageSize: 50), jobId)).Items)
            await prod.CompleteOperationAsync(o.Id, new OperationCompletion(Math.Max(o.PlannedHours, 0.5m), o.OperationType == OperationType.Cutting ? 1 : 0, 2, EmployeeId: it.OperatorId));
        var sheetLineBefore = lines.Single(l => l.MaterialId == it.Acrylic.Id && l.Source == ComponentSource.Inventory).ActualCost;
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.NormalScrap, MaterialId = it.Acrylic.Id, Quantity = 0.1m, Reason = "burnt corner" });
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.Rework, MachineId = it.MachineId, EmployeeId = it.OperatorId, Quantity = 1, Hours = 0.25m, Reason = "re-engrave logo" });
        var sheetLineAfter = (await jc.ListAsync(jobId)).Single(l => l.MaterialId == it.Acrylic.Id && l.Source == ComponentSource.Inventory).ActualCost;
        Assert.Equal(Money.Round(sheetLineBefore - 0.1m * it.Acrylic.AverageCost), sheetLineAfter);
        await prod.CompleteProductionAsync(jobId);
        await prod.RecordQualityCheckAsync(new QualityCheck { JobId = jobId, Date = day, QuantityProduced = 2, QuantityAccepted = 2, Status = QualityStatus.Passed });
        await t.Get<JobService>().DeliverAsync(jobId, day, "installed");

        // estimated vs actual per line reconciles to the job totals and to WIP
        var costing = t.Get<JobCostingService>();
        var cs = await costing.CostSheetAsync(jobId);
        Assert.Equal(cs.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        Assert.Equal(cs.Variance.EstimatedTotal, cs.Components.Sum(c => c.Estimated));
        Assert.Equal(cs.ActualCost, cs.Components.Sum(c => c.Actual));
        Assert.Equal(10, cs.Components.Count(c => c.IsLine));
        foreach (var l in await jc.ListAsync(jobId))
        {
            var row = cs.Components.Single(c => c.JobComponentId == l.Id);
            Assert.Equal(l.EstimatedCost, row.Estimated);
            Assert.Equal(l.ActualCost, row.Actual);
        }
        Assert.Contains(cs.Components, c => !c.IsLine && c.Component == CostComponent.Scrap && c.Actual > 0);
        Assert.Contains(cs.Components, c => !c.IsLine && c.Component == CostComponent.Rework && c.Actual > 0);

        // invoice, payment, profitability
        var sales = t.Get<SalesService>();
        var invoiceId = await sales.CreateInvoiceFromJobAsync(jobId);
        await sales.PostInvoiceAsync(invoiceId);
        var invoice = (await sales.GetInvoiceAsync(invoiceId))!;
        await sales.RecordPaymentAsync(customerId, invoiceId, invoice.Total, PaymentMethod.Bank, day, "TRF-88");
        var final = await costing.CostSheetAsync(jobId);
        Assert.Equal(final.Revenue - final.ActualCost, final.GrossProfit);
        var profit = (await costing.JobProfitabilityAsync()).Single(r => r.JobId == jobId);
        Assert.Equal(final.GrossProfit, profit.GrossProfit);
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        await Ledger.AssertBooksBalanceAsync(t);

        // reports and documents carry the component lines
        var catalog = t.Get<ReportCatalog>();
        var rep = await catalog.RunAsync("JobComponents", new ReportFilter(new DateRange(day.AddDays(-1), day.AddDays(1)), day, JobId: jobId));
        Assert.Equal(10, rep.Rows.Count(r => r.Style is not (RowStyle.Subtotal or RowStyle.Header)));
        var eva = await catalog.RunAsync("EstimatedVsActual", new ReportFilter(new DateRange(day.AddDays(-1), day.AddDays(1)), day, JobId: jobId));
        Assert.True(eva.Rows.Count >= 10);
        var pdf = DocumentRenderer.JobCostSheet(final, await t.Get<SettingsService>().GetAsync());
        Assert.True(pdf.Length > 1000);
        _out.WriteLine($"estimate {saved.TotalCost}, actual {final.ActualCost}, revenue {final.Revenue}, profit {final.GrossProfit} ({final.MarginPercent}%)");
    }

    /// <summary>
    /// Spec §29: an engraved wooden serving board with the restaurant's logo — plywood (stock), an acrylic logo cut from a remnant,
    /// a brass handle bought through purchasing (order → receipt → supplier invoice → issue to the job), a gift box and glue.
    /// Every posting is checked against the chart of accounts and every journal entry must balance on its own.
    /// </summary>
    [Fact]
    public async Task Serving_board_with_purchased_accessory_end_to_end()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var mat = t.Get<MaterialService>();
        var inv = t.Get<InventoryService>();
        long unit(string code) { using var db = t.Get<IAppDbFactory>().Create(); return db.Units.First(u => u.Code == code).Id; }
        var plywood = await mat.SaveAsync(new Material { Name = "Plywood Birch 4mm 122x244", Kind = MaterialKind.RawMaterial, UnitId = unit("SHEET"), Length = 244, Width = 122, Thickness = 4, PurchaseCost = 62 });
        await inv.OpeningBalanceAsync(plywood, it.Wh, 5, 62, day);
        var handle = await mat.SaveAsync(new Material { Name = "Brass handle", Kind = MaterialKind.PurchasedComponent, UnitId = unit("PCS"), PurchaseCost = 14 });
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Al Sultan Restaurant", CreditLimit = 20000, PaymentTermsDays = 30 });

        // the handle is bought for stock: Dr Inventory – purchased components / Cr GRNI, then Dr GRNI + input tax / Cr supplier
        var pur = t.Get<PurchaseService>();
        var po = new PurchaseOrder { SupplierId = it.Supplier, Date = day, ExpectedDate = day };
        po.Lines.Add(new PurchaseOrderLine { MaterialId = handle, Quantity = 20, UnitCost = 14, TaxRate = 15 });
        var poId = await pur.SaveOrderAsync(po);
        await pur.SetOrderStatusAsync(poId, PurchaseOrderStatus.Approved);
        var rec = await pur.ReceiptFromOrderAsync(poId);
        rec.WarehouseId = it.Wh; rec.Date = day;
        var compInvBefore = await Ledger.BalanceAsync(t, SystemAccounts.InventoryComponents);
        var apBefore = await Ledger.BalanceAsync(t, SystemAccounts.AP);
        var recId = await pur.PostReceiptAsync(rec);
        Assert.Equal(280m, await Ledger.BalanceAsync(t, SystemAccounts.InventoryComponents) - compInvBefore);
        await pur.PostSupplierInvoiceFromReceiptAsync(recId, "SI-5521", day);
        Assert.Equal(-322m, await Ledger.BalanceAsync(t, SystemAccounts.AP) - apBefore);           // 280 + 15 % VAT
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.GRNI));

        // acrylic offcut for the logo
        var remnantId = await inv.CreateRemnantAsync(new RemnantInput(it.Acrylic.Id, it.Wh, 40, 30, null, null, 12, day, "logo offcut"));

        // request → V1 → V2 → approval → estimate with five component lines
        var reqSvc = t.Get<RequestService>();
        var requestId = await reqSvc.SaveAsync(new CustomerRequest
        {
            CustomerId = customerId, RequestDate = day, Description = "Engraved wooden serving board with restaurant logo", Quantity = 10, RequiredDate = day.AddDays(15),
            Items =
            {
                new RequestItem { MaterialId = plywood, Quantity = 1 },
                new RequestItem { MaterialId = it.Acrylic.Id, Quantity = 1, Notes = "logo inlay" },
                new RequestItem { MaterialId = handle, Quantity = 1 },
                new RequestItem { MaterialId = it.Box, Quantity = 1 },
                new RequestItem { MaterialId = it.Tape, Quantity = 0.1m, Notes = "glue / tape" }
            }
        });
        var design = t.Get<DesignService>();
        var v1 = await design.SaveAsync(new DesignRevision { RequestId = requestId, Date = day, Width = 40, Height = 25, MaterialId = plywood, Thickness = 4, CuttingLengthM = 1.5m, EstimatedMachineMinutes = 8 });
        await design.RejectAsync(v1);
        var v2 = await design.SaveAsync(new DesignRevision { RequestId = requestId, Date = day, Width = 45, Height = 25, MaterialId = plywood, Thickness = 4, CuttingLengthM = 1.7m, EstimatedMachineMinutes = 9 });
        await design.ApproveAsync(v2);
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(customerId, requestId);
        Assert.Equal(5, est.MaterialLines.Count);
        var board = est.MaterialLines.Single(l => l.MaterialId == plywood);
        board.Pieces.Clear();
        board.Pieces.Add(new EstimatePiece { Name = "Board", Length = 45, Width = 25, QuantityPerUnit = 1 });
        var logo = est.MaterialLines.Single(l => l.MaterialId == it.Acrylic.Id);
        logo.Source = ComponentSource.Remnant; logo.RemnantId = remnantId; logo.UnitCost = 12;
        est.MachineLines.Clear();
        est.MachineLines.Add(new EstimateMachineLine { MachineId = it.MachineId, Operation = OperationType.Engraving, MinutesPerUnit = 9 });
        est.LaborLines.Add(new EstimateLaborLine { EmployeeId = it.OperatorId, Operation = OperationType.Assembly, MinutesPerUnit = 6, HourlyRate = 30 });
        est.ScrapAllowancePercent = 5; est.ReworkAllowancePercent = 3;
        var estimateId = await estSvc.SaveAsync(est);
        var saved = (await estSvc.GetAsync(estimateId))!;
        Assert.Equal(ComponentSource.Remnant, saved.MaterialLines.Single(l => l.MaterialId == it.Acrylic.Id).Source);
        Assert.Equal(12m, saved.MaterialLines.Single(l => l.MaterialId == it.Acrylic.Id).Cost);

        // quotation → approval → job
        var quotes = t.Get<QuotationService>();
        var q = await quotes.CreateFromEstimateAsync(estimateId);
        await quotes.MarkSentAsync(q);
        await quotes.ApproveAsync(q);
        var jobId = await quotes.CreateJobAsync(q, new JobCreationOptions(day.AddDays(10), JobPriority.Normal, it.MachineId, it.OperatorId));
        var jc = t.Get<JobComponentService>();
        var lines = await jc.ListAsync(jobId);
        Assert.Equal(5, lines.Count);

        // material issue, purchased component issue, remnant use: Dr WIP / Cr the right inventory account
        var rawBefore = await Ledger.BalanceAsync(t, SystemAccounts.Inventory);
        var compBefore = await Ledger.BalanceAsync(t, SystemAccounts.InventoryComponents);
        var remBefore = await Ledger.BalanceAsync(t, SystemAccounts.InventoryRemnants);
        var boardLine = lines.Single(l => l.MaterialId == plywood);
        await jc.IssueAsync(boardLine.Id, it.Wh, boardLine.PlannedQuantity, day);
        var handleLine = lines.Single(l => l.MaterialId == handle);
        await jc.IssueAsync(handleLine.Id, it.Wh, 10, day);
        await jc.UseRemnantAsync(lines.Single(l => l.Source == ComponentSource.Remnant).Id, remnantId, day);
        foreach (var l in lines.Where(l => l.MaterialId == it.Box || l.MaterialId == it.Tape)) await jc.IssueAsync(l.Id, it.Wh, l.PlannedQuantity, day);
        Assert.Equal(-boardLine.PlannedQuantity * 62, await Ledger.BalanceAsync(t, SystemAccounts.Inventory) - rawBefore);
        Assert.Equal(-140m, await Ledger.BalanceAsync(t, SystemAccounts.InventoryComponents) - compBefore);
        Assert.Equal(-12m, await Ledger.BalanceAsync(t, SystemAccounts.InventoryRemnants) - remBefore);
        Assert.Equal(140m, (await jc.ListAsync(jobId)).Single(l => l.Id == handleLine.Id).ActualCost);

        // machine operation and labor, scrap, rework, quality check, completion, delivery
        var prod = t.Get<ProductionService>();
        foreach (var o in (await prod.ListOperationsAsync(new PageRequest(PageSize: 50), jobId)).Items)
            await prod.CompleteOperationAsync(o.Id, new OperationCompletion(Math.Max(o.PlannedHours, 0.25m), o.OperationType is OperationType.Engraving or OperationType.Cutting ? 1.6m : 0, 10, EmployeeId: it.OperatorId));
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.NormalScrap, MaterialId = plywood, Quantity = 0.05m, Reason = "engraving test piece" });
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.Rework, MachineId = it.MachineId, EmployeeId = it.OperatorId, Quantity = 1, Hours = 0.2m, Reason = "re-engrave logo" });
        await prod.CompleteProductionAsync(jobId);
        await prod.RecordQualityCheckAsync(new QualityCheck { JobId = jobId, Date = day, QuantityProduced = 10, QuantityAccepted = 10, Status = QualityStatus.Passed });
        await t.Get<JobService>().DeliverAsync(jobId, day, "delivered to restaurant");
        var costing = t.Get<JobCostingService>();
        var before = await costing.CostSheetAsync(jobId);
        Assert.Equal(before.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));

        // invoice (Dr customer / Cr sales + output tax, Dr COGS / Cr WIP) and payment
        var sales = t.Get<SalesService>();
        var cogsBefore = await Ledger.BalanceAsync(t, SystemAccounts.COGS);
        var invoiceId = await sales.CreateInvoiceFromJobAsync(jobId);
        await sales.PostInvoiceAsync(invoiceId);
        var invoice = (await sales.GetInvoiceAsync(invoiceId))!;
        Assert.Equal(invoice.Total, await Ledger.BalanceAsync(t, SystemAccounts.AR, customerId: customerId));
        Assert.Equal(before.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.COGS) - cogsBefore);
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        await sales.RecordPaymentAsync(customerId, invoiceId, invoice.Total, PaymentMethod.Cash, day, null);
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.AR, customerId: customerId));

        // actual cost and profitability reconcile; every journal entry balances on its own
        var cs = await costing.CostSheetAsync(jobId);
        Assert.Equal(cs.ActualCost, cs.Components.Sum(c => c.Actual));
        Assert.Equal(saved.TotalCost, cs.Components.Sum(c => c.Estimated));
        Assert.Equal(invoice.Subtotal - invoice.DiscountAmount, cs.Revenue);
        Assert.Equal(cs.Revenue - cs.ActualCost, cs.GrossProfit);
        Assert.Equal(cs.Revenue == 0 ? 0 : Math.Round(cs.GrossProfit / cs.Revenue * 100m, 2), Math.Round(cs.MarginPercent, 2));
        await using (var db = t.Get<IAppDbFactory>().Create())
        {
            var entries = await db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft)
                .Select(e => new { e.Number, D = e.Lines.Sum(l => l.Debit), C = e.Lines.Sum(l => l.Credit) }).ToListAsync();
            Assert.NotEmpty(entries);
            Assert.All(entries, e => Assert.True(e.D == e.C, $"{e.Number}: {e.D} ≠ {e.C}"));
            Assert.Equal(0, await db.InventoryTransactions.CountAsync(x => x.JobId == jobId && x.JobComponentId == null));
            Assert.All(await db.InventoryTransactions.AsNoTracking().Where(x => x.JobId == jobId).ToListAsync(),
                x => Assert.True(x.WarehouseId > 0 && x.CreatedBy != null && x.Date == day && x.TotalCost == Money.Round(x.Quantity * x.UnitCost) || x.RemnantId != null, $"tx {x.Number}"));
        }
        await Ledger.AssertBooksBalanceAsync(t);
        _out.WriteLine($"serving board: estimate {saved.TotalCost}, actual {cs.ActualCost}, revenue {cs.Revenue}, profit {cs.GrossProfit} ({cs.MarginPercent:0.##}%)");
    }

    /// <summary>A job with no component lines at all (labor/machine only) still costs, reconciles and reports.</summary>
    [Fact]
    public async Task Job_without_components_and_manual_lines()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Walk-in" });
        var jobId = await t.Get<JobService>().SaveAsync(new Job { CustomerId = customerId, Title = "Engrave customer's own wood", Quantity = 1, EstimatedCost = 80, SellingPrice = 150, OrderDate = day });
        var jc = t.Get<JobComponentService>();
        Assert.Empty(await jc.ListAsync(jobId));
        var cs = await t.Get<JobCostingService>().CostSheetAsync(jobId);
        Assert.DoesNotContain(cs.Components, c => c.IsLine);
        Assert.Equal(80m, cs.Components.Sum(c => c.Estimated));
        Assert.Equal(0m, cs.Components.Sum(c => c.Actual));

        // one line, added later; editing it before any cost is allowed, afterwards its identity is locked
        var lineId = await jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.Inventory, MaterialId = it.Screws, PlannedQuantity = 4, EstimatedUnitCost = 2.5m });
        var line = (await jc.ListAsync(jobId)).Single();
        Assert.Equal(ComponentCategory.PurchasedComponent, line.Category);
        Assert.Equal(10m, line.EstimatedCost);
        await jc.IssueAsync(lineId, it.Wh, 4, day);
        var ex = await Assert.ThrowsAsync<DomainException>(() => jc.SaveAsync(new JobComponent { Id = lineId, JobId = jobId, Source = ComponentSource.Inventory, MaterialId = it.Adapter, PlannedQuantity = 4 }));
        Assert.Equal("Err.ComponentHasCost", ex.Code);
        ex = await Assert.ThrowsAsync<DomainException>(() => jc.DeleteAsync(lineId));
        Assert.Equal("Err.ComponentHasCost", ex.Code);
        // quantity/notes may still change
        await jc.SaveAsync(new JobComponent { Id = lineId, JobId = jobId, Source = ComponentSource.Inventory, MaterialId = it.Screws, PlannedQuantity = 6, EstimatedUnitCost = 2.5m, Notes = "two spare" });
        // return 1 → used 3
        await jc.ReturnAsync(lineId, it.Wh, 1, day);
        line = (await jc.ListAsync(jobId)).Single();
        Assert.Equal(3m, line.UsedQuantity);
        Assert.Equal(7.5m, line.ActualCost);
        // an issue without a line lands on a new "unplanned" line; nothing is ever unlinked
        await t.Get<InventoryService>().IssueToJobAsync(jobId, it.Tape, it.Wh, 1, day, null);
        var all = await jc.ListAsync(jobId);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, l => l.MaterialId == it.Tape && l.Category == ComponentCategory.Consumable && l.ActualCost == 22m);
        Assert.Equal(0, await CountAsync(t, db => db.InventoryTransactions.Where(x => x.JobId == jobId && x.JobComponentId == null)));
        // a duplicate carries no cost and can be deleted
        var dup = await jc.DuplicateAsync(lineId);
        Assert.Equal(0m, (await jc.ListAsync(jobId)).Single(l => l.Id == dup).ActualCost);
        await jc.DeleteAsync(dup);
        await Ledger.AssertBooksBalanceAsync(t);
    }

    /// <summary>Rules that keep the component model consistent.</summary>
    [Fact]
    public async Task Component_rules_are_enforced()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var inv = t.Get<InventoryService>();
        // a service is never stocked
        var ex = await Assert.ThrowsAsync<DomainException>(() => inv.OpeningBalanceAsync(it.UvService, it.Wh, 1, 10, day));
        Assert.Equal("Err.ServiceNotStocked", ex.Code);
        // a remnant is only made of raw material
        ex = await Assert.ThrowsAsync<DomainException>(() => inv.CreateRemnantAsync(new RemnantInput(it.Box, it.Wh, 10, 10, null, null, 1, day, null)));
        Assert.Equal("Err.RemnantNeedsRawMaterial", ex.Code);
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Rules" });
        var jobId = await t.Get<JobService>().SaveAsync(new Job { CustomerId = customerId, Title = "Rules", Quantity = 1, OrderDate = day });
        var jc = t.Get<JobComponentService>();
        // a service can't be an inventory line; a remnant line needs raw material; a free-text line needs a description
        ex = await Assert.ThrowsAsync<DomainException>(() => jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.Inventory, MaterialId = it.UvService, PlannedQuantity = 1 }));
        Assert.Equal("Err.ServiceNotStocked", ex.Code);
        ex = await Assert.ThrowsAsync<DomainException>(() => jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.Remnant, MaterialId = it.Led, PlannedQuantity = 1 }));
        Assert.Equal("Err.RemnantNeedsRawMaterial", ex.Code);
        await Assert.ThrowsAsync<DomainException>(() => jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.ManualCost, PlannedQuantity = 1 }));
        // credit purchase needs a supplier
        var direct = await jc.SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.DirectPurchase, Category = ComponentCategory.PurchasedComponent, Description = "Special hinge", PlannedQuantity = 2, EstimatedUnitCost = 15 });
        ex = await Assert.ThrowsAsync<DomainException>(() => jc.RecordDirectCostAsync(new DirectCostInput(direct, day, 2, 30, 0, PaymentMethod.OnCredit, null, null, null)));
        Assert.Equal("Err.SupplierRequiredForCredit", ex.Code);
        // a direct cost can be reversed: the line and WIP go back to zero, the original entry stays
        var entry = await jc.RecordDirectCostAsync(new DirectCostInput(direct, day, 2, 30, 4.5m, PaymentMethod.OnCredit, it.Supplier, "H-1", null));
        Assert.Equal(30m, (await jc.ListAsync(jobId)).Single(l => l.Id == direct).ActualCost);
        await jc.ReverseDirectCostAsync(entry, day, "wrong supplier invoice");
        Assert.Equal(0m, (await jc.ListAsync(jobId)).Single(l => l.Id == direct).ActualCost);
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        await Assert.ThrowsAsync<DomainException>(() => jc.ReverseDirectCostAsync(entry, day, "again"));
        Assert.Equal(2, await CountAsync(t, db => db.JobCostEntries.Where(e => e.JobComponentId == direct)));
        await Ledger.AssertBooksBalanceAsync(t);
    }

    /// <summary>Twenty component lines of every category and source through estimate, job, issue and reports.</summary>
    [Fact]
    public async Task Twenty_component_lines()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Mall Signage Co." });
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(customerId);
        est.Description = "Directory board with 20 components";
        est.Quantity = 3;
        est.MaterialLines.Clear();
        var stock = new[] { it.Led, it.Adapter, it.Screws, it.Tape, it.Box };
        for (var i = 0; i < 20; i++)
        {
            est.MaterialLines.Add((i % 4) switch
            {
                0 => new EstimateMaterialLine { Category = ComponentCategory.RawMaterial, Source = ComponentSource.Inventory, MaterialId = it.Acrylic.Id, SheetBased = false, QuantityPerUnit = 0.25m, UnitCost = it.Acrylic.AverageCost },
                1 => new EstimateMaterialLine { Source = ComponentSource.Inventory, MaterialId = stock[i % 5], Category = i % 5 == 3 ? ComponentCategory.Consumable : i % 5 == 4 ? ComponentCategory.Packaging : ComponentCategory.PurchasedComponent, QuantityPerUnit = 1, UnitCost = 5, SheetBased = false },
                2 => new EstimateMaterialLine { Category = ComponentCategory.PurchasedComponent, Source = ComponentSource.DirectPurchase, Description = $"Bought part {i}", QuantityPerUnit = 2, UnitCost = 3, SheetBased = false },
                _ => new EstimateMaterialLine { Category = ComponentCategory.ExternalService, Source = ComponentSource.ExternalService, Description = $"Outsourced step {i}", QuantityPerUnit = 1, UnitCost = 10, SheetBased = false }
            });
        }
        var id = await estSvc.SaveAsync(est);
        var saved = (await estSvc.GetAsync(id))!;
        Assert.Equal(20, saved.MaterialLines.Count);
        Assert.Equal(Enumerable.Range(1, 20), saved.MaterialLines.OrderBy(l => l.LineNo).Select(l => l.LineNo));
        Assert.Equal(saved.MaterialLines.Where(l => l.Category == ComponentCategory.ExternalService).Sum(l => l.Cost),
            saved.Components.Single(c => c.Component == CostComponent.ExternalServices).Amount);
        // duplicate keeps every line
        var copy = (await estSvc.GetAsync(await estSvc.DuplicateAsync(id)))!;
        Assert.Equal(saved.MaterialLines.Select(l => (l.LineNo, l.Category, l.Source, l.MaterialId, l.Cost)), copy.MaterialLines.OrderBy(l => l.LineNo).Select(l => (l.LineNo, l.Category, l.Source, l.MaterialId, l.Cost)));

        var quotes = t.Get<QuotationService>();
        var q = await quotes.CreateFromEstimateAsync(id);
        await quotes.MarkSentAsync(q);
        await quotes.ApproveAsync(q);
        var jobId = await quotes.CreateJobAsync(q, new JobCreationOptions(day.AddDays(5), JobPriority.Normal, it.MachineId, it.OperatorId));
        var jc = t.Get<JobComponentService>();
        var lines = await jc.ListAsync(jobId);
        Assert.Equal(20, lines.Count);
        foreach (var l in lines)
        {
            if (l.IsStocked) await jc.IssueAsync(l.Id, it.Wh, l.PlannedQuantity, day);
            else await jc.RecordDirectCostAsync(new DirectCostInput(l.Id, day, l.PlannedQuantity, l.EstimatedCost, 0, PaymentMethod.Cash, null, null, null));
        }
        var cs = await t.Get<JobCostingService>().CostSheetAsync(jobId);
        Assert.Equal(20, cs.Components.Count(c => c.IsLine));
        Assert.Equal(cs.ActualCost, cs.Components.Sum(c => c.Actual));
        Assert.Equal(cs.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));
        var rep = await t.Get<ReportCatalog>().RunAsync("JobComponents", new ReportFilter(new DateRange(day, day), day, JobId: jobId));
        Assert.Equal(20, rep.Rows.Count(r => r.Style is not (RowStyle.Subtotal or RowStyle.Header)));
        await Ledger.AssertBooksBalanceAsync(t);
    }

    /// <summary>Product templates: an estimate or a job saved as a template starts a new estimate with the same bill of materials.</summary>
    [Fact]
    public async Task Product_templates_from_estimate_and_job()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var it = await SetupItemsAsync(t);
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Repeat Customer" });
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(customerId);
        est.Description = "Name plate kit"; est.Quantity = 10;
        est.MaterialLines.Clear();
        est.MaterialLines.Add(new EstimateMaterialLine { Category = ComponentCategory.RawMaterial, Source = ComponentSource.Inventory, MaterialId = it.Acrylic.Id, SheetBased = true,
            SheetLength = 244, SheetWidth = 122, Spacing = 0.5m, NestingEfficiency = 85, ChargeFullSheets = true, UnitCost = it.Acrylic.AverageCost,
            Pieces = { new EstimatePiece { Name = "Plate", Length = 30, Width = 10, QuantityPerUnit = 1 } } });
        est.MaterialLines.Add(new EstimateMaterialLine { Category = ComponentCategory.PurchasedComponent, Source = ComponentSource.Inventory, MaterialId = it.Screws, QuantityPerUnit = 2, UnitCost = 2.5m, SheetBased = false });
        est.MaterialLines.Add(new EstimateMaterialLine { Category = ComponentCategory.Packaging, Source = ComponentSource.Inventory, MaterialId = it.Box, QuantityPerUnit = 0.1m, UnitCost = 9, SheetBased = false });
        est.MaterialLines.Add(new EstimateMaterialLine { Category = ComponentCategory.ExternalService, Source = ComponentSource.ExternalService, MaterialId = it.UvService, QuantityPerUnit = 1, UnitCost = 12, SheetBased = false });
        var id = await estSvc.SaveAsync(est);
        var saved = (await estSvc.GetAsync(id))!;

        var tpl = t.Get<ProductTemplateService>();
        var tplId = await tpl.SaveFromEstimateAsync(id, "Name plate kit");
        var row = (await tpl.ListAsync()).Single(r => r.Id == tplId);
        Assert.Equal(4, row.Lines);
        Assert.StartsWith("TPL-", row.Code);
        var fromTpl = await tpl.NewEstimateAsync(tplId, customerId, estSvc);
        var newId = await estSvc.SaveAsync(fromTpl);
        var again = (await estSvc.GetAsync(newId))!;
        Assert.Equal(saved.MaterialLines.OrderBy(l => l.LineNo).Select(l => (l.Category, l.Source, l.MaterialId, l.Cost)),
            again.MaterialLines.OrderBy(l => l.LineNo).Select(l => (l.Category, l.Source, l.MaterialId, l.Cost)));
        Assert.Equal(saved.MaterialLines.Sum(l => l.Cost), again.MaterialLines.Sum(l => l.Cost));

        // save a job (with a line added during production) as a template
        var quotes = t.Get<QuotationService>();
        var q = await quotes.CreateFromEstimateAsync(id);
        await quotes.MarkSentAsync(q);
        await quotes.ApproveAsync(q);
        var jobId = await quotes.CreateJobAsync(q, new JobCreationOptions(day.AddDays(5), JobPriority.Normal, it.MachineId, it.OperatorId));
        await t.Get<JobComponentService>().SaveAsync(new JobComponent { JobId = jobId, Source = ComponentSource.Inventory, MaterialId = it.Tape, PlannedQuantity = 1, EstimatedUnitCost = 22 });
        var jobTpl = await tpl.SaveFromJobAsync(jobId, "Name plate kit v2");
        Assert.Equal(5, (await tpl.ListAsync()).Single(r => r.Id == jobTpl).Lines);
        var fromJob = await tpl.NewEstimateAsync(jobTpl, customerId, estSvc);
        Assert.Equal(5, fromJob.MaterialLines.Count);
        Assert.Contains(fromJob.MaterialLines, l => l.MaterialId == it.Tape && l.QuantityPerUnit == 0.1m);
        Assert.True(fromJob.MaterialLines.Single(l => l.MaterialId == it.Acrylic.Id).SheetBased);
        await tpl.DeleteAsync(tplId);
        Assert.DoesNotContain(await tpl.ListAsync(), r => r.Id == tplId);
    }
}
