using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LaserWorks.Desktop;

/// <summary>Draws a stroked line icon defined on a 24×24 grid using the current foreground brush.</summary>
public sealed class Ico : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Ico, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<Ico>();
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<Ico, double>(nameof(Thickness), 1.9);

    static Ico() => AffectsRender<Ico>(DataProperty, ForegroundProperty, ThicknessProperty);

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data == null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        if (scale <= 0) return;
        var pen = new Pen(Foreground ?? Brushes.Gray, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, Data);
    }
}
