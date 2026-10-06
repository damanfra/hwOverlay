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
        insetFill: ["#70000000@0", "#38000000@0.3", "#20000000@0.65", "#14FFFFFF@1"],
        insetBorder: ["#A0000000@0", "#40000000@0.5", "#38FFFFFF@1"]);

    /// <summary>Para a barra no tema claro: tons mais escuros para manter o contraste.</summary>
    public static TaskbarPalette Light { get; } = new(
        text: "#15191E", label: "#23292F", muted: "#59626E", track: "#26000000",
        accent: "#0F9D63", warning: "#C76E00", critical: "#D32F2F", noData: "#8A939F",
        insetFill: ["#34000000@0", "#12000000@0.3", "#04000000@0.65", "#22FFFFFF@1"],
        insetBorder: ["#60000000@0", "#20000000@0.5", "#D0FFFFFF@1"]);

    private TaskbarPalette(string text, string label, string muted, string track,
        string accent, string warning, string critical, string noData,
        string[] insetFill, string[] insetBorder)
    {
        InsetFill = Gradient(insetFill);
        InsetBorder = Gradient(insetBorder);
        Text = Make(text);
        Label = Make(label);
        Muted = Make(muted);
        Track = Make(track);
        Accent = Make(accent);
        Warning = Make(warning);
        Critical = Make(critical);
        NoData = Make(noData);
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

    /// <summary>
    /// Fundo "afundado": sombra interna mais escura no topo que se dissolve para baixo
    /// (termina com um leve brilho, como a luz batendo na borda de baixo de um rebaixo).
    /// </summary>
    public Brush InsetFill { get; }

    /// <summary>Contorno do rebaixo: escuro em cima, claro embaixo.</summary>
    public Brush InsetBorder { get; }

    private static Brush Make(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Gradiente vertical (de cima para baixo) a partir de "cor@posição". A sombra fica concentrada
    /// no topo (some até ~30% da altura) — distribuída por igual parecia um painel liso, não um rebaixo.
    /// O alfa nunca chega a zero: pixel 100% transparente não recebe clique (arrastar/menu).
    /// </summary>
    private static Brush Gradient(string[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        foreach (var stop in stops)
        {
            var parts = stop.Split('@');
            var color = (Color)ColorConverter.ConvertFromString(parts[0]);
            brush.GradientStops.Add(new GradientStop(color, double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
        }

        brush.Freeze();
        return brush;
    }
}
