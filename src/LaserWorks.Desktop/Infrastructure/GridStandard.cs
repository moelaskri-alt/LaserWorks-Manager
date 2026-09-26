using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using LaserWorks.Localization;

namespace LaserWorks.Desktop;

/// <summary>
/// The application-wide DataGrid standard, switched on for every grid by the global DataGrid style:
/// <list type="bullet">
/// <item>no truncated column header, in any language: headers wrap instead of trimming, each column's minimum width grows to fit
/// the longest word of its header (recomputed when the language changes), and the header also shows as a tooltip;</item>
/// <item>an empty-state message (or a loading message while the page is busy) over a grid without rows;</item>
/// <item>the first column of list pages stays frozen while scrolling horizontally.</item>
/// </list>
/// Sorting, resizing, reordering, scrolling and column visibility come from the DataGrid style and <see cref="ColumnChooser"/>.
/// </summary>
public static class GridStandard
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enabled", typeof(GridStandard));
    /// <summary>Resource key of the message shown when the grid has no rows (default Grid.Empty).</summary>
    public static readonly AttachedProperty<string?> EmptyTextProperty = AvaloniaProperty.RegisterAttached<DataGrid, string?>("EmptyText", typeof(GridStandard));

    public static bool GetEnabled(DataGrid g) => g.GetValue(EnabledProperty);
    public static void SetEnabled(DataGrid g, bool v) => g.SetValue(EnabledProperty, v);
    public static string? GetEmptyText(DataGrid g) => g.GetValue(EmptyTextProperty);
    public static void SetEmptyText(DataGrid g, string? v) => g.SetValue(EmptyTextProperty, v);

    /// <summary>Header font metrics (must match the DataGridColumnHeader style) and the room taken by padding, sort glyph and resize grip.</summary>
    private const double HeaderFontSize = 12;
    public const double HeaderChrome = 16 + 34;

    private static readonly ConditionalWeakTable<DataGridColumn, StrongBox<double>> AuthorMinWidth = new();
    private static readonly ConditionalWeakTable<DataGrid, State> States = new();

    private sealed class State
    {
        public TextBlock? Overlay;
        public INotifyCollectionChanged? Items;
        public INotifyPropertyChanged? Context;
        public NotifyCollectionChangedEventHandler? ItemsHandler;
        public PropertyChangedEventHandler? ContextHandler;
    }

    static GridStandard()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (e.NewValue is not true || States.TryGetValue(g, out _)) return;
            States.Add(g, new State());
            g.TemplateApplied += (_, te) => AddOverlay(g, te.NameScope);
            g.AttachedToVisualTree += (_, _) => Attach(g);
            g.DetachedFromVisualTree += (_, _) => Detach(g);
            if (g.IsAttachedToVisualTree()) Attach(g);
        });
        // queued on the UI thread and contained per grid: re-fitting headers must never break a language switch
        Loc.Instance.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var g in Live())
            {
                try { FitAll(g); UpdateOverlay(g); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Grid header refit failed for {Grid}", g.Name); }
            }
        });
    }

    private static readonly List<WeakReference<DataGrid>> Attached = new();

    private static IEnumerable<DataGrid> Live()
    {
        lock (Attached)
        {
            Attached.RemoveAll(w => !w.TryGetTarget(out _));
            return Attached.Select(w => w.TryGetTarget(out var g) ? g : null).OfType<DataGrid>().ToList();
        }
    }

    private static void Attach(DataGrid g)
    {
        lock (Attached)
            if (!Attached.Any(w => w.TryGetTarget(out var x) && ReferenceEquals(x, g))) Attached.Add(new WeakReference<DataGrid>(g));
        if (GridBehaviors.GetServerSort(g) && g.FrozenColumnCount == 0 && g.Columns.Count > 3) g.FrozenColumnCount = 1;
        foreach (var c in g.Columns) Track(g, c);
        g.Columns.CollectionChanged -= OnColumnsChanged;
        g.Columns.CollectionChanged += OnColumnsChanged;
        g.PropertyChanged -= OnGridPropertyChanged;
        g.PropertyChanged += OnGridPropertyChanged;
        FitAll(g);
        HookItems(g);
        HookContext(g);
        UpdateOverlay(g);
    }

    private static void Detach(DataGrid g)
    {
        lock (Attached) Attached.RemoveAll(w => !w.TryGetTarget(out var x) || ReferenceEquals(x, g));
        if (!States.TryGetValue(g, out var s)) return;
        if (s.Items != null) s.Items.CollectionChanged -= s.ItemsHandler;
        if (s.Context != null) s.Context.PropertyChanged -= s.ContextHandler;
        s.Items = null; s.Context = null;
    }

    private static void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null) return;
        foreach (var g in Live().Where(g => ReferenceEquals(g.Columns, sender)))
            foreach (DataGridColumn c in e.NewItems) { Track(g, c); Fit(g, c); }
    }

    private static void Track(DataGrid g, DataGridColumn c)
    {
        if (AuthorMinWidth.TryGetValue(c, out _)) return;
        AuthorMinWidth.Add(c, new StrongBox<double>(double.IsNaN(c.MinWidth) ? 0 : c.MinWidth));
        // amounts, quantities and percentages line up at the end of the cell (mirrored in right-to-left)
        if (c is DataGridBoundColumn { Binding: { } b } && b.GetType().GetProperty("Converter")?.GetValue(b) is { } conv
            && (ReferenceEquals(conv, Conv.Number) || ReferenceEquals(conv, Conv.Money) || ReferenceEquals(conv, Conv.Percent))
            && !c.CellStyleClasses.Contains("num"))
            c.CellStyleClasses.Add("num");
        c.PropertyChanged += (_, e) => { if (e.Property == DataGridColumn.HeaderProperty) Fit(g, c); };
    }

    private static void FitAll(DataGrid g) { foreach (var c in g.Columns) Fit(g, c); }

    /// <summary>Raises the column's minimum width so its full header text fits (never below the width the view asked for).</summary>
    private static void Fit(DataGrid g, DataGridColumn c)
    {
        if (!AuthorMinWidth.TryGetValue(c, out var author)) return;
        var text = c.Header as string;
        if (string.IsNullOrEmpty(text)) return;
        // headers wrap onto several lines (see the DataGridColumnHeader style), so the column must fit the longest word
        var need = Math.Ceiling(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Max(w => MeasureHeader(g, w)) + HeaderChrome);
        var min = Math.Max(author.Value, need);
        if (Math.Abs(c.MinWidth - min) > 0.5) c.MinWidth = min;
        if (c.Width.IsAbsolute && c.Width.Value < min) c.Width = new DataGridLength(min);
    }

    /// <summary>Width of cell text in the grid's cell font (used to size generated report columns to their content).</summary>
    public static double MeasureCell(Control g, string text, bool bold = false)
    {
        using var layout = new TextLayout(text, new Typeface(g is TemplatedControl tc ? tc.FontFamily : FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), 13, null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    public static double MeasureHeader(Control g, string text)
    {
        using var layout = new TextLayout(text, new Typeface(g is TemplatedControl tc ? tc.FontFamily : FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), HeaderFontSize, null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    private static void OnGridPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not DataGrid g) return;
        if (e.Property == DataGrid.ItemsSourceProperty) { HookItems(g); UpdateOverlay(g); }
        else if (e.Property == StyledElement.DataContextProperty) { HookContext(g); UpdateOverlay(g); }
        else if (e.Property == Visual.IsVisibleProperty || e.Property == EmptyTextProperty) UpdateOverlay(g);
    }

    private static void HookItems(DataGrid g)
    {
        if (!States.TryGetValue(g, out var s)) return;
        s.ItemsHandler ??= (_, _) => UiThread.Run(() => UpdateOverlay(g));
        if (s.Items != null) s.Items.CollectionChanged -= s.ItemsHandler;
        s.Items = g.ItemsSource as INotifyCollectionChanged;
        if (s.Items != null) s.Items.CollectionChanged += s.ItemsHandler;
    }

    private static void HookContext(DataGrid g)
    {
        if (!States.TryGetValue(g, out var s)) return;
        s.ContextHandler ??= (_, e) => { if (e.PropertyName is "IsBusy") UiThread.Run(() => UpdateOverlay(g)); };
        if (s.Context != null) s.Context.PropertyChanged -= s.ContextHandler;
        s.Context = g.DataContext as INotifyPropertyChanged;
        if (s.Context != null) s.Context.PropertyChanged += s.ContextHandler;
    }

    private static bool IsEmpty(IEnumerable? items)
    {
        if (items == null) return true;
        if (items is ICollection c) return c.Count == 0;
        var en = items.GetEnumerator();
        try { return !en.MoveNext(); }
        finally { (en as IDisposable)?.Dispose(); }
    }

    /// <summary>Places the empty/loading message in the grid's own template, over the rows area (below the column headers).</summary>
    private static void AddOverlay(DataGrid g, INameScope scope)
    {
        if (!States.TryGetValue(g, out var s)) return;
        var rows = scope.Find<Control>("PART_RowsPresenter");
        if (rows?.Parent is not Grid host) return;
        s.Overlay = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 460, IsHitTestVisible = false, Margin = new Thickness(16, 24, 16, 16), IsVisible = false, Classes = { "muted", "grid-empty" }
        };
        Grid.SetRow(s.Overlay, Grid.GetRow(rows));
        Grid.SetRowSpan(s.Overlay, Math.Max(1, Grid.GetRowSpan(rows)));
        Grid.SetColumn(s.Overlay, 0);
        Grid.SetColumnSpan(s.Overlay, Math.Max(1, host.ColumnDefinitions.Count));
        host.Children.Add(s.Overlay);
        UpdateOverlay(g);
    }

    private static void UpdateOverlay(DataGrid g)
    {
        if (!States.TryGetValue(g, out var s) || s.Overlay == null) return;
        var busy = g.DataContext is ViewModels.ViewModelBase { IsBusy: true };
        s.Overlay.IsVisible = IsEmpty(g.ItemsSource);
        if (s.Overlay.IsVisible) s.Overlay.Text = Loc.Instance[busy ? "Grid.Loading" : GetEmptyText(g) ?? "Grid.Empty"];
    }

    private static bool IsAttachedToVisualTree(this Visual v) => v.GetVisualRootSafe() != null;
    private static Visual? GetVisualRootSafe(this Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualRoot(v) as Visual;
}
