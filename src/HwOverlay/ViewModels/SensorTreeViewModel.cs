using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HwOverlay.Models;
using HwOverlay.Services;

namespace HwOverlay.ViewModels;

/// <summary>Árvore de sensores (estilo HWMonitor) + ações de diagnóstico.</summary>
public sealed class SensorTreeViewModel : ObservableObject
{
    private readonly HardwareMonitorService _monitor;
    private readonly Dictionary<string, SensorNodeViewModel> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private string? _structureKey;
    private string _filterText = "";
    private TreeNodeViewModel? _selectedNode;
    private string _statusText = "Detectando hardware… (pode levar alguns segundos)";
    private bool _isLoading = true;
    private string? _errorText;
    private string? _feedback;
    private string _updateStatus = "";
    private bool _isCheckingUpdate;
    private bool _isInstallingUpdate;
    private UpdateCheckResult? _availableUpdate;

    public SensorTreeViewModel(OverlayViewModel overlay, HardwareMonitorService monitor)
    {
        Overlay = overlay;
        _monitor = monitor;
        Warning = EnvironmentStatus.Warning;

        Overlay.GaugesChanged += (_, _) => RefreshOverlayMarks();

        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedSensor is not null);
        CopySensorListCommand = new RelayCommand(CopySensorList);
        SaveReportCommand = new RelayCommand(SaveReport);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
        CollapseAllCommand = new RelayCommand(CollapseToHardware);
        CopySelectedIdCommand = new RelayCommand(() => CopyToClipboard(SelectedSensor?.Id, "ID copiado."), () => SelectedSensor is not null);
        OpenPawnIoSiteCommand = new RelayCommand(() => OpenUrl("https://pawnio.eu/"));
        CopyWingetCommand = new RelayCommand(() => CopyToClipboard("winget install namazso.PawnIO", "Comando copiado. Cole num terminal como administrador."));
        OpenSettingsFolderCommand = new RelayCommand(() => OpenUrl(SettingsService.Folder));
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsCheckingUpdate && !IsInstallingUpdate);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _availableUpdate is not null && !IsInstallingUpdate);
        OpenReleasesCommand = new RelayCommand(() => OpenUrl(UpdateService.ReleasesPage));
    }

    public OverlayViewModel Overlay { get; }

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    public string? Warning { get; }

    public bool HasWarning => Warning is not null;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            ApplyFilter();
        }
    }

    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value)) return;
            OnPropertyChanged(nameof(SelectedSensor));
            OnPropertyChanged(nameof(HasSelectedSensor));
            AddSelectedCommand.NotifyCanExecuteChanged();
            CopySelectedIdCommand.NotifyCanExecuteChanged();
        }
    }

    public SensorNodeViewModel? SelectedSensor => SelectedNode as SensorNodeViewModel;

    public bool HasSelectedSensor => SelectedSensor is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ErrorText is not null;

    /// <summary>Mensagem curta de retorno de ações (copiado, salvo...).</summary>
    public string? Feedback
    {
        get => _feedback;
        private set => SetProperty(ref _feedback, value);
    }

    // ---------- Atualização (manual: nada acontece sem o clique do usuário) ----------

    /// <summary>A nova versão já está no lugar do exe: o App reinicia (inicia a nova e encerra esta).</summary>
    public event EventHandler? RestartRequested;

    public string CurrentVersionText => $"Versão {UpdateService.CurrentVersionText}";

    public string WindowTitle => $"HwOverlay {UpdateService.CurrentVersionText} — Sensores";

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingUpdate, value))
                CheckUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        private set
        {
            if (SetProperty(ref _isInstallingUpdate, value))
            {
                CheckUpdateCommand.NotifyCanExecuteChanged();
                InstallUpdateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasUpdate => _availableUpdate is not null;

    public AsyncRelayCommand CheckUpdateCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }
    public RelayCommand OpenReleasesCommand { get; }

    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        SetAvailableUpdate(null);
        UpdateStatus = "Verificando…";

        try
        {
            var result = await UpdateService.CheckAsync();
            if (result.IsNewer)
            {
                SetAvailableUpdate(result);
                UpdateStatus = $"Nova versão disponível: {result.LatestVersion}. Clique em \"Atualizar agora\": o HwOverlay baixa, troca o executável e reinicia sozinho.";
            }
            else
            {
                UpdateStatus = $"Você já está na versão mais recente ({UpdateService.CurrentVersionText}).";
            }
        }
        catch (InvalidOperationException ex)
        {
            UpdateStatus = ex.Message;
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Não foi possível verificar: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } update) return;

        // Sem exe na release (versões antigas publicavam .zip) ou rodando fora do exe publicado: abre a página.
        if (update.Exe is null || !UpdateService.CanSelfUpdate)
        {
            OpenUrl(update.PageUrl);
            UpdateStatus = update.Exe is null
                ? "Esta versão não tem executável para atualização automática; baixe-o na página da versão."
                : "Atualização automática só funciona no executável publicado (não em build de desenvolvimento).";
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatus = "Baixando…";
        try
        {
            var progress = new Progress<double>(p => UpdateStatus = $"Baixando… {p:P0}");
            await UpdateService.InstallAsync(update.Exe, progress);
            UpdateStatus = "Reiniciando na nova versão…";
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Não foi possível atualizar: {ex.Message}";
            IsInstallingUpdate = false;
        }
    }

    /// <summary>Mensagem exibida pela nova instância logo após a atualização.</summary>
    public void ReportUpdated() => UpdateStatus = $"Atualizado para a versão {UpdateService.CurrentVersionText}.";

    private void SetAvailableUpdate(UpdateCheckResult? update)
    {
        _availableUpdate = update;
        OnPropertyChanged(nameof(HasUpdate));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    public RelayCommand AddSelectedCommand { get; }
    public RelayCommand CopySensorListCommand { get; }
    public RelayCommand SaveReportCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand CopySelectedIdCommand { get; }
    public RelayCommand OpenPawnIoSiteCommand { get; }
    public RelayCommand CopyWingetCommand { get; }
    public RelayCommand OpenSettingsFolderCommand { get; }

    /// <summary>Chamado na thread de UI a cada leitura (só enquanto a janela está aberta).</summary>
    public void ApplySnapshot(MonitorSnapshot snapshot)
    {
        IsLoading = false;
        ErrorText = null;

        if (snapshot.StructureKey != _structureKey)
        {
            Rebuild(snapshot);
            _structureKey = snapshot.StructureKey;
        }
        else
        {
            foreach (var sensor in snapshot.AllSensors)
            {
                if (_sensors.TryGetValue(sensor.Id, out var node))
                    node.Update(sensor);
            }
        }

        StatusText = $"{snapshot.Hardware.Count} componentes · {_sensors.Count} sensores · atualizado às {DateTime.Now:HH:mm:ss}";
    }

    public void ReportError(Exception ex)
    {
        IsLoading = false;
        ErrorText = $"Erro ao ler sensores: {ex.Message}";
    }

    /// <summary>Adiciona ao overlay o sensor do nó informado (duplo clique).</summary>
    public void AddToOverlay(TreeNodeViewModel? node)
    {
        if (node is not SensorNodeViewModel sensorNode) return;
        if (_monitor.Latest?.SensorsById.GetValueOrDefault(sensorNode.Id) is not { } sensor) return;

        Overlay.AddSensor(sensor);
        Feedback = $"\"{sensor.Name}\" está no overlay. Ajuste o gauge no painel ao lado.";
    }

    private void AddSelected() => AddToOverlay(SelectedSensor);

    private void Rebuild(MonitorSnapshot snapshot)
    {
        var selectedId = SelectedSensor?.Id;
        var collapsed = Roots.OfType<HardwareNodeViewModel>()
            .Concat(Roots.SelectMany(r => r.Descendants()).OfType<HardwareNodeViewModel>())
            .Where(h => !h.IsExpanded)
            .Select(h => h.Id)
            .ToHashSet();

        Roots.Clear();
        _sensors.Clear();

        foreach (var hardware in snapshot.Hardware)
            Roots.Add(BuildHardwareNode(hardware, collapsed));

        RefreshOverlayMarks();
        ApplyFilter();

        if (selectedId is not null && _sensors.TryGetValue(selectedId, out var reselect))
            SelectedNode = reselect;
    }

    private HardwareNodeViewModel BuildHardwareNode(HardwareSnapshot hardware, HashSet<string> collapsed)
    {
        var node = new HardwareNodeViewModel(hardware) { IsExpanded = !collapsed.Contains(hardware.Id) };

        foreach (var sub in hardware.SubHardware)
            node.Children.Add(BuildHardwareNode(sub, collapsed));

        foreach (var group in hardware.Sensors.GroupBy(s => s.Type))
        {
            var groupNode = new SensorGroupNodeViewModel(group.Key);
            foreach (var sensor in group)
            {
                var sensorNode = new SensorNodeViewModel(sensor);
                _sensors[sensor.Id] = sensorNode;
                groupNode.Children.Add(sensorNode);
            }

            node.Children.Add(groupNode);
        }

        return node;
    }

    private void RefreshOverlayMarks()
    {
        foreach (var node in _sensors.Values)
            node.IsOnOverlay = Overlay.Contains(node.Id);
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        foreach (var root in Roots)
            root.ApplyFilter(filter);
    }

    private void SetExpanded(bool expanded)
    {
        foreach (var root in Roots)
        {
            root.IsExpanded = expanded;
            foreach (var d in root.Descendants()) d.IsExpanded = expanded;
        }
    }

    private void CollapseToHardware()
    {
        foreach (var root in Roots)
        {
            root.IsExpanded = false;
            foreach (var d in root.Descendants()) d.IsExpanded = d is not HardwareNodeViewModel;
        }
    }

    /// <summary>
    /// Lista legível de todos os sensores (com IDs), boa para colar numa conversa
    /// e decidir quais vão para os gauges.
    /// </summary>
    private void CopySensorList()
    {
        var snapshot = _monitor.Latest;
        if (snapshot is null)
        {
            Feedback = "Ainda não há leituras.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"HwOverlay — sensores detectados em {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Admin: {(EnvironmentStatus.IsAdministrator ? "sim" : "não")} · PawnIO: {EnvironmentStatus.PawnIoVersion?.ToString() ?? "não instalado"}");
        sb.AppendLine();

        void Write(HardwareSnapshot h, int depth)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}[{h.Type}] {h.Name}  ({h.Id})");
            foreach (var group in h.Sensors.GroupBy(s => s.Type))
            {
                sb.AppendLine($"{indent}  {SensorFormatting.GroupName(group.Key)}:");
                foreach (var s in group)
                    sb.AppendLine($"{indent}    {s.Name,-32} {SensorFormatting.Format(s.Value, s.Type),14}   {s.Id}");
            }

            foreach (var sub in h.SubHardware) Write(sub, depth + 1);
            sb.AppendLine();
        }

        foreach (var h in snapshot.Hardware) Write(h, 0);

        CopyToClipboard(sb.ToString(), "Lista de sensores copiada para a área de transferência.");
    }

    private void SaveReport()
    {
        try
        {
            var path = Path.Combine(SettingsService.Folder, $"relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, _monitor.GetReport());
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            Feedback = $"Relatório salvo em {path}";
        }
        catch (Exception ex)
        {
            Feedback = $"Não foi possível salvar o relatório: {ex.Message}";
        }
    }

    private void CopyToClipboard(string? text, string message)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetText(text);
            Feedback = message;
        }
        catch (Exception ex)
        {
            // A área de transferência pode estar travada por outro app.
            Feedback = $"Não foi possível copiar: {ex.Message}";
        }
    }

    private void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Feedback = $"Não foi possível abrir {target}: {ex.Message}";
        }
    }
}
