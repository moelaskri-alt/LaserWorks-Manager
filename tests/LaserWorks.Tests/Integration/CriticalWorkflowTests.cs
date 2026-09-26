using ClosedXML.Excel;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Spec §43 — the 32-step critical scenario, run against the real services and a real SQLite database.
/// (The same scenario is driven through the desktop view models in Ui/UiCriticalWorkflowTests.)
/// </summary>
public class CriticalWorkflowTests
{
    private readonly ITestOutputHelper _out;
    public CriticalWorkflowTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Request_to_profit_end_to_end()
    {
        await using var t = await TestDb.CreateAsync();
        var day = t.Clock.Now.Date;
        var materials = await t.Get<MaterialService>().LookupAsync();
        var acrylic = materials.First(m => m.Name.StartsWith("Acrylic"));
        var machine = (await t.Get<MachineService>().LookupAsync()).Single();
        var wh = (await t.Get<SettingsService>().GetAsync()).DefaultWarehouseId!.Value;
        var designer = await t.Get<EmployeeService>().SaveAsync(new Employee { Name = "Sara Designer", Role = "Designer", HourlyCost = 45 });
        var operatorId = await t.Get<EmployeeService>().SaveAsync(new Employee { Name = "Khalid Operator", Role = "Laser operator", HourlyCost = 30 });
        var stockBefore = (await t.Get<MaterialService>().GetAsync(acrylic.Id))!.QuantityOnHand;

        // 1. customer
        var customerId = await t.Get<CustomerService>().SaveAsync(new Customer { Name = "Al Noor Signs", Phone = "0550000000", CreditLimit = 50000, PaymentTermsDays = 30 });
        // 2. request
        var reqSvc = t.Get<RequestService>();
        var requestId = await reqSvc.SaveAsync(new CustomerRequest
        {
            CustomerId = customerId, RequestDate = day, Description = "Acrylic name plates 30×20 cm", Quantity = 20, RequiredDate = day.AddDays(14),
            Items = { new RequestItem { LineNo = 1, Category = ComponentCategory.RawMaterial, MaterialId = acrylic.Id, Quantity = 1 } }
        });
        // 3. reference file
        var file = Path.Combine(t.Folder, "reference.svg");
        await File.WriteAllTextAsync(file, "<svg xmlns='http://www.w3.org/2000/svg'><rect width='30' height='20'/></svg>");
        await reqSvc.AddAttachmentAsync(AttachmentOwner.Request, requestId, file);
        var attachments = await reqSvc.AttachmentsAsync(AttachmentOwner.Request, requestId);
        Assert.Single(attachments);
        // 4–5. design revision approved as final
        var design = t.Get<DesignService>();
        var revId = await design.SaveAsync(new DesignRevision { RequestId = requestId, Date = day, DesignerId = designer, Width = 30, Height = 20, MaterialId = acrylic.Id, Thickness = 3, CuttingLengthM = 2, EstimatedMachineMinutes = 6 });
        await design.ApproveAsync(revId);
        Assert.Equal(RevisionStatus.Approved, (await design.GetAsync(revId))!.Status);

        // 6–11. estimate: material usage, machine, labor, overhead, total
        var estSvc = t.Get<EstimateService>();
        var est = await estSvc.NewDraftAsync(customerId, requestId);
        var ml = est.MaterialLines.Single();
        Assert.Equal(acrylic.Id, ml.MaterialId);
        Assert.True(ml.SheetBased);
        ml.Pieces.Clear();
        ml.Pieces.Add(new EstimatePiece { Name = "Plate", Length = 30, Width = 20, QuantityPerUnit = 1 });
        est.MachineLines.Clear();
        est.MachineLines.Add(new EstimateMachineLine { MachineId = machine.Id, Operation = OperationType.Cutting, MinutesPerUnit = 6 });
        est.LaborLines.Add(new EstimateLaborLine { EmployeeId = operatorId, Operation = OperationType.Finishing, MinutesPerUnit = 3, HourlyRate = 30 });
        est.DesignHours = 1; est.DesignRate = 45; est.SetupHours = 0.5m; est.SetupRate = 30;
        var calc = await estSvc.CalculateAsync(est);
        Assert.Equal(145m, calc[CostComponent.Material]);             // one 122×244 sheet
        Assert.Equal(2m, calc.TotalMachineHours);                     // 6 min × 20
        Assert.True(calc[CostComponent.Machine] > 0);
        Assert.Equal(30m, calc[CostComponent.Labor]);                 // 3 min × 20 = 1 h × 30
        Assert.True(calc.Overhead > 0);
        Assert.Equal(calc.DirectCost + calc.Overhead, calc.TotalCost);
        var estimateId = await estSvc.SaveAsync(est);
        var savedEstimate = (await estSvc.GetAsync(estimateId))!;
        Assert.Equal(calc.TotalCost, savedEstimate.TotalCost);
        Assert.True(savedEstimate.SellingPrice > savedEstimate.TotalCost);

        // 12–14. quotation → approved → job
        var quotes = t.Get<QuotationService>();
        var quoteId = await quotes.CreateFromEstimateAsync(estimateId);
        await quotes.MarkSentAsync(quoteId);
        await quotes.ApproveAsync(quoteId);
        var jobId = await quotes.CreateJobAsync(quoteId, new JobCreationOptions(day.AddDays(10), JobPriority.Normal, machine.Id, operatorId));
        var job = (await t.Get<JobService>().GetAsync(jobId))!;
        Assert.Equal(savedEstimate.TotalCost, job.EstimatedCost);
        Assert.Equal(RequestStatus.ConvertedToJob, (await reqSvc.GetAsync(requestId))!.Status);

        // 15. issue material
        var inv = t.Get<InventoryService>();
        await inv.IssueToJobAsync(jobId, acrylic.Id, wh, 1, day, "cutting");
        // 16–17. machine time and labor via the job operations
        var prod = t.Get<ProductionService>();
        var ops = (await prod.ListOperationsAsync(new PageRequest(PageSize: 50), jobId)).Items;
        Assert.Equal(4, ops.Count); // design, preparation, cutting, finishing
        foreach (var o in ops)
        {
            var isMachine = o.OperationType == OperationType.Cutting;
            await prod.CompleteOperationAsync(o.Id, new OperationCompletion(isMachine ? 2.2m : o.PlannedHours, isMachine ? 2.5m : 0, 20,
                EmployeeId: o.OperationType == OperationType.Design ? designer : operatorId));
        }
        // 18. scrap (normal scrap: reclassifies part of the material cost)
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.NormalScrap, MaterialId = acrylic.Id, Quantity = 0.1m, Reason = "burn marks" });
        // 19. rework (adds machine + labor cost)
        await prod.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = day, Type = ScrapType.Rework, MachineId = machine.Id, EmployeeId = operatorId, Quantity = 2, Hours = 0.5m, Reason = "re-engrave" });
        // 20–22. complete, quality check, deliver
        await prod.CompleteProductionAsync(jobId);
        await prod.RecordQualityCheckAsync(new QualityCheck { JobId = jobId, Date = day, QuantityProduced = 20, QuantityAccepted = 20, Status = QualityStatus.Passed });
        await t.Get<JobService>().DeliverAsync(jobId, day, "handed over");
        Assert.Equal(JobStatus.Delivered, (await t.Get<JobService>().GetAsync(jobId))!.Status);

        // WIP before invoicing equals the accumulated job cost
        var sheetBefore = await t.Get<JobCostingService>().CostSheetAsync(jobId);
        Assert.Equal(sheetBefore.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));

        // 23–24. invoice and payment
        var sales = t.Get<SalesService>();
        var invoiceId = await sales.CreateInvoiceFromJobAsync(jobId);
        await sales.PostInvoiceAsync(invoiceId);
        var invoice = (await sales.GetInvoiceAsync(invoiceId))!;
        Assert.Equal(job.SellingPrice, invoice.Subtotal - invoice.DiscountAmount);
        Assert.Equal(Money.Round(invoice.Subtotal - invoice.DiscountAmount) * 0.15m, invoice.TaxAmount, 2);
        await sales.RecordPaymentAsync(customerId, invoiceId, invoice.Total, PaymentMethod.Bank, day, "TRF-1");

        // 25–27. actual cost, estimated vs actual, gross profit
        var sheet = await t.Get<JobCostingService>().CostSheetAsync(jobId);
        Assert.Equal(sheet.Entries.Sum(e => e.Amount), sheet.ActualCost);
        Assert.Contains(sheet.Entries, e => e.Component == CostComponent.Scrap && e.Amount > 0);
        Assert.Contains(sheet.Entries, e => e.Component == CostComponent.Rework && e.Amount > 0);
        Assert.Equal(savedEstimate.TotalCost, sheet.Variance.EstimatedTotal);
        Assert.Equal(sheet.ActualCost, sheet.Variance.ActualTotal);
        Assert.NotNull(sheet.Variance.MainDriver);
        Assert.Equal(job.SellingPrice, sheet.Revenue);
        Assert.Equal(sheet.Revenue - sheet.ActualCost, sheet.GrossProfit);
        var profit = (await t.Get<JobCostingService>().JobProfitabilityAsync()).Single(r => r.JobId == jobId);
        Assert.Equal(sheet.GrossProfit, profit.GrossProfit);

        // 28–30. job cost sheet report, PDF, Excel
        var company = await t.Get<SettingsService>().GetAsync();
        var pdf = DocumentRenderer.JobCostSheet(sheet, company);
        Assert.True(pdf.Length > 2000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var catalog = t.Get<ReportCatalog>();
        var table = await catalog.RunAsync("JobCostSheet", new ReportFilter(new DateRange(day.AddDays(-30), day), day, JobId: jobId));
        Assert.NotEmpty(table.Rows);
        var xlsx = Path.Combine(t.Folder, "costsheet.xlsx");
        ExcelExporter.Export(table, company, xlsx);
        using (var wb = new XLWorkbook(xlsx)) Assert.True(wb.Worksheet(1).LastRowUsed()!.RowNumber() > 3);
        var reportPdf = PdfExporter.Render(table, company);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(reportPdf, 0, 4));

        // 31. accounting entries
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.WIP, jobId));                   // all cost moved out of WIP
        Assert.Equal(sheet.ActualCost, await Ledger.BalanceAsync(t, SystemAccounts.COGS, jobId));      // … into COGS
        Assert.Equal(-job.SellingPrice, await Ledger.BalanceAsync(t, SystemAccounts.Sales, customerId: customerId));
        Assert.Equal(-invoice.TaxAmount, await Ledger.BalanceAsync(t, SystemAccounts.OutputTax));
        Assert.Equal(0m, await Ledger.BalanceAsync(t, SystemAccounts.AR, customerId: customerId));    // fully paid
        Assert.Equal(100000m + invoice.Total, await Ledger.BalanceAsync(t, SystemAccounts.Bank));
        await Ledger.AssertBooksBalanceAsync(t);

        // 32. inventory movements
        var txs = (await inv.ListTransactionsAsync(new PageRequest(PageSize: 50), jobId: jobId)).Items;
        var issue = Assert.Single(txs, x => x.Type == InventoryTxType.MaterialIssue);
        Assert.Equal(-1m, issue.Quantity);
        Assert.Equal(145m, issue.UnitCost);
        var material = (await t.Get<MaterialService>().GetAsync(acrylic.Id))!;
        Assert.Equal(stockBefore - 1, material.QuantityOnHand);
        Assert.Equal(material.StockValue, await Ledger.BalanceAsync(t, SystemAccounts.Inventory) - await OtherRawMaterialValue(t, acrylic.Id));

        _out.WriteLine($"estimated {savedEstimate.TotalCost}, actual {sheet.ActualCost}, revenue {sheet.Revenue}, gross profit {sheet.GrossProfit} ({sheet.MarginPercent}%)");
    }

    private static async Task<decimal> OtherRawMaterialValue(TestDb t, long exceptMaterialId)
    {
        await using var db = t.Get<IAppDbFactory>().Create();
        var vals = await db.Materials.Where(m => m.Id != exceptMaterialId && m.Kind == MaterialKind.RawMaterial).Select(m => m.StockValue).ToListAsync();
        return vals.Sum();
    }
}
