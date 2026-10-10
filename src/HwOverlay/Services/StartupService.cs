using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace HwOverlay.Services;

/// <summary>
/// Iniciar com o Windows. Como o app exige administrador, a chave "Run" não serve (o Windows ignora
/// entradas elevadas ou pediria UAC a cada logon): usa uma tarefa agendada com privilégios máximos.
/// </summary>
public static class StartupService
{
    /// <summary>Argumento de linha de comando: sobe só na bandeja, sem abrir a janela de sensores.</summary>
    public const string HiddenArgument = "--minimizado";

    private const string TaskName = "HwOverlay";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"").ExitCode == 0;

    /// <summary>Executável que a tarefa abre (nulo se a tarefa não existe ou não deu para ler).</summary>
    public static string? GetTaskCommand()
    {
        var query = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
        if (query.ExitCode != 0) return null;
        try
        {
            var command = XDocument.Parse(query.Output).Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return string.IsNullOrWhiteSpace(command) ? null : Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A tarefa guarda o caminho do exe de quando a opção foi ligada. Se o exe mudou de pasta (versão nova
    /// baixada em outro lugar), o Windows tenta abrir o arquivo antigo e nada aparece. Recria apontando para
    /// o exe atual. Retorna o caminho antigo quando corrigiu; nulo se não precisou (ou a tarefa não existe).
    /// </summary>
    public static string? RepairIfMoved()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;

        var command = GetTaskCommand();
        if (command is null || string.Equals(Path.GetFullPath(command), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            return null;

        return SetEnabled(true) is null ? command : null;
    }

    /// <summary>
    /// Última execução da tarefa pelo Windows, em texto para a tela. Nulo se a tarefa não existe.
    /// Usa a saída CSV do schtasks /V: a ordem das colunas é fixa (os títulos mudam com o idioma).
    /// </summary>
    public static string? DescribeLastRun()
    {
        var query = RunSchtasks($"/Query /TN \"{TaskName}\" /V /FO CSV /NH");
        if (query.ExitCode != 0) return null;

        var line = query.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;

        // "Host","Nome","Próxima execução","Status","Modo de logon","Última execução","Último resultado",...
        var fields = line.Trim('"').Split("\",\"");
        if (fields.Length < 7) return null;

        var lastRun = fields[5];
        if (!int.TryParse(fields[6], out var result)) return $"Última execução: {lastRun}.";

        return result switch
        {
            0 => $"Última execução pelo Windows: {lastRun} (ok).",
            0x41303 => "A tarefa ainda não foi executada pelo Windows (só roda no próximo logon).",
            0x41301 => $"Em execução desde {lastRun}.",
            _ => $"Última execução pelo Windows: {lastRun}, falhou (código 0x{result:X}).",
        };
    }

    /// <summary>Cria ou remove a tarefa. Retorna a mensagem de erro, ou nulo se deu certo.</summary>
    public static string? SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (!IsEnabled()) return null;
            var del = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            return del.ExitCode == 0 ? null : del.Output;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return "Não foi possível descobrir o caminho do executável.";

        var xmlPath = Path.Combine(Path.GetTempPath(), $"HwOverlay-tarefa-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode);
            var create = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            return create.ExitCode == 0 ? null : create.Output;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* temporário */ }
        }
    }

    private static string BuildTaskXml(string exe)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var command = SecurityElement.Escape(exe);
        var folder = SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <StartWhenAvailable>true</StartWhenAvailable>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{HiddenArgument}</Arguments>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
