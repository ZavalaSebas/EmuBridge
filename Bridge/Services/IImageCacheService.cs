namespace Bridge.Services;

public interface IImageCacheService
{
    /// <summary>
    /// Returns the local path to a cached, resized copy of the image at <paramref name="imageUrl"/>,
    /// downloading and resizing it first if not already cached. Returns null if the image could not
    /// be downloaded or decoded — never throws for network/decode failures.
    /// </summary>
    Task<string?> GetOrCacheImageAsync(string imageUrl, int targetWidth, int targetHeight, CancellationToken ct = default);
}
