using Avalonia.Threading;

namespace LaserWorks.Desktop;

/// <summary>Runs UI updates on the UI thread (language changes can be raised from background work such as report generation).</summary>
public static class UiThread
{
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
