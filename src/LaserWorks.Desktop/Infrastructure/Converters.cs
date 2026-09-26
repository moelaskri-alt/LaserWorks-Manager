using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LaserWorks.Localization;

namespace LaserWorks.Desktop;

public static class Conv
{
    public static readonly IValueConverter Enum = new OneWay<object?, string>(v => v == null ? "" : Loc.Instance.Enum(v));
    public static readonly IValueConverter Money = new OneWay<object?, string>(v => v switch { decimal d => Loc.Instance.Money(d), null => "", _ => v.ToString() ?? "" });
    public static readonly IValueConverter Number = new OneWay<object?, string>(v => v switch { decimal d => Loc.Instance.Number(d), null => "", _ => v.ToString() ?? "" });
    public static readonly IValueConverter Percent = new OneWay<object?, string>(v => v switch { decimal d => Loc.Instance.Percent(d), null => "", _ => v.ToString() ?? "" });
    /// <summary>Document / source type code → translated name (never the raw code).</summary>
    public static readonly IValueConverter Source = new OneWay<object?, string>(v => Loc.Instance.Source(v as string));
    public static readonly IValueConverter EntityName = new OneWay<object?, string>(v => Loc.Instance.EntityName(v as string));
    public static readonly IValueConverter SequenceName = new OneWay<object?, string>(v => Loc.Instance.SequenceName(v as string));
    public static readonly IValueConverter AuditDetails = new OneWay<object?, string>(v => AuditText.Format(v as string));
    public static readonly IValueConverter Date = new OneWay<object?, string>(v => v switch { DateTime d => Loc.Instance.Date(d), _ => "" });
    public static readonly IValueConverter DateTimeShort = new OneWay<object?, string>(v => v switch { DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), _ => "" });
    /// <summary>Name of a bilingual record (account, unit) in the current UI language.</summary>
    public static readonly IValueConverter BiName = new OneWay<object?, string>(v =>
    {
        var ar = Loc.Instance.IsRightToLeft;
        return v switch
        {
            LaserWorks.Domain.Entities.Account a => ar ? a.NameAr : a.NameEn,
            LaserWorks.Domain.Entities.UnitOfMeasure u => ar ? u.NameAr : u.NameEn,
            LaserWorks.Application.Services.AccountRow r => ar ? r.NameAr : r.NameEn,
            null => "",
            _ => v.ToString() ?? ""
        };
    });
    public static readonly IValueConverter YesNo = new OneWay<bool, string>(v => v ? Loc.Instance["Common.Yes"] : Loc.Instance["Common.No"]);
    public static readonly IValueConverter NotNull = new OneWay<object?, bool>(v => v != null);
    public static readonly IValueConverter IsNull = new OneWay<object?, bool>(v => v == null);
    public static readonly IValueConverter NotEmpty = new OneWay<string?, bool>(v => !string.IsNullOrWhiteSpace(v));
    public static readonly IValueConverter Not = new OneWay<bool, bool>(v => !v);
    public static readonly IValueConverter Positive = new OneWay<object?, bool>(v => v switch { decimal d => d > 0, int i => i > 0, _ => false });
    public static readonly IValueConverter Negative = new OneWay<object?, bool>(v => v is decimal d && d < 0);
    public static readonly IValueConverter FileSize = new OneWay<long, string>(v => v < 1024 ? $"{v} B" : v < 1024 * 1024 ? $"{v / 1024.0:0.#} KB" : $"{v / 1024.0 / 1024.0:0.#} MB");

    /// <summary>Red text for negative amounts, green for positive (profit), default otherwise.</summary>
    public static readonly IValueConverter SignBrush = new OneWay<object?, IBrush?>(v =>
    {
        var key = v is decimal d ? d < 0 ? "DangerBrush" : d > 0 ? "SuccessBrush" : "TextBrush" : "TextBrush";
        return Resource(key);
    });

    /// <summary>Red when positive (cost over budget), green when negative.</summary>
    public static readonly IValueConverter VarianceBrush = new OneWay<object?, IBrush?>(v =>
    {
        var key = v is decimal d ? d > 0 ? "DangerBrush" : d < 0 ? "SuccessBrush" : "TextBrush" : "TextBrush";
        return Resource(key);
    });

    public static IBrush? Resource(string key)
    {
        var app = Avalonia.Application.Current;
        if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out var r) && r is IBrush b) return b;
        return Brushes.Gray;
    }

    /// <summary>Localizes "{parameter}.{value}", e.g. parameter "Period" and value "ThisMonth".</summary>
    public static readonly IValueConverter Prefixed = new OneWay<object?, string?, string>((v, p) => v == null ? "" : Loc.Instance[$"{p}.{v}"]);

    public static readonly IValueConverter LocKey = new OneWay<string?, string>(k => k == null ? "" : Loc.Instance[k]);
    public static readonly IValueConverter DialogMaxHeight = new OneWay<double, double>(h => Math.Max(300, h - 60));

    public static readonly IValueConverter FlowDirection = new OneWay<bool, FlowDirection>(rtl => rtl ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight);
}

/// <summary>Two-way "yyyy-MM-dd HH:mm" text ⇄ DateTime? converter for time entry fields.</summary>
public sealed class DateTimeTextConverter : IValueConverter
{
    public static readonly DateTimeTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : new Avalonia.Data.BindingNotification(new FormatException("yyyy-MM-dd HH:mm"), Avalonia.Data.BindingErrorType.DataValidationError);
    }
}

/// <summary>
/// Display-only converter. Unlike FuncValueConverter, ConvertBack returns DoNothing, so two-way
/// bindings (e.g. DataGrid text columns) never try to parse formatted text back into the source.
/// </summary>
public sealed class OneWay<TIn, TOut> : IValueConverter
{
    private readonly Func<TIn, TOut> _convert;
    public OneWay(Func<TIn, TOut> convert) => _convert = convert;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TIn v) return _convert(v);
        if (value == null && default(TIn) == null) return _convert(default!);
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

public sealed class OneWay<TIn, TParam, TOut> : IValueConverter
{
    private readonly Func<TIn, TParam, TOut> _convert;
    public OneWay(Func<TIn, TParam, TOut> convert) => _convert = convert;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var p = parameter is TParam tp ? tp : default!;
        if (value is TIn v) return _convert(v, p);
        if (value == null && default(TIn) == null) return _convert(default!, p);
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
