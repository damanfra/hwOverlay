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

        var updated = e.Args.Contains(UpdateService.UpdatedArgument, StringComparer.OrdinalIgnoreCase);

        _singleInstance = new Mutex(true, @"Local\HwOverlay.SingleInstance", out var isFirst);
        // Logo após uma atualização a versão anterior ainda está encerrando: espera ela soltar o mutex.
        if (!isFirst && updated) isFirst = WaitForPreviousInstance(_singleInstance);
        if (!isFirst)
        {
            MessageBox.Show("O HwOverlay já está aberto (veja o ícone na bandeja do sistema).", "HwOverlay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        // Erros fora da thread de UI derrubam o app sem mensagem: ao menos ficam no erros.log.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Log(ex); };
        TaskScheduler.UnobservedTaskException += (_, args) => Log(args.Exception);
        LogStart(e.Args);
        UpdateService.CleanupAfterUpdate();

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
        overlayVm.RefreshTaskbarMonitors();
        // Monitor conectado/desconectado ou resolução trocada: atualiza a lista de monitores da barra.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        treeVm.RestartRequested += (_, _) => RestartForUpdate();

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

        // Tarefa de "Iniciar com o Windows" apontando para um exe em outra pasta: recria para o exe atual.
        _ = Task.Run(StartupService.RepairIfMoved).ContinueWith(t =>
        {
            if (t.Result is not { } oldPath) return;
            LogLine($"Tarefa de início com o Windows corrigida: apontava para {oldPath}");
            Dispatcher.InvokeAsync(() =>
            {
                overlayVm.StartupRepairNote = $"Corrigido: a tarefa abria {oldPath}, que não é o executável atual.";
                _ = overlayVm.RefreshStartupStatusAsync();
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        SyncOverlayVisibility();

        // Iniciado pelo Windows (--minimizado): só bandeja/overlay, sem a janela de sensores.
        var hidden = e.Args.Contains(StartupService.HiddenArgument, StringComparer.OrdinalIgnoreCase);
        if (updated)
        {
            // Volta para onde o usuário estava (a atualização foi pedida na janela de sensores).
            treeVm.ReportUpdated();
            ShowSensorWindow();
        }
        else if (!hidden && (settings.Settings.ShowSensorWindowOnStartup || !overlayVm.OverlayVisible))
        {
            ShowSensorWindow();
        }
    }

    private static bool WaitForPreviousInstance(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            return true; // a instância anterior saiu sem liberar: o mutex é nosso
        }
    }

    private void RestartForUpdate()
    {
        try
        {
            UpdateService.Restart();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show($"A nova versão foi instalada, mas não foi possível reiniciar: {ex.Message}\n\nAbra o HwOverlay novamente.",
                "HwOverlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ExitApp();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => _overlayVm?.RefreshTaskbarMonitors());

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

        _ = _overlayVm?.RefreshStartupStatusAsync();

        // As barras secundárias podem ter aparecido depois (ex.: opção ligada no Windows com o app aberto).
        _overlayVm?.RefreshTaskbarMonitors();

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
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
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

    /// <summary>Uma linha por início do app (para saber se o Windows chegou a abrir pelo logon).</summary>
    private static void LogStart(string[] args) =>
        LogLine($"Iniciado {UpdateService.CurrentVersionText} · {Environment.ProcessPath} · argumentos: {(args.Length == 0 ? "(nenhum)" : string.Join(" ", args))}");

    private static void LogLine(string text)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            var path = Path.Combine(SettingsService.Folder, "inicio.log");
            if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Delete(path); // não cresce para sempre
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
        }
        catch
        {
            // sem log, sem drama
        }
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
