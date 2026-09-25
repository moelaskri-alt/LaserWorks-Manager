using System.Collections;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LaserWorks.Application.Services;
using LaserWorks.Localization;

namespace LaserWorks.Desktop.Controls;

public enum ChartKind { Column, Line, HorizontalBar, Donut }

/// <summary>
/// Lightweight chart drawn from real data (no sample data). Supports column, line, horizontal bar and donut.
/// Clicking a bar/slice executes <see cref="PointCommand"/> with the <see cref="ChartPoint"/> (drill-down).
/// </summary>
public sealed class Chart : Control
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<Chart, IEnumerable?>(nameof(Items));
    public static readonly StyledProperty<ChartKind> KindProperty = AvaloniaProperty.Register<Chart, ChartKind>(nameof(Kind));
    public static readonly StyledProperty<ICommand?> PointCommandProperty = AvaloniaProperty.Register<Chart, ICommand?>(nameof(PointCommand));
    public static readonly StyledProperty<bool> LocalizeLabelsProperty = AvaloniaProperty.Register<Chart, bool>(nameof(LocalizeLabels));
    public static readonly StyledProperty<string?> LabelPrefixProperty = AvaloniaProperty.Register<Chart, string?>(nameof(LabelPrefix));

    private readonly List<(Rect Rect, ChartPoint Point)> _hit = new();
    private readonly List<(double From, double To, ChartPoint Point)> _slices = new();
    private int _hover = -1;

    static Chart()
    {
        AffectsRender<Chart>(ItemsProperty, KindProperty);
    }

    public Chart()
    {
        ClipToBounds = true;
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public ChartKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public ICommand? PointCommand { get => GetValue(PointCommandProperty); set => SetValue(PointCommandProperty, value); }
    /// <summary>When set, labels are treated as enum names and localized with the prefix, e.g. "Enum.JobStatus.".</summary>
    public string? LabelPrefix { get => GetValue(LabelPrefixProperty); set => SetValue(LabelPrefixProperty, value); }
    public bool LocalizeLabels { get => GetValue(LocalizeLabelsProperty); set => SetValue(LocalizeLabelsProperty, value); }

    private List<ChartPoint> Points => Items?.OfType<ChartPoint>().ToList() ?? new();

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    private Color Palette(int i)
    {
        var key = $"ChartColor{i % 7 + 1}";
        return this.TryFindResource(key, ActualThemeVariant, out var r) && r is Color c ? c : Colors.SteelBlue;
    }

    private string Label(ChartPoint p)
    {
        if (LabelPrefix != null && Loc.Instance.Has(LabelPrefix + p.Label)) return Loc.Instance[LabelPrefix + p.Label];
        return p.Label;
    }

    private static string Short(decimal v)
    {
        var a = Math.Abs(v);
        return a >= 1_000_000 ? (v / 1_000_000m).ToString("0.#M", CultureInfo.InvariantCulture)
            : a >= 10_000 ? (v / 1000m).ToString("0.#K", CultureInfo.InvariantCulture)
            : v.ToString("#,0.##", CultureInfo.InvariantCulture);
    }

    private FormattedText Text(string s, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(AppFonts.LatinUri + ", " + AppFonts.ArabicUri), FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);

    public override void Render(DrawingContext ctx)
    {
        _hit.Clear();
        _slices.Clear();
        var pts = Points;
        var text = Res("TextBrush", Brushes.Black);
        var muted = Res("MutedBrush", Brushes.Gray);
        var grid = Res("ChartGridBrush", Brushes.LightGray);
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        if (pts.Count == 0 || pts.All(p => p.Value == 0))
        {
            var t = Text(Loc.Instance["Chart.NoData"], 12, muted);
            ctx.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            return;
        }
        switch (Kind)
        {
            case ChartKind.Donut: RenderDonut(ctx, pts, text, muted); break;
            case ChartKind.HorizontalBar: RenderHBar(ctx, pts, text, muted, grid); break;
            default: RenderColumns(ctx, pts, text, muted, grid, Kind == ChartKind.Line); break;
        }
    }

    private void RenderColumns(DrawingContext ctx, List<ChartPoint> pts, IBrush text, IBrush muted, IBrush grid, bool line)
    {
        const double left = 52, bottom = 26, top = 10, right = 10;
        var w = Bounds.Width - left - right;
        var h = Bounds.Height - top - bottom;
        if (w <= 10 || h <= 10) return;
        var max = (double)Math.Max(pts.Max(p => p.Value), 0);
        var min = (double)Math.Min(pts.Min(p => p.Value), 0);
        if (max == min) max = min + 1;
        var range = NiceRange(min, max);
        double Y(double v) => top + h - (v - range.Min) / (range.Max - range.Min) * h;
        var gpen = new Pen(grid, 1);
        for (var i = 0; i <= 4; i++)
        {
            var v = range.Min + (range.Max - range.Min) * i / 4;
            var y = Y(v);
            ctx.DrawLine(gpen, new Point(left, y), new Point(left + w, y));
            var t = Text(Short((decimal)v), 10, muted);
            ctx.DrawText(t, new Point(left - t.Width - 6, y - t.Height / 2));
        }
        var n = pts.Count;
        var slot = w / n;
        var every = Math.Max(1, (int)Math.Ceiling(n / (w / 56)));
        var color = Palette(0);
        var brush = new SolidColorBrush(color);
        if (line)
        {
            var geo = new StreamGeometry();
            var area = new StreamGeometry();
            using (var g = geo.Open())
            using (var a = area.Open())
            {
                for (var i = 0; i < n; i++)
                {
                    var p = new Point(left + slot * i + slot / 2, Y((double)pts[i].Value));
                    if (i == 0) { g.BeginFigure(p, false); a.BeginFigure(new Point(p.X, Y(Math.Max(range.Min, 0))), true); a.LineTo(p); }
                    else { g.LineTo(p); a.LineTo(p); }
                }
                a.LineTo(new Point(left + slot * (n - 1) + slot / 2, Y(Math.Max(range.Min, 0))));
                a.EndFigure(true);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(new SolidColorBrush(color, 0.15), null, area);
            ctx.DrawGeometry(null, new Pen(brush, 2.2, lineJoin: PenLineJoin.Round), geo);
            for (var i = 0; i < n; i++)
            {
                var c = new Point(left + slot * i + slot / 2, Y((double)pts[i].Value));
                ctx.DrawEllipse(i == _hover ? brush : Res("SurfaceBrush", Brushes.White), new Pen(brush, 1.6), c, 3.2, 3.2);
                _hit.Add((new Rect(left + slot * i, top, slot, h), pts[i]));
            }
        }
        else
        {
            var bw = Math.Max(3, Math.Min(46, slot * 0.62));
            for (var i = 0; i < n; i++)
            {
                var v = (double)pts[i].Value;
                var y0 = Y(0); var y1 = Y(v);
                var r = new Rect(left + slot * i + (slot - bw) / 2, Math.Min(y0, y1), bw, Math.Max(1, Math.Abs(y1 - y0)));
                var fill = v < 0 ? Res("DangerBrush", Brushes.IndianRed) : new SolidColorBrush(color, i == _hover ? 1 : 0.88);
                ctx.DrawRectangle(fill, null, r, 3, 3);
                _hit.Add((new Rect(left + slot * i, top, slot, h), pts[i]));
            }
        }
        for (var i = 0; i < n; i += every)
        {
            var t = Text(Label(pts[i]), 10, muted);
            var x = left + slot * i + slot / 2 - t.Width / 2;
            ctx.DrawText(t, new Point(Math.Max(left, x), top + h + 6));
        }
        if (_hover >= 0 && _hover < n)
        {
            var p = pts[_hover];
            var tip = Text($"{Label(p)}: {Loc.Instance.Money(p.Value)}", 11, text, true);
            var x = Math.Min(Bounds.Width - tip.Width - 8, Math.Max(left, left + slot * _hover + slot / 2 - tip.Width / 2));
            var rect = new Rect(x - 6, top, tip.Width + 12, tip.Height + 6);
            ctx.DrawRectangle(Res("SurfaceBrush", Brushes.White), new Pen(grid, 1), rect, 4, 4);
            ctx.DrawText(tip, new Point(x, top + 3));
        }
    }

    private void RenderHBar(DrawingContext ctx, List<ChartPoint> pts, IBrush text, IBrush muted, IBrush grid)
    {
        var n = pts.Count;
        var rowH = Math.Min(30, Bounds.Height / n);
        var labelW = Math.Min(170, Bounds.Width * 0.38);
        var valueW = 76;
        var barW = Bounds.Width - labelW - valueW - 12;
        var max = (double)pts.Max(p => Math.Abs(p.Value));
        if (max == 0) max = 1;
        for (var i = 0; i < n; i++)
        {
            var y = i * rowH;
            var p = pts[i];
            var lbl = Text(Label(p), 11.5, text) ;
            lbl.MaxTextWidth = labelW - 8;
            lbl.Trimming = TextTrimming.CharacterEllipsis;
            lbl.MaxLineCount = 1;
            ctx.DrawText(lbl, new Point(0, y + (rowH - lbl.Height) / 2));
            var len = Math.Max(2, Math.Abs((double)p.Value) / max * barW);
            var fill = p.Value < 0 ? Res("DangerBrush", Brushes.IndianRed) : new SolidColorBrush(Palette(i), i == _hover ? 1 : 0.85);
            var r = new Rect(labelW, y + rowH * 0.2, len, rowH * 0.6);
            ctx.DrawRectangle(fill, null, r, 3, 3);
            var v = Text(Short(p.Value), 11, muted);
            ctx.DrawText(v, new Point(labelW + len + 6, y + (rowH - v.Height) / 2));
            _hit.Add((new Rect(0, y, Bounds.Width, rowH), p));
        }
    }

    private void RenderDonut(DrawingContext ctx, List<ChartPoint> pts, IBrush text, IBrush muted)
    {
        var positive = pts.Where(p => p.Value > 0).ToList();
        var total = (double)positive.Sum(p => p.Value);
        if (total <= 0) return;
        var size = Math.Min(Bounds.Height, Bounds.Width * 0.45) - 8;
        var center = new Point(size / 2 + 4, Bounds.Height / 2);
        var ro = size / 2; var ri = ro * 0.58;
        double angle = -Math.PI / 2;
        for (var i = 0; i < positive.Count; i++)
        {
            var sweep = (double)positive[i].Value / total * Math.PI * 2;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var a0 = angle; var a1 = angle + Math.Max(sweep - 0.01, 0.001);
                Point P(double r, double a) => new(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
                g.BeginFigure(P(ro, a0), true);
                g.ArcTo(P(ro, a1), new Size(ro, ro), 0, sweep > Math.PI, SweepDirection.Clockwise);
                g.LineTo(P(ri, a1));
                g.ArcTo(P(ri, a0), new Size(ri, ri), 0, sweep > Math.PI, SweepDirection.CounterClockwise);
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(Palette(i), i == _hover ? 1 : 0.9), null, geo);
            _slices.Add((angle, angle + sweep, positive[i]));
            angle += sweep;
        }
        var totalText = Text(Short((decimal)total), 13, text, true);
        ctx.DrawText(totalText, new Point(center.X - totalText.Width / 2, center.Y - totalText.Height / 2));
        var lx = center.X + ro + 16;
        var rowH = Math.Min(22, Bounds.Height / Math.Max(1, positive.Count));
        var y0 = center.Y - rowH * positive.Count / 2;
        for (var i = 0; i < positive.Count; i++)
        {
            var y = y0 + i * rowH;
            ctx.DrawRectangle(new SolidColorBrush(Palette(i)), null, new Rect(lx, y + rowH / 2 - 5, 10, 10), 2, 2);
            var pct = (double)positive[i].Value / total * 100;
            // value and label are drawn as separate runs so mixed Arabic text and digits never reorder
            var v = Text(pct.ToString("0.#", CultureInfo.InvariantCulture) + "%", 11, muted);
            ctx.DrawText(v, new Point(lx + 16, y + (rowH - v.Height) / 2));
            var t = Text(Label(positive[i]), 11, text);
            t.MaxTextWidth = Math.Max(20, Bounds.Width - lx - 30 - 44);
            t.MaxLineCount = 1;
            t.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(t, new Point(lx + 16 + 44, y + (rowH - t.Height) / 2));
            _hit.Add((new Rect(lx, y, Bounds.Width - lx, rowH), positive[i]));
        }
    }

    private static (double Min, double Max) NiceRange(double min, double max)
    {
        var span = max - min;
        var step = Math.Pow(10, Math.Floor(Math.Log10(span / 4)));
        var err = span / 4 / step;
        step *= err >= 7.5 ? 10 : err >= 3.5 ? 5 : err >= 1.5 ? 2 : 1;
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
    }

    private ChartPoint? HitTest(Point p)
    {
        foreach (var (r, pt) in _hit) if (r.Contains(p)) return pt;
        if (_slices.Count > 0)
        {
            var size = Math.Min(Bounds.Height, Bounds.Width * 0.45) - 8;
            var c = new Point(size / 2 + 4, Bounds.Height / 2);
            var d = p - c;
            var dist = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (dist <= size / 2 && dist >= size / 2 * 0.58)
            {
                var a = Math.Atan2(d.Y, d.X);
                if (a < -Math.PI / 2) a += Math.PI * 2;
                foreach (var s in _slices) if (a >= s.From && a < s.To) return s.Point;
            }
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        var idx = hit == null ? -1 : Points.IndexOf(hit);
        if (Kind == ChartKind.Donut && hit != null) idx = Points.Where(p => p.Value > 0).ToList().IndexOf(hit);
        if (idx != _hover) { _hover = idx; InvalidateVisual(); }
        ToolTip.SetTip(this, hit == null ? null : $"{Label(hit)}: {Loc.Instance.Money(hit.Value)}");
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit != null && PointCommand?.CanExecute(hit) == true) PointCommand.Execute(hit);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty && change.NewValue is System.Collections.Specialized.INotifyCollectionChanged ncc)
            ncc.CollectionChanged += (_, _) => InvalidateVisual();
    }
}
