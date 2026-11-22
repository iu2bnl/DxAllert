using System.Text.Json;
using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Una versione pubblicata nelle Release di GitHub, con il suo installer.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes,
    string InstallerName, string InstallerUrl, long InstallerSize, string? Sha256);

/// <summary>Legge la risposta di api.github.com/repos/{owner}/{repo}/releases/latest.</summary>
public static class ReleaseParser
{
    public const string Owner = "mattianobis";
    public const string Repo = "DxAllert";
    public static string LatestApiUrl => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly Regex InstallerName = new(@"^DXAllert[-_ ]?v?(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"v1.1.3", "1.1.3", "DXAllert 1.1.3" → 1.1.3.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    /// <summary>
    /// La release con il suo installer DXAllert&lt;versione&gt;.exe, oppure null se la release è una bozza,
    /// una pre-release, non ha un installer o l'indirizzo non è di github.com.
    /// </summary>
    public static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (Bool(r, "draft") || Bool(r, "prerelease")) return null;
        var tag = Str(r, "tag_name");
        var version = ParseVersion(tag);
        if (version is null || !r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var a in assets.EnumerateArray())
        {
            var name = Str(a, "name");
            var url = Str(a, "browser_download_url");
            if (!InstallerName.IsMatch(name) || !IsGitHubDownload(url)) continue;
            // GitHub indica l'impronta SHA-256 di ogni file ("digest": "sha256:…"): serve a verificare il download.
            var digest = Str(a, "digest");
            var sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
            return new UpdateInfo(version, tag, Str(r, "html_url"), Str(r, "body"), name, url,
                a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0, sha);
        }
        return null;
    }

    /// <summary>Solo file delle Release di questo repository su github.com, in https.</summary>
    public static bool IsGitHubDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com" &&
        u.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
