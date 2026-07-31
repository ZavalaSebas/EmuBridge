using Bridge.Models;

namespace Bridge.Services;

public interface IDownloadVerificationService
{
    // progress reports cumulative bytes downloaded so far (not a percentage — the caller already
    // has expectedSizeBytes and can compute one if it wants).
    Task<DownloadResult> DownloadAndVerifyAsync(
        string sourceUrl,
        string destinationFileName,
        string expectedSha256,
        long expectedSizeBytes,
        IProgress<long>? progress = null,
        CancellationToken ct = default);
}
