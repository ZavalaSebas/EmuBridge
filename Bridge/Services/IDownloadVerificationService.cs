using Bridge.Models;

namespace Bridge.Services;

public interface IDownloadVerificationService
{
    Task<DownloadResult> DownloadAndVerifyAsync(
        string sourceUrl,
        string destinationFileName,
        string expectedSha256,
        long expectedSizeBytes,
        CancellationToken ct = default);
}
