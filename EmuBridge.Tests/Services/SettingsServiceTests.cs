using System.IO;
using EmuBridge.Models;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"emubridge_settings_test_{Guid.NewGuid()}.json");
        _service = new SettingsService(_settingsPath, NullLogger<SettingsService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    [Fact]
    public async Task GetSteamGridDbApiKeyAsync_NoSettingsFileYet_ReturnsNull()
    {
        var key = await _service.GetSteamGridDbApiKeyAsync();

        Assert.Null(key);
    }

    [Fact]
    public async Task SetThenGetSteamGridDbApiKeyAsync_RoundTripsCorrectly()
    {
        await _service.SetSteamGridDbApiKeyAsync("my-secret-key");

        var key = await _service.GetSteamGridDbApiKeyAsync();

        Assert.Equal("my-secret-key", key);
    }

    [Fact]
    public async Task SetSteamGridDbApiKeyAsync_StoresKeyEncryptedNotPlainText()
    {
        await _service.SetSteamGridDbApiKeyAsync("my-secret-key");

        var fileContents = await File.ReadAllTextAsync(_settingsPath);

        Assert.DoesNotContain("my-secret-key", fileContents);
    }

    [Fact]
    public async Task GetTheGamesDbApiKeyAsync_NoSettingsFileYet_ReturnsNull()
    {
        var key = await _service.GetTheGamesDbApiKeyAsync();

        Assert.Null(key);
    }

    [Fact]
    public async Task SetThenGetTheGamesDbApiKeyAsync_RoundTripsCorrectly()
    {
        await _service.SetTheGamesDbApiKeyAsync("tgdb-secret-key");

        var key = await _service.GetTheGamesDbApiKeyAsync();

        Assert.Equal("tgdb-secret-key", key);
    }

    [Fact]
    public async Task SetTheGamesDbApiKeyAsync_StoresKeyEncryptedNotPlainText()
    {
        await _service.SetTheGamesDbApiKeyAsync("tgdb-secret-key");

        var fileContents = await File.ReadAllTextAsync(_settingsPath);

        Assert.DoesNotContain("tgdb-secret-key", fileContents);
    }

    [Fact]
    public async Task SetBothApiKeys_RoundTripIndependently()
    {
        await _service.SetSteamGridDbApiKeyAsync("sgdb-key");
        await _service.SetTheGamesDbApiKeyAsync("tgdb-key");

        Assert.Equal("sgdb-key", await _service.GetSteamGridDbApiKeyAsync());
        Assert.Equal("tgdb-key", await _service.GetTheGamesDbApiKeyAsync());
    }

    [Fact]
    public async Task GetThemePreferenceAsync_NoSettingsFileYet_DefaultsToSystem()
    {
        var theme = await _service.GetThemePreferenceAsync();

        Assert.Equal(ThemePreference.System, theme);
    }

    [Fact]
    public async Task SetThenGetThemePreferenceAsync_RoundTrips()
    {
        await _service.SetThemePreferenceAsync(ThemePreference.Dark);

        var theme = await _service.GetThemePreferenceAsync();

        Assert.Equal(ThemePreference.Dark, theme);
    }

    [Fact]
    public async Task ThemeIsPersistedByNameNotNumber()
    {
        await _service.SetThemePreferenceAsync(ThemePreference.Dark);

        var fileContents = await File.ReadAllTextAsync(_settingsPath);

        Assert.Contains("Dark", fileContents);
        Assert.DoesNotContain("\"2\"", fileContents);
    }

    [Fact]
    public async Task SetThemePreferenceAsync_DoesNotClobberExistingApiKey()
    {
        await _service.SetSteamGridDbApiKeyAsync("keep-me");
        await _service.SetThemePreferenceAsync(ThemePreference.Dark);

        Assert.Equal("keep-me", await _service.GetSteamGridDbApiKeyAsync());
    }

    [Fact]
    public async Task GetCheckForUpdatesOnStartupAsync_NoSettingsFileYet_DefaultsToTrue()
    {
        Assert.True(await _service.GetCheckForUpdatesOnStartupAsync());
    }

    [Fact]
    public async Task SetThenGetCheckForUpdatesOnStartupAsync_RoundTrips()
    {
        await _service.SetCheckForUpdatesOnStartupAsync(false);

        Assert.False(await _service.GetCheckForUpdatesOnStartupAsync());
    }
}
