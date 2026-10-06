namespace HwOverlay.Models;

public enum GaugeStyle
{
    /// <summary>Arco preenchido com o valor no centro.</summary>
    Arc,

    /// <summary>Relógio com ponteiro, marcações e faixas verde/amarela/vermelha.</summary>
    Needle,
}

/// <summary>Configuração de um gauge do overlay (persistida em settings.json).</summary>
public sealed class GaugeConfig
{
    /// <summary>Identificador do sensor no LibreHardwareMonitor, ex.: "/amdcpu/0/temperature/2".</summary>
    public string SensorId { get; set; } = "";

    public string Label { get; set; } = "";

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
}

public sealed class AppSettings
{
    public double? OverlayLeft { get; set; }

    public double? OverlayTop { get; set; }

    /// <summary>Overlay travado: o mouse atravessa a janela (click-through).</summary>
    public bool ClickThrough { get; set; }

    public bool OverlayVisible { get; set; } = true;

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
