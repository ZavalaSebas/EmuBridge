using Bridge.Models;

namespace Bridge.Services;

public interface IMetadataService
{
    Task<MetadataFetchResult> FetchMissingBoxArtAsync(
        int targetWidth,
        int targetHeight,
        int verticalTargetWidth,
        int verticalTargetHeight,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
