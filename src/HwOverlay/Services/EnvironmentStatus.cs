using System.Security.Principal;

namespace HwOverlay.Services;

/// <summary>
/// Verifica pré-requisitos para ler todos os sensores:
/// - processo elevado (administrador);
/// - driver PawnIO instalado (o LHM 0.9.6+ usa o PawnIO no lugar do antigo WinRing0).
/// Sem eles o app funciona, mas temperaturas de CPU/placa-mãe/RAM costumam faltar.
/// </summary>
public static class EnvironmentStatus
{
    public static readonly Version MinimumPawnIoVersion = new(2, 0, 0, 0);

    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static Version? PawnIoVersion
    {
        get
        {
            try
            {
                return LibreHardwareMonitor.PawnIo.PawnIo.Version;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool IsPawnIoOk => PawnIoVersion is { } v && v >= MinimumPawnIoVersion;

    /// <summary>Texto do aviso exibido na janela de sensores (nulo = tudo certo).</summary>
    public static string? Warning
    {
        get
        {
            var problems = new List<string>();

            if (!IsAdministrator)
                problems.Add("o app não está rodando como administrador");

            var pawn = PawnIoVersion;
            if (pawn is null)
                problems.Add("o driver PawnIO não está instalado");
            else if (pawn < MinimumPawnIoVersion)
                problems.Add($"o PawnIO instalado ({pawn}) é antigo — precisa da 2.0 ou superior");

            if (problems.Count == 0) return null;

            return "Alguns sensores (principalmente temperaturas de CPU, placa-mãe e RAM) podem não aparecer porque "
                   + string.Join(" e ", problems)
                   + ". Para instalar o PawnIO: winget install namazso.PawnIO (ou baixe em pawnio.eu) e reinicie o HwOverlay.";
        }
    }
}
