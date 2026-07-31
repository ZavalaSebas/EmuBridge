using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

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

    private string GetCachePath(string imageUrl, int width, int height)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)))[..16];
        return Path.Combine(_cacheDirectory, $"{hash}_{width}x{height}.png");
    }

    // Both dimensions are fixed to the exact target size (see ARCHITECTURE.md — "resize/cache box
    // art to the exact display pixel size"). A source image with a different aspect ratio will be
    // stretched rather than cropped — a deliberate Phase 1 simplification; a proper aspect-preserving
    // crop would need CroppedBitmap on top of this and isn't justified by any current requirement.
    private static void ResizeAndSave(byte[] sourceBytes, int width, int height, string destinationPath)
    {
        using var sourceStream = new MemoryStream(sourceBytes);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = width;
        bitmap.DecodePixelHeight = height;
        bitmap.StreamSource = sourceStream;
        bitmap.EndInit();
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var destinationStream = File.Create(destinationPath);
        encoder.Save(destinationStream);
    }
}
