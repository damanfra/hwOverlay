using HwOverlay.Models;
using LibreHardwareMonitor.Hardware;

namespace HwOverlay.Services;

/// <summary>
/// Encapsula o LibreHardwareMonitor: abre o <see cref="Computer"/>, atualiza os sensores
/// numa thread de fundo e publica uma <see cref="MonitorSnapshot"/> imutável a cada ciclo.
/// A UI nunca toca nos objetos do LHM diretamente.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly object _sync = new();
    private readonly UpdateVisitor _visitor = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _opened;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true,
            IsPowerMonitorEnabled = true,
        };
    }

    /// <summary>Disparado (na thread de fundo) após cada atualização.</summary>
    public event EventHandler<MonitorSnapshot>? Updated;

    /// <summary>Disparado (na thread de fundo) se a abertura ou uma atualização falhar.</summary>
    public event EventHandler<Exception>? Failed;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public MonitorSnapshot? Latest { get; private set; }

    public void Start()
    {
        if (_loop is not null) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Factory.StartNew(
            () => RunAsync(token),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Relatório completo do LibreHardwareMonitor (útil para diagnóstico).</summary>
    public string GetReport()
    {
        lock (_sync)
            return _opened ? _computer.GetReport() : "O monitor de hardware ainda não foi inicializado.";
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            // Open() pode levar alguns segundos (detecção de hardware, drivers).
            lock (_sync)
            {
                _computer.Open();
                _opened = true;
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                MonitorSnapshot snapshot;
                lock (_sync)
                {
                    _computer.Accept(_visitor);
                    snapshot = new MonitorSnapshot(_computer.Hardware.Select(BuildHardware).ToList());
                }

                Latest = snapshot;
                Updated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                Failed?.Invoke(this, ex);
            }

            try
            {
                await Task.Delay(Interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static HardwareSnapshot BuildHardware(IHardware hardware)
    {
        var sensors = hardware.Sensors
            .OrderBy(s => s.SensorType)
            .ThenBy(s => s.Index)
            .Select(s => new SensorSnapshot(
                s.Identifier.ToString(),
                s.Name,
                s.SensorType,
                s.Value,
                s.Min,
                s.Max,
                hardware.Identifier.ToString(),
                hardware.Name,
                hardware.HardwareType))
            .ToList();

        var sub = hardware.SubHardware.Select(BuildHardware).ToList();

        return new HardwareSnapshot(
            hardware.Identifier.ToString(),
            hardware.Name,
            hardware.HardwareType,
            sensors,
            sub);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // encerrando: ignora
        }

        lock (_sync)
        {
            if (_opened)
            {
                _computer.Close();
                _opened = false;
            }
        }

        _cts?.Dispose();
    }

    /// <summary>Visitor padrão do LHM: chama Update() em cada componente e sub-componente.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);

            // Por padrão o LHM guarda 24h de histórico por sensor (centenas de MB ao longo do dia).
            // Não usamos esse histórico, então desligamos.
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.ValuesTimeWindow != TimeSpan.Zero)
                    sensor.ValuesTimeWindow = TimeSpan.Zero;
            }
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
