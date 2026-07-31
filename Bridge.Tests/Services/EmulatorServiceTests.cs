using System.IO;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class EmulatorServiceTests : IDisposable
{
    private readonly string _fakeExecutablePath;
    private readonly FakeLibraryRepository _repository;
    private readonly EmulatorService _service;

    public EmulatorServiceTests()
    {
        _fakeExecutablePath = Path.Combine(Path.GetTempPath(), $"bridge_fake_emu_{Guid.NewGuid()}.exe");
        File.WriteAllBytes(_fakeExecutablePath, [0]);

        _repository = new FakeLibraryRepository();
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "NES", Extensions = ["nes"] });

        _service = new EmulatorService(_repository, NullLogger<EmulatorService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_fakeExecutablePath))
        {
            File.Delete(_fakeExecutablePath);
        }
    }

    private EmulatorConfig ValidConfig() => new()
    {
        PlatformId = "nes",
        Name = "Test Emulator",
        ExecutablePath = _fakeExecutablePath,
        ArgumentTemplate = "\"{RomPath}\""
    };

    [Fact]
    public async Task SaveEmulatorConfigAsync_ValidConfig_Persists()
    {
        await _service.SaveEmulatorConfigAsync(ValidConfig());

        var stored = await _service.GetEmulatorConfigForPlatformAsync("nes");
        Assert.NotNull(stored);
        Assert.Equal(_fakeExecutablePath, stored.ExecutablePath);
    }

    [Fact]
    public async Task SaveEmulatorConfigAsync_ExecutablePathDoesNotExist_ThrowsBridgeException()
    {
        var config = ValidConfig();
        config.ExecutablePath = @"C:\does\not\exist.exe";

        await Assert.ThrowsAsync<BridgeException>(() => _service.SaveEmulatorConfigAsync(config));
    }

    [Fact]
    public async Task SaveEmulatorConfigAsync_ArgumentTemplateMissingRomPath_ThrowsBridgeException()
    {
        var config = ValidConfig();
        config.ArgumentTemplate = "-fullscreen";

        await Assert.ThrowsAsync<BridgeException>(() => _service.SaveEmulatorConfigAsync(config));
    }

    [Fact]
    public async Task SaveEmulatorConfigAsync_UnknownPlatformId_ThrowsBridgeException()
    {
        var config = ValidConfig();
        config.PlatformId = "does-not-exist";

        await Assert.ThrowsAsync<BridgeException>(() => _service.SaveEmulatorConfigAsync(config));
    }

    [Fact]
    public async Task GetEmulatorConfigForPlatformAsync_NoConfigForPlatform_ReturnsNull()
    {
        var result = await _service.GetEmulatorConfigForPlatformAsync("nes");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveEmulatorConfigAsync_SamePlatformTwice_UpdatesWithoutDuplicating()
    {
        await _service.SaveEmulatorConfigAsync(ValidConfig());

        var updated = ValidConfig();
        updated.Name = "Updated Name";
        await _service.SaveEmulatorConfigAsync(updated);

        Assert.Single(_repository.EmulatorConfigs);
        Assert.Equal("Updated Name", _repository.EmulatorConfigs[0].Name);
    }
}
