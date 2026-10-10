using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace HwOverlay.Services;

/// <summary>Executável publicado numa release (tamanho e SHA-256 vêm da API do GitHub, quando ela informa).</summary>
public sealed record UpdateAsset(string Url, long Size, string? Sha256);

/// <summary>Resultado de uma verificação manual de atualização.</summary>
public sealed record UpdateCheckResult(bool IsNewer, string LatestVersion, string PageUrl, UpdateAsset? Exe);

/// <summary>
/// Atualização pelas Releases do GitHub. Só roda quando o usuário pede (botões na janela de sensores):
/// nada é consultado nem instalado automaticamente.
/// Instalação: baixa o novo .exe ao lado do atual, confere tamanho/SHA-256, renomeia o exe em execução
/// (o Windows permite renomear, não sobrescrever) e põe o novo no lugar. O app então reinicia com
/// <see cref="UpdatedArgument"/>; a nova instância espera a antiga sair e apaga o exe antigo.
/// </summary>
public static class UpdateService
{
    public const string Repository = "damanfra/hwOverlay";

    /// <summary>Argumento da instância iniciada logo após a troca do executável.</summary>
    public const string UpdatedArgument = "--atualizado";

    private const string OldSuffix = ".antigo";
    private const string NewSuffix = ".novo";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>Versão em execução (vem do &lt;Version&gt; do build; a Action grava a da tag).</summary>
    // Ordem importa: CurrentVersion depende de CurrentVersionText (inicializadores estáticos rodam em ordem textual).
    public static string CurrentVersionText { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";

    public static Version CurrentVersion { get; } = ParseVersion(CurrentVersionText) ?? new Version(0, 0, 0);

    /// <summary>
    /// Só o executável publicado (arquivo único) se atualiza sozinho. Rodando do Visual Studio/dotnet run
    /// o exe é só um lançador do HwOverlay.dll, e trocá-lo quebraria a pasta de build.
    /// </summary>
    public static bool CanSelfUpdate =>
        string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location) && !string.IsNullOrEmpty(Environment.ProcessPath);

    /// <summary>Consulta a última release publicada. Lança exceção em falha de rede ou resposta inesperada.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(TimeSpan.FromSeconds(15));
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

        UpdateAsset? exe = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var download = asset.GetProperty("browser_download_url").GetString();
                if (string.IsNullOrEmpty(download)) continue;

                // "digest": "sha256:abc..." (nem toda release antiga tem).
                string? sha = null;
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d
                    && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = d["sha256:".Length..];

                exe = new UpdateAsset(download, asset.GetProperty("size").GetInt64(), sha);
                break;
            }
        }

        return new UpdateCheckResult(latest > CurrentVersion, tag.TrimStart('v', 'V'), page, exe);
    }

    /// <summary>
    /// Baixa e coloca o novo executável no lugar do atual. Depois disso basta chamar <see cref="Restart"/>
    /// e encerrar o app. <paramref name="progress"/> recebe de 0 a 1.
    /// </summary>
    public static async Task InstallAsync(UpdateAsset exe, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (!CanSelfUpdate) throw new InvalidOperationException("Atualização automática só funciona no executável publicado.");

        var current = Environment.ProcessPath!;
        var downloaded = current + NewSuffix;
        var old = current + OldSuffix;

        try
        {
            using (var http = CreateClient(TimeSpan.FromMinutes(10)))
            using (var response = await http.GetAsync(exe.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? exe.Size;

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(downloaded, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    sha.AppendData(buffer, 0, read);
                    received += read;
                    if (total > 0) progress?.Report((double)received / total);
                }

                if (exe.Size > 0 && received != exe.Size)
                    throw new InvalidOperationException($"Download incompleto ({received:N0} de {exe.Size:N0} bytes).");

                var hash = Convert.ToHexString(sha.GetHashAndReset());
                if (exe.Sha256 is not null && !hash.Equals(exe.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("O arquivo baixado não confere com o da release (SHA-256 diferente).");
            }

            // Exe em execução não pode ser sobrescrito, mas pode ser renomeado.
            File.Move(current, old, overwrite: true);
            try
            {
                File.Move(downloaded, current);
            }
            catch
            {
                File.Move(old, current, overwrite: true);
                throw;
            }
        }
        finally
        {
            TryDelete(downloaded);
        }
    }

    /// <summary>Inicia a nova versão (que espera esta instância encerrar). O chamador deve encerrar o app em seguida.</summary>
    public static void Restart() =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, UpdatedArgument) { UseShellExecute = true });

    /// <summary>Apaga o exe antigo deixado pela atualização (a instância anterior pode levar um instante para soltar o arquivo).</summary>
    public static void CleanupAfterUpdate()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe + OldSuffix)) return;

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 20 && File.Exists(exe + OldSuffix); i++)
            {
                if (TryDelete(exe + OldSuffix)) return;
                await Task.Delay(500);
            }
        });
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HwOverlay", CurrentVersionText));
        return http;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
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
