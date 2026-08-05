using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeRomScannerService : IRomScannerService
{
    public ScanResult NextScanResult { get; set; } = new();
    public int ScanAsyncCallCount { get; private set; }

    /// <summary>When set, ScanAsync awaits this instead of returning immediately — lets a test
    /// hold a scan "in progress" open long enough to exercise busy-guard/cancellation behavior.</summary>
    public TaskCompletionSource<ScanResult>? ScanGate { get; set; }

    public Func<ScanFolder, Task>? AddScanFolderHandler { get; set; }

    public async Task<ScanResult> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ScanAsyncCallCount++;
        if (ScanGate is not null)
        {
            using var registration = ct.Register(() => ScanGate.TrySetCanceled(ct));
            return await ScanGate.Task;
        }

        return NextScanResult;
    }

    public Task AddScanFolderAsync(ScanFolder folder, CancellationToken ct = default)
        => AddScanFolderHandler?.Invoke(folder) ?? Task.CompletedTask;
}
