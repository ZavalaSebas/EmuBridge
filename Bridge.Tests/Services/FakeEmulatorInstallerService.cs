using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeEmulatorInstallerService : IEmulatorInstallerService
{
    public HashSet<string> PlatformsWithKnownInstallOption { get; } = [];
    public InstallResult NextResult { get; set; } = new() { Outcome = InstallOutcome.Success };
    public List<string> InstalledPlatformIds { get; } = [];

    /// <summary>When set, InstallAsync awaits this instead of returning immediately — lets a test
    /// hold an install "in progress" open long enough to exercise cancellation behavior.</summary>
    public TaskCompletionSource<InstallResult>? InstallGate { get; set; }

    public Task<bool> HasKnownInstallOptionAsync(string platformId, CancellationToken ct = default)
        => Task.FromResult(PlatformsWithKnownInstallOption.Contains(platformId));

    public async Task<InstallResult> InstallAsync(string platformId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Deliberately does not call progress.Report — System.Progress<T>'s dispatch is
        // asynchronous relative to the calling code (fine for real WPF use, where the Dispatcher
        // naturally serializes it; not deterministic in a test with no SynchronizationContext).
        // Progress reporting itself is covered at the DownloadVerificationServiceTests and
        // EmulatorInstallerServiceTests level, not needed here.
        InstalledPlatformIds.Add(platformId);

        if (InstallGate is not null)
        {
            using var registration = ct.Register(() => InstallGate.TrySetCanceled(ct));
            return await InstallGate.Task;
        }

        return NextResult;
    }
}
