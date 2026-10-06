using HwOverlay.Models;
using LibreHardwareMonitor.Hardware;

namespace HwOverlay.Services;

/// <summary>
/// Escolhe sensores prováveis para os gauges iniciais
/// (temperatura de CPU/GPU/RAM/placa-mãe e uso de CPU/GPU/RAM).
/// Os nomes variam por fabricante, então é tudo heurística — a árvore de sensores
/// existe justamente para trocar o que não ficar certo.
/// Quando um sensor não existe (comum em notebooks: GPU integrada sem temperatura,
/// placa-mãe sem Super I/O legível), entra uma alternativa útil no lugar.
/// </summary>
public static class DefaultGaugeSuggester
{
    public static List<GaugeConfig> Suggest(MonitorSnapshot snapshot)
    {
        var all = snapshot.AllSensors.ToList();
        var result = new List<GaugeConfig>();

        bool Add(SensorSnapshot? sensor, string label)
        {
            if (sensor is null || result.Any(g => g.SensorId == sensor.Id)) return false;
            result.Add(SensorFormatting.CreateGaugeConfig(sensor, label));
            return true;
        }

        var cpu = all.Where(s => s.HardwareType == HardwareType.Cpu).ToList();
        var gpu = all.Where(s => IsGpu(s.HardwareType))
            // GPU dedicada antes da integrada
            .OrderBy(s => s.HardwareType == HardwareType.GpuIntel ? 1 : 0)
            .ToList();
        var memory = all.Where(s => s.HardwareType == HardwareType.Memory).ToList();
        var board = all.Where(s => s.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO).ToList();
        var storage = all.Where(s => s.HardwareType == HardwareType.Storage).ToList();

        Add(Pick(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Core Average", "Core Max"), "CPU Temp");
        Add(Pick(cpu, SensorType.Load, "CPU Total"), "CPU Uso");

        var hasGpuTemp = Add(Pick(gpu, SensorType.Temperature, "GPU Core", "GPU Hot Spot"), "GPU Temp");
        Add(Pick(gpu, SensorType.Load, "GPU Core", "D3D 3D", "GPU"), "GPU Uso");

        Add(PickPhysicalMemoryLoad(memory), "RAM Uso");
        Add(Pick(memory, SensorType.Temperature), "RAM Temp");

        var hasBoardTemp = Add(PickBoardTemperature(board), "Placa-mãe");

        // Alternativas para o que a máquina não expõe.
        if (!hasGpuTemp)
        {
            // GPU integrada (Intel/AMD APU) não tem temperatura própria: fica no mesmo chip da CPU.
            // O consumo da CPU mostra bem quando o chip está "fazendo força".
            Add(Pick(cpu, SensorType.Power, "CPU Package"), "CPU Potência");
        }

        if (!hasBoardTemp)
            Add(Pick(storage, SensorType.Temperature, "Composite Temperature", "Temperature"), "SSD Temp");

        return result;
    }

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    /// <summary>Primeiro sensor do tipo cujo nome bate (na ordem de preferência); senão, o primeiro do tipo.</summary>
    private static SensorSnapshot? Pick(List<SensorSnapshot> sensors, SensorType type, params string[] preferredNames)
    {
        var ofType = sensors
            .Where(s => s.Type == type && s.Value is not null)
            // "Warning/Critical Temperature" de SSDs são limites fixos, não leituras.
            .Where(s => !s.Name.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Critical", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var name in preferredNames)
        {
            var match = ofType.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? ofType.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return ofType.FirstOrDefault();
    }

    /// <summary>
    /// Uso da memória física. O LHM tem dois componentes com um sensor chamado "Memory":
    /// "Total Memory" (/ram, a RAM de verdade) e "Virtual Memory" (/vram, RAM + arquivo de paginação).
    /// </summary>
    private static SensorSnapshot? PickPhysicalMemoryLoad(List<SensorSnapshot> memory)
    {
        var loads = memory
            .Where(s => s.Type == SensorType.Load && s.Value is not null)
            .Where(s => !IsVirtualMemory(s))
            .ToList();

        return loads.FirstOrDefault(s => s.HardwareId.Equals("/ram", StringComparison.OrdinalIgnoreCase))
               ?? loads.FirstOrDefault(s => s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
               ?? loads.FirstOrDefault();
    }

    private static bool IsVirtualMemory(SensorSnapshot s) =>
        s.HardwareId.Equals("/vram", StringComparison.OrdinalIgnoreCase)
        || s.HardwareName.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private static SensorSnapshot? PickBoardTemperature(List<SensorSnapshot> board)
    {
        // Sensores de Super I/O sem nada ligado costumam ler 0, -55, 127 etc.
        var temps = board
            .Where(s => s.Type == SensorType.Temperature && s.Value is > 5 and < 110)
            .ToList();

        string[] preferred = ["Motherboard", "System", "Mainboard", "PCH", "Chipset"];
        foreach (var name in preferred)
        {
            var match = temps.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return temps.FirstOrDefault();
    }
}
