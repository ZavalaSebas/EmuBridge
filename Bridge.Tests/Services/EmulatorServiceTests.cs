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
        _repository.Platforms.Add(new Platform { Id = "snes", Name = "SNES", Extensions = ["sfc"] });

        _service = new EmulatorService(_repository, NullLogger<EmulatorService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_fakeExecutablePath))
        {
            File.Delete(_fakeExecutablePath);
        }
    }

    [Fact]
    public async Task SaveProfileAsync_ValidInput_Persists()
    {
        await _service.SaveProfileAsync("nes", "Test Emulator", _fakeExecutablePath, "\"{RomPath}\"");

        var stored = await _service.GetProfileForPlatformAsync("nes");
        Assert.NotNull(stored);
        Assert.Equal(_fakeExecutablePath, stored.ExecutablePath);
    }

    [Fact]
    public async Task SaveProfileAsync_ExecutablePathDoesNotExist_ThrowsBridgeException()
    {
        await Assert.ThrowsAsync<BridgeException>(
            () => _service.SaveProfileAsync("nes", "Test Emulator", @"C:\does\not\exist.exe", "\"{RomPath}\""));
    }

    [Fact]
    public async Task SaveProfileAsync_ArgumentTemplateMissingRomPath_ThrowsBridgeException()
    {
        await Assert.ThrowsAsync<BridgeException>(
            () => _service.SaveProfileAsync("nes", "Test Emulator", _fakeExecutablePath, "-fullscreen"));
    }

    [Fact]
    public async Task SaveProfileAsync_UnknownPlatformId_ThrowsBridgeException()
    {
        await Assert.ThrowsAsync<BridgeException>(
            () => _service.SaveProfileAsync("does-not-exist", "Test Emulator", _fakeExecutablePath, "\"{RomPath}\""));
    }

    [Fact]
    public async Task GetProfileForPlatformAsync_NoProfileForPlatform_ReturnsNull()
    {
        var result = await _service.GetProfileForPlatformAsync("nes");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveProfileAsync_SamePlatformTwice_UpdatesWithoutDuplicating()
    {
        await _service.SaveProfileAsync("nes", "Test Emulator", _fakeExecutablePath, "\"{RomPath}\"");
        await _service.SaveProfileAsync("nes", "Updated Name", _fakeExecutablePath, "\"{RomPath}\" -fs");

        Assert.Single(_repository.Emulators);
        Assert.Single(_repository.EmulatorProfiles);
        Assert.Equal("Updated Name", _repository.Emulators[0].Name);
        Assert.Equal("\"{RomPath}\" -fs", _repository.EmulatorProfiles[0].ArgumentTemplate);
    }

    // The actual reason for the Emulator/EmulatorProfile split (ADR-11): one physical install
    // (e.g. RetroArch) backing multiple platforms shares a single Emulator row instead of
    // duplicating it per platform, the way the old 1:1 EmulatorConfig would have.
    [Fact]
    public async Task SaveProfileAsync_SameExecutablePathDifferentPlatforms_SharesOneEmulatorRow()
    {
        await _service.SaveProfileAsync("nes", "RetroArch", _fakeExecutablePath, "-L cores\\nestopia.dll \"{RomPath}\"");
        await _service.SaveProfileAsync("snes", "RetroArch", _fakeExecutablePath, "-L cores\\snes9x.dll \"{RomPath}\"");

        Assert.Single(_repository.Emulators);
        Assert.Equal(2, _repository.EmulatorProfiles.Count);

        var nesProfile = await _service.GetProfileForPlatformAsync("nes");
        var snesProfile = await _service.GetProfileForPlatformAsync("snes");
        Assert.Equal(_fakeExecutablePath, nesProfile!.ExecutablePath);
        Assert.Equal(_fakeExecutablePath, snesProfile!.ExecutablePath);
        Assert.NotEqual(nesProfile.ArgumentTemplate, snesProfile.ArgumentTemplate);
    }
}
