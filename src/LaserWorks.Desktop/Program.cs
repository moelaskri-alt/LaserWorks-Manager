using Avalonia;
using LaserWorks.Infrastructure.Files;
using Serilog;

namespace LaserWorks.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = new AppPaths();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "laserworks-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error(e.Exception, "Unobserved task exception"); e.SetObserved(); };
        try
        {
            Log.Information("LaserWorks Manager starting. Data: {Data}", paths.DataDirectory);
            App.Paths = paths;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal startup error");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(AppFonts.Options)
            .LogToTrace();
}
