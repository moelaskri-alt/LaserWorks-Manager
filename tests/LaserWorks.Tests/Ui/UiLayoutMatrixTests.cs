using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Services;
using LaserWorks.Desktop;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Ui;

/// <summary>
/// Layout audit of the critical screens at 1366×768, 1600×900 and 1920×1080, in Arabic and English, light and dark:
/// every button is reachable (inside the window horizontally; vertically either on screen or inside a scrolling area),
/// dialog Save/Cancel are always on screen, grids stay inside the window, and no column header is truncated.
/// Every state is captured as a screenshot.
/// </summary>
public class UiLayoutMatrixTests
{
    private readonly ITestOutputHelper _out;
    public UiLayoutMatrixTests(ITestOutputHelper output) => _out = output;

    public static readonly (int W, int H)[] Resolutions = { (1366, 768), (1600, 900), (1920, 1080) };

    private sealed record Ids(long JobId, long EstimateId, long RequestId, int Components);

    private static async Task<Ids> PickAsync()
    {
        await using var db = App.Services.GetRequiredService<IAppDbFactory>().Create();
        var job = await db.Jobs.AsNoTracking().OrderByDescending(j => j.Components.Count).Select(j => new { j.Id, j.EstimateId, j.RequestId, N = j.Components.Count })
            .FirstAsync(j => j.EstimateId != null && j.RequestId != null);
        return new Ids(job.Id, job.EstimateId!.Value, job.RequestId!.Value, job.N);
    }

    [AvaloniaFact]
    public async Task Critical_screens_fit_every_resolution_language_and_theme()
    {
        var window = await UiSession.EnsureStartedAsync();
        var dialogs = ViewModelBase.Get<DialogService>();
        var nav = ViewModelBase.Get<Navigator>();
        var ids = await PickAsync();
        Assert.True(ids.Components >= 5, "demo job with several component lines expected");
        var failures = new List<string>();
        var checks = 0;
        var shots = 0;
        try
        {
            foreach (var (w, h) in Resolutions)
            foreach (var lang in new[] { "ar", "en" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                window.Width = w; window.Height = h;
                App.ApplyLanguage(lang);
                App.ApplyTheme(theme);
                await UiSession.SettleAsync();
                var tag = $"{w}x{h}-{lang}-{theme}";

                async Task Check(string screen)
                {
                    await UiSession.SettleAsync();
                    var name = $"layout-{tag}-{screen}";
                    UiSession.Capture(name);
                    shots++;
                    var found = Audit(window, dialogs.Stack.Count > 0);
                    checks += found.Checked;
                    failures.AddRange(found.Problems.Select(p => $"{name}: {p}"));
                }

                async Task Dialog(DialogViewModel vm, string screen, Func<Task>? inside = null)
                {
                    _ = dialogs.ShowAsync(vm);
                    await UiSession.SettleAsync();
                    await Check(screen);
                    if (inside != null) await inside();
                    vm.Cancel();
                    await UiSession.SettleAsync();
                }

                // lists
                foreach (var page in new[] { "Requests", "Estimates", "Jobs", "Materials", "Inventory" })
                {
                    UiSession.Shell.NavigateTo(page);
                    await Check(page);
                }

                // request editor: header, requested items, attachments, design revisions
                var req = new RequestEditorViewModel(ids.RequestId);
                await Dialog(req, "RequestEditor-items", async () =>
                {
                    for (var tab = 1; tab <= 2; tab++)
                    {
                        req.SelectedTab = tab;
                        await Check($"RequestEditor-tab{tab}");
                    }
                });

                // estimate editor with its component lines
                var est = new EstimateEditorViewModel(ids.EstimateId);
                nav.Navigate(est);
                await UiSession.SettleAsync();
                await Check("EstimateEditor");
                est.SelectedLine = est.MaterialLines.FirstOrDefault(l => l.ShowSheet) ?? est.MaterialLines.FirstOrDefault();
                await Check("EstimateEditor-sheetline");
                ScrollAllToEnd(window);
                await Check("EstimateEditor-scrolled");

                // job detail, every tab, and the component dialogs
                var job = new JobDetailViewModel(ids.JobId);
                nav.Navigate(job);
                await UiSession.SettleAsync();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                for (var i = 0; i < tabs.ItemCount; i++)
                {
                    job.SelectedTab = i;
                    await Check($"JobDetail-tab{i}");
                }
                job.SelectedTab = 1;
                await UiSession.SettleAsync();
                await Dialog(new JobComponentEditorViewModel(ids.JobId, job.Components[0].Id), "JobComponentEditor");
                var direct = job.Components.FirstOrDefault(c => !c.IsStocked);
                if (direct != null) await Dialog(new DirectCostDialogViewModel(direct), "DirectCostDialog");
                await Dialog(new TemplatePickerViewModel(), "TemplatePicker");
                await Dialog(new StockMovementDialogViewModel(StockAction.IssueToJob, ids.JobId, job.Components[0].MaterialId, job.Components[0].Id, 1), "IssueToLine");
            }
        }
        finally
        {
            window.Width = 1366; window.Height = 768;
            App.ApplyLanguage("en");
            App.ApplyTheme("Light");
            await UiSession.SettleAsync();
        }
        _out.WriteLine($"{shots} screenshots, {checks} element checks, {failures.Count} problems");
        foreach (var f in failures.Distinct().Take(200)) _out.WriteLine(f);
        Assert.Empty(failures.Distinct());
    }

    private static void ScrollAllToEnd(Window window)
    {
        foreach (var sv in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible))
            sv.Offset = new Vector(sv.Offset.X, Math.Max(0, sv.Extent.Height - sv.Viewport.Height));
    }

    private sealed record AuditResult(int Checked, List<string> Problems);

    private static Rect? BoundsIn(Visual v, Visual root) => v.TransformToVisual(root) is { } m ? new Rect(v.Bounds.Size).TransformToAABB(m) : null;

    /// <summary>Layout rules checked on the current window content.</summary>
    public static (int Checked, List<string> Problems) AuditPublic(Window window, bool dialogOpen) { var r = Audit(window, dialogOpen); return (r.Checked, r.Problems); }

    private static AuditResult Audit(Window window, bool dialogOpen)
    {
        var problems = new List<string>();
        var n = 0;
        var size = window.ClientSize;
        const double tol = 1.5;
        var all = window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0).ToList();

        // the overlay dialog (when open) is the only interactive layer: audit what it shows
        Control? dialogCard = null;
        if (dialogOpen)
        {
            var cancel = all.OfType<Button>().LastOrDefault(b => b.IsCancel);
            dialogCard = cancel?.GetVisualAncestors().OfType<Border>().LastOrDefault(b => b.Classes.Contains("card"));
            foreach (var b in all.OfType<Button>().Where(b => (b.IsCancel || b.IsDefault) && b.GetVisualAncestors().Contains(dialogCard!)))
            {
                n++;
                var r = BoundsIn(b, window);
                if (r is not { } rr || rr.X < -tol || rr.Y < -tol || rr.Right > size.Width + tol || rr.Bottom > size.Height + tol)
                    problems.Add($"dialog footer button '{Label(b)}' off screen at {r}");
            }
        }
        IEnumerable<Control> scope = dialogCard != null ? all.Where(c => c.GetVisualAncestors().Contains(dialogCard)) : all;
        scope = scope.ToList();

        foreach (var b in scope.OfType<Button>())
        {
            if (b.GetVisualAncestors().Any(a => a is DataGrid || a is ComboBox || a is CalendarDatePicker || a is NumericUpDown || a is ScrollBar || a is Calendar)) continue;
            n++;
            var r = BoundsIn(b, window);
            if (r is not { } rr) continue;
            if (rr.X < -tol || rr.Right > size.Width + tol) problems.Add($"button '{Label(b)}' outside the window horizontally ({rr.X:0}..{rr.Right:0})");
            var scroller = b.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller == null && (rr.Y < -tol || rr.Bottom > size.Height + tol)) problems.Add($"button '{Label(b)}' below the window with no scrolling ({rr.Y:0}..{rr.Bottom:0})");
            // a button clipped by a non-scrolling parent is unreachable
            var clip = b.GetVisualAncestors().OfType<Control>().TakeWhile(a => a is not Avalonia.Controls.Presenters.ScrollContentPresenter && a is not ScrollViewer).FirstOrDefault(a => a.ClipToBounds && a is not Button);
            if (clip != null && BoundsIn(clip, window) is { } cr && (rr.Right > cr.Right + tol || rr.X < cr.X - tol))
                problems.Add($"button '{Label(b)}' clipped by {clip.GetType().Name}");
        }

        foreach (var g in scope.OfType<DataGrid>())
        {
            n++;
            if (BoundsIn(g, window) is { } gr && (gr.X < -tol || gr.Right > size.Width + tol)) problems.Add($"grid {g.Name ?? "?"} wider than the window ({gr.X:0}..{gr.Right:0})");
            if (g.Bounds.Height < 60) problems.Add($"grid {g.Name ?? "?"} squeezed to {g.Bounds.Height:0}px");
            foreach (var header in g.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(x => x.IsEffectivelyVisible && x.Bounds.Width > 1))
            {
                var tb = header.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
                if (tb == null) continue;
                n++;
                if (tb.TextTrimming != TextTrimming.None) problems.Add($"header '{tb.Text}' can be trimmed");
                // wrapping is allowed between words; a word split across lines means the column is too narrow for its header
                var words = tb.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (tb.TextLayout.TextLines.Count > words) problems.Add($"header '{tb.Text}' breaks inside a word ({tb.TextLayout.TextLines.Count} lines at {tb.Bounds.Width:0}px)");
                if (tb.TextLayout.Width > tb.Bounds.Width + tol) problems.Add($"header '{tb.Text}' wider than its cell");
                if (BoundsIn(tb, header) is { } tr && (tr.X < -tol || tr.Right > header.Bounds.Width + tol)) problems.Add($"header '{tb.Text}' clipped horizontally");
                if (tb.DesiredSize.Height > header.Bounds.Height + tol) problems.Add($"header '{tb.Text}' clipped vertically");
            }
        }
        return new AuditResult(n, problems);
    }

    private static string Label(Button b) => b.Name ?? (b.Content as string) ?? b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? ToolTip.GetTip(b)?.ToString() ?? b.GetType().Name;
}
