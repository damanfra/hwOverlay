using System.Globalization;
using HwOverlay.Models;
using LibreHardwareMonitor.Hardware;

namespace HwOverlay.Services;

/// <summary>Unidades, nomes amigáveis e faixas padrão por tipo de sensor.</summary>
public static class SensorFormatting
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Unit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Factor => "",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.TimeSpan => "s",
        SensorType.Timing => "ns",
        SensorType.Energy => "mWh",
        SensorType.Noise => "dBA",
        SensorType.Conductivity => "µS/cm",
        SensorType.Humidity => "%",
        _ => "",
    };

    public static string GroupName(SensorType type) => type switch
    {
        SensorType.Voltage => "Tensões",
        SensorType.Current => "Correntes",
        SensorType.Power => "Potência",
        SensorType.Clock => "Clocks",
        SensorType.Temperature => "Temperaturas",
        SensorType.Load => "Uso",
        SensorType.Frequency => "Frequências",
        SensorType.Fan => "Ventoinhas",
        SensorType.Flow => "Fluxo",
        SensorType.Control => "Controle (PWM)",
        SensorType.Level => "Níveis",
        SensorType.Factor => "Fatores",
        SensorType.Data => "Dados",
        SensorType.SmallData => "Dados (MB)",
        SensorType.Throughput => "Taxa de transferência",
        SensorType.TimeSpan => "Tempo",
        SensorType.Timing => "Latências",
        SensorType.Energy => "Energia",
        SensorType.Noise => "Ruído",
        SensorType.Conductivity => "Condutividade",
        SensorType.Humidity => "Umidade",
        _ => type.ToString(),
    };

    /// <summary>Formata um valor com a unidade do tipo. Throughput vira KB/s, MB/s...</summary>
    public static string Format(float? value, SensorType type)
    {
        if (value is null || float.IsNaN(value.Value)) return "—";

        var v = value.Value;

        if (type == SensorType.Throughput)
        {
            string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
            var i = 0;
            double d = v;
            while (d >= 1024 && i < units.Length - 1)
            {
                d /= 1024;
                i++;
            }

            return d.ToString(i == 0 ? "0" : "0.0", Culture) + " " + units[i];
        }

        var format = type switch
        {
            SensorType.Voltage => "0.000",
            SensorType.Current => "0.00",
            SensorType.Power => "0.0",
            SensorType.Clock => "0",
            SensorType.Temperature => "0.0",
            SensorType.Load => "0.0",
            SensorType.Fan => "0",
            SensorType.Control => "0.0",
            SensorType.Level => "0.0",
            SensorType.Data => "0.0",
            SensorType.SmallData => "0",
            SensorType.Factor => "0.000",
            _ => "0.##",
        };

        var unit = Unit(type);
        var text = v.ToString(format, Culture);
        return string.IsNullOrEmpty(unit) ? text : $"{text} {unit}";
    }

    /// <summary>
    /// Unidade que cabe dentro do mini-anel da barra de tarefas: °C → "°", unidades de até 2 letras
    /// ficam como estão (%, W, GB...), as maiores (MHz, RPM...) somem — aparecem só na dica do mouse.
    /// </summary>
    public static string CompactUnit(string unit) =>
        unit.StartsWith('°') ? "°"
        : unit.Length <= 2 ? unit
        : "";

    private static readonly string[] RedundantSuffixes = ["Temp", "Temperatura", "Uso", "Carga", "Potência"];

    /// <summary>
    /// Rótulo curto automático: tira a última palavra quando ela só repete o que a unidade já diz
    /// ("CPU Temp" → "CPU", com "°" no anel; "CPU Potência" → "CPU", com "W").
    /// </summary>
    public static string AutoShortLabel(string label)
    {
        label = label.Trim();
        var space = label.LastIndexOf(' ');
        if (space <= 0) return label;

        var last = label[(space + 1)..];
        return RedundantSuffixes.Contains(last, StringComparer.OrdinalIgnoreCase)
            ? label[..space].TrimEnd(' ', '·')
            : label;
    }

    public static string FormatNumber(double value, int decimals) =>
        value.ToString("F" + Math.Clamp(decimals, 0, 3), Culture);

    /// <summary>Configuração inicial razoável de gauge para um sensor.</summary>
    public static GaugeConfig CreateGaugeConfig(SensorSnapshot sensor, string? label = null)
    {
        var config = new GaugeConfig
        {
            SensorId = sensor.Id,
            Label = label ?? ShortLabel(sensor),
            Min = 0,
        };

        switch (sensor.Type)
        {
            case SensorType.Temperature:
                config.Max = 100;
                config.Warn = 70;
                config.Crit = 85;
                break;
            case SensorType.Load:
            case SensorType.Control:
            case SensorType.Level:
            case SensorType.Humidity:
                config.Max = 100;
                config.Warn = 80;
                config.Crit = 95;
                break;
            case SensorType.Clock:
                config.Max = RoundUp(Math.Max(sensor.Max ?? 0, sensor.Value ?? 0) * 1.1, 500, 1000);
                break;
            case SensorType.Power:
                config.Max = RoundUp(Math.Max(sensor.Max ?? 0, sensor.Value ?? 0) * 1.3, 50, 100);
                break;
            case SensorType.Fan:
                config.Max = RoundUp(Math.Max(sensor.Max ?? 0, sensor.Value ?? 0) * 1.2, 500, 2000);
                break;
            case SensorType.Voltage:
                config.Max = RoundUp(Math.Max(sensor.Max ?? 0, sensor.Value ?? 0) * 1.2, 0.5, 1.5);
                config.Decimals = 2;
                break;
            default:
                config.Max = RoundUp(Math.Max(sensor.Max ?? 0, sensor.Value ?? 0) * 1.2, 10, 100);
                config.Decimals = sensor.Type is SensorType.Data ? 1 : 0;
                break;
        }

        return config;
    }

    private static string ShortLabel(SensorSnapshot sensor)
    {
        var prefix = sensor.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
            HardwareType.Memory => "RAM",
            HardwareType.Motherboard or HardwareType.SuperIO => "Placa-mãe",
            HardwareType.Storage => "Disco",
            HardwareType.Network => "Rede",
            _ => sensor.HardwareName,
        };

        return $"{prefix} · {sensor.Name}";
    }

    private static double RoundUp(double value, double step, double fallback)
    {
        if (value <= 0 || double.IsNaN(value)) return fallback;
        return Math.Ceiling(value / step) * step;
    }
}
