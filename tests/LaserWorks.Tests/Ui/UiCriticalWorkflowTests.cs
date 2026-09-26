using Avalonia.Headless.XUnit;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Desktop;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Ui;

/// <summary>
/// Spec §43 driven through the desktop application: every step is performed with the same view-model commands
/// the buttons invoke, dialogs are filled in and saved, and the rendered window is captured along the way.
/// Native file pickers are replaced by fixed paths (DialogService overrides) because a headless session has none.
/// </summary>
public class UiCriticalWorkflowTests
{
    private readonly ITestOutputHelper _out;
    public UiCriticalWorkflowTests(ITestOutputHelper output) => _out = output;

    private static DialogService Dialogs => ViewModelBase.Get<DialogService>();
    private static T S<T>() where T : notnull => App.Services.GetRequiredService<T>();

    private static async Task<T> OpenDialog<T>(System.Windows.Input.ICommand command, object? parameter = null) where T : ViewModelBase
    {
        var before = Dialogs.Stack.Count;
        command.Execute(parameter);
        await UiSession.SettleAsync();
        Assert.True(Dialogs.Stack.Count > before, $"no dialog opened for {typeof(T).Name}");
        return Assert.IsType<T>(Dialogs.Stack[^1]);
    }

    private static async Task SaveDialog(DialogViewModel d, string step)
    {
        await d.SaveCommand.ExecuteAsync(null);
        await UiSession.SettleAsync();
        Assert.True(d.ErrorMessage == null, $"{step}: {d.ErrorMessage}");
        Assert.DoesNotContain(d, Dialogs.Stack);
    }

    private static async Task Run(IAsyncRelayCommand command, ViewModelBase vm, string step, object? parameter = null)
    {
        await command.ExecuteAsync(parameter);
        await UiSession.SettleAsync();
        Assert.True(vm.ErrorMessage == null, $"{step}: {vm.ErrorMessage}");
    }

    [AvaloniaFact]
    public async Task Request_to_profit_through_the_user_interface()
    {
        await UiSession.EnsureStartedAsync();
        App.ApplyLanguage("en");
        App.ApplyTheme("Light");
        var work = Path.Combine(Path.GetTempPath(), "laserworks-tests", "ui-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var reference = Path.Combine(work, "customer-logo.svg");
        await File.WriteAllTextAsync(reference, "<svg xmlns='http://www.w3.org/2000/svg'><circle r='10'/></svg>");
        Dialogs.OpenFileOverride = () => reference;
        Dialogs.SaveFileOverride = name => Path.Combine(work, name);
        Dialogs.ConfirmOverride = _ => true;
        var shot = 0;
        void Shot(string name) => UiSession.Capture($"e2e-{++shot:00}-{name}");
        try
        {
            // 1. create customer
            UiSession.Shell.NavigateTo("Customers");
            await UiSession.SettleAsync();
            var customers = (CustomersViewModel)UiSession.Shell.CurrentPage!;
            var ce = await OpenDialog<CustomerEditorViewModel>(customers.NewCommand);
            ce.Name = "UI Workflow Trading";
            ce.Phone = "0551112233";
            ce.CreditLimit = 20000;
            ce.PaymentTermsDays = 30;
            Shot("customer");
            await SaveDialog(ce, "1 customer");
            var customer = (await S<CustomerService>().LookupAsync()).Single(c => c.Name == "UI Workflow Trading");

            // 2–5. request, attachment, design revision, approval
            UiSession.Shell.NavigateTo("Requests");
            await UiSession.SettleAsync();
            var requests = (RequestsViewModel)UiSession.Shell.CurrentPage!;
            var re = await OpenDialog<RequestEditorViewModel>(requests.NewCommand);
            re.Customer = re.Customers.Single(c => c.Id == customer.Id);
            re.Description = "Engraved acrylic door signs";
            re.AddItemCommand.Execute(null);
            re.Items[0].Material = re.Materials.First(m => m.Code == "MAT-00001");
            re.Quantity = 10;
            re.Dimensions = "30 × 15 cm";
            await Run(re.ApplyCommand, re, "2 request");
            Assert.True(re.IsSaved);
            await Run(re.AddAttachmentCommand, re, "3 attachment");
            Assert.Single(re.Attachments);
            var rev = await OpenDialog<RevisionEditorViewModel>(re.NewRevisionCommand);
            rev.Width = 30; rev.Height = 15; rev.CuttingLengthM = 1; rev.EstimatedMachineMinutes = 5;
            rev.Designer = rev.Designers.FirstOrDefault();
            await SaveDialog(rev, "4 revision");
            Assert.Single(re.Revisions);
            await Run(re.ApproveRevisionCommand, re, "5 approve", re.Revisions[0]);
            Assert.Equal(RevisionStatus.Approved, re.Revisions[0].Status);
            Shot("request");

            // 6–11. estimate
            re.CreateEstimateCommand.Execute(null);
            await UiSession.SettleAsync();
            var est = Assert.IsType<EstimateEditorViewModel>(UiSession.Shell.CurrentPage);
            var ml = Assert.Single(est.MaterialLines);
            Assert.NotNull(ml.Material);                                     // 6. material from the request
            ml.Pieces.Clear();
            ml.AddPieceCommand.Execute(null);
            ml.Pieces[0].Name = "Sign"; ml.Pieces[0].Length = 30; ml.Pieces[0].Width = 15; ml.Pieces[0].QuantityPerUnit = 1;
            est.Quantity = 10;
            if (est.MachineLines.Count == 0) est.AddMachineCommand.Execute(null);
            est.MachineLines[0].MinutesPerUnit = 5;
            if (est.LaborLines.Count == 0) est.AddLaborCommand.Execute(null);
            est.LaborLines[0].MinutesPerUnit = 2;
            est.DesignHours = 1; est.SetupHours = 0.25m;
            await Task.Delay(400); // the editor recalculates 150 ms after the last change
            await UiSession.SettleAsync();
            Assert.True(ml.SheetsRequired >= 1 && ml.Cost > 0, "7. material usage");
            Assert.True(est.MachineLines[0].Cost > 0, "8. machine cost");
            Assert.True(est.LaborLines[0].Cost > 0, $"9. labor: emp={est.LaborLines[0].Employee?.Name} rate={est.LaborLines[0].HourlyRate} hours={est.LaborLines[0].Hours} min={est.LaborLines[0].MinutesPerUnit} n={est.LaborLines.Count}");
            Assert.True(est.TotalCost > est.DirectCost, "10. overhead");
            Assert.True(est.TotalCost > 0 && est.SuggestedPrice > est.TotalCost, "11. total & price");
            Assert.Equal(est.SuggestedPrice, est.SellingPrice); // an untouched new estimate is priced at the suggestion
            Assert.False(est.BelowMinimum);
            Shot("estimate");
            await Run(est.SaveCommand, est, "11 save estimate");

            // 12–14. quotation, approval, job
            est.CreateQuotationCommand.Execute(null);
            await UiSession.SettleAsync();
            Assert.True(est.ErrorMessage == null, est.ErrorMessage);
            var q = Assert.IsType<QuotationEditorViewModel>(Dialogs.Stack[^1]);
            Shot("quotation");
            await Run(q.MarkSentCommand, q, "12 send quotation");
            await Run(q.ApproveCommand, q, "13 approve");
            Assert.Equal(QuotationStatus.Approved, q.Status);
            await Run(q.CreateJobCommand, q, "14 create job");
            Assert.NotNull(q.JobId);
            q.OpenJobCommand.Execute(null);
            await UiSession.SettleAsync();
            var job = Assert.IsType<JobDetailViewModel>(UiSession.Shell.CurrentPage);
            var jobId = q.JobId!.Value;

            // 15. issue material
            var issue = await OpenDialog<StockMovementDialogViewModel>(job.IssueMaterialCommand);
            issue.Material ??= issue.Materials.First(m => m.Code == "MAT-00001");
            issue.Warehouse ??= issue.Warehouses.First();
            issue.Quantity = 1;
            await SaveDialog(issue, "15 issue material");

            // 16–17. machine time and labor: complete every operation
            foreach (var op in job.Sheet!.Operations.ToList())
            {
                var row = job.Sheet!.Operations.Single(o => o.Id == op.Id);
                var done = await OpenDialog<CompleteOperationDialogViewModel>(job.CompleteOperationCommand, row);
                done.LaborHours = row.PlannedHours > 0 ? row.PlannedHours : 0.5m;
                if (row.OperationType is OperationType.Cutting or OperationType.Engraving) done.MachineHours = row.PlannedHours * 1.2m;
                done.Employee ??= done.Employees.FirstOrDefault();
                await SaveDialog(done, $"16/17 complete {row.OperationType}");
            }
            Assert.All(job.Sheet!.Operations, o => Assert.Equal(OperationStatus.Done, o.Status));

            // 18. scrap
            var scrap = await OpenDialog<ScrapDialogViewModel>(job.RecordScrapCommand);
            scrap.Type = ScrapType.NormalScrap;
            scrap.Material ??= scrap.Materials.First(m => m.Code == "MAT-00001");
            scrap.Quantity = 0.1m;
            scrap.Reason = "burn marks";
            await SaveDialog(scrap, "18 scrap");
            // 19. rework
            var rework = await OpenDialog<ScrapDialogViewModel>(job.RecordReworkCommand);
            rework.Machine ??= rework.Machines.FirstOrDefault();
            rework.Employee ??= rework.Employees.FirstOrDefault();
            rework.Quantity = 1; rework.Hours = 0.25m; rework.Reason = "re-engrave one sign";
            await SaveDialog(rework, "19 rework");

            // 20–22. complete production, quality check, deliver
            await Run(job.CompleteProductionCommand, job, "20 complete production");
            var qc = await OpenDialog<QualityCheckDialogViewModel>(job.QualityCheckCommand);
            qc.QuantityProduced = 10; qc.QuantityAccepted = 10; qc.QuantityRejected = 0; qc.Status = QualityStatus.Passed;
            await SaveDialog(qc, "21 quality check");
            var deliver = await OpenDialog<DeliverDialogViewModel>(job.DeliverCommand);
            deliver.Note = "Collected by customer";
            await SaveDialog(deliver, "22 deliver");
            Assert.Equal(JobStatus.Delivered, job.Job!.Status);
            Shot("job-delivered");

            // 23–24. invoice and payment
            var invoice = await OpenDialog<InvoiceEditorViewModel>(job.CreateInvoiceCommand);
            await Run(invoice.PostCommand, invoice, "23 post invoice");
            Assert.Equal(DocumentStatus.Posted, invoice.Status);
            var pay = await OpenDialog<PaymentEditorViewModel>(invoice.RecordPaymentCommand);
            pay.Amount = invoice.Total;
            pay.Method = PaymentMethod.Bank;
            await SaveDialog(pay, "24 payment");
            Assert.Equal(invoice.Total, invoice.Paid);
            Shot("invoice");
            invoice.CancelCommand.Execute(null);
            await UiSession.SettleAsync();

            // 25–27. actual cost, estimated vs actual, gross profit on the job screen
            await job.LoadAsync();
            await UiSession.SettleAsync();
            var sheet = job.Sheet!;
            Assert.True(sheet.ActualCost > 0);
            Assert.Equal(sheet.Entries.Sum(e => e.Amount), sheet.ActualCost);
            Assert.True(sheet.Variance.Lines.Count >= 5);
            Assert.Equal(sheet.Revenue - sheet.ActualCost, sheet.GrossProfit);
            Assert.True(sheet.Revenue > sheet.Variance.EstimatedTotal, "quoted price must cover the estimated cost");
            job.SelectedTab = 0;
            Shot("job-cost");

            // 28–30. cost sheet PDF and Excel
            await Run(job.CostSheetCommand, job, "28/29 cost sheet PDF", "pdf");
            var pdf = Directory.GetFiles(work, "*.pdf").Single();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
            await Run(job.CostSheetCommand, job, "30 export Excel", "xlsx");
            var xlsx = Directory.GetFiles(work, "*.xlsx").Single();
            using (var wb = new XLWorkbook(xlsx)) Assert.True(wb.Worksheet(1).LastRowUsed()!.RowNumber() > 3);

            // 31. accounting entries for this job
            await using (var db = S<IAppDbFactory>().Create())
            {
                var lines = await db.JournalLines.Where(l => l.JobId == jobId && l.JournalEntry!.Status == JournalStatus.Posted)
                    .Select(l => new { l.Account!.SystemKey, l.Debit, l.Credit }).ToListAsync();
                decimal Bal(string key) => lines.Where(l => l.SystemKey == key).Sum(l => l.Debit - l.Credit);
                Assert.Equal(0m, Bal(SystemAccounts.WIP));
                Assert.Equal(sheet.ActualCost, Bal(SystemAccounts.COGS));
                var entries = await db.JournalEntries.Where(e => e.Status == JournalStatus.Posted).Select(e => new { D = e.Lines.Sum(l => l.Debit), C = e.Lines.Sum(l => l.Credit) }).ToListAsync();
                Assert.All(entries, e => Assert.Equal(e.D, e.C));
            }
            var checks = await S<ReconciliationService>().RunAsync();
            Assert.All(checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Expected} vs {c.Actual}"));

            // 32. inventory movements shown for the job
            var txs = (await S<InventoryService>().ListTransactionsAsync(new PageRequest(PageSize: 50), jobId: jobId)).Items;
            Assert.Contains(txs, x => x.Type == InventoryTxType.MaterialIssue && x.Quantity == -1);
            UiSession.Shell.NavigateTo("Inventory");
            await UiSession.SettleAsync();
            Shot("inventory");
            _out.WriteLine($"job {job.Job!.Number}: estimated {sheet.Variance.EstimatedTotal}, actual {sheet.ActualCost}, revenue {sheet.Revenue}, profit {sheet.GrossProfit}");
        }
        finally
        {
            Dialogs.OpenFileOverride = null;
            Dialogs.SaveFileOverride = null;
            Dialogs.ConfirmOverride = null;
            while (Dialogs.Stack.Count > 0) Dialogs.Remove(Dialogs.Stack[^1]);
        }
    }
}
