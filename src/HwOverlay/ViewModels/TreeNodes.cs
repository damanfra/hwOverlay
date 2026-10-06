using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HwOverlay.Models;
using HwOverlay.Services;
using LibreHardwareMonitor.Hardware;

namespace HwOverlay.ViewModels;

/// <summary>Nó genérico da árvore (componente, grupo de tipo ou sensor).</summary>
public abstract class TreeNodeViewModel : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isVisible = true;

    public abstract string Name { get; }

    /// <summary>Etiqueta curta exibida antes do nome (CPU, GPU, RAM...).</summary>
    public virtual string Tag => "";

    /// <summary>Cor da etiqueta (hex).</summary>
    public virtual string TagColor => "#5A6472";

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public IEnumerable<TreeNodeViewModel> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants()) yield return d;
        }
    }

    /// <summary>Aplica o filtro de texto; retorna true se este nó (ou algum filho) ficou visível.</summary>
    public virtual bool ApplyFilter(string filter)
    {
        var anyChild = false;
        foreach (var child in Children)
            anyChild |= child.ApplyFilter(filter);

        var selfMatch = filter.Length == 0 || Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        IsVisible = selfMatch || anyChild;

        if (filter.Length > 0 && anyChild) IsExpanded = true;

        // Se o próprio componente/grupo bate no filtro, mostra tudo dentro dele.
        if (filter.Length > 0 && selfMatch)
            foreach (var d in Descendants()) d.IsVisible = true;

        return IsVisible;
    }
}

public sealed class HardwareNodeViewModel : TreeNodeViewModel
{
    public HardwareNodeViewModel(HardwareSnapshot hardware)
    {
        Id = hardware.Id;
        Name = hardware.Name;
        Type = hardware.Type;
    }

    public string Id { get; }

    public override string Name { get; }

    public HardwareType Type { get; }

    public string TypeName => Type switch
    {
        HardwareType.Cpu => "Processador",
        HardwareType.GpuNvidia => "GPU NVIDIA",
        HardwareType.GpuAmd => "GPU AMD",
        HardwareType.GpuIntel => "GPU Intel",
        HardwareType.Memory when Id.Equals("/ram", StringComparison.OrdinalIgnoreCase) => "Memória física (RAM)",
        HardwareType.Memory when Id.Equals("/vram", StringComparison.OrdinalIgnoreCase) => "Memória virtual (RAM + arquivo de paginação)",
        HardwareType.Memory when Id.Contains("/dimm/", StringComparison.OrdinalIgnoreCase) => "Pente de memória",
        HardwareType.Memory => "Memória",
        HardwareType.Motherboard => "Placa-mãe",
        HardwareType.SuperIO => "Super I/O (sensores da placa-mãe)",
        HardwareType.Storage => "Armazenamento",
        HardwareType.Network => "Rede",
        HardwareType.Cooler => "Cooler",
        HardwareType.EmbeddedController => "Controlador embarcado",
        HardwareType.Psu => "Fonte",
        HardwareType.Battery => "Bateria",
        HardwareType.PowerMonitor => "Monitor de energia",
        _ => Type.ToString(),
    };

    public override string Tag => Type switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
        HardwareType.Memory => "RAM",
        HardwareType.Motherboard => "MB",
        HardwareType.SuperIO => "SIO",
        HardwareType.Storage => "SSD",
        HardwareType.Network => "NET",
        HardwareType.Battery => "BAT",
        HardwareType.Psu => "PSU",
        HardwareType.PowerMonitor => "PWR",
        HardwareType.Cooler => "FAN",
        HardwareType.EmbeddedController => "EC",
        _ => "HW",
    };

    public override string TagColor => Type switch
    {
        HardwareType.Cpu => "#3B82F6",
        HardwareType.GpuNvidia => "#76B900",
        HardwareType.GpuAmd => "#ED1C24",
        HardwareType.GpuIntel => "#0071C5",
        HardwareType.Memory => "#A855F7",
        HardwareType.Motherboard or HardwareType.SuperIO => "#F59E0B",
        HardwareType.Storage => "#14B8A6",
        HardwareType.Network => "#64748B",
        _ => "#5A6472",
    };
}

public sealed class SensorGroupNodeViewModel : TreeNodeViewModel
{
    public SensorGroupNodeViewModel(SensorType type)
    {
        Type = type;
        Name = SensorFormatting.GroupName(type);
    }

    public SensorType Type { get; }

    public override string Name { get; }
}

public sealed class SensorNodeViewModel : TreeNodeViewModel
{
    private float? _value;
    private float? _min;
    private float? _max;
    private bool _isOnOverlay;

    public SensorNodeViewModel(SensorSnapshot sensor)
    {
        Id = sensor.Id;
        Name = sensor.Name;
        Type = sensor.Type;
        HardwareName = sensor.HardwareName;
        HardwareType = sensor.HardwareType;
        Update(sensor);
    }

    public string Id { get; }

    public override string Name { get; }

    public SensorType Type { get; }

    public string HardwareName { get; }

    public HardwareType HardwareType { get; }

    public string TypeName => SensorFormatting.GroupName(Type);

    public float? Value
    {
        get => _value;
        private set
        {
            if (SetProperty(ref _value, value)) OnPropertyChanged(nameof(ValueText));
        }
    }

    public float? Min
    {
        get => _min;
        private set
        {
            if (SetProperty(ref _min, value)) OnPropertyChanged(nameof(MinText));
        }
    }

    public float? Max
    {
        get => _max;
        private set
        {
            if (SetProperty(ref _max, value)) OnPropertyChanged(nameof(MaxText));
        }
    }

    public string ValueText => SensorFormatting.Format(Value, Type);

    public string MinText => SensorFormatting.Format(Min, Type);

    public string MaxText => SensorFormatting.Format(Max, Type);

    /// <summary>Já existe um gauge no overlay para este sensor.</summary>
    public bool IsOnOverlay
    {
        get => _isOnOverlay;
        set => SetProperty(ref _isOnOverlay, value);
    }

    public void Update(SensorSnapshot sensor)
    {
        Value = sensor.Value;
        Min = sensor.Min;
        Max = sensor.Max;
    }
}
