using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeMetadataService : IMetadataService
{
    public MetadataFetchResult NextResult { get; set; } = new();
    public bool FetchMissingBoxArtCalled { get; private set; }

    public Task<MetadataFetchResult> FetchMissingBoxArtAsync(
        int targetWidth,
        int targetHeight,
        int verticalTargetWidth,
        int verticalTargetHeight,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        FetchMissingBoxArtCalled = true;
        return Task.FromResult(NextResult);
    }
}
