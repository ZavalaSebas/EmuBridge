using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeUpdateService : IUpdateService
{
    // Set by tests to control what CheckForUpdateAsync returns.
    public UpdateCheckResult NextCheckResult { get; set; } = new() { IsUpdateAvailable = false };

    // Captures what DownloadAndApplyAsync returned when the test exercises the apply path.
    public UpdateApplyResult? NextApplyResult { get; set; }
    public UpdateCheckResult? LastAppliedUpdate { get; private set; }
    public List<string> ReportedProgress { get; } = [];

    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
        => Task.FromResult(NextCheckResult);

    public Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateCheckResult update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        LastAppliedUpdate = update;
        progress?.Report("downloaded");
        return Task.FromResult(NextApplyResult ?? new UpdateApplyResult { Outcome = UpdateApplyOutcome.Success });
    }

    public void CleanupOldExecutable()
    {
    }
}
