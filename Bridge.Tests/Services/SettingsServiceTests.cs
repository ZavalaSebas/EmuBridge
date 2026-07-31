using System.IO;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"bridge_settings_test_{Guid.NewGuid()}.json");
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
}
