using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeSettingsService : ISettingsService
{
    public string? ApiKey { get; set; }
    public string? TheGamesDbApiKey { get; set; }

    // Defaults to true to match SettingsService's real "never set yet" behavior.
    public bool AutoApplyCheatsOnLaunch { get; set; } = true;

    public Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default)
        => Task.FromResult(ApiKey);

    public Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        ApiKey = apiKey;
        return Task.CompletedTask;
    }

    public Task<string?> GetTheGamesDbApiKeyAsync(CancellationToken ct = default)
        => Task.FromResult(TheGamesDbApiKey);

    public Task SetTheGamesDbApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        TheGamesDbApiKey = apiKey;
        return Task.CompletedTask;
    }

    public Task<bool> GetAutoApplyCheatsOnLaunchAsync(CancellationToken ct = default)
        => Task.FromResult(AutoApplyCheatsOnLaunch);

    public Task SetAutoApplyCheatsOnLaunchAsync(bool enabled, CancellationToken ct = default)
    {
        AutoApplyCheatsOnLaunch = enabled;
        return Task.CompletedTask;
    }
}
