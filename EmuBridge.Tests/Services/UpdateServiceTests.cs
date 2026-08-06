using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class UpdateServiceTests
{
    private const string LatestUrl = "https://api.github.com/repos/ZavalaSebas/EmuBridge/releases/latest";

    private static UpdateService CreateService(FakeHttpMessageHandler handler, Version currentVersion)
    {
        var httpClient = new HttpClient(handler);
        return new UpdateService(httpClient, LatestUrl, currentVersion, NullLogger<UpdateService>.Instance);
    }

    private static string ReleaseJson(string tag, string assetUrl, string? digest = null)
    {
        var digestField = digest is null ? "" : $",\"digest\":\"{digest}\"";
        return $$"""
            {
              "tag_name": "{{tag}}",
              "html_url": "https://github.com/ZavalaSebas/EmuBridge/releases/tag/{{tag}}",
              "assets": [
                { "name": "EmuBridge.exe", "browser_download_url": "{{assetUrl}}", "size": 123{{digestField}} }
              ]
            }
            """;
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewerReleaseAvailable_ReturnsIt()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v1.0.0", "https://github.com/ZavalaSebas/EmuBridge/releases/download/v1.0.0/EmuBridge.exe", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"))
        });
        var service = CreateService(handler, new Version(0, 10, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 0, 0), result.LatestVersion);
        Assert.Equal("0.10.0", result.CurrentVersionText);
        Assert.Equal("1.0.0", result.LatestVersionText);
        Assert.Equal("https://github.com/ZavalaSebas/EmuBridge/releases/download/v1.0.0/EmuBridge.exe", result.DownloadUrl);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Sha256Digest);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameVersion_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v1.0.0", "https://example.com/EmuBridge.exe"))
        });
        var service = CreateService(handler, new Version(1, 0, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_CurrentVersionNewer_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v0.9.0", "https://example.com/EmuBridge.exe"))
        });
        var service = CreateService(handler, new Version(1, 0, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SendsUserAgentHeader()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v0.9.0", "https://example.com/EmuBridge.exe"))
        });
        var service = CreateService(handler, new Version(0, 10, 0));

        await service.CheckForUpdateAsync();

        Assert.Contains(handler.Requests, r => r.Headers.UserAgent.ToString().Contains("EmuBridge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpError_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler, new Version(0, 10, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReleaseWithoutAsset_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "tag_name": "v1.0.0", "assets": [] }""")
        });
        var service = CreateService(handler, new Version(0, 10, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NonParseableTag_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "tag_name": "latest-garbage", "assets": [] }""")
        });
        var service = CreateService(handler, new Version(0, 10, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdateAsync_MalformedJson_NoUpdate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this is not json")
        });
        var service = CreateService(handler, new Version(0, 10, 0));

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public void HashMatches_MatchingDigest_ReturnsTrue()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, [1, 2, 3, 4, 5]);
            var hex = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

            Assert.True(UpdateService.HashMatches(file, $"sha256:{hex}"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void HashMatches_DifferentDigest_ReturnsFalse()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, [1, 2, 3, 4, 5]);

            Assert.False(UpdateService.HashMatches(file, $"sha256:{new string('0', 64)}"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void HashMatches_MissingDigest_ReturnsTrue()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, [1, 2, 3, 4, 5]);

            Assert.True(UpdateService.HashMatches(file, null));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
