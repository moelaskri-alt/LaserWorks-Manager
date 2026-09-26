using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
/// Multi-component jobs driven through the desktop screens: request items, design V1 (rejected) and V2 (approved),
/// estimate component grid, quotation, job component lines (stock issue per line, direct purchase, external service,
/// manual cost, an added line, a reversal), save as template, a new estimate from the template, and a 20-line estimate.
/// </summary>
public class UiMultiComponentTests
{
    private readonly ITestOutputHelper _out;
    public UiMultiComponentTests(ITestOutputHelper output) => _out = output;

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

    private static async Task Recalc()
    {
        await Task.Delay(400); // the estimate editor recalculates 150 ms after the last change
        await UiSession.SettleAsync();
    }

    private static void AssertLayout(string step)
    {
        var (_, problems) = UiLayoutMatrixTests.AuditPublic(UiSession.Window, Dialogs.Stack.Count > 0);
        Assert.True(problems.Count == 0, step + ": " + string.Join("; ", problems));
    }

    [AvaloniaFact]
    public async Task Restaurant_sign_with_many_components_through_the_user_interface()
    {
        await UiSession.EnsureStartedAsync();
        App.ApplyLanguage("en");
        App.ApplyTheme("Light");
        Dialogs.ConfirmOverride = _ => true;
        var shot = 0;
        void Shot(string name) { UiSession.Capture($"mc-{++shot:00}-{name}"); AssertLayout(name); }
        try
        {
            var customer = (await S<CustomerService>().LookupAsync()).First();
            var mats = await S<MaterialService>().LookupAsync();
            var sheetMat = mats.First(m => m.Kind == MaterialKind.RawMaterial && m.IsSheet && Math.Max(m.Length, m.Width) >= 130 && Math.Min(m.Length, m.Width) >= 50 && m.QuantityOnHand >= 2);
            var led = mats.First(m => m.Kind == MaterialKind.PurchasedComponent && m.Unit == "M");
            var box = mats.First(m => m.Kind == MaterialKind.Packaging);
            var uv = mats.First(m => m.Kind == MaterialKind.Service);
            var tape = mats.First(m => m.Kind == MaterialKind.Consumable && m.QuantityOnHand > 1);

            // request with requested items
            UiSession.Shell.NavigateTo("Requests");
            await UiSession.SettleAsync();
            var requests = (RequestsViewModel)UiSession.Shell.CurrentPage!;
            var re = await OpenDialog<RequestEditorViewModel>(requests.NewCommand);
            re.Customer = re.Customers.Single(c => c.Id == customer.Id);
            re.Description = "Illuminated restaurant sign";
            re.Quantity = 2;
            re.Dimensions = "120 × 40 cm";
            foreach (var (m, perUnit) in new[] { (sheetMat, 1m), (led, 3m), (box, 1m), (uv, 1m) })
            {
                re.AddItemCommand.Execute(null);
                re.Items[^1].Material = re.Materials.Single(x => x.Id == m.Id);
                re.Items[^1].Quantity = perUnit;
            }
            re.AddItemCommand.Execute(null);
            re.Items[^1].Category = ComponentCategory.PurchasedComponent;
            re.Items[^1].Description = "Brass wall-mounting kit";
            re.DuplicateItemCommand.Execute(re.Items[1]);
            Assert.Equal(6, re.Items.Count);
            re.RemoveItemCommand.Execute(re.Items[2]);
            Assert.Equal(5, re.Items.Count);
            await Run(re.ApplyCommand, re, "request");
            Assert.True(re.IsSaved);
            Shot("request-items");

            // design V1 rejected, V2 approved
            var v1 = await OpenDialog<RevisionEditorViewModel>(re.NewRevisionCommand);
            v1.Width = 110; v1.Height = 40; v1.CuttingLengthM = 6; v1.EstimatedMachineMinutes = 20;
            v1.Designer = v1.Designers.FirstOrDefault();
            await SaveDialog(v1, "V1");
            var v1Edit = await OpenDialog<RevisionEditorViewModel>(re.OpenRevisionCommand, re.Revisions.Single());
            await v1Edit.RejectCommand.ExecuteAsync(null);
            await UiSession.SettleAsync();
            var v2 = await OpenDialog<RevisionEditorViewModel>(re.NewRevisionCommand);
            v2.Width = 120; v2.Height = 40; v2.CuttingLengthM = 7; v2.EstimatedMachineMinutes = 25;
            v2.Designer = v2.Designers.FirstOrDefault();
            await v2.ApproveCommand.ExecuteAsync(null);
            await UiSession.SettleAsync();
            Assert.True(v2.ErrorMessage == null, v2.ErrorMessage);
            Assert.Equal(2, re.Revisions.Count);
            Assert.Single(re.Revisions, r => r.Status == RevisionStatus.Approved);
            Assert.Single(re.Revisions, r => r.Status == RevisionStatus.Rejected);
            re.SelectedTab = 2;
            Shot("request-revisions");

            // estimate: one line per requested item, plus an installation cost
            re.CreateEstimateCommand.Execute(null);
            await UiSession.SettleAsync();
            var est = Assert.IsType<EstimateEditorViewModel>(UiSession.Shell.CurrentPage);
            Assert.Equal(5, est.MaterialLines.Count);
            Assert.Equal(3, est.MaterialLines.Count(l => l.Source == ComponentSource.Inventory));          // sheet, LED, box
            Assert.Single(est.MaterialLines, l => l.Source == ComponentSource.ExternalService && l.Category == ComponentCategory.ExternalService);
            Assert.Single(est.MaterialLines, l => l.Source == ComponentSource.DirectPurchase && l.Material == null);
            var kit = est.MaterialLines.Single(l => l.Source == ComponentSource.DirectPurchase);
            est.SelectedLine = kit;
            kit.UnitCost = 25;
            var sheet = est.MaterialLines.Single(l => l.Material?.Id == sheetMat.Id);
            est.SelectedLine = sheet;
            sheet.SheetBased = true;
            if (sheet.Pieces.Count == 0) sheet.AddPieceCommand.Execute(null);
            sheet.Pieces[0].Length = 120; sheet.Pieces[0].Width = 40; sheet.Pieces[0].QuantityPerUnit = 1;
            var install = est.AddLine(ComponentSource.ManualCost, null, ComponentCategory.OtherDirect);
            install.Description = "Installation transport"; install.QuantityPerUnit = 0.5m; install.UnitCost = 80;
            est.DuplicateLineCommand.Execute(install);
            Assert.Equal(7, est.MaterialLines.Count);
            est.RemoveLineCommand.Execute(est.MaterialLines[^1]);
            Assert.Equal(6, est.MaterialLines.Count);
            est.ReworkAllowancePercent = 5;
            if (est.MachineLines.Count == 0) est.AddMachineCommand.Execute(null);
            est.MachineLines[0].MinutesPerUnit = 25;
            if (est.LaborLines.Count == 0) est.AddLaborCommand.Execute(null);
            est.LaborLines[0].MinutesPerUnit = 45;
            await Recalc();
            Assert.Null(est.CalcError);
            Assert.Equal(est.MaterialLines.Sum(l => l.Cost), est.ComponentsTotal);
            Assert.Equal(est.MaterialLines.Where(l => l.Category == ComponentCategory.ExternalService).Sum(l => l.Cost), est.Components.Single(c => c.Component == CostComponent.ExternalServices).Amount);
            Assert.Equal(est.MaterialLines.Where(l => l.Category == ComponentCategory.PurchasedComponent).Sum(l => l.Cost), est.Components.Single(c => c.Component == CostComponent.PurchasedComponents).Amount);
            Assert.True(est.Components.Single(c => c.Component == CostComponent.Rework).Amount > 0);
            Shot("estimate");
            await Run(est.SaveCommand, est, "save estimate");

            // quotation → job
            est.CreateQuotationCommand.Execute(null);
            await UiSession.SettleAsync();
            var q = Assert.IsType<QuotationEditorViewModel>(Dialogs.Stack[^1]);
            await Run(q.MarkSentCommand, q, "send");
            await Run(q.ApproveCommand, q, "approve");
            await Run(q.CreateJobCommand, q, "create job");
            q.OpenJobCommand.Execute(null);
            await UiSession.SettleAsync();
            var job = Assert.IsType<JobDetailViewModel>(UiSession.Shell.CurrentPage);
            job.SelectedTab = 1;
            await UiSession.SettleAsync();
            Assert.Equal(6, job.Components.Count);
            Shot("job-components");

            // issue every stock line from its own row
            foreach (var id in job.Components.Where(c => c.IsStocked).Select(c => c.Id).ToList())
            {
                job.SelectedComponent = job.Components.Single(c => c.Id == id);
                Assert.True(job.CanIssueComponent);
                var issue = await OpenDialog<StockMovementDialogViewModel>(job.IssueComponentCommand);
                Assert.True(issue.MaterialFixed);
                Assert.Equal(job.SelectedComponent!.RemainingQuantity, issue.Quantity);
                Shot("issue-line");
                await SaveDialog(issue, "issue " + id);
            }
            // direct purchase on credit, external service and manual cost in cash
            async Task Direct(ComponentSource source, decimal amount, PaymentMethod method)
            {
                job.SelectedComponent = job.Components.First(c => c.Source == source && c.ActualCost == 0);
                Assert.True(job.CanRecordDirectCost);
                Assert.False(job.CanIssueComponent);
                var d = await OpenDialog<DirectCostDialogViewModel>(job.RecordDirectCostCommand);
                d.Amount = amount; d.TaxAmount = Math.Round(amount * 0.15m, 2); d.Method = method;
                if (method == PaymentMethod.OnCredit) d.Supplier = d.Suppliers.First();
                d.Reference = "UI-" + source;
                Shot("direct-" + source);
                await SaveDialog(d, "direct " + source);
            }
            await Direct(ComponentSource.DirectPurchase, 52, PaymentMethod.OnCredit);
            await Direct(ComponentSource.ExternalService, 250, PaymentMethod.Cash);
            await Direct(ComponentSource.ManualCost, 40, PaymentMethod.Cash);

            // a line added during production, issued, then a direct cost reversed from the cost tab
            var add = await OpenDialog<JobComponentEditorViewModel>(job.AddComponentCommand, "Inventory");
            add.Material = add.ItemChoices.Single(m => m.Id == tape.Id);
            add.PlannedQuantity = 1;
            Assert.Equal(ComponentCategory.Consumable, add.Category);
            Shot("add-line");
            await SaveDialog(add, "add line");
            Assert.Equal(7, job.Components.Count);
            job.SelectedComponent = job.Components.Single(c => c.MaterialId == tape.Id && c.UsedQuantity == 0);
            await SaveDialog(await OpenDialog<StockMovementDialogViewModel>(job.IssueComponentCommand), "issue added line");
            job.SelectedTab = 0;
            await UiSession.SettleAsync();
            job.SelectedEntry = job.Entries.First(e => e.SourceType == "DirectCost" && e.Amount == 40);
            Assert.True(job.CanReverseEntry);
            await Run(job.ReverseEntryCommand, job, "reverse");
            Shot("job-cost-components");

            // per-line estimated vs actual reconciles to the job and to the ledger
            var jobId = job.JobId;
            var cs = job.Sheet!;
            Assert.Equal(7, cs.Components.Count(c => c.IsLine));
            Assert.Equal(cs.ActualCost, cs.Components.Sum(c => c.Actual));
            Assert.Equal(cs.Variance.EstimatedTotal, cs.Components.Sum(c => c.Estimated));
            Assert.Equal(0m, job.Components.Single(c => c.Source == ComponentSource.ManualCost).ActualCost);
            Assert.Equal(52m, job.Components.Single(c => c.Source == ComponentSource.DirectPurchase).ActualCost);
            Assert.All(job.Components.Where(c => c.IsStocked), c => Assert.Equal(c.PlannedQuantity, c.UsedQuantity));
            await using (var db = S<IAppDbFactory>().Create())
            {
                var wip = await db.JournalLines.Where(l => l.JobId == jobId && l.Account!.SystemKey == SystemAccounts.WIP && l.JournalEntry!.Status != JournalStatus.Draft)
                    .Select(l => new { l.Debit, l.Credit }).ToListAsync();
                Assert.Equal(cs.ActualCost, wip.Sum(l => l.Debit - l.Credit));
                Assert.Equal(0, await db.InventoryTransactions.CountAsync(t => t.JobId == jobId && t.JobComponentId == null));
            }
            Assert.All(await S<ReconciliationService>().RunAsync(), c => Assert.True(c.Passed, $"{c.Name}: {c.Expected} vs {c.Actual}"));

            // save the job as a template and start a new estimate from it
            job.SelectedTab = 1;
            await Run(job.SaveAsTemplateCommand, job, "save job as template");
            UiSession.Shell.NavigateTo("Estimates");
            await UiSession.SettleAsync();
            var list = (EstimatesViewModel)UiSession.Shell.CurrentPage!;
            var picker = await OpenDialog<TemplatePickerViewModel>(list.NewFromTemplateCommand);
            picker.Selected = picker.Templates.First(t => t.Name == job.Job!.Title);
            Assert.Equal(7, picker.Selected.Lines);
            Shot("template-picker");
            await SaveDialog(picker, "new from template");
            var fromTpl = Assert.IsType<EstimateEditorViewModel>(UiSession.Shell.CurrentPage);
            await UiSession.SettleAsync();
            Assert.Equal(7, fromTpl.MaterialLines.Count);
            Shot("estimate-from-template");
            _out.WriteLine($"job {job.Job!.Number}: estimated {cs.Variance.EstimatedTotal}, actual {cs.ActualCost}");
        }
        finally
        {
            Dialogs.ConfirmOverride = null;
            while (Dialogs.Stack.Count > 0) Dialogs.Remove(Dialogs.Stack[^1]);
        }
    }

    /// <summary>Twenty component lines entered in the estimate grid, saved, reloaded in order and scrolled, at the smallest resolution in both languages.</summary>
    [AvaloniaFact]
    public async Task Twenty_component_lines_in_the_estimate_grid()
    {
        var window = await UiSession.EnsureStartedAsync();
        Dialogs.ConfirmOverride = _ => true;
        try
        {
            foreach (var lang in new[] { "en", "ar" })
            {
                App.ApplyLanguage(lang);
                App.ApplyTheme("Light");
                UiSession.Shell.NavigateTo("Estimates");
                await UiSession.SettleAsync();
                ((EstimatesViewModel)UiSession.Shell.CurrentPage!).NewCommand.Execute(null);
                await UiSession.SettleAsync();
                var est = Assert.IsType<EstimateEditorViewModel>(UiSession.Shell.CurrentPage);
                est.Description = "Directory board — 20 components";
                est.Quantity = 3;
                while (est.MaterialLines.Count > 0) est.RemoveLineCommand.Execute(est.MaterialLines[0]);
                var adders = new IRelayCommand[] { est.AddMaterialCommand, est.AddPurchasedCommand, est.AddServiceCommand, est.AddManualCommand };
                for (var i = 0; i < 20; i++)
                {
                    adders[i % 4].Execute(null);
                    var l = est.MaterialLines[^1];
                    if (l.Source == ComponentSource.Inventory) { l.SheetBased = false; l.QuantityPerUnit = 0.25m; }
                    else { l.Description = $"Line {i + 1}"; l.QuantityPerUnit = 1; l.UnitCost = 10 + i; }
                }
                Assert.Equal(20, est.MaterialLines.Count);
                Assert.Equal(Enumerable.Range(1, 20), est.MaterialLines.Select(l => l.LineNo));
                await Recalc();
                Assert.Null(est.CalcError);
                await Run(est.SaveCommand, est, "save 20 lines");
                var id = (await S<EstimateService>().ListAsync(new PageRequest(PageSize: 5, SortBy: "Id", Descending: true))).Items.First().Id;

                var reopened = new EstimateEditorViewModel(id);
                ViewModelBase.Get<Navigator>().Navigate(reopened);
                await UiSession.SettleAsync();
                Assert.Equal(20, reopened.MaterialLines.Count);
                Assert.Equal(est.MaterialLines.Select(l => (l.Source, l.Category, l.Description, l.Cost)), reopened.MaterialLines.Select(l => (l.Source, l.Category, l.Description, l.Cost)));
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "gridComponents");
                Assert.True(grid.Bounds.Height < 300, "the component grid keeps a fixed height and scrolls");
                grid.ScrollIntoView(reopened.MaterialLines[^1], null);
                reopened.SelectedLine = reopened.MaterialLines[^1];
                await UiSession.SettleAsync();
                UiSession.Capture($"mc-20-lines-{lang}");
                AssertLayout("20 lines " + lang);
                Assert.Contains(grid.GetVisualDescendants().OfType<DataGridRow>(), r => r.IsEffectivelyVisible && r.DataContext == reopened.MaterialLines[^1]);
            }
        }
        finally
        {
            Dialogs.ConfirmOverride = null;
            App.ApplyLanguage("en");
        }
    }
}
