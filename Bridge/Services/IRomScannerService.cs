using Bridge.Models;

namespace Bridge.Services;

public interface IRomScannerService
{
    Task<ScanResult> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task AddScanFolderAsync(ScanFolder folder, CancellationToken ct = default);
}
