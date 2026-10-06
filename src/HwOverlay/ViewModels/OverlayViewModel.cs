using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HwOverlay.Models;
using HwOverlay.Services;

namespace HwOverlay.ViewModels;

/// <summary>
/// Estado do overlay (gauges + aparência). Compartilhado entre a janela do overlay
/// e o painel de configuração da janela de sensores.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly HardwareMonitorService _monitor;
    private GaugeViewModel? _selectedGauge;
    private MonitorSnapshot? _lastSnapshot;

    public OverlayViewModel(SettingsService settingsService, HardwareMonitorService monitor)
    {
        _settingsService = settingsService;
        _monitor = monitor;
        _monitor.Interval = TimeSpan.FromMilliseconds(Math.Clamp(Settings.UpdateIntervalMs, 250, 10000));

        foreach (var config in Settings.Gauges)
            Gauges.Add(CreateGauge(config));

        Gauges.CollectionChanged += OnGaugesChanged;

        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedGauge is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedGauge is not null && Gauges.IndexOf(SelectedGauge) > 0);
        MoveDownCommand = new RelayCommand(() => Move(+1), () => SelectedGauge is not null && Gauges.IndexOf(SelectedGauge) < Gauges.Count - 1);
        ResetToSuggestionsCommand = new RelayCommand(ResetToSuggestions);
    }

    /// <summary>Disparado quando gauges são adicionados/removidos/reordenados.</summary>
    public event EventHandler? GaugesChanged;

    private AppSettings Settings => _settingsService.Settings;

    public ObservableCollection<GaugeViewModel> Gauges { get; } = [];

    public GaugeViewModel? SelectedGauge
    {
        get => _selectedGauge;
        set
        {
            if (!SetProperty(ref _selectedGauge, value)) return;
            OnPropertyChanged(nameof(HasSelectedGauge));
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSelectedGauge => SelectedGauge is not null;

    public bool HasGauges => Gauges.Count > 0;

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public RelayCommand ResetToSuggestionsCommand { get; }

    // ---------- Aparência / comportamento ----------

    public double GaugeSize
    {
        get => Settings.GaugeSize;
        set
        {
            value = Math.Clamp(Math.Round(value), 70, 320);
            if (Math.Abs(Settings.GaugeSize - value) < 0.5) return;
            Settings.GaugeSize = value;
            foreach (var g in Gauges) g.SetSize(value);
            OnPropertyChanged();
            Save();
        }
    }

    public double OverlayOpacity
    {
        get => Settings.OverlayOpacity;
        set
        {
            value = Math.Clamp(value, 0.2, 1);
            if (Math.Abs(Settings.OverlayOpacity - value) < 0.001) return;
            Settings.OverlayOpacity = value;
            OnPropertyChanged();
            Save();
        }
    }

    public double BackgroundOpacity
    {
        get => Settings.BackgroundOpacity;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(Settings.BackgroundOpacity - value) < 0.001) return;
            Settings.BackgroundOpacity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundBrush));
            Save();
        }
    }

    /// <summary>
    /// Fundo do overlay. Alfa mínimo de 1/255: pixels 100% transparentes não recebem clique
    /// em janelas WPF transparentes, e aí não daria para arrastar.
    /// </summary>
    public Brush BackgroundBrush
    {
        get
        {
            var alpha = (byte)Math.Clamp(Math.Round(BackgroundOpacity * 255), 1, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x10, 0x14, 0x1A));
            brush.Freeze();
            return brush;
        }
    }

    public int Columns
    {
        get => Settings.Columns;
        set
        {
            value = Math.Clamp(value, 0, 12);
            if (Settings.Columns == value) return;
            Settings.Columns = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveColumns));
            Save();
        }
    }

    /// <summary>Colunas efetivas do UniformGrid (0 = todos numa linha).</summary>
    public int EffectiveColumns => Columns <= 0 ? Math.Max(1, Gauges.Count) : Columns;

    public bool ClickThrough
    {
        get => Settings.ClickThrough;
        set
        {
            if (Settings.ClickThrough == value) return;
            Settings.ClickThrough = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool OverlayVisible
    {
        get => Settings.OverlayVisible;
        set
        {
            if (Settings.OverlayVisible == value) return;
            Settings.OverlayVisible = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ---------- Modo barra de tarefas (ultra compacto) ----------

    public OverlayMode Mode
    {
        get => Settings.Mode;
        set
        {
            if (Settings.Mode == value) return;
            Settings.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTaskbarMode));
            Save();
        }
    }

    public bool IsTaskbarMode
    {
        get => Mode == OverlayMode.Taskbar;
        set => Mode = value ? OverlayMode.Taskbar : OverlayMode.Floating;
    }

    public TaskbarSide TaskbarSide
    {
        get => Settings.TaskbarSide;
        set
        {
            if (Settings.TaskbarSide == value) return;
            Settings.TaskbarSide = value;
            OnPropertyChanged();
            Save();
        }
    }

    public static IReadOnlyList<TaskbarSideOption> TaskbarSideOptions { get; } =
    [
        new(TaskbarSide.Right, "Direita (junto à bandeja)"),
        new(TaskbarSide.Left, "Esquerda"),
    ];

    /// <summary>Distância (DIPs) até a bandeja ou até a borda esquerda da barra.</summary>
    public double TaskbarOffset
    {
        get => Settings.TaskbarOffset;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 2000);
            if (Math.Abs(Settings.TaskbarOffset - value) < 0.5) return;
            Settings.TaskbarOffset = value;
            OnPropertyChanged();
            Save();
        }
    }

    public int UpdateIntervalMs
    {
        get => Settings.UpdateIntervalMs;
        set
        {
            value = Math.Clamp(value / 250 * 250, 250, 10000);
            if (Settings.UpdateIntervalMs == value) return;
            Settings.UpdateIntervalMs = value;
            _monitor.Interval = TimeSpan.FromMilliseconds(value);
            OnPropertyChanged();
            Save();
        }
    }

    public bool ShowSensorWindowOnStartup
    {
        get => Settings.ShowSensorWindowOnStartup;
        set
        {
            if (Settings.ShowSensorWindowOnStartup == value) return;
            Settings.ShowSensorWindowOnStartup = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ---------- Operações ----------

    public bool Contains(string sensorId) =>
        Gauges.Any(g => string.Equals(g.SensorId, sensorId, StringComparison.OrdinalIgnoreCase));

    public void AddSensor(SensorSnapshot sensor)
    {
        if (Contains(sensor.Id))
        {
            SelectedGauge = Gauges.First(g => string.Equals(g.SensorId, sensor.Id, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var gauge = CreateGauge(SensorFormatting.CreateGaugeConfig(sensor));
        gauge.Update(sensor);
        Gauges.Add(gauge);
        SelectedGauge = gauge;
    }

    public void SavePosition(double left, double top)
    {
        Settings.OverlayLeft = left;
        Settings.OverlayTop = top;
        Save();
    }

    public (double? Left, double? Top) SavedPosition => (Settings.OverlayLeft, Settings.OverlayTop);

    /// <summary>Chamado na thread de UI a cada leitura.</summary>
    public void ApplySnapshot(MonitorSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        // Primeira execução: cria os gauges sugeridos.
        if (!Settings.DefaultsApplied && Gauges.Count == 0)
        {
            foreach (var config in DefaultGaugeSuggester.Suggest(snapshot))
                Gauges.Add(CreateGauge(config));

            Settings.DefaultsApplied = true;
            Save();
        }

        foreach (var gauge in Gauges)
            gauge.Update(snapshot.SensorsById.GetValueOrDefault(gauge.SensorId));
    }

    private GaugeViewModel CreateGauge(GaugeConfig config)
    {
        var gauge = new GaugeViewModel(config, Settings.GaugeSize);
        gauge.ConfigChanged += (_, _) => Save();
        if (_lastSnapshot is not null)
            gauge.Update(_lastSnapshot.SensorsById.GetValueOrDefault(config.SensorId));
        return gauge;
    }

    /// <summary>Troca os gauges atuais pelos sugeridos para esta máquina.</summary>
    private void ResetToSuggestions()
    {
        if (_lastSnapshot is null) return;

        var suggested = DefaultGaugeSuggester.Suggest(_lastSnapshot);
        if (suggested.Count == 0) return;

        if (Gauges.Count > 0)
        {
            var answer = MessageBox.Show(
                $"Substituir os {Gauges.Count} gauges atuais pelos {suggested.Count} sugeridos para esta máquina?",
                "HwOverlay", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }

        SelectedGauge = null;
        Gauges.Clear();
        foreach (var config in suggested)
            Gauges.Add(CreateGauge(config));
    }

    private void RemoveSelected()
    {
        if (SelectedGauge is null) return;

        var index = Gauges.IndexOf(SelectedGauge);
        Gauges.Remove(SelectedGauge);
        SelectedGauge = Gauges.Count == 0 ? null : Gauges[Math.Min(index, Gauges.Count - 1)];
    }

    private void Move(int delta)
    {
        if (SelectedGauge is null) return;

        var from = Gauges.IndexOf(SelectedGauge);
        var to = from + delta;
        if (to < 0 || to >= Gauges.Count) return;

        var selected = SelectedGauge;
        Gauges.Move(from, to);
        SelectedGauge = selected;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void OnGaugesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Mantém a lista persistida na mesma ordem da tela.
        Settings.Gauges = Gauges.Select(g => g.Config).ToList();

        OnPropertyChanged(nameof(HasGauges));
        OnPropertyChanged(nameof(EffectiveColumns));
        GaugesChanged?.Invoke(this, EventArgs.Empty);
        Save();
    }

    private void Save() => _settingsService.RequestSave();
}

public sealed record TaskbarSideOption(TaskbarSide Value, string Name);
