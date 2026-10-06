using LibreHardwareMonitor.Hardware;

namespace HwOverlay.Models;

/// <summary>Leitura imutável de um sensor num instante (segura para passar entre threads).</summary>
public sealed record SensorSnapshot(
    string Id,
    string Name,
    SensorType Type,
    float? Value,
    float? Min,
    float? Max,
    string HardwareId,
    string HardwareName,
    HardwareType HardwareType);

/// <summary>Um componente (CPU, GPU, placa-mãe...) e seus sensores/sub-componentes.</summary>
public sealed record HardwareSnapshot(
    string Id,
    string Name,
    HardwareType Type,
    IReadOnlyList<SensorSnapshot> Sensors,
    IReadOnlyList<HardwareSnapshot> SubHardware);

/// <summary>Foto completa da árvore de sensores após um ciclo de atualização.</summary>
public sealed class MonitorSnapshot
{
    public MonitorSnapshot(IReadOnlyList<HardwareSnapshot> hardware)
    {
        Hardware = hardware;

        var byId = new Dictionary<string, SensorSnapshot>(StringComparer.OrdinalIgnoreCase);
        var structure = new System.Text.StringBuilder();
        foreach (var h in hardware) Index(h, byId, structure);

        SensorsById = byId;
        StructureKey = structure.ToString();
    }

    public IReadOnlyList<HardwareSnapshot> Hardware { get; }

    public IReadOnlyDictionary<string, SensorSnapshot> SensorsById { get; }

    /// <summary>
    /// Muda quando sensores/componentes aparecem ou somem. A árvore só é reconstruída
    /// quando isso muda; nos demais ciclos apenas os valores são atualizados.
    /// </summary>
    public string StructureKey { get; }

    public IEnumerable<SensorSnapshot> AllSensors => SensorsById.Values;

    private static void Index(HardwareSnapshot h, Dictionary<string, SensorSnapshot> byId, System.Text.StringBuilder sb)
    {
        sb.Append(h.Id).Append('|');
        foreach (var s in h.Sensors)
        {
            byId[s.Id] = s;
            sb.Append(s.Id).Append(';');
        }

        foreach (var sub in h.SubHardware) Index(sub, byId, sb);
    }
}
