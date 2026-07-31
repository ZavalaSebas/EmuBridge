using Bridge.Models;

namespace Bridge.Services;

public interface IEmulatorInstallerService
{
    // True only if the catalog has a fully-verified (no placeholder data) KnownEmulatorCore for
    // this platform — drives whether Settings even offers the auto-install option.
    Task<bool> HasKnownInstallOptionAsync(string platformId, CancellationToken ct = default);

    // progress reports short, human-readable stage labels ("Downloading RetroArch... 45 / 193 MB").
    Task<InstallResult> InstallAsync(string platformId, IProgress<string>? progress = null, CancellationToken ct = default);
}
