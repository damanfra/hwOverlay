using System.Windows;
using System.Windows.Media;

namespace HwOverlay.Views;

/// <summary>
/// Termômetro dos gauges de temperatura na barra de tarefas: tubo com bulbo embaixo,
/// a coluna sobe conforme a fração. O valor fica ao lado (não cabe dentro).
/// </summary>
public sealed class MiniThermometer : MiniIndicator
{
    protected override void OnRender(DrawingContext dc)
    {
        var h = ActualHeight;
        if (h <= 0 || ActualWidth <= 0) return;

        // Medidas pensadas para 26 px de altura (barra de 40 px) e escaladas a partir disso.
        var k = h / 26;
        var cx = ActualWidth / 2;
        var bulbY = h - 6 * k;

        // Trilho: tubo + bulbo numa geometria só, para o alfa do trilho não somar na sobreposição.
        dc.DrawGeometry(Track, null, Union(
            new RectangleGeometry(new Rect(cx - 2.6 * k, 2.5 * k, 5.2 * k, bulbY - 2.5 * k), 2.6 * k, 2.6 * k),
            new EllipseGeometry(new Point(cx, bulbY), 4.8 * k, 4.8 * k)));

        // Coluna: bulbo sempre cheio; o tubo enche de baixo (acima do bulbo) até o topo.
        var top = 3.5 * k;
        var bottom = h - 7.5 * k;
        var y = bottom - (bottom - top) * Math.Clamp(Fraction, 0, 1);
        dc.DrawGeometry(Stroke, null, Union(
            new RectangleGeometry(new Rect(cx - 1.3 * k, y, 2.6 * k, bulbY - y), 1.3 * k, 1.3 * k),
            new EllipseGeometry(new Point(cx, bulbY), 3.2 * k, 3.2 * k)));
    }

    private static GeometryGroup Union(params Geometry[] parts)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var part in parts) group.Children.Add(part);
        group.Freeze();
        return group;
    }
}
