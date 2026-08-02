using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class ImageCacheServiceTests : IDisposable
{
    // Well-known minimal valid 1x1 transparent PNG.
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // A wide (4:1), fully opaque red source — real box art aspect ratios aren't this extreme, but
    // an exaggerated ratio makes the letterbox bars unambiguous to assert on (see ARCHITECTURE.md
    // -> ADR-23 Update).
    private static byte[] CreateSolidColorPngBytes(int width, int height, Color color)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        }

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static (int Width, int Height, byte A, byte R, byte G, byte B) ReadPixel(string pngPath, int x, int y)
    {
        var decoder = new PngBitmapDecoder(new Uri(pngPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[frame.PixelHeight * stride];
        frame.CopyPixels(pixels, stride, 0);
        var offset = y * stride + x * 4;
        // Pbgra32 byte order: B, G, R, A.
        return (frame.PixelWidth, frame.PixelHeight, pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

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

    [Fact]
    public async Task DeleteCachedImageAsync_ExistingFile_RemovesIt()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ValidPngBytes)
        });
        var service = CreateService(handler);
        var path = await service.GetOrCacheImageAsync("https://example.com/cover.png", 100, 150);
        Assert.True(File.Exists(path));

        await service.DeleteCachedImageAsync(path!);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteCachedImageAsync_NonexistentFile_NoOpDoesNotThrow()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await service.DeleteCachedImageAsync(Path.Combine(_cacheDirectory, "never-existed.png"));
    }

    [Fact]
    public async Task GetOrCacheImageAsync_MismatchedAspectRatio_OutputIsExactlyTargetSize()
    {
        var wideSource = CreateSolidColorPngBytes(400, 100, Colors.Red);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(wideSource)
        });
        var service = CreateService(handler);

        var path = await service.GetOrCacheImageAsync("https://example.com/wide.png", 100, 100);

        var (pixelWidth, pixelHeight, _, _, _, _) = ReadPixel(path!, 0, 0);
        Assert.Equal(100, pixelWidth);
        Assert.Equal(100, pixelHeight);
    }

    [Fact]
    public async Task GetOrCacheImageAsync_MismatchedAspectRatio_LettersboxedNotStretched()
    {
        // 4:1 source into a 1:1 target — Uniform-fit scales to fill the full width and leaves
        // transparent bars top and bottom, not a stretch that fills every pixel.
        var wideSource = CreateSolidColorPngBytes(400, 100, Colors.Red);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(wideSource)
        });
        var service = CreateService(handler);

        var path = await service.GetOrCacheImageAsync("https://example.com/wide.png", 100, 100);

        // Corner: inside the letterbox bar — must be transparent, not stretched red.
        var corner = ReadPixel(path!, 5, 5);
        Assert.Equal(0, corner.A);

        // Vertical center: inside the scaled image — must be the real, undistorted source color.
        var center = ReadPixel(path!, 50, 50);
        Assert.Equal(255, center.A);
        Assert.Equal(255, center.R);
        Assert.Equal(0, center.G);
        Assert.Equal(0, center.B);
    }

    [Fact]
    public async Task GetOrCacheImageAsync_MatchingAspectRatio_FillsEntireTargetNoBars()
    {
        // Source and target share the same 2:1 ratio — Uniform-fit scale is exact, no letterbox.
        var source = CreateSolidColorPngBytes(200, 100, Colors.Blue);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(source)
        });
        var service = CreateService(handler);

        var path = await service.GetOrCacheImageAsync("https://example.com/match.png", 100, 50);

        var corner = ReadPixel(path!, 0, 0);
        Assert.Equal(255, corner.A);
        Assert.Equal(0, corner.R);
        Assert.Equal(0, corner.G);
        Assert.Equal(255, corner.B);
    }
}
