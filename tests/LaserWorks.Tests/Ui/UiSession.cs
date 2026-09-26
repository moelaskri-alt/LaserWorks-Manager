using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaserWorks.Application.Services;
using LaserWorks.Desktop;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Desktop.Views;
using LaserWorks.Infrastructure;
using LaserWorks.Infrastructure.Files;
using LaserWorks.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LaserWorks.Tests.Ui;

/// <summary>
/// One desktop application session per test process (the app uses a static service provider):
/// a demo database, a logged-in administrator and a real MainWindow rendered with Skia.
/// </summary>
public static class UiSession
{
    private static MainWindowViewModel? _main;
    private static MainWindow? _window;

    public static string ScreenshotFolder
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("LASERWORKS_SCREENSHOTS")
                       ?? Path.Combine(Path.GetTempPath(), "laserworks-tests", "screenshots");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static MainWindowViewModel Main => _main!;
    public static MainWindow Window => _window!;
    public static ShellViewModel Shell => (ShellViewModel)Main.Content!;

    public static async Task<MainWindow> EnsureStartedAsync()
    {
        if (_window != null) return _window;
        var folder = TestDb.NewFolder("ui");
        var paths = new AppPaths(folder);
        App.BuildServices(paths);
        await Task.Run(async () =>
        {
            await App.Services.InitializeDatabaseAsync();
            await App.Services.GetRequiredService<SetupService>().CompleteAsync(TestDb.DefaultSetup(true, DateTime.Now), null);
        });
        _main = new MainWindowViewModel();
        _window = new MainWindow { DataContext = _main, Width = 1366, Height = 768 };
        _window.Show();
        await _main.StartAsync();
        await SettleAsync();
        await LoginAsync("admin", "Admin@2026");
        return _window;
    }

    public static async Task LoginAsync(string user, string password)
    {
        var login = Main.Content as LoginViewModel ?? throw new InvalidOperationException("Login screen expected, got " + Main.Content?.GetType().Name);
        login.Username = user;
        login.Password = password;
        await login.LoginCommand.ExecuteAsync(null);
        await SettleAsync();
        if (Main.Content is not ShellViewModel) throw new InvalidOperationException("Login failed: " + login.ErrorMessage);
    }

    /// <summary>Pumps the dispatcher until background loads finish and layout is stable.</summary>
    public static async Task SettleAsync(int maxMs = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var quiet = 0;
        while (sw.ElapsedMilliseconds < maxMs)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
            Dispatcher.UIThread.RunJobs();
            if (AnyBusy()) quiet = 0; else if (++quiet >= 4) break;
        }
        _window?.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool AnyBusy()
    {
        if (_window == null) return false;
        return _window.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ViewModelBase>().Distinct().Any(v => v.IsBusy);
    }

    public static string Capture(string name)
    {
        _window!.UpdateLayout();
        var path = Path.Combine(ScreenshotFolder, name + ".png");
        // Render into a bitmap we own and dispose, then encode in managed code. (Encoding the headless window
        // surface through Skia while it was being re-rendered crashed the test host intermittently.)
        var size = new Avalonia.PixelSize((int)_window.ClientSize.Width, (int)_window.ClientSize.Height);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
        bitmap.Render(_window);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        Png.Write(path, size.Width, size.Height, stride, pixels, rgba: false);
        return path;
    }

    /// <summary>Error messages currently shown by any view model in the window (banners).</summary>
    public static List<string> VisibleErrors() =>
        _window!.GetVisualDescendants().OfType<Control>().Select(c => c.DataContext).OfType<ViewModelBase>().Distinct()
            .Where(v => !string.IsNullOrEmpty(v.ErrorMessage)).Select(v => v.GetType().Name + ": " + v.ErrorMessage).ToList();
}
