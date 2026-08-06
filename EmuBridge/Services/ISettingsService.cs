using EmuBridge.Models;

namespace EmuBridge.Services;

public interface ISettingsService
{
    Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default);
    Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default);

    Task<string?> GetTheGamesDbApiKeyAsync(CancellationToken ct = default);
    Task SetTheGamesDbApiKeyAsync(string apiKey, CancellationToken ct = default);

    // Whether LaunchService should append RetroArch's apply_cheats_after_load override
    // (ARCHITECTURE.md -> ADR-27) for games with a EmuBridge-managed cheat file. Defaults to true
    // when never explicitly set — matches the approved design ("default ON").
    Task<bool> GetAutoApplyCheatsOnLaunchAsync(CancellationToken ct = default);
    Task SetAutoApplyCheatsOnLaunchAsync(bool enabled, CancellationToken ct = default);

    // Theme customization (Phase Polish) — persisted here so settings.json keeps a single owner
    // (SettingsService writes it, ThemeService only ever reads the same file at startup). Nullable
    // on disk so "never set" is distinguishable from an explicit System — both resolve to System.
    Task<ThemePreference> GetThemePreferenceAsync(CancellationToken ct = default);
    Task SetThemePreferenceAsync(ThemePreference preference, CancellationToken ct = default);

    // Whether to auto-check GitHub Releases for a newer EmuBridge on startup (Phase Polish ->
    // "Auto-updater"). Defaults to true when never explicitly set — updates are how users reach
    // fixes, and the check is a cheap, silent, non-blocking API call.
    Task<bool> GetCheckForUpdatesOnStartupAsync(CancellationToken ct = default);
    Task SetCheckForUpdatesOnStartupAsync(bool enabled, CancellationToken ct = default);
}
