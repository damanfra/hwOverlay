using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HwOverlay.Models;
using HwOverlay.Services;
using LibreHardwareMonitor.Hardware;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Extensions;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace HwOverlay.ViewModels;

public enum GaugeState
{
    NoData,
    Normal,
    Warning,
    Critical,
}

/// <summary>
/// Um "relógio" do overlay. Monta as séries do LiveCharts2 (arco ou ponteiro) a partir do
/// <see cref="GaugeConfig"/> e as atualiza a cada leitura do sensor.
/// </summary>
public sealed class GaugeViewModel : ObservableObject
{
    // Paleta (a mesma do ícone): verde-água / âmbar / vermelho.
    private static readonly SKColor NormalColor = SKColor.Parse("#3DDC97");
    private static readonly SKColor WarningColor = SKColor.Parse("#FFB547");
    private static readonly SKColor CriticalColor = SKColor.Parse("#FF5C5C");
    private static readonly SKColor TrackColor = new(255, 255, 255, 28);
    private static readonly SKColor NeedleColor = SKColor.Parse("#EBF0F5");
    private static readonly SKColor TickColor = new(255, 255, 255, 90);
    private static readonly SKColor TickLabelColor = SKColor.Parse("#9AA4B2");

    private static readonly Brush NoDataBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x82)));
    private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF0)));
    private static readonly Brush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB5, 0x47)));
    private static readonly Brush CriticalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C)));

    private readonly GaugeConfig _config;
    private readonly ObservableValue _arcValue = new(0);

    private PieSeries<ObservableValue>? _arcSeries;
    private NeedleVisual? _needle;
    private double _size;
    private double? _rawValue;
    private SensorType? _sensorType;
    private string _sensorName = "(aguardando leitura)";
    private GaugeState _state = GaugeState.NoData;

    public GaugeViewModel(GaugeConfig config, double size)
    {
        _config = config;
        _size = size;
        Rebuild();
    }

    /// <summary>Disparado quando algo persistível muda (para salvar settings.json).</summary>
    public event EventHandler? ConfigChanged;

    public GaugeConfig Config => _config;

    public string SensorId => _config.SensorId;

    // ---------- Propriedades editáveis (janela de configuração) ----------

    public string Label
    {
        get => _config.Label;
        set
        {
            if (_config.Label == value) return;
            _config.Label = value;
            OnPropertyChanged();
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Min
    {
        get => _config.Min;
        set => SetConfig(_config.Min, value, v => _config.Min = v, rebuild: true);
    }

    public double Max
    {
        get => _config.Max;
        set => SetConfig(_config.Max, value, v => _config.Max = v, rebuild: true);
    }

    public double? Warn
    {
        get => _config.Warn;
        set => SetConfig(_config.Warn, value, v => _config.Warn = v, rebuild: true);
    }

    public double? Crit
    {
        get => _config.Crit;
        set => SetConfig(_config.Crit, value, v => _config.Crit = v, rebuild: true);
    }

    public GaugeStyle Style
    {
        get => _config.Style;
        set => SetConfig(_config.Style, value, v => _config.Style = v, rebuild: true);
    }

    public int Decimals
    {
        get => _config.Decimals;
        set => SetConfig(_config.Decimals, Math.Clamp(value, 0, 3), v => _config.Decimals = v, rebuild: false);
    }

    /// <summary>Unidade personalizada; vazio = unidade padrão do tipo de sensor.</summary>
    public string? Unit
    {
        get => _config.Unit;
        set => SetConfig(_config.Unit, string.IsNullOrWhiteSpace(value) ? null : value.Trim(), v => _config.Unit = v, rebuild: false);
    }

    public static IReadOnlyList<StyleOption> StyleOptions { get; } =
    [
        new(GaugeStyle.Arc, "Arco"),
        new(GaugeStyle.Needle, "Ponteiro (relógio)"),
    ];

    // ---------- Estado de exibição ----------

    public string SensorName
    {
        get => _sensorName;
        private set => SetProperty(ref _sensorName, value);
    }

    public double Size => _size;

    public double ValueFontSize => Math.Max(11, _size * (IsNeedle ? 0.15 : 0.21));

    public double UnitFontSize => Math.Max(9, _size * 0.085);

    public double LabelFontSize => Math.Max(10, _size * 0.095);

    public bool IsNeedle => _config.Style == GaugeStyle.Needle;

    /// <summary>Arco: valor no centro. Ponteiro: valor embaixo, no vão do mostrador.</summary>
    public VerticalAlignment ValueVerticalAlignment => IsNeedle ? VerticalAlignment.Bottom : VerticalAlignment.Center;

    public Thickness ValueMargin => IsNeedle ? new Thickness(0, 0, 0, _size * 0.02) : new Thickness(0, _size * 0.04, 0, 0);

    public string ValueText => _rawValue is { } v ? SensorFormatting.FormatNumber(v, _config.Decimals) : "N/D";

    public string UnitText => _config.Unit ?? (_sensorType is { } t ? SensorFormatting.Unit(t) : "");

    public GaugeState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(StateBrush));
            if (_arcSeries is not null) _arcSeries.Fill = new SolidColorPaint(StateColor(value));
        }
    }

    public Brush StateBrush => _state switch
    {
        GaugeState.Warning => WarningBrush,
        GaugeState.Critical => CriticalBrush,
        GaugeState.NoData => NoDataBrush,
        _ => NormalBrush,
    };

    // ---------- Dados do gráfico (LiveCharts2) ----------

    public ISeries[] Series { get; private set; } = [];

    public IEnumerable<IChartElement> VisualElements { get; private set; } = [];

    public double ChartMin { get; private set; }

    public double ChartMax { get; private set; } = 100;

    // ---------- API ----------

    public void SetSize(double size)
    {
        if (Math.Abs(size - _size) < 0.5) return;
        _size = size;
        Rebuild();
    }

    /// <summary>Aplica uma nova leitura (nulo = sensor não encontrado nesta máquina).</summary>
    public void Update(SensorSnapshot? sensor)
    {
        if (sensor is null)
        {
            SensorName = "Sensor não encontrado";
            _rawValue = null;
        }
        else
        {
            SensorName = $"{sensor.HardwareName} › {sensor.Name}";
            _rawValue = sensor.Value;

            if (_sensorType != sensor.Type)
            {
                _sensorType = sensor.Type;
                OnPropertyChanged(nameof(UnitText));
            }
        }

        ApplyValue();
        OnPropertyChanged(nameof(ValueText));
    }

    private void ApplyValue()
    {
        var min = _config.Min;
        var max = Math.Max(_config.Max, min + 0.0001);

        if (_rawValue is not { } v || double.IsNaN(v))
        {
            State = GaugeState.NoData;
            _arcValue.Value = 0;
            if (_needle is not null) _needle.Value = min;
            return;
        }

        State = _config.Crit is { } crit && v >= crit ? GaugeState.Critical
              : _config.Warn is { } warn && v >= warn ? GaugeState.Warning
              : GaugeState.Normal;

        // O arco é desenhado de 0 até (max - min), por isso o valor é deslocado.
        _arcValue.Value = Math.Clamp(v - min, 0, max - min);

        if (_needle is not null) _needle.Value = Math.Clamp(v, min, max);
    }

    /// <summary>Recria séries e elementos visuais (estilo, faixa ou tamanho mudaram).</summary>
    private void Rebuild()
    {
        var min = _config.Min;
        var max = Math.Max(_config.Max, min + 0.0001);
        var s = _size;

        if (_config.Style == GaugeStyle.Needle)
        {
            _arcSeries = null;

            var warn = Math.Clamp(_config.Warn ?? max, min, max);
            var crit = Math.Clamp(_config.Crit ?? max, warn, max);

            var sections = new List<GaugeItem>();
            void Section(double span, SKColor color)
            {
                if (span <= 0) return;
                sections.Add(new GaugeItem(span, series =>
                {
                    series.Fill = new SolidColorPaint(color);
                    series.OuterRadiusOffset = s * 0.36;
                    series.MaxRadialColumnWidth = s * 0.05;
                    series.CornerRadius = 0;
                }));
            }

            Section(warn - min, NormalColor);
            Section(crit - warn, WarningColor);
            Section(max - crit, CriticalColor);

            _needle = new NeedleVisual
            {
                Value = min,
                Width = Math.Max(4, s * 0.045),
                Fill = new SolidColorPaint(NeedleColor),
            };

            Series = GaugeGenerator.BuildAngularGaugeSections([.. sections]);
            VisualElements =
            [
                new AngularTicksVisual
                {
                    Labeler = value => value.ToString("0"),
                    LabelsSize = Math.Max(7, s * 0.065),
                    LabelsOuterOffset = s * 0.055,
                    OuterOffset = s * 0.2,
                    TicksLength = s * 0.05,
                    Stroke = new SolidColorPaint(TickColor),
                    LabelsPaint = new SolidColorPaint(TickLabelColor),
                },
                _needle,
            ];
            ChartMin = min;
            ChartMax = max;
        }
        else
        {
            _needle = null;

            var thickness = Math.Max(5, s * 0.085);

            Series = GaugeGenerator.BuildSolidGauge(
                new GaugeItem(_arcValue, series =>
                {
                    _arcSeries = series;
                    series.Fill = new SolidColorPaint(StateColor(_state));
                    series.MaxRadialColumnWidth = thickness;
                    series.CornerRadius = thickness / 2;
                    series.DataLabelsPaint = null; // o valor é um TextBlock do WPF (mais controle de fonte)
                    series.IsHoverable = false;
                }),
                new GaugeItem(GaugeItem.Background, series =>
                {
                    series.Fill = new SolidColorPaint(TrackColor);
                    series.MaxRadialColumnWidth = thickness;
                    series.CornerRadius = thickness / 2;
                }));
            VisualElements = [];
            ChartMin = 0;
            ChartMax = max - min;
        }

        ApplyValue();

        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(VisualElements));
        OnPropertyChanged(nameof(ChartMin));
        OnPropertyChanged(nameof(ChartMax));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(IsNeedle));
        OnPropertyChanged(nameof(ValueFontSize));
        OnPropertyChanged(nameof(UnitFontSize));
        OnPropertyChanged(nameof(LabelFontSize));
        OnPropertyChanged(nameof(ValueVerticalAlignment));
        OnPropertyChanged(nameof(ValueMargin));
    }

    private void SetConfig<T>(T current, T value, Action<T> assign, bool rebuild, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;

        assign(value);
        OnPropertyChanged(name);

        if (rebuild) Rebuild();
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(UnitText));

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SKColor StateColor(GaugeState state) => state switch
    {
        GaugeState.Warning => WarningColor,
        GaugeState.Critical => CriticalColor,
        GaugeState.NoData => TrackColor,
        _ => NormalColor,
    };

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed record StyleOption(GaugeStyle Value, string Name);
