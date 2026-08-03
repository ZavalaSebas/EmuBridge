using System.Net;
using System.Net.Http;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class ManifestUpdateServiceTests
{
    private static ManifestUpdateService CreateService(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), NullLogger<ManifestUpdateService>.Instance);

    private static string ValidCatalogJson() => """
        [
          {
            "Id": "retroarch",
            "Name": "RetroArch",
            "Version": "1.22.2",
            "DownloadUrl": "https://example.com/retroarch.7z",
            "Sha256": "abc123",
            "ExpectedSizeBytes": 100,
            "ExecutableRelativePath": "RetroArch-Win64\\retroarch.exe",
            "Cores": [
              {
                "Id": "fceumm",
                "PlatformId": "nes",
                "DownloadUrl": "https://example.com/fceumm.zip",
                "Sha256": "def456",
                "ExpectedSizeBytes": 200,
                "CoreFileName": "fceumm_libretro.dll",
                "CapturedAt": "2026-08-02"
              }
            ]
          }
        ]
        """;

    [Fact]
    public void GetCatalog_BeforeAnyRefresh_FallsBackToEmbeddedCatalog()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, e => e.Id == "retroarch");
    }

    [Fact]
    public async Task RefreshAsync_ValidResponse_UpdatesCatalog()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidCatalogJson())
        });
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        var entry = Assert.Single(catalog);
        Assert.Equal("retroarch", entry.Id);
        Assert.Equal("abc123", entry.Sha256);
    }

    [Fact]
    public async Task RefreshAsync_NetworkFailure_DoesNotThrowAndKeepsFallback()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, e => e.Id == "retroarch");
    }

    [Fact]
    public async Task RefreshAsync_ServerError_DoesNotThrowAndKeepsFallback()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, e => e.Id == "retroarch");
    }

    [Fact]
    public async Task RefreshAsync_MalformedJson_DoesNotThrowAndKeepsFallback()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json")
        });
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, e => e.Id == "retroarch");
    }

    [Fact]
    public async Task RefreshAsync_EmptyArray_TreatedAsUnusableAndKeepsFallback()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, e => e.Id == "retroarch");
    }

    [Fact]
    public async Task RefreshAsync_ResponseContainsPlaceholderData_RejectedAndKeepsFallback()
    {
        var withPlaceholder = ValidCatalogJson().Replace("abc123", Config.UnverifiedManifestPlaceholder);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(withPlaceholder)
        });
        var service = CreateService(handler);

        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        // Falls back to the real embedded catalog, which never carries the placeholder in a
        // Release-shaped build — proves the tainted fetched copy was never cached, not just that
        // some catalog is present.
        Assert.DoesNotContain(catalog, e => e.Sha256 == Config.UnverifiedManifestPlaceholder);
    }

    [Fact]
    public async Task RefreshAsync_SecondSuccessfulCallOverwritesFirst()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            var json = callCount == 1 ? ValidCatalogJson() : ValidCatalogJson().Replace("abc123", "updated-hash");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        var service = CreateService(handler);

        await service.RefreshAsync();
        await service.RefreshAsync();
        var catalog = service.GetCatalog();

        Assert.Equal("updated-hash", Assert.Single(catalog).Sha256);
    }
}
