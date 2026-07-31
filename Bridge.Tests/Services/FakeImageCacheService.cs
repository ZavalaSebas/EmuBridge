using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeImageCacheService : IImageCacheService
{
    public List<string> RequestedUrls { get; } = [];

    /// <summary>Function computing the result path for a given URL, or null to simulate a failed fetch.</summary>
    public Func<string, string?> ResultFactory { get; set; } = url => $"C:\\fake-cache\\{url.GetHashCode()}.png";

    public Task<string?> GetOrCacheImageAsync(string imageUrl, int targetWidth, int targetHeight, CancellationToken ct = default)
    {
        RequestedUrls.Add(imageUrl);
        return Task.FromResult(ResultFactory(imageUrl));
    }
}
