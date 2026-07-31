using Bridge.Models;

namespace Bridge.Services;

public interface IEmulatorService
{
    Task SaveProfileAsync(string platformId, string emulatorName, string executablePath, string argumentTemplate, CancellationToken ct = default);
    Task<ResolvedEmulatorProfile?> GetProfileForPlatformAsync(string platformId, CancellationToken ct = default);

    // Auto-install path (ADR-14) — EmulatorInstallerService is the only caller. Kept on
    // IEmulatorService, not exposed via ILibraryRepository directly, so EmulatorService stays the
    // sole consumer of Emulator/EmulatorProfile persistence (ADR-11).
    Task<Emulator?> GetInstalledKnownEmulatorAsync(string knownEmulatorId, CancellationToken ct = default);
    Task<Emulator> RegisterInstalledEmulatorAsync(string knownEmulatorId, string name, string executablePath, string installedSha256, CancellationToken ct = default);
    Task RegisterCoreProfileAsync(string platformId, Guid emulatorId, string corePath, string argumentTemplate, CancellationToken ct = default);
}
