using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace LaserWorks.Localization;

/// <summary>
/// Central string resources (Resources/ar.json, Resources/en.json). Supports runtime switching;
/// UI bindings listen to the "Item[]" indexer change notification.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Supported = { "ar", "en" };

    public static Loc Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _languages = new();
    private Dictionary<string, string> _current;
    private readonly Dictionary<string, string> _fallback;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private Loc()
    {
        foreach (var lang in Supported) _languages[lang] = Load(lang);
        _fallback = _languages["en"];
        _current = _languages["ar"];
        Language = "ar";
    }

    private static Dictionary<string, string> Load(string lang)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".{lang}.json", StringComparison.OrdinalIgnoreCase));
        if (name == null) return new();
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
    }

    public string Language { get; private set; }

    public bool IsRightToLeft => Language == "ar";

    /// <summary>Formatting culture: invariant digits and Gregorian yyyy-MM-dd dates in both languages (unambiguous for accounting).</summary>
    public CultureInfo Culture { get; } = CultureInfo.InvariantCulture;

    public IReadOnlyDictionary<string, string> Strings(string lang) => _languages[lang];

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (_current.TryGetValue(key, out var v)) return v;
        if (_fallback.TryGetValue(key, out var f)) { lock (_missing) _missing.Add(Language + ":" + key); return f; }
        lock (_missing) _missing.Add("*:" + key);
        return key;
    }

    private readonly HashSet<string> _missing = new();

    /// <summary>Keys requested at runtime that were missing in the current language ("lang:key") or everywhere ("*:key").</summary>
    public IReadOnlyCollection<string> MissingKeys { get { lock (_missing) return _missing.ToList(); } }

    /// <summary>
    /// Display name of a document/source type code stored on journal entries, cost entries and stock movements
    /// (e.g. "JobOperation" → "Operation" / "عملية تشغيل"). Codes are never shown raw.
    /// </summary>
    public string Source(string? code) => string.IsNullOrEmpty(code) ? "" : Has("Source." + code) ? Get("Source." + code) : Get("Source.Other");

    public bool Has(string key) => _current.ContainsKey(key) || _fallback.ContainsKey(key);

    public string Format(string key, params object?[] args)
    {
        var fmt = Get(key);
        try { return args.Length == 0 ? fmt : string.Format(Culture, fmt, args); }
        catch (FormatException) { return fmt + " " + string.Join(", ", args); }
    }

    /// <summary>Localized enum value: key "Enum.{Type}.{Value}".</summary>
    public string Enum(object value)
    {
        var key = $"Enum.{value.GetType().Name}.{value}";
        if (!Has(key)) lock (_missing) _missing.Add("*:" + key);
        return Has(key) ? Get(key) : value.ToString() ?? "";
    }

    public void SetLanguage(string lang)
    {
        if (!_languages.ContainsKey(lang)) lang = "en";
        Language = lang;
        _current = _languages[lang];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Left-to-right mark: keeps the minus sign and % in place inside right-to-left text.</summary>
    public const string Lrm = "\u200E";

    public string Money(decimal v, int decimals = 2) => Lrm + v.ToString("N" + decimals, Culture);
    public string Number(decimal v) => Lrm + v.ToString("#,0.###", Culture);
    public string Percent(decimal v) => Lrm + v.ToString("0.##", Culture) + "%";
    public string Date(DateTime? d) => d?.ToString("yyyy-MM-dd", Culture) ?? "";
}
