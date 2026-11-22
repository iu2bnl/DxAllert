using System.Net;
using System.Security.Cryptography;
using System.Text;
using DXAllert.Core;
using DXAllert.Data;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Aggiornamenti dalle Release di GitHub.</summary>
public class UpdateTests
{
    private const string Url = "https://github.com/iu2bnl/DxAllert/releases/download/v1.1.3/DXAllert1.1.3.exe";

    private static string Release(string tag = "v1.1.3", string name = "DXAllert1.1.3.exe", string url = Url,
        string digest = "sha256:ABCDEF", bool prerelease = false, long size = 123) => $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/iu2bnl/DxAllert/releases/tag/{{tag}}",
          "draft": false, "prerelease": {{(prerelease ? "true" : "false")}}, "body": "Novità",
          "assets": [ { "name": "note.txt", "browser_download_url": "https://github.com/iu2bnl/DxAllert/releases/download/x/note.txt" },
                      { "name": "{{name}}", "browser_download_url": "{{url}}", "size": {{size}}, "digest": "{{digest}}" } ] }
        """;

    [Fact]
    public void ReadsVersionInstallerAndDigest()
    {
        var u = ReleaseParser.Parse(Release())!;
        Assert.Equal(new Version(1, 1, 3), u.Version);
        Assert.Equal("DXAllert1.1.3.exe", u.InstallerName);
        Assert.Equal(Url, u.InstallerUrl);
        Assert.Equal("abcdef", u.Sha256);
        Assert.Equal(123, u.InstallerSize);
    }

    [Theory]
    [InlineData("v1.2", 1, 2, 0)]
    [InlineData("1.1.3", 1, 1, 3)]
    [InlineData("DXAllert 2.0.1", 2, 0, 1)]
    public void ParsesTags(string tag, int a, int b, int c) => Assert.Equal(new Version(a, b, c), ReleaseParser.ParseVersion(tag));

    [Fact]
    public void IgnoresPrereleasesForeignHostsAndOtherFiles()
    {
        Assert.Null(ReleaseParser.Parse(Release(prerelease: true)));
        Assert.Null(ReleaseParser.Parse(Release(url: "https://example.com/iu2bnl/DxAllert/releases/download/v1.1.3/DXAllert1.1.3.exe")));
        Assert.Null(ReleaseParser.Parse(Release(url: "https://github.com/altro/DxAllert/releases/download/v1.1.3/DXAllert1.1.3.exe")));
        Assert.Null(ReleaseParser.Parse(Release(name: "setup.exe")));
    }

    [Fact]
    public async Task OnlyNewerVersionsAreOffered()
    {
        var svc = new UpdateService(new HttpClient(new Stub(Encoding.UTF8.GetBytes(Release()))));
        Assert.NotNull(await svc.CheckAsync(new Version(1, 1, 2, 0), default));
        Assert.Null(await svc.CheckAsync(new Version(1, 1, 3, 0), default));
    }

    [Fact]
    public async Task DownloadIsVerifiedWithSha256()
    {
        var data = Encoding.ASCII.GetBytes("installer");
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var svc = new UpdateService(new HttpClient(new Stub(data)));
        var good = new UpdateInfo(new Version(1, 1, 3), "v1.1.3", "", "", "DXAllert1.1.3.exe", Url, data.Length, sha);
        var path = await svc.DownloadAsync(good, null, default);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
        File.Delete(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => svc.DownloadAsync(good with { Sha256 = new string('0', 64) }, null, default));
        Assert.False(File.Exists(path));
    }

    private sealed class Stub(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
