namespace Bridge.Services;

public interface IImageCacheService
{
    /// <summary>
    /// Returns the local path to a cached, resized copy of the image at <paramref name="imageUrl"/>,
    /// downloading and resizing it first if not already cached. Returns null if the image could not
    /// be downloaded or decoded — never throws for network/decode failures.
    /// </summary>
    Task<string?> GetOrCacheImageAsync(string imageUrl, int targetWidth, int targetHeight, CancellationToken ct = default);

    /// <summary>
    /// Deletes a cached image file at <paramref name="localPath"/>, if present. Best-effort: logs
    /// and swallows I/O failures rather than throwing, since a failed cache cleanup shouldn't block
    /// whatever larger operation (e.g. removing a Game) triggered it. No-op if the file is already gone.
    /// </summary>
    Task DeleteCachedImageAsync(string localPath, CancellationToken ct = default);
}
