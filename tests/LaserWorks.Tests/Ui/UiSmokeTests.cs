using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LaserWorks.Desktop;
using LaserWorks.Localization;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Ui;

public class UiSmokeTests
{
    private readonly ITestOutputHelper _out;
    public UiSmokeTests(ITestOutputHelper output) => _out = output;

    public static readonly string[] Pages =
    {
        "Dashboard", "Customers", "Requests", "Design", "Estimates", "Quotations", "Jobs", "Production", "Machines", "Employees",
        "Materials", "Inventory", "Purchases", "Sales", "Expenses", "Accounting", "Profitability", "Reports", "Settings", "Backup", "Users"
    };

    /// <summary>Opens every module and every tab at 1366×768 (Arabic/Light, English/Dark), 1600×900 (English/Light) and 1920×1080 (Arabic/Dark): no binding errors, error banners or layout-audit problems.</summary>
    [AvaloniaFact]
    public async Task Every_page_and_tab_renders_in_both_languages_and_themes()
    {
        var window = await UiSession.EnsureStartedAsync();
        var failures = new List<string>();
        foreach (var (lang, theme, w, h) in new[] { ("ar", "Light", 1366, 768), ("en", "Dark", 1366, 768), ("en", "Light", 1600, 900), ("ar", "Dark", 1920, 1080) })
        {
            window.Width = w; window.Height = h;
            App.ApplyLanguage(lang);
            App.ApplyTheme(theme);
            await UiSession.SettleAsync();
            Assert.Equal(lang == "ar" ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight, window.FlowDirection);
            foreach (var page in Pages)
            {
                UiLog.Instance.Clear();
                UiSession.Shell.NavigateTo(page);
                await UiSession.SettleAsync();
                Assert.Equal(page, UiSession.Shell.CurrentKey);
                UiSession.Capture($"{w}-{lang}-{theme}-{page}");
                failures.AddRange(UiLayoutMatrixTests.AuditPublic(window, false).Problems.Select(e => $"{lang}/{page}: layout {e}"));
                var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
                if (tabs != null)
                {
                    for (var i = 1; i < tabs.ItemCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        await UiSession.SettleAsync();
                        UiSession.Capture($"{w}-{lang}-{theme}-{page}-tab{i}");
                        failures.AddRange(UiLayoutMatrixTests.AuditPublic(window, false).Problems.Select(e => $"{lang}/{page}/tab{i}: layout {e}"));
                    }
                    tabs.SelectedIndex = 0;
                }
                failures.AddRange(UiLog.Instance.Snapshot().Select(e => $"{lang}/{page}: {e}"));
                failures.AddRange(UiSession.VisibleErrors().Select(e => $"{lang}/{page}: banner {e}"));
            }
        }
        window.Width = 1366; window.Height = 768;
        foreach (var f in failures.Distinct()) _out.WriteLine(f);
        Assert.Empty(failures.Distinct());
    }

    [AvaloniaFact]
    public async Task No_missing_translation_keys_after_visiting_all_pages()
    {
        await Every_page_and_tab_renders_in_both_languages_and_themes();
        var missing = Loc.Instance.MissingKeys.OrderBy(k => k).ToList();
        foreach (var k in missing) _out.WriteLine(k);
        Assert.Empty(missing);
    }
}
