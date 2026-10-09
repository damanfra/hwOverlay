using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace HwOverlay.Services;

/// <summary>Resultado de uma verificação manual de atualização.</summary>
public sealed record UpdateCheckResult(bool IsNewer, string LatestVersion, string PageUrl, string? DownloadUrl);

/// <summary>
/// Verificação de atualização pelas Releases do GitHub. Só roda quando o usuário pede (botão na janela de
/// sensores): nada é consultado nem exibido automaticamente. Não baixa nem instala nada sozinho.
/// </summary>
public static class UpdateService
{
    public const string Repository = "damanfra/hwOverlay";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>Versão em execução (vem do &lt;Version&gt; do build; a Action grava a da tag).</summary>
    // Ordem importa: CurrentVersion depende de CurrentVersionText (inicializadores estáticos rodam em ordem textual).
    public static string CurrentVersionText { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";

    public static Version CurrentVersion { get; } = ParseVersion(CurrentVersionText) ?? new Version(0, 0, 0);

    /// <summary>Consulta a última release publicada. Lança exceção em falha de rede ou resposta inesperada.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HwOverlay", CurrentVersionText));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Nenhuma versão foi publicada ainda.");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var latest = ParseVersion(tag) ?? throw new InvalidOperationException($"Versão não reconhecida: \"{tag}\".");
        var page = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesPage : ReleasesPage;

        string? download = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                download = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        return new UpdateCheckResult(latest > CurrentVersion, tag.TrimStart('v', 'V'), page, download);
    }

    /// <summary>"v1.2.3", "1.2.3-beta+abc" → 1.2.3. Nulo se não for um número de versão.</summary>
    private static Version? ParseVersion(string text)
    {
        text = text.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['-', '+']);
        if (end >= 0) text = text[..end];
        return Version.TryParse(text, out var version) ? version : null;
    }
}
