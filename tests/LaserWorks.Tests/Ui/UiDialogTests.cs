using System.Reflection;
using System.Windows.Input;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Desktop;
using LaserWorks.Desktop.ViewModels;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Ui;

public class UiDialogTests
{
    private readonly ITestOutputHelper _out;
    public UiDialogTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Prefixes = { "New", "Edit" };

    /// <summary>
    /// For every module: runs each "New…", "Edit…" and "Open" command (on the first row where one is needed),
    /// renders the resulting dialog or detail page, and fails on binding errors or error banners.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_editor_dialog_and_detail_page_opens_cleanly()
    {
        await UiSession.EnsureStartedAsync();
        var dialogs = ViewModelBase.Get<DialogService>();
        var failures = new List<string>();
        var opened = 0;
        foreach (var (lang, theme) in new[] { ("ar", "Light"), ("en", "Light") })
        {
            App.ApplyLanguage(lang);
            App.ApplyTheme(theme);
            foreach (var page in UiSmokeTests.Pages)
            {
                UiSession.Shell.NavigateTo(page);
                await UiSession.SettleAsync();
                var root = UiSession.Shell.CurrentPage!;
                foreach (var vm in new[] { (object)root }.Concat(ChildPages(root)))
                {
                    foreach (var (name, cmd) in Commands(vm))
                    {
                        var param = NeedsRow(name) ? FirstRow(vm) : null;
                        if (NeedsRow(name) && param == null) continue;
                        if (!cmd.CanExecute(param)) continue;
                        UiLog.Instance.Clear();
                        var before = dialogs.Stack.Count;
                        cmd.Execute(param);
                        await UiSession.SettleAsync();
                        var tag = $"{lang}-{page}-{vm.GetType().Name}-{name}";
                        if (dialogs.Stack.Count > before)
                        {
                            opened++;
                            UiSession.Capture("dlg-" + tag);
                            failures.AddRange(UiLog.Instance.Snapshot().Select(e => $"{tag}: {e}"));
                            failures.AddRange(UiSession.VisibleErrors().Select(e => $"{tag}: banner {e}"));
                            await CloseAllAsync(dialogs);
                        }
                        else if (UiSession.Shell.CurrentPage != root)
                        {
                            opened++;
                            UiSession.Capture("page-" + tag);
                            failures.AddRange(UiLog.Instance.Snapshot().Select(e => $"{tag}: {e}"));
                            failures.AddRange(UiSession.VisibleErrors().Select(e => $"{tag}: banner {e}"));
                            UiSession.Shell.NavigateTo(page);
                            await UiSession.SettleAsync();
                            root = UiSession.Shell.CurrentPage!;
                            break; // child VMs belong to the old page instance
                        }
                        else if (vm is ViewModelBase b && !string.IsNullOrEmpty(b.ErrorMessage))
                        {
                            failures.Add($"{tag}: error {b.ErrorMessage}");
                        }
                    }
                }
            }
        }
        foreach (var f in failures.Distinct()) _out.WriteLine(f);
        _out.WriteLine($"opened {opened} dialogs/pages");
        Assert.True(opened > 60, $"only {opened} dialogs/pages opened");
        Assert.Empty(failures.Distinct());
    }

    private static bool NeedsRow(string name) => name.StartsWith("Edit") || name == "Open";

    private static IEnumerable<object> ChildPages(object page) =>
        page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(PageViewModel).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0)
            .Select(p => p.GetValue(page)).OfType<object>();

    private static IEnumerable<(string Name, ICommand Command)> Commands(object vm) =>
        vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType) && p.Name.EndsWith("Command"))
            .Select(p => (Name: p.Name[..^"Command".Length], Command: (ICommand)p.GetValue(vm)!))
            .Where(c => c.Name == "Open" || Prefixes.Any(x => c.Name.StartsWith(x)))
            .OrderBy(c => c.Name)
            .ToList();

    private static object? FirstRow(object vm)
    {
        var items = vm.GetType().GetProperty("Items")?.GetValue(vm) as System.Collections.IEnumerable;
        return items?.Cast<object>().FirstOrDefault();
    }

    private static async Task CloseAllAsync(DialogService dialogs)
    {
        for (var guard = 0; dialogs.Stack.Count > 0 && guard < 10; guard++)
        {
            var top = dialogs.Stack[^1];
            var cancel = top.GetType().GetProperty("CancelCommand")?.GetValue(top) as ICommand
                         ?? top.GetType().GetProperty("NoCommand")?.GetValue(top) as ICommand
                         ?? top.GetType().GetProperty("OkCommand")?.GetValue(top) as ICommand;
            if (cancel != null) cancel.Execute(null); else dialogs.Remove(top);
            await UiSession.SettleAsync();
        }
    }
}
