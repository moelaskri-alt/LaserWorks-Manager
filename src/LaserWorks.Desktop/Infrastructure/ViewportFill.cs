using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace LaserWorks.Desktop;

/// <summary>
/// Hosts a page inside a vertical ScrollViewer: the page fills the visible height exactly (so its grids scroll internally),
/// but never gets shorter than <see cref="MinFillHeight"/> — on a small screen the whole page scrolls instead of squeezing
/// grids and panels to nothing. Every button and grid therefore stays reachable at any window size.
/// </summary>
public sealed class ViewportFill : Decorator
{
    public static readonly StyledProperty<double> MinFillHeightProperty = AvaloniaProperty.Register<ViewportFill, double>(nameof(MinFillHeight), 520);

    public double MinFillHeight { get => GetValue(MinFillHeightProperty); set => SetValue(MinFillHeightProperty, value); }

    private ScrollViewer? _owner;

    static ViewportFill() => AffectsMeasure<ViewportFill>(MinFillHeightProperty);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = this.FindAncestorOfType<ScrollViewer>();
        if (_owner != null) _owner.SizeChanged += OnOwnerResized;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_owner != null) _owner.SizeChanged -= OnOwnerResized;
        _owner = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerResized(object? sender, SizeChangedEventArgs e) => InvalidateMeasure();

    private double Target(Size available)
    {
        var viewport = _owner?.Bounds.Height ?? 0;
        if (viewport <= 0 || double.IsInfinity(viewport)) viewport = double.IsInfinity(available.Height) ? MinFillHeight : available.Height;
        return Math.Max(MinFillHeight, viewport - Margin.Top - Margin.Bottom);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Target(availableSize);
        Child?.Measure(new Size(availableSize.Width, h));
        return new Size(Child?.DesiredSize.Width ?? 0, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
