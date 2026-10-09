using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using HwOverlay.Models;
using HwOverlay.Services;
using HwOverlay.ViewModels;
using HwOverlay.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace HwOverlay;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private SettingsService? _settings;
    private HardwareMonitorService? _monitor;
    private TrayIconService? _tray;
    private TrayGaugeIconService? _trayGauges;
    private OverlayViewModel? _overlayVm;
    private SensorTreeViewModel? _treeVm;
    private OverlayWindow? _overlayWindow;
    private TaskbarWindow? _taskbarWindow;
    private SensorTreeWindow? _sensorWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\HwOverlay.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("O HwOverlay já está aberto (veja o ícone na bandeja do sistema).", "HwOverlay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        // Números com vírgula (pt-BR) nos campos de texto das bindings.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddDarkTheme());

        var settings = _settings = new SettingsService();
        var monitor = _monitor = new HardwareMonitorService();
        var overlayVm = _overlayVm = new OverlayViewModel(settings, monitor);
        var treeVm = _treeVm = new SensorTreeViewModel(overlayVm, monitor);

        _overlayWindow = new OverlayWindow(overlayVm);
        _overlayWindow.OpenSensorsRequested += (_, _) => ShowSensorWindow();
        _overlayWindow.ExitRequested += (_, _) => ExitApp();
        _overlayWindow.TaskbarModeRequested += (_, _) => overlayVm.Mode = OverlayMode.Taskbar;

        _taskbarWindow = new TaskbarWindow(overlayVm);
        _taskbarWindow.OpenSensorsRequested += (_, _) => ShowSensorWindow();
        _taskbarWindow.ExitRequested += (_, _) => ExitApp();

        var tray = _tray = new TrayIconService();
        tray.OpenSensorsRequested += (_, _) => ShowSensorWindow();
        tray.ToggleOverlayRequested += (_, _) => overlayVm.OverlayVisible = !overlayVm.OverlayVisible;
        tray.ToggleClickThroughRequested += (_, _) => overlayVm.ClickThrough = !overlayVm.ClickThrough;
        tray.ToggleTaskbarModeRequested += (_, _) => overlayVm.IsTaskbarMode = !overlayVm.IsTaskbarMode;
        tray.ToggleTrayIconsRequested += (_, _) => overlayVm.TrayIconsEnabled = !overlayVm.TrayIconsEnabled;
        tray.ExitRequested += (_, _) => ExitApp();

        // Criado depois do ícone principal: a ordem de criação define o id de cada ícone (e a fixação no Win11).
        var trayGauges = _trayGauges = new TrayGaugeIconService(overlayVm, tray.Menu);
        trayGauges.OpenSensorsRequested += (_, _) => ShowSensorWindow();

        overlayVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(OverlayViewModel.OverlayVisible) or nameof(OverlayViewModel.ClickThrough)
                or nameof(OverlayViewModel.Mode) or nameof(OverlayViewModel.TrayIconsEnabled))
                SyncOverlayVisibility();

            if (args.PropertyName == nameof(OverlayViewModel.ClickThrough) && overlayVm.ClickThrough)
                tray.ShowBalloon("Overlay travado", "O mouse agora atravessa o overlay. Para mover ou configurar, use este ícone na bandeja.");
        };

        monitor.Updated += (_, snapshot) => Dispatcher.InvokeAsync(() =>
        {
            overlayVm.ApplySnapshot(snapshot);
            trayGauges.Refresh();
            if (_sensorWindow is { IsVisible: true }) treeVm.ApplySnapshot(snapshot);
        });
        monitor.Failed += (_, ex) => Dispatcher.InvokeAsync(() =>
        {
            Log(ex);
            treeVm.ReportError(ex);
        });
        monitor.Start();

        SyncOverlayVisibility();

        // Iniciado pelo Windows (--minimizado): só bandeja/overlay, sem a janela de sensores.
        var hidden = e.Args.Contains(StartupService.HiddenArgument, StringComparer.OrdinalIgnoreCase);
        if (!hidden && (settings.Settings.ShowSensorWindowOnStartup || !overlayVm.OverlayVisible))
            ShowSensorWindow();
    }

    private void SyncOverlayVisibility()
    {
        if (_overlayWindow is null || _taskbarWindow is null || _overlayVm is null) return;

        var floating = _overlayVm.OverlayVisible && !_overlayVm.IsTaskbarMode;
        if (floating)
        {
            if (!_overlayWindow.IsVisible) _overlayWindow.Show();
        }
        else
        {
            _overlayWindow.Hide();
        }

        _taskbarWindow.SetActive(_overlayVm.OverlayVisible && _overlayVm.IsTaskbarMode);

        _tray?.SetState(_overlayVm.OverlayVisible, _overlayVm.ClickThrough, _overlayVm.IsTaskbarMode, _overlayVm.TrayIconsEnabled);
    }

    private void ShowSensorWindow()
    {
        if (_treeVm is null) return;

        if (_sensorWindow is null)
        {
            _sensorWindow = new SensorTreeWindow(_treeVm);
            _sensorWindow.Closed += (_, _) => _sensorWindow = null;
        }

        // Preenche a árvore imediatamente com a última leitura, sem esperar o próximo ciclo.
        if (_monitor?.Latest is { } latest) _treeVm.ApplySnapshot(latest);

        _sensorWindow.Show();
        if (_sensorWindow.WindowState == WindowState.Minimized) _sensorWindow.WindowState = WindowState.Normal;
        _sensorWindow.Activate();
    }

    private void ExitApp()
    {
        _settings?.SaveNow();
        _sensorWindow?.Close();
        _overlayWindow?.Close();
        _taskbarWindow?.Close();
        _trayGauges?.Dispose();
        _tray?.Dispose();
        _monitor?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings?.SaveNow();
        _trayGauges?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show($"Erro inesperado: {e.Exception.Message}\n\nDetalhes em {Path.Combine(SettingsService.Folder, "erros.log")}",
            "HwOverlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            File.AppendAllText(Path.Combine(SettingsService.Folder, "erros.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // sem log, sem drama
        }
    }
}
