using System.Windows;
using System.Windows.Media;

namespace HwOverlay.Views;

/// <summary>
/// Superfície "afundada" na barra de tarefas. O WPF não tem sombra interna, então ela é desenhada à mão:
/// <list type="bullet">
/// <item>interior liso, só um pouco mais escuro que a barra;</item>
/// <item>sombra interna esfumada colada na borda de cima (camadas sobrepostas imitam o desfoque);</item>
/// <item>linha de brilho de 1 px logo abaixo da borda inferior, por fora — a luz batendo no degrau.</item>
/// </list>
/// Um gradiente claro embaixo, por dentro, faz o contrário: parece botão saltado (primeira tentativa).
/// </summary>
public sealed class InsetSurface : FrameworkElement
{
    private const int ShadowLayers = 6;

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(InsetSurface),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(InsetSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
        nameof(FillOpacity), typeof(double), typeof(InsetSurface),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(
        nameof(ShadowColor), typeof(Color), typeof(InsetSurface),
        new FrameworkPropertyMetadata(Color.FromArgb(0x60, 0, 0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Brush), typeof(InsetSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Interior. Precisa de alfa &gt; 0 para receber o mouse (arrastar, menu).</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>0..1: intensidade do interior. Em 0 o rebaixo fica transparente (só contorno e sombra).</summary>
    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    /// <summary>Cor da sombra na borda de cima (o alfa é o máximo, junto à borda).</summary>
    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    public Brush? Highlight
    {
        get => (Brush?)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 4 || h < 4) return;

        var radius = Math.Min(CornerRadius, Math.Min(w, h) / 2);
        var bounds = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
        bounds.Freeze();

        // Brilho por fora: a parte da forma deslocada 1 px para baixo que fica fora da forma original.
        if (Highlight is { } highlight)
        {
            var below = new RectangleGeometry(new Rect(0, 1, w, h), radius, radius);
            dc.DrawGeometry(highlight, null, Geometry.Combine(below, bounds, GeometryCombineMode.Exclude, null));
        }

        if (Fill is { } fill && FillOpacity > 0.001)
        {
            dc.PushOpacity(Math.Clamp(FillOpacity, 0, 1));
            dc.DrawGeometry(fill, null, bounds);
            dc.Pop();
        }

        // Sombra interna: cada camada é "a forma menos um buraco" — buraco menor e mais deslocado para
        // baixo a cada camada. Junto à borda de cima todas as camadas se somam (sombra cheia); para dentro
        // e para baixo sobram cada vez menos, o que dá o degradê de um desfoque.
        dc.PushClip(bounds);
        var layer = ShadowColor;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(layer.A / ShadowLayers), layer.R, layer.G, layer.B));
        brush.Freeze();

        for (var k = 1; k <= ShadowLayers; k++)
        {
            var spread = k * 0.6;  // laterais: até ~3,6 px
            var drop = k * 0.55;   // topo ganha mais: a luz vem de cima
            var hole = new Rect(spread, spread + drop, Math.Max(0, w - 2 * spread), Math.Max(0, h - 2 * spread));
            var holeRadius = Math.Max(0, radius - spread);
            var ring = Geometry.Combine(bounds, new RectangleGeometry(hole, holeRadius, holeRadius), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(brush, null, ring);
        }

        dc.Pop();
    }
}
