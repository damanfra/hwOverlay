using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

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
