using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using HwOverlay.Models;

namespace HwOverlay.Services;

/// <summary>Lê/grava %AppData%\HwOverlay\settings.json. Gravações são agrupadas (debounce).</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DispatcherTimer _saveTimer;

    public SettingsService()
    {
        Directory.CreateDirectory(Folder);
        Settings = Load();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HwOverlay");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    public AppSettings Settings { get; }

    /// <summary>Agenda uma gravação (várias mudanças seguidas viram uma escrita só).</summary>
    public void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Configuração é conveniência: não derruba o app por falha de disco.
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // arquivo corrompido: começa do zero (o antigo fica como .bak)
            try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch { }
        }

        return new AppSettings();
    }
}
