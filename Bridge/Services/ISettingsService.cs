namespace Bridge.Services;

public interface ISettingsService
{
    Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default);
    Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default);

    Task<string?> GetTheGamesDbApiKeyAsync(CancellationToken ct = default);
    Task SetTheGamesDbApiKeyAsync(string apiKey, CancellationToken ct = default);

    // Whether LaunchService should append RetroArch's apply_cheats_after_load override
    // (ARCHITECTURE.md -> ADR-27) for games with a Bridge-managed cheat file. Defaults to true
    // when never explicitly set — matches the approved design ("default ON").
    Task<bool> GetAutoApplyCheatsOnLaunchAsync(CancellationToken ct = default);
    Task SetAutoApplyCheatsOnLaunchAsync(bool enabled, CancellationToken ct = default);
}
