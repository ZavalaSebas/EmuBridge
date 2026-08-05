using System.IO;
using EmuBridge.Exceptions;
using EmuBridge.Models;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class EmulatorServiceTests : IDisposable
{
    private readonly string _fakeExecutablePath;
    private readonly FakeLibraryRepository _repository;
    private readonly EmulatorService _service;

    public EmulatorServiceTests()
    {
        _fakeExecutablePath = Path.Combine(Path.GetTempPath(), $"emubridge_fake_emu_{Guid.NewGuid()}.exe");
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
    public async Task SaveProfileAsync_ExecutablePathDoesNotExist_ThrowsEmuBridgeException()
    {
        await Assert.ThrowsAsync<EmuBridgeException>(
            () => _service.SaveProfileAsync("nes", "Test Emulator", @"C:\does\not\exist.exe", "\"{RomPath}\""));
    }

    [Fact]
    public async Task SaveProfileAsync_ArgumentTemplateMissingRomPath_ThrowsEmuBridgeException()
    {
        await Assert.ThrowsAsync<EmuBridgeException>(
            () => _service.SaveProfileAsync("nes", "Test Emulator", _fakeExecutablePath, "-fullscreen"));
    }

    [Fact]
    public async Task SaveProfileAsync_UnknownPlatformId_ThrowsEmuBridgeException()
    {
        await Assert.ThrowsAsync<EmuBridgeException>(
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

    [Fact]
    public async Task GetInstalledKnownEmulatorAsync_NotInstalled_ReturnsNull()
    {
        var result = await _service.GetInstalledKnownEmulatorAsync("retroarch");

        Assert.Null(result);
    }

    [Fact]
    public async Task RegisterInstalledEmulatorAsync_PersistsAndIsFindableByKnownEmulatorId()
    {
        var registered = await _service.RegisterInstalledEmulatorAsync("retroarch", "RetroArch", _fakeExecutablePath, "abc123");

        Assert.Equal(InstallSource.EmuBridgeManaged, registered.InstallSource);
        var found = await _service.GetInstalledKnownEmulatorAsync("retroarch");
        Assert.NotNull(found);
        Assert.Equal(registered.Id, found!.Id);
        Assert.Equal("abc123", found.InstalledSha256);
    }

    [Fact]
    public async Task RegisterCoreProfileAsync_PersistsCorePathOnResolvedProfile()
    {
        var emulator = await _service.RegisterInstalledEmulatorAsync("retroarch", "RetroArch", _fakeExecutablePath, "abc123");
        var corePath = Path.Combine(Path.GetTempPath(), "fceumm_libretro.dll");

        await _service.RegisterCoreProfileAsync("nes", emulator.Id, corePath, "-L {CorePath} {RomPath}");

        var profile = await _service.GetProfileForPlatformAsync("nes");
        Assert.NotNull(profile);
        Assert.Equal(corePath, profile.CorePath);
        Assert.Equal(_fakeExecutablePath, profile.ExecutablePath);
    }

    // Per-game override (ARCHITECTURE.md -> ADR-24). The exact motivating case: 20 SNES games
    // share one platform-wide profile, but one game needs a different argument.
    [Fact]
    public async Task GetProfileForGameAsync_OverrideExists_UsesOverrideNotPlatformDefault()
    {
        await _service.SaveProfileAsync("snes", "Snes9x", _fakeExecutablePath, "\"{RomPath}\"");
        var oddGame = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Star Fox 2" };

        await _service.SaveProfileAsync("snes", "Snes9x (compat)", _fakeExecutablePath, "\"{RomPath}\" --gfx-compat", oddGame.Id);

        var resolved = await _service.GetProfileForGameAsync(oddGame);
        Assert.NotNull(resolved);
        Assert.Equal("\"{RomPath}\" --gfx-compat", resolved.ArgumentTemplate);
    }

    [Fact]
    public async Task GetProfileForGameAsync_NoOverride_FallsBackToPlatformDefault()
    {
        await _service.SaveProfileAsync("snes", "Snes9x", _fakeExecutablePath, "\"{RomPath}\"");
        var ordinaryGame = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Super Mario World" };

        var resolved = await _service.GetProfileForGameAsync(ordinaryGame);

        Assert.NotNull(resolved);
        Assert.Equal("\"{RomPath}\"", resolved.ArgumentTemplate);
    }

    [Fact]
    public async Task GetProfileForGameAsync_NeitherOverrideNorPlatformDefaultExists_ReturnsNull()
    {
        var game = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Unconfigured" };

        var resolved = await _service.GetProfileForGameAsync(game);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task SaveProfileAsync_OverrideForOneGame_DoesNotAffectSiblingGamesOnSamePlatform()
    {
        await _service.SaveProfileAsync("snes", "Snes9x", _fakeExecutablePath, "\"{RomPath}\"");
        var oddGame = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Star Fox 2" };
        var siblingGame = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Super Mario World" };

        await _service.SaveProfileAsync("snes", "Snes9x (compat)", _fakeExecutablePath, "\"{RomPath}\" --gfx-compat", oddGame.Id);

        var siblingResolved = await _service.GetProfileForGameAsync(siblingGame);
        var platformDefault = await _service.GetProfileForPlatformAsync("snes");
        Assert.Equal("\"{RomPath}\"", siblingResolved!.ArgumentTemplate);
        Assert.Equal("\"{RomPath}\"", platformDefault!.ArgumentTemplate);
        Assert.Equal(2, _repository.EmulatorProfiles.Count); // platform default + the one override
    }

    [Fact]
    public async Task SaveProfileAsync_PlatformDefaultAfterOverrideExists_DoesNotClobberOverride()
    {
        var oddGame = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Star Fox 2" };
        await _service.SaveProfileAsync("snes", "Snes9x (compat)", _fakeExecutablePath, "\"{RomPath}\" --gfx-compat", oddGame.Id);

        // Platform default configured (or reconfigured) afterward — must not touch the override.
        await _service.SaveProfileAsync("snes", "Snes9x", _fakeExecutablePath, "\"{RomPath}\"");

        var overrideResolved = await _service.GetProfileForGameAsync(oddGame);
        Assert.Equal("\"{RomPath}\" --gfx-compat", overrideResolved!.ArgumentTemplate);
    }

    [Fact]
    public async Task HasGameOverrideAsync_ReflectsWhetherOverrideExists()
    {
        var game = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Star Fox 2" };
        Assert.False(await _service.HasGameOverrideAsync(game.Id));

        await _service.SaveProfileAsync("snes", "Snes9x (compat)", _fakeExecutablePath, "\"{RomPath}\" --gfx-compat", game.Id);

        Assert.True(await _service.HasGameOverrideAsync(game.Id));
    }

    [Fact]
    public async Task ClearGameOverrideAsync_RemovesOverride_ResolutionFallsBackToPlatformDefault()
    {
        await _service.SaveProfileAsync("snes", "Snes9x", _fakeExecutablePath, "\"{RomPath}\"");
        var game = new Game { Id = Guid.NewGuid(), PlatformId = "snes", Name = "Star Fox 2" };
        await _service.SaveProfileAsync("snes", "Snes9x (compat)", _fakeExecutablePath, "\"{RomPath}\" --gfx-compat", game.Id);

        await _service.ClearGameOverrideAsync(game.Id);

        Assert.False(await _service.HasGameOverrideAsync(game.Id));
        var resolved = await _service.GetProfileForGameAsync(game);
        Assert.Equal("\"{RomPath}\"", resolved!.ArgumentTemplate);
    }
}
