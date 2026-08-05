using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public class ImageCacheService : IImageCacheService
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ILogger<ImageCacheService> _logger;

    public ImageCacheService(HttpClient httpClient, ILogger<ImageCacheService> logger)
        : this(httpClient, Config.ImageCachePath, logger)
    {
    }

    public ImageCacheService(HttpClient httpClient, string cacheDirectory, ILogger<ImageCacheService> logger)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _logger = logger;
    }

    public async Task<string?> GetOrCacheImageAsync(string imageUrl, int targetWidth, int targetHeight, CancellationToken ct = default)
    {
        var cachePath = GetCachePath(imageUrl, targetWidth, targetHeight);
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        byte[] sourceBytes;
        try
        {
            sourceBytes = await _httpClient.GetByteArrayAsync(imageUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to download image {ImageUrl}.", imageUrl);
            return null;
        }

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            ResizeAndSave(sourceBytes, targetWidth, targetHeight, cachePath);
            return cachePath;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            _logger.LogWarning(ex, "Failed to decode/resize/save image {ImageUrl}.", imageUrl);
            return null;
        }
    }

    public Task DeleteCachedImageAsync(string localPath, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete cached image {LocalPath}; leaving it in place.", localPath);
        }

        return Task.CompletedTask;
    }

    private string GetCachePath(string imageUrl, int width, int height)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)))[..16];
        return Path.Combine(_cacheDirectory, $"{hash}_{width}x{height}.png");
    }

    // Aspect ratio is preserved (Uniform-fit), not stretched to the exact target box — SteamGridDB's
    // real grid dimensions (460x215/920x430 horizontal, 600x900/342x482 vertical) don't reliably
    // match EmuBridge's own tile shapes, and forcing both dimensions visibly distorted the cover.
    // Letterboxed with a transparent background (not a baked-in solid color) so it stays correct
    // if the app's placeholder color (currently #333333, hardcoded in XAML) ever changes — the
    // existing tile Border behind the Image already provides that color. See ARCHITECTURE.md ->
    // ADR-23 (Update).
    private static void ResizeAndSave(byte[] sourceBytes, int width, int height, string destinationPath)
    {
        using var sourceStream = new MemoryStream(sourceBytes);

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = sourceStream;
        source.EndInit();
        source.Freeze();

        var scale = Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
        var scaledWidth = source.PixelWidth * scale;
        var scaledHeight = source.PixelHeight * scale;
        var offsetX = (width - scaledWidth) / 2;
        var offsetY = (height - scaledHeight) / 2;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(offsetX, offsetY, scaledWidth, scaledHeight));
        }

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var destinationStream = File.Create(destinationPath);
        encoder.Save(destinationStream);
    }
}
