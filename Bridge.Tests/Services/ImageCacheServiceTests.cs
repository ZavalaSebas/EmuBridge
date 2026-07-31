using System.IO;
using System.Net;
using System.Net.Http;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class ImageCacheServiceTests : IDisposable
{
    // Well-known minimal valid 1x1 transparent PNG.
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _cacheDirectory;

    public ImageCacheServiceTests()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), $"bridge_imagecache_test_{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private ImageCacheService CreateService(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), _cacheDirectory, NullLogger<ImageCacheService>.Instance);

    [Fact]
    public async Task GetOrCacheImageAsync_NewUrl_DownloadsAndCachesImage()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ValidPngBytes)
        });
        var service = CreateService(handler);

        var path = await service.GetOrCacheImageAsync("https://example.com/cover.png", 100, 150);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetOrCacheImageAsync_SameUrlAndSizeCalledTwice_OnlyDownloadsOnce()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ValidPngBytes)
        });
        var service = CreateService(handler);

        await service.GetOrCacheImageAsync("https://example.com/cover.png", 100, 150);
        await service.GetOrCacheImageAsync("https://example.com/cover.png", 100, 150);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetOrCacheImageAsync_DownloadFails_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var path = await service.GetOrCacheImageAsync("https://example.com/missing.png", 100, 150);

        Assert.Null(path);
    }

    [Fact]
    public async Task GetOrCacheImageAsync_DifferentTargetSizesSameUrl_ProduceDifferentCacheFiles()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ValidPngBytes)
        });
        var service = CreateService(handler);

        var pathA = await service.GetOrCacheImageAsync("https://example.com/cover.png", 100, 150);
        var pathB = await service.GetOrCacheImageAsync("https://example.com/cover.png", 50, 75);

        Assert.NotEqual(pathA, pathB);
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
    }
}
