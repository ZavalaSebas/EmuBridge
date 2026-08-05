using EmuBridge.Models;

namespace EmuBridge.Services;

public interface IEmulatorService
{
    // gameId null (the default) saves/updates the platform-wide profile, unchanged from before
    // ADR-24. A real gameId saves/updates that one game's override instead, leaving the platform
    // default (and every other game on that platform) untouched.
    Task SaveProfileAsync(string platformId, string emulatorName, string executablePath, string argumentTemplate, Guid? gameId = null, CancellationToken ct = default);
    Task<ResolvedEmulatorProfile?> GetProfileForPlatformAsync(string platformId, CancellationToken ct = default);

    // Per-game override if one exists (ADR-24), falling back to the platform default — the
    // resolution LaunchService actually needs. GetProfileForPlatformAsync stays as-is for callers
    // that explicitly want the platform default regardless of any override (e.g. SettingsViewModel).
    Task<ResolvedEmulatorProfile?> GetProfileForGameAsync(Game game, CancellationToken ct = default);

    // Whether a per-game override currently exists for this game — the EmulatorOverrideWindow
    // needs this to distinguish "editing an existing override" from "about to create one,
    // currently just showing the platform default as a starting point."
    Task<bool> HasGameOverrideAsync(Guid gameId, CancellationToken ct = default);

    Task ClearGameOverrideAsync(Guid gameId, CancellationToken ct = default);

    // Auto-install path (ADR-14) — EmulatorInstallerService is the only caller. Kept on
    // IEmulatorService, not exposed via ILibraryRepository directly, so EmulatorService stays the
    // sole consumer of Emulator/EmulatorProfile persistence (ADR-11).
    Task<Emulator?> GetInstalledKnownEmulatorAsync(string knownEmulatorId, CancellationToken ct = default);
    Task<Emulator> RegisterInstalledEmulatorAsync(string knownEmulatorId, string name, string executablePath, string installedSha256, CancellationToken ct = default);
    Task RegisterCoreProfileAsync(string platformId, Guid emulatorId, string corePath, string argumentTemplate, CancellationToken ct = default);
}
