using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DXAllert.Core;

namespace DXAllert.Data;

/// <summary>Controlla le Release di GitHub e scarica l'installer della versione nuova.</summary>
public sealed class UpdateService
{
    private readonly HttpClient _http;

    public UpdateService(HttpClient http) => _http = http;

    /// <summary>La release più recente se è più nuova di <paramref name="current"/>, altrimenti null.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReleaseParser.LatestApiUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct);
        // 404: nessuna release pubblicata, oppure repository privato (senza credenziali GitHub non si vede).
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("su GitHub non c'è una release pubblica di DXAllert (repository privato o senza release)");
        resp.EnsureSuccessStatusCode();
        var info = ReleaseParser.Parse(await resp.Content.ReadAsStringAsync(ct));
        return info is not null && info.Version > Normalize(current) ? info : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Scarica l'installer in %TEMP%\DXAllert-update e ne verifica dimensione e impronta SHA-256.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        if (!ReleaseParser.IsGitHubDownload(info.InstallerUrl))
            throw new InvalidOperationException("Indirizzo di download non valido.");
        var dir = Path.Combine(Path.GetTempPath(), "DXAllert-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(info.InstallerName));
        var partial = path + ".part";

        using (var resp = await _http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.InstallerSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(partial);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        var size = new FileInfo(partial).Length;
        if (info.InstallerSize > 0 && size != info.InstallerSize)
        {
            File.Delete(partial);
            throw new InvalidDataException($"Download incompleto ({size} byte invece di {info.InstallerSize}).");
        }
        if (info.Sha256 is { } expected)
        {
            await using var f = File.OpenRead(partial);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
            if (actual != expected)
            {
                f.Close();
                File.Delete(partial);
                throw new InvalidDataException("L'impronta SHA-256 del file scaricato non corrisponde a quella della release.");
            }
        }
        File.Move(partial, path, overwrite: true);
        return path;
    }
}
