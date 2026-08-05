using System.IO;
using System.Net.Http;
using EmuBridge.Models;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class LaunchServiceTests : IDisposable
{
    private readonly string _romPath;
    private readonly string _cheatsDirectory;
    private readonly FakeEmulatorService _emulatorService;
    private readonly CheatService _cheatService;
    private readonly FakeSettingsService _settingsService;
    private readonly LaunchService _launchService;

    public LaunchServiceTests()
    {
        _romPath = Path.Combine(Path.GetTempPath(), $"emubridge_test_rom_{Guid.NewGuid()}.nes");
        File.WriteAllBytes(_romPath, [1, 2, 3]);
        _cheatsDirectory = Path.Combine(Path.GetTempPath(), $"emubridge_test_launch_cheats_{Guid.NewGuid()}");

        _emulatorService = new FakeEmulatorService();
        // Real CheatService, not a fake - GetCheatDirectoryIfExists is plain File.Exists logic
        // with no network involvement, so exercising the real implementation costs nothing and
        // proves the actual integration, not a stand-in's assumptions about it.
        _cheatService = new CheatService(new HttpClient(), _cheatsDirectory, NullLogger<CheatService>.Instance);
        _settingsService = new FakeSettingsService();
        _launchService = new LaunchService(_emulatorService, _cheatService, _settingsService, NullLogger<LaunchService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_romPath))
        {
            File.Delete(_romPath);
        }

        if (Directory.Exists(_cheatsDirectory))
        {
            Directory.Delete(_cheatsDirectory, recursive: true);
        }
    }

    private Game MakeGame(string platformId = "nes") => new()
    {
        Id = Guid.NewGuid(),
        Path = _romPath,
        Name = "Test Game",
        PlatformId = platformId
    };

    [Fact]
    public async Task LaunchAsync_RomFileMissing_ReturnsRomFileNotFound()
    {
        var game = MakeGame();
        game.Path = @"C:\does\not\exist.nes";

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.RomFileNotFound, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_KnownPlatformNoConfig_ReturnsNoEmulatorConfiguredWithSetupMessage()
    {
        var game = MakeGame("nes");

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.NoEmulatorConfigured, result.Outcome);
        Assert.Contains("Set one up in Settings", result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_UnknownPlatform_ReturnsNoEmulatorConfiguredWithUnknownMessage()
    {
        var game = MakeGame(Config.UnknownPlatformId);

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.NoEmulatorConfigured, result.Outcome);
        Assert.Contains("couldn't identify this ROM's system", result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_ConfiguredExecutableMissing_ReturnsExecutableNotFound()
    {
        var game = MakeGame("nes");
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\moved\or\uninstalled.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.ExecutableNotFound, result.Outcome);
    }

    [Fact]
    public async Task LaunchAsync_ConfiguredCoreMissing_ReturnsCoreNotFound()
    {
        var game = MakeGame("nes");
        var cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = cmdExePath,
            ArgumentTemplate = "-L {CorePath} {RomPath}",
            CorePath = @"C:\moved\or\uninstalled\core.dll"
        };

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.CoreNotFound, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_ValidConfigWithCorePath_LaunchesProcessAndReturnsSessionEndedTask()
    {
        var game = MakeGame("nes");
        var cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = cmdExePath,
                ArgumentTemplate = "/c echo -L {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);

            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            Assert.NotNull(result.GameSessionEndedTask);

            var completed = await Task.WhenAny(result.GameSessionEndedTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(result.GameSessionEndedTask, completed);
        }
        finally
        {
            File.Delete(corePath);
        }
    }

    [Fact]
    public async Task LaunchAsync_ValidConfig_LaunchesProcessAndReturnsSessionEndedTask()
    {
        var game = MakeGame("nes");
        var cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = cmdExePath,
            ArgumentTemplate = "/c echo {RomPath}"
        };

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.NotNull(result.GameSessionEndedTask);

        var completed = await Task.WhenAny(result.GameSessionEndedTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(result.GameSessionEndedTask, completed);
    }

    // Per-game override (ARCHITECTURE.md -> ADR-24) must win at the actual launch point, not just
    // at the resolution-service level. Proven decisively, not just "it started": the platform
    // default points at a nonexistent executable (would fail with ExecutableNotFound on its own),
    // while the override points at a real one — only a real Started outcome proves the override's
    // ExecutablePath was actually the one used.
    [Fact]
    public async Task LaunchAsync_GameHasOverride_UsesOverrideExecutableNotPlatformDefault()
    {
        var game = MakeGame("nes");
        var cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\moved\or\uninstalled.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };
        _emulatorService.ProfilesByGameId[game.Id] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = cmdExePath,
            ArgumentTemplate = "/c echo {RomPath}"
        };

        var result = await _launchService.LaunchAsync(game);

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.NotNull(result.GameSessionEndedTask);

        var completed = await Task.WhenAny(result.GameSessionEndedTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(result.GameSessionEndedTask, completed);
    }

    [Fact]
    public async Task LaunchAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var game = MakeGame("nes");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _launchService.LaunchAsync(game, cts.Token));
    }

    // ARCHITECTURE.md -> ADR-27: both settings LaunchService needs to hand RetroArch for a
    // EmuBridge-managed cheat file (cheat_database_path pointing at the per-game folder, and the
    // optional apply_cheats_after_load auto-apply) go through RetroArch's own per-game "override"
    // config file, not an env var or --appendconfig - both of those were confirmed, separately, to
    // leak permanently into the user's real retroarch.cfg. Uses the real CheatService against a
    // real, writable directory standing in for "the emulator's own executable directory"
    // (retroarch.exe can't be copied for a test, but LaunchService only ever derives
    // Path.GetDirectoryName(profile.ExecutablePath) - any writable directory containing a real
    // launchable exe proves the same wiring).
    private (string RetroArchDir, string ExePath) CreateFakeRetroArchDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"emubridge_test_retroarch_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var exePath = Path.Combine(dir, "cmd.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exePath);
        return (dir, exePath);
    }

    [Fact]
    public async Task LaunchAsync_CheatFileExistsForRetroArchGame_WritesCheatDatabasePathToOverrideFile()
    {
        var game = MakeGame("nes");
        var (retroArchDir, exePath) = CreateFakeRetroArchDirectory();
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);

        // RetroArch's own cheat_manager_get_game_specific_filename requires a "{core_name}"
        // subfolder ("FCEUmm" - the real, verified library_name for the nes core, see
        // CheatService.RetroArchCoreNames) - cheat_database_path itself must point at the per-game
        // ROOT, one level above that subfolder, so RetroArch's own lookup resolves.
        var expectedCheatDir = Path.Combine(_cheatsDirectory, game.Id.ToString());
        var coreDir = Path.Combine(expectedCheatDir, "FCEUmm");
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, $"{game.Name}.cht"), "cheats = 0\n");

        // Isolated from mechanism 2 on purpose - this test is only about cheat_database_path.
        _settingsService.AutoApplyCheatsOnLaunch = false;

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = exePath,
                ArgumentTemplate = "/c echo {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);
            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            await result.GameSessionEndedTask!;

            var expectedOverridePath = Path.Combine(retroArchDir, "FCEUmm", $"{game.Name}.cfg");
            Assert.True(File.Exists(expectedOverridePath));
            var content = await File.ReadAllTextAsync(expectedOverridePath);
            Assert.Equal($"cheat_database_path = \"{expectedCheatDir}\"\n", content);
        }
        finally
        {
            File.Delete(corePath);
            Directory.Delete(retroArchDir, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_NoCheatFileForGame_DoesNotWriteOverrideFile()
    {
        var game = MakeGame("nes");
        var (retroArchDir, exePath) = CreateFakeRetroArchDirectory();
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);
        // Deliberately no cheat file written anywhere under _cheatsDirectory for this game.

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = exePath,
                ArgumentTemplate = "/c echo {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);
            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            await result.GameSessionEndedTask!;

            var expectedOverridePath = Path.Combine(retroArchDir, "FCEUmm", $"{game.Name}.cfg");
            Assert.False(File.Exists(expectedOverridePath));
        }
        finally
        {
            File.Delete(corePath);
            Directory.Delete(retroArchDir, recursive: true);
        }
    }

    // ARCHITECTURE.md -> ADR-27 (mechanism 2): the "Auto-apply cheats on launch" Settings toggle
    // (default true) additionally makes LaunchService write apply_cheats_after_load into the same
    // override file, but only when a EmuBridge-managed cheat file already exists for this exact game -
    // same gate as cheat_database_path above.
    [Fact]
    public async Task LaunchAsync_CheatFileExistsAndAutoApplyToggleOn_WritesBothSettingsToOverrideFile()
    {
        var game = MakeGame("nes");
        var (retroArchDir, exePath) = CreateFakeRetroArchDirectory();
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);

        var expectedCheatDir = Path.Combine(_cheatsDirectory, game.Id.ToString());
        var coreDir = Path.Combine(expectedCheatDir, "FCEUmm");
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, $"{game.Name}.cht"), "cheats = 0\n");

        _settingsService.AutoApplyCheatsOnLaunch = true;

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = exePath,
                ArgumentTemplate = "/c echo {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);
            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            await result.GameSessionEndedTask!;

            var expectedOverridePath = Path.Combine(retroArchDir, "FCEUmm", $"{game.Name}.cfg");
            Assert.True(File.Exists(expectedOverridePath));
            var content = await File.ReadAllTextAsync(expectedOverridePath);
            Assert.Contains($"cheat_database_path = \"{expectedCheatDir}\"", content);
            Assert.Contains("apply_cheats_after_load = true", content);
        }
        finally
        {
            File.Delete(corePath);
            Directory.Delete(retroArchDir, recursive: true);
        }
    }

    // Point of this test: the toggle being off must never destroy a real override file the user
    // saved themselves via RetroArch's own "Save Game Override" menu action for that same game -
    // only the one apply_cheats_after_load line EmuBridge itself owns is ever removed. Unlike that
    // line, cheat_database_path is written regardless of the toggle - it has no "off" state.
    [Fact]
    public async Task LaunchAsync_CheatFileExistsAndAutoApplyToggleOff_WritesCheatDatabasePathAndRemovesOnlyApplyCheatsLine()
    {
        var game = MakeGame("nes");
        var (retroArchDir, exePath) = CreateFakeRetroArchDirectory();
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);

        var expectedCheatDir = Path.Combine(_cheatsDirectory, game.Id.ToString());
        var coreDir = Path.Combine(expectedCheatDir, "FCEUmm");
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, $"{game.Name}.cht"), "cheats = 0\n");

        var overrideDir = Path.Combine(retroArchDir, "FCEUmm");
        Directory.CreateDirectory(overrideDir);
        var overridePath = Path.Combine(overrideDir, $"{game.Name}.cfg");
        await File.WriteAllTextAsync(overridePath, "video_shader_enable = \"true\"\napply_cheats_after_load = \"true\"\naspect_ratio_index = \"5\"\n");

        _settingsService.AutoApplyCheatsOnLaunch = false;

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = exePath,
                ArgumentTemplate = "/c echo {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);
            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            await result.GameSessionEndedTask!;

            Assert.True(File.Exists(overridePath));
            var content = await File.ReadAllTextAsync(overridePath);
            Assert.Contains("video_shader_enable = \"true\"", content);
            Assert.Contains("aspect_ratio_index = \"5\"", content);
            Assert.Contains($"cheat_database_path = \"{expectedCheatDir}\"", content);
            Assert.DoesNotContain("apply_cheats_after_load", content);
        }
        finally
        {
            File.Delete(corePath);
            Directory.Delete(retroArchDir, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_NoCheatFileForGameAndAutoApplyToggleOn_DoesNotWriteOverrideFile()
    {
        var game = MakeGame("nes");
        var (retroArchDir, exePath) = CreateFakeRetroArchDirectory();
        var corePath = Path.Combine(Path.GetTempPath(), $"emubridge_test_core_{Guid.NewGuid()}.dll");
        File.WriteAllBytes(corePath, [1]);
        // Deliberately no cheat file written anywhere under _cheatsDirectory for this game.

        _settingsService.AutoApplyCheatsOnLaunch = true;

        try
        {
            _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
            {
                PlatformId = "nes",
                ExecutablePath = exePath,
                ArgumentTemplate = "/c echo {CorePath} {RomPath}",
                CorePath = corePath
            };

            var result = await _launchService.LaunchAsync(game);
            Assert.Equal(LaunchOutcome.Started, result.Outcome);
            await result.GameSessionEndedTask!;

            var expectedOverridePath = Path.Combine(retroArchDir, "FCEUmm", $"{game.Name}.cfg");
            Assert.False(File.Exists(expectedOverridePath));
        }
        finally
        {
            File.Delete(corePath);
            Directory.Delete(retroArchDir, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_CheatFileExistsButNoCorePath_DoesNotWriteOverrideFile()
    {
        // A manually-configured (non-RetroArch) profile has no CorePath and no cheat concept
        // EmuBridge understands - even if a cheat file coincidentally existed at the expected path,
        // it must never be surfaced to an emulator EmuBridge didn't install.
        var game = MakeGame("nes");
        var cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var expectedCheatDir = Path.Combine(_cheatsDirectory, game.Id.ToString());
        Directory.CreateDirectory(expectedCheatDir);
        await File.WriteAllTextAsync(Path.Combine(expectedCheatDir, $"{game.Name}.cht"), "cheats = 0\n");

        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = cmdExePath,
            ArgumentTemplate = "/c echo {RomPath}"
            // No CorePath set.
        };

        var result = await _launchService.LaunchAsync(game);
        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        await result.GameSessionEndedTask!;
    }
}
