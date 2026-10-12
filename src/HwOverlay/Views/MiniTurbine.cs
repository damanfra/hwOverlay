using System.Windows;
using System.Windows.Media;

namespace HwOverlay.Views;

/// <summary>
/// Anel de "pás" inclinadas (turbina) dos gauges de ventoinha na barra de tarefas.
/// As pás acendem no sentido horário, a partir do topo, conforme a fração; o valor fica no centro.
/// </summary>
public sealed class MiniTurbine : MiniIndicator
{
    private const int Blades = 12;
    private const double Tilt = 14; // graus entre a ponta interna e a externa da pá

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;

        var k = size / 26;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var inner = size / 2 - 3.4 * k;
        var outer = size / 2 - 0.3 * k;
        var fraction = Math.Clamp(Fraction, 0, 1);

        var lit = new StreamGeometry();
        var unlit = new StreamGeometry();
        using (var litCtx = lit.Open())
        using (var unlitCtx = unlit.Open())
        {
            for (var i = 0; i < Blades; i++)
            {
                var angle = -90 + i * 360.0 / Blades;
                var ctx = (i + 0.5) / Blades <= fraction ? litCtx : unlitCtx;
                ctx.BeginFigure(At(center, inner, angle), isFilled: false, isClosed: false);
                ctx.LineTo(At(center, outer, angle + Tilt), isStroked: true, isSmoothJoin: false);
            }
        }

        lit.Freeze();
        unlit.Freeze();

        var width = Thickness * 0.77;
        dc.DrawGeometry(null, MakePen(Track, width), unlit);
        dc.DrawGeometry(null, MakePen(Stroke, width), lit);
    }

    private static Pen MakePen(Brush brush, double width) => new(brush, width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static Point At(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
