using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeDownloadVerificationService : IDownloadVerificationService
{
    public Dictionary<string, DownloadResult> ResultsByUrl { get; } = [];
    public DownloadResult DefaultResult { get; set; } = new()
    {
        Outcome = DownloadOutcome.NetworkError,
        ErrorMessage = "No result configured for this URL in the test."
    };
    public List<string> RequestedUrls { get; } = [];

    /// <summary>When set, DownloadAndVerifyAsync awaits this instead of returning immediately —
    /// lets a test hold a download "in progress" open long enough to exercise cancellation.</summary>
    public TaskCompletionSource<DownloadResult>? DownloadGate { get; set; }

    public async Task<DownloadResult> DownloadAndVerifyAsync(
        string sourceUrl,
        string destinationFileName,
        string expectedSha256,
        long expectedSizeBytes,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        RequestedUrls.Add(sourceUrl);

        if (DownloadGate is not null)
        {
            using var registration = ct.Register(() => DownloadGate.TrySetCanceled(ct));
            return await DownloadGate.Task;
        }

        return ResultsByUrl.GetValueOrDefault(sourceUrl, DefaultResult);
    }
}
