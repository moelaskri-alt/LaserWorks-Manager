using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using LaserWorks.Application.Abstractions;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Desktop.Views;
using LaserWorks.Infrastructure;
using LaserWorks.Localization;
using LaserWorks.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace LaserWorks.Desktop;

public partial class App : Avalonia.Application
{
    /// <summary>Data location; set by Program (or by tests before the app starts).</summary>
    public static IAppPaths? Paths { get; set; }

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// "--smoke-test": start normally, open the database and the first screen, then exit with 0 (success) or 1.
    /// Used by the release pipeline to validate the packaged executable on a real Windows machine.
    /// </summary>
    public static bool SmokeTest { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public static IServiceProvider BuildServices(IAppPaths paths)
    {
        var services = new ServiceCollection();
        services.AddLaserWorks(paths);
        services.AddLaserWorksReporting();
        services.AddSingleton<DialogService>();
        services.AddSingleton<Navigator>();
        Services = services.BuildServiceProvider();
        return Services;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            BuildServices(Paths ?? new LaserWorks.Infrastructure.Files.AppPaths());
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            if (SmokeTest) _ = RunSmokeTestAsync(desktop, vm);
            else _ = vm.StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunSmokeTestAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm)
    {
        var code = 1;
        try
        {
            await vm.StartAsync();
            await Task.Delay(1500); // let the first screen render
            var ok = vm.StartupError == null && vm.Content != null && desktop.MainWindow?.IsVisible == true;
            // PDF engine (native library bundled in the single-file exe) and Excel export
            var company = await Services.GetRequiredService<LaserWorks.Application.Services.SettingsService>().GetAsync();
            var table = new LaserWorks.Reporting.Core.ReportTable { Title = "Smoke test" }.Col("a", "Col.Name").Col("b", "Col.Amount", LaserWorks.Reporting.Core.ColumnKind.Money);
            table.Add("اختبار / test", 12.5m);
            var pdf = LaserWorks.Reporting.Core.PdfExporter.Render(table, company);
            var xlsx = Path.Combine(Path.GetTempPath(), $"lw-smoke-{Guid.NewGuid():N}.xlsx");
            LaserWorks.Reporting.Core.ExcelExporter.Export(table, company, xlsx);
            ok &= pdf.Length > 500 && new FileInfo(xlsx).Length > 500;
            File.Delete(xlsx);
            Serilog.Log.Information("Smoke test {Result}: screen {Screen}, error {Error}", ok ? "passed" : "failed", vm.Content?.GetType().Name, vm.StartupError);
            code = ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Smoke test failed");
        }
        desktop.Shutdown(code);
    }

    /// <summary>Applies Light / Dark / System theme.</summary>
    public static void ApplyTheme(string theme)
    {
        if (Current == null) return;
        Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public static void ApplyLanguage(string lang) => Loc.Instance.SetLanguage(lang);
}
