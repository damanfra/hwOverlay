using System.Windows;
using System.Windows.Media;

namespace HwOverlay.Views;

/// <summary>
/// Arco de 270° desenhado direto no WPF (mesmo formato do gauge "Arco"), para os mini-gauges
/// da barra de tarefas — bem mais leve que um gráfico do LiveCharts por item.
/// </summary>
public sealed class MiniArc : FrameworkElement
{
    private const double StartAngle = 135; // canto inferior esquerdo (y cresce para baixo)
    private const double Sweep = 270;

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(MiniArc),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(MiniArc),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(MiniArc),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(MiniArc),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;

        dc.DrawGeometry(null, MakePen(Track), Arc(center, radius, Sweep));

        var sweep = Sweep * Math.Clamp(Fraction, 0, 1);
        if (sweep > 0.5) dc.DrawGeometry(null, MakePen(Stroke), Arc(center, radius, sweep));
    }

    private Pen MakePen(Brush brush) => new(brush, Thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static StreamGeometry Arc(Point center, double radius, double sweep)
    {
        static Point At(Point c, double r, double degrees)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(center, radius, StartAngle), isFilled: false, isClosed: false);
            ctx.ArcTo(At(center, radius, StartAngle + sweep), new Size(radius, radius), 0,
                isLargeArc: sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}
