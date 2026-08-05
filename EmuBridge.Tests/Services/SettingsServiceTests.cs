using System.IO;
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
}
