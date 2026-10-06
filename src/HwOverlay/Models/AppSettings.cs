namespace HwOverlay.Models;

public enum GaugeStyle
{
    /// <summary>Arco preenchido com o valor no centro.</summary>
    Arc,

    /// <summary>Relógio com ponteiro, marcações e faixas verde/amarela/vermelha.</summary>
    Needle,
}

public enum OverlayMode
{
    /// <summary>Janela flutuante com os gauges completos.</summary>
    Floating,

    /// <summary>Ultra compacto: mini-gauges sobrepostos à barra de tarefas.</summary>
    Taskbar,
}

/// <summary>Em que ponta da barra de tarefas os mini-gauges ficam.</summary>
public enum TaskbarSide
{
    /// <summary>Logo à esquerda da bandeja do sistema (relógio, ícones).</summary>
    Right,

    /// <summary>Junto à borda esquerda da barra.</summary>
    Left,
}

/// <summary>Configuração de um gauge do overlay (persistida em settings.json).</summary>
public sealed class GaugeConfig
{
    /// <summary>Identificador do sensor no LibreHardwareMonitor, ex.: "/amdcpu/0/temperature/2".</summary>
    public string SensorId { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>Rótulo no modo barra de tarefas. Nulo = automático a partir de <see cref="Label"/>.</summary>
    public string? ShortLabel { get; set; }

    public double Min { get; set; }

    public double Max { get; set; } = 100;

    /// <summary>A partir deste valor o gauge fica amarelo. Nulo = sem alerta.</summary>
    public double? Warn { get; set; }

    /// <summary>A partir deste valor o gauge fica vermelho. Nulo = sem alerta.</summary>
    public double? Crit { get; set; }

    public GaugeStyle Style { get; set; } = GaugeStyle.Arc;

    public int Decimals { get; set; }

    /// <summary>Sobrescreve a unidade padrão do tipo de sensor (°C, %, MHz...).</summary>
    public string? Unit { get; set; }

    /// <summary>Vira um ícone com o valor na bandeja (quando os ícones da bandeja estão ligados).</summary>
    public bool ShowInTray { get; set; } = true;
}

public sealed class AppSettings
{
    public double? OverlayLeft { get; set; }

    public double? OverlayTop { get; set; }

    /// <summary>Overlay travado: o mouse atravessa a janela (click-through).</summary>
    public bool ClickThrough { get; set; }

    public bool OverlayVisible { get; set; } = true;

    public OverlayMode Mode { get; set; } = OverlayMode.Floating;

    public TaskbarSide TaskbarSide { get; set; } = TaskbarSide.Right;

    /// <summary>Distância (DIPs) entre os mini-gauges e a ponta escolhida da barra (bandeja ou borda esquerda).</summary>
    public double TaskbarOffset { get; set; } = 8;

    /// <summary>Opacidade do fundo dos mini-gauges (0 = direto sobre a barra, cores seguem o tema dela).</summary>
    public double TaskbarBackgroundOpacity { get; set; }

    /// <summary>Um ícone por gauge na bandeja do sistema, com o valor desenhado nele.</summary>
    public bool TrayIconsEnabled { get; set; }

    public double OverlayOpacity { get; set; } = 0.92;

    /// <summary>Opacidade só do fundo do overlay (0 = só os gauges flutuando).</summary>
    public double BackgroundOpacity { get; set; } = 0.75;

    /// <summary>Tamanho (largura/altura) de cada gauge, em DIPs.</summary>
    public double GaugeSize { get; set; } = 130;

    /// <summary>Gauges por linha. 0 = todos numa linha só.</summary>
    public int Columns { get; set; }

    public int UpdateIntervalMs { get; set; } = 1000;

    public bool ShowSensorWindowOnStartup { get; set; } = true;

    /// <summary>Indica que os gauges sugeridos já foram criados uma vez (não recria se o usuário apagar).</summary>
    public bool DefaultsApplied { get; set; }

    public List<GaugeConfig> Gauges { get; set; } = [];
}
