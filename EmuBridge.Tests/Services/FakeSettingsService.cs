using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeSettingsService : ISettingsService
{
    public string? ApiKey { get; set; }
    public string? TheGamesDbApiKey { get; set; }

    // Defaults to true to match SettingsService's real "never set yet" behavior.
    public bool AutoApplyCheatsOnLaunch { get; set; } = true;

    // Defaults to System, matching SettingsService's real "never set yet" behavior.
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    // Defaults to true, matching SettingsService's real "never set yet" behavior.
    public bool CheckForUpdatesOnStartup { get; set; } = true;

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

    public Task<ThemePreference> GetThemePreferenceAsync(CancellationToken ct = default)
        => Task.FromResult(Theme);

    public Task SetThemePreferenceAsync(ThemePreference preference, CancellationToken ct = default)
    {
        Theme = preference;
        return Task.CompletedTask;
    }

    public Task<bool> GetCheckForUpdatesOnStartupAsync(CancellationToken ct = default)
        => Task.FromResult(CheckForUpdatesOnStartup);

    public Task SetCheckForUpdatesOnStartupAsync(bool enabled, CancellationToken ct = default)
    {
        CheckForUpdatesOnStartup = enabled;
        return Task.CompletedTask;
    }
}
