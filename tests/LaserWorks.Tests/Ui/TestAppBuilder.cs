using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using LaserWorks.Desktop;
using LaserWorks.Tests.Ui;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LaserWorks.Tests.Ui;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Logger.Sink = UiLog.Instance;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(AppFonts.Options);
    }
}

/// <summary>Collects Avalonia binding/property warnings so UI tests can fail on broken bindings.</summary>
public sealed class UiLog : ILogSink
{
    public static readonly UiLog Instance = new();
    private readonly List<string> _entries = new();

    public IReadOnlyList<string> Snapshot() { lock (_entries) return _entries.ToList(); }
    public void Clear() { lock (_entries) _entries.Clear(); }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property or LogArea.Control or LogArea.Visual or LogArea.Layout;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Add(level, area, source, messageTemplate, Array.Empty<object?>());

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Add(level, area, source, messageTemplate, propertyValues);

    private void Add(LogEventLevel level, string area, object? source, string template, object?[] values)
    {
        var msg = template;
        foreach (var v in values)
        {
            var i = msg.IndexOf('{');
            var j = i >= 0 ? msg.IndexOf('}', i) : -1;
            if (i < 0 || j < 0) break;
            msg = msg[..i] + v + msg[(j + 1)..];
        }
        // a null intermediate value in a binding path (e.g. nothing selected yet) is expected, not a defect
        if (msg.Contains("Value is null")) return;
        lock (_entries) _entries.Add($"[{level}] {area}: {msg} (source: {source?.GetType().Name})");
    }
}
