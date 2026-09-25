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
            _ = vm.StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
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
