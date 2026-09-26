using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LaserWorks.Desktop;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Reporting.Core;
using LaserWorks.Tests.Integration;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Ui;

/// <summary>
/// Runs every report on the real Reports screen (Arabic/Light and English/Dark) and reads the text actually rendered
/// in the grid cells: no "System.Object", type names, raw codes or empty grids where the report has rows.
/// </summary>
public class UiReportScreenTests
{
    private readonly ITestOutputHelper _out;
    public UiReportScreenTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public async Task Every_report_renders_business_values_on_screen()
    {
        var window = await UiSession.EnsureStartedAsync();
        var problems = new List<string>();
        var cellsRead = 0; var runs = 0;
        try
        {
            foreach (var (lang, theme) in new[] { ("ar", "Light"), ("en", "Dark") })
            {
                App.ApplyLanguage(lang);
                App.ApplyTheme(theme);
                UiSession.Shell.NavigateTo("Reports");
                await UiSession.SettleAsync();
                var vm = (ReportsViewModel)UiSession.Shell.CurrentPage!;
                foreach (var item in vm.AllReports)
                {
                    vm.SelectedReport = item;
                    await UiSession.SettleAsync();
                    vm.Customer ??= vm.CustomerList.FirstOrDefault();
                    vm.Job ??= vm.JobList.FirstOrDefault();
                    vm.Account ??= vm.AccountList.FirstOrDefault(a => a.Code == "1020") ?? vm.AccountList.FirstOrDefault();
                    await vm.Run();
                    await UiSession.SettleAsync();
                    runs++;
                    var key = item.Definition.Key;
                    if (vm.ErrorMessage != null) { problems.Add($"{lang}/{key}: error {vm.ErrorMessage}"); continue; }
                    var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "grid");
                    var rows = grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsEffectivelyVisible).ToList();
                    if (vm.Table!.Rows.Count > 0 && rows.Count == 0) problems.Add($"{lang}/{key}: {vm.Table.Rows.Count} rows but none rendered");
                    foreach (var row in rows)
                        foreach (var tb in row.GetVisualDescendants().OfType<TextBlock>())
                        {
                            var text = tb.Text ?? "";
                            cellsRead++;
                            if (!ReportOutputTests.IsArtifact(text)) continue;
                            var cell = tb.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
                            problems.Add($"{lang}/{key}: cell \"{text}\" (row {row.Index} of {grid.ItemsSource?.Cast<object>().Count()}, item {row.DataContext?.GetType().Name ?? "null"}, " +
                                $"column {(cell?.GetVisualParent() is Panel pp ? pp.Children.IndexOf(cell) : -1)})");
                        }
                    problems.AddRange(ReportOutputTests.Problems(key, vm.Table!).Select(p => $"{lang}/{key} after display: {p}"));
                    foreach (var h in grid.GetVisualDescendants().OfType<DataGridColumnHeader>())
                        if (h.Content is string s && (s.StartsWith("Col.") || s.StartsWith("Rpt.") || ReportOutputTests.IsArtifact(s))) problems.Add($"{lang}/{key}: header \"{s}\"");
                    if (key is "JobCostSheet" or "EstimatedVsActual" or "TrialBalance" or "GeneralLedger" or "JobProfitability" or "CustomerList") UiSession.Capture($"report-{lang}-{theme}-{key}");
                }
            }
            // drill-down: a report row opens its source document (job profitability → the job)
            UiSession.Shell.NavigateTo("Reports");
            await UiSession.SettleAsync();
            var reports = (ReportsViewModel)UiSession.Shell.CurrentPage!;
            reports.SelectedReport = reports.AllReports.Single(r => r.Definition.Key == "JobProfitability");
            await reports.Run();
            await UiSession.SettleAsync();
            var grid2 = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "grid");
            var first = grid2.ItemsSource!.Cast<ReportGridRow>().First(r => r.Source.SourceType == "Job");
            Assert.True(reports.OpenRowCommand.CanExecute(first));
            reports.OpenRowCommand.Execute(first);
            await UiSession.SettleAsync();
            var detail = Assert.IsType<JobDetailViewModel>(UiSession.Shell.CurrentPage);
            Assert.Equal(first.Source.SourceId, detail.JobId);
        }
        finally
        {
            App.ApplyLanguage("en");
            App.ApplyTheme("Light");
        }
        _out.WriteLine($"{runs} report runs, {cellsRead} rendered cells read, {problems.Count} problems");
        foreach (var p in problems.Distinct().Take(100)) _out.WriteLine(p);
        Assert.Empty(problems.Distinct());
    }
}
