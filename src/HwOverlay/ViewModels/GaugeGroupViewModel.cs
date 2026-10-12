namespace HwOverlay.ViewModels;

/// <summary>Gauges vizinhos que aparecem juntos no modo barra de tarefas (ex.: CPU temperatura + uso).</summary>
public sealed class GaugeGroupViewModel(IReadOnlyList<GaugeViewModel> gauges, bool isFirst)
{
    public IReadOnlyList<GaugeViewModel> Gauges { get; } = gauges;

    /// <summary>O primeiro grupo não tem divisória antes dele.</summary>
    public bool IsFirst { get; } = isFirst;
}
