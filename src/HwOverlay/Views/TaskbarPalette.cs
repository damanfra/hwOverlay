using System.Windows.Media;

namespace HwOverlay.Views;

/// <summary>
/// Cores dos mini-gauges da barra de tarefas. Sem fundo próprio, o texto precisa contrastar
/// com a própria barra — clara ou escura conforme o tema do Windows.
/// </summary>
public sealed class TaskbarPalette
{
    /// <summary>Para fundo escuro (barra no tema escuro ou "pílula" do overlay).</summary>
    public static TaskbarPalette Dark { get; } = new(
        text: "#E6EAF0", label: "#C7CED8", muted: "#8C96A5", track: "#28FFFFFF",
        accent: "#3DDC97", warning: "#FFB547", critical: "#FF5C5C", noData: "#6B7582",
        insetFill: "#38000000", insetShadow: "#C0000000", insetHighlight: "#38FFFFFF");

    /// <summary>Para a barra no tema claro: tons mais escuros para manter o contraste.</summary>
    public static TaskbarPalette Light { get; } = new(
        text: "#15191E", label: "#23292F", muted: "#59626E", track: "#26000000",
        accent: "#0F9D63", warning: "#C76E00", critical: "#D32F2F", noData: "#8A939F",
        insetFill: "#14000000", insetShadow: "#70000000", insetHighlight: "#D0FFFFFF");

    private TaskbarPalette(string text, string label, string muted, string track,
        string accent, string warning, string critical, string noData,
        string insetFill, string insetShadow, string insetHighlight)
    {
        Text = Make(text);
        Label = Make(label);
        Muted = Make(muted);
        Track = Make(track);
        Accent = Make(accent);
        Warning = Make(warning);
        Critical = Make(critical);
        NoData = Make(noData);
        InsetFill = Make(insetFill);
        InsetShadow = (Color)ColorConverter.ConvertFromString(insetShadow);
        InsetHighlight = Make(insetHighlight);
    }

    /// <summary>Valor no estado normal.</summary>
    public Brush Text { get; }

    /// <summary>Rótulo do gauge.</summary>
    public Brush Label { get; }

    /// <summary>Unidade.</summary>
    public Brush Muted { get; }

    /// <summary>Trilho do arco (parte "vazia").</summary>
    public Brush Track { get; }

    /// <summary>Arco no estado normal.</summary>
    public Brush Accent { get; }

    public Brush Warning { get; }

    public Brush Critical { get; }

    public Brush NoData { get; }

    // ---------- Fundo "afundado" (ver InsetSurface) ----------

    /// <summary>Interior do rebaixo: só um pouco mais escuro que a barra.</summary>
    public Brush InsetFill { get; }

    /// <summary>Sombra interna na borda de cima (alfa máximo, junto à borda).</summary>
    public Color InsetShadow { get; }

    /// <summary>Brilho de 1 px logo abaixo da borda inferior, por fora.</summary>
    public Brush InsetHighlight { get; }

    private static Brush Make(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
