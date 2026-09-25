using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using LaserWorks.Localization;

namespace LaserWorks.Desktop;

/// <summary>XAML localization: Text="{l:T Nav.Dashboard}". Updates automatically when the language changes.</summary>
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding { Source = LocEntry.For(Key), Path = nameof(LocEntry.Value), Mode = BindingMode.OneWay };
}

/// <summary>One shared, change-notifying source per resource key (lives for the process; there are about a thousand keys).</summary>
public sealed class LocEntry : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocEntry> Entries = new();
    private static readonly PropertyChangedEventArgs ValueChanged = new(nameof(Value));

    static LocEntry()
    {
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            List<LocEntry> all;
            lock (Entries) all = Entries.Values.ToList();
            foreach (var e in all) e.PropertyChanged?.Invoke(e, ValueChanged);
        };
    }

    private LocEntry(string key) => Key = key;

    public static LocEntry For(string key)
    {
        lock (Entries)
        {
            if (!Entries.TryGetValue(key, out var e)) Entries[key] = e = new LocEntry(key);
            return e;
        }
    }

    public string Key { get; }
    public string Value => Loc.Instance[Key];
    public event PropertyChangedEventHandler? PropertyChanged;
}
