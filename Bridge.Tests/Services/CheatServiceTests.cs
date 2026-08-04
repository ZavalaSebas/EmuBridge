using System.Net;
using System.Net.Http;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class CheatServiceTests : IDisposable
{
    private const string ValidCheatFile = """
        cheats = 1

        cheat0_desc = "Infinite Lives"
        cheat0_code = "AAAAAAAA"
        cheat0_enable = false
        """;

    private readonly string _cheatsDirectory;

    public CheatServiceTests()
    {
        _cheatsDirectory = Path.Combine(Path.GetTempPath(), $"bridge_test_cheats_{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_cheatsDirectory))
        {
            Directory.Delete(_cheatsDirectory, recursive: true);
        }
    }

    private static Game MakeGame(string platformId = "nes") => new()
    {
        Id = Guid.NewGuid(),
        Path = @"C:\roms\Test Game.nes",
        Name = "Test Game",
        PlatformId = platformId
    };

    private CheatService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new FakeHttpMessageHandler(responder)), _cheatsDirectory, NullLogger<CheatService>.Instance);

    // RetroArch's own cheat_manager_get_game_specific_filename requires
    // "{cheat_database_path}/{core_name}/{game_name}.cht" - "FCEUmm" is the real, verified
    // library_name for the nes core (see CheatService.RetroArchCoreNames). Every test game here
    // uses platform "nes" unless stated otherwise.
    private string GetCoreDir(Game game) => Path.Combine(_cheatsDirectory, game.Id.ToString(), "FCEUmm");

    [Fact]
    public async Task LoadCheatsAsync_PlatformNotInDatabase_ReturnsPlatformNotSupportedWithoutHttpCall()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called for an unmapped platform"));
        var game = MakeGame("wonderswan");

        var result = await service.LoadCheatsAsync(game, "wonderswan");

        Assert.Equal(CheatFetchOutcome.PlatformNotSupported, result.Outcome);
    }

    [Fact]
    public async Task LoadCheatsAsync_FetchSucceeds_ReturnsCheatsAndPersistsFileAndSourceSidecar()
    {
        var service = CreateService(req => req.RequestUri!.AbsoluteUri.Contains("Test%20Game.cht")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidCheatFile) }
            : throw new InvalidOperationException($"Unexpected request: {req.RequestUri}"));
        var game = MakeGame("nes");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.Success, result.Outcome);
        Assert.Single(result.Cheats);
        Assert.Equal("Infinite Lives", result.Cheats[0].Description);
        Assert.False(result.Cheats[0].Enabled);
        Assert.NotNull(result.SourceFileUrl);
        Assert.Contains("github.com/libretro/libretro-database/blob/master/cht", result.SourceFileUrl);
        Assert.Contains("Nintendo%20-%20Nintendo%20Entertainment%20System", result.SourceFileUrl);

        var expectedFilePath = Path.Combine(GetCoreDir(game), "Test Game.cht");
        Assert.True(File.Exists(expectedFilePath));
        Assert.Equal(ValidCheatFile, await File.ReadAllTextAsync(expectedFilePath));

        var sidecarPath = Path.Combine(GetCoreDir(game), "source.txt");
        Assert.True(File.Exists(sidecarPath));
    }

    [Fact]
    public async Task LoadCheatsAsync_FetchReturns404_ReturnsNotFoundAndWritesNoFile()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var game = MakeGame("nes");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.NotFound, result.Outcome);
        Assert.False(Directory.Exists(Path.Combine(_cheatsDirectory, game.Id.ToString())));
    }

    [Fact]
    public async Task LoadCheatsAsync_FetchReturnsServerError_ReturnsFetchFailedWithMessage()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var game = MakeGame("nes");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.FetchFailed, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadCheatsAsync_FetchedContentDoesNotParse_ReturnsCorruptedAndWritesNoFile()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not a real cheat file") });
        var game = MakeGame("nes");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.Corrupted, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
        // Never persist something that failed to parse - a corrupted fetch must not poison the
        // local cache the same way a corrupted local file would.
        Assert.False(Directory.Exists(Path.Combine(_cheatsDirectory, game.Id.ToString())));
    }

    [Fact]
    public async Task LoadCheatsAsync_LocalFileAlreadyExists_ReturnsItWithoutAnyHttpCall()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called when a local file already exists"));
        var game = MakeGame("nes");
        var coreDir = GetCoreDir(game);
        Directory.CreateDirectory(coreDir);
        await File.WriteAllTextAsync(Path.Combine(coreDir, "Test Game.cht"), ValidCheatFile);
        await File.WriteAllTextAsync(Path.Combine(coreDir, "source.txt"), "https://example.com/existing-source");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.Success, result.Outcome);
        Assert.Single(result.Cheats);
        Assert.Equal("https://example.com/existing-source", result.SourceFileUrl);
    }

    [Fact]
    public async Task LoadCheatsAsync_LocalFileExistsWithNoSourceSidecar_ReturnsCheatsWithNullSourceUrl()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var gameDir = GetCoreDir(game);
        Directory.CreateDirectory(gameDir);
        await File.WriteAllTextAsync(Path.Combine(gameDir, "Test Game.cht"), ValidCheatFile);
        // No source.txt written - simulates a .cht placed by something other than CheatService.

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.Success, result.Outcome);
        Assert.Null(result.SourceFileUrl);
    }

    [Fact]
    public async Task LoadCheatsAsync_LocalFileIsCorrupted_ReturnsCorruptedWithoutAnyHttpCall()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called for a corrupted local file - never silently re-fetch over it"));
        var game = MakeGame("nes");
        var gameDir = GetCoreDir(game);
        Directory.CreateDirectory(gameDir);
        await File.WriteAllTextAsync(Path.Combine(gameDir, "Test Game.cht"), "this is not a valid .cht file");

        var result = await service.LoadCheatsAsync(game, "nes");

        Assert.Equal(CheatFetchOutcome.Corrupted, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task SetCheatEnabledAsync_TogglesTheSpecificCheatInTheLocalFile()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var gameDir = GetCoreDir(game);
        Directory.CreateDirectory(gameDir);
        var filePath = Path.Combine(gameDir, "Test Game.cht");
        await File.WriteAllTextAsync(filePath, ValidCheatFile);

        await service.SetCheatEnabledAsync(game, 0, true);

        var reloaded = await service.LoadCheatsAsync(game, "nes");
        Assert.Equal(CheatFetchOutcome.Success, reloaded.Outcome);
        Assert.True(reloaded.Cheats[0].Enabled);
    }

    // Mechanism 2, second attempt: --appendconfig was abandoned after a real leaked line proved its
    // injected value never reverts and gets permanently baked into the user's actual retroarch.cfg
    // by RetroArch's own config_save_on_exit default. RetroArch's real per-game "override" file
    // (config_load_override/config_unload_override, configuration.c) is explicitly excluded from
    // that save, and is auto-discovered by RetroArch itself with no CLI flag needed - these tests
    // use a plain temp directory with a fake "retroarch.exe" standing in for the real emulator
    // executable, matching exactly what LaunchService passes (profile.ExecutablePath).
    //
    // cheat_database_path (mechanism 1) moved into this same file too, off the
    // LIBRETRO_CHEATS_DIRECTORY env var - verified against configuration.c's config_load_file that
    // the env var is read AFTER the override merges on every call, including the exact one
    // config_unload_override() makes to "restore" the config before config_save_on_exit, so it
    // leaked the same way --appendconfig did (confirmed: a stale per-game path lingering in a real
    // retroarch.cfg's own cheat_database_path). It has no "off" state - it's written whenever
    // ApplyCheatLaunchOverridesAsync is called at all (LaunchService only calls it when a
    // Bridge-managed cheat file exists for this game), so it's never removed by this method.
    private const string TestCheatDirectory = @"C:\Bridge\Cheats\test-game-id";

    private static (string Dir, string ExePath) CreateFakeRetroArchInstall()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bridge_test_retroarch_config_{Guid.NewGuid()}")).FullName;
        return (dir, Path.Combine(dir, "retroarch.exe"));
    }

    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_NoExistingFile_WritesCheatDatabasePath()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        // No retroarch.cfg present - falls back to the executable's own directory.

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: false);

            var expectedPath = Path.Combine(dir, "FCEUmm", "Test Game.cfg");
            Assert.True(File.Exists(expectedPath));
            Assert.Equal($"cheat_database_path = \"{TestCheatDirectory}\"\n", await File.ReadAllTextAsync(expectedPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_AutoApplyEnabledWithNoExistingFile_WritesBothKeysWithTheRealRetroArchName()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            // Real key name verified against configuration.c's SETTING_BOOL binding - not
            // "cheat_apply_after_load", which was guessed from the DEFAULT_APPLY_CHEATS_AFTER_LOAD
            // constant name (reads the other way round) and silently did nothing, since RetroArch
            // ignores unknown config keys.
            var expectedPath = Path.Combine(dir, "FCEUmm", "Test Game.cfg");
            Assert.True(File.Exists(expectedPath));
            var content = await File.ReadAllTextAsync(expectedPath);
            Assert.Contains($"cheat_database_path = \"{TestCheatDirectory}\"", content);
            Assert.Contains("apply_cheats_after_load = true", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // The real risk flagged before implementing this: this exact file is also where RetroArch
    // writes a user's own manually-saved "Game Override" (e.g. a shader or resolution tweak for
    // this one game). Writing Bridge's own keys must never clobber that.
    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_AutoApplyEnabledWithExistingFileWithOtherKeys_PreservesThemAndAddsBothKeys()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        var coreDir = Directory.CreateDirectory(Path.Combine(dir, "FCEUmm")).FullName;
        var overridePath = Path.Combine(coreDir, "Test Game.cfg");
        await File.WriteAllTextAsync(overridePath, "video_shader_enable = \"true\"\naspect_ratio_index = \"5\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            var content = await File.ReadAllTextAsync(overridePath);
            Assert.Contains("video_shader_enable = \"true\"", content);
            Assert.Contains("aspect_ratio_index = \"5\"", content);
            Assert.Contains($"cheat_database_path = \"{TestCheatDirectory}\"", content);
            Assert.Contains("apply_cheats_after_load = true", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Same preservation guarantee in the other direction - auto-apply disabled must remove only
    // the one line Bridge itself owns for that toggle, never the user's own saved override content,
    // and must still write cheat_database_path (no "off" state for that one).
    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_AutoApplyDisabledWithExistingFileWithOtherKeys_RemovesOnlyApplyCheatsLine()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        var coreDir = Directory.CreateDirectory(Path.Combine(dir, "FCEUmm")).FullName;
        var overridePath = Path.Combine(coreDir, "Test Game.cfg");
        await File.WriteAllTextAsync(overridePath, "video_shader_enable = \"true\"\napply_cheats_after_load = \"true\"\naspect_ratio_index = \"5\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: false);

            Assert.True(File.Exists(overridePath));
            var content = await File.ReadAllTextAsync(overridePath);
            Assert.Contains("video_shader_enable = \"true\"", content);
            Assert.Contains("aspect_ratio_index = \"5\"", content);
            Assert.Contains($"cheat_database_path = \"{TestCheatDirectory}\"", content);
            Assert.DoesNotContain("apply_cheats_after_load", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Distinct from a truly empty result - cheat_database_path has no "off" state, so a file that
    // used to hold only apply_cheats_after_load is never deleted outright anymore; it ends up
    // holding just cheat_database_path instead.
    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_AutoApplyDisabledWithFileThatOnlyHadApplyCheatsLine_ReplacesItWithCheatDatabasePath()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        var coreDir = Directory.CreateDirectory(Path.Combine(dir, "FCEUmm")).FullName;
        var overridePath = Path.Combine(coreDir, "Test Game.cfg");
        await File.WriteAllTextAsync(overridePath, "apply_cheats_after_load = true\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: false);

            Assert.True(File.Exists(overridePath));
            Assert.Equal($"cheat_database_path = \"{TestCheatDirectory}\"\n", await File.ReadAllTextAsync(overridePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Real bug, found via a live test and RetroArch's own log file: a correctly-placed,
    // correctly-named override file at the executable's own directory was silently ignored by
    // RetroArch. Root cause was this exact resolution - RetroArch's portable-install default
    // (rgui_config_directory = ":\config" in its own retroarch.cfg) means the real override
    // directory is "{executable directory}\config", not the executable directory itself. Confirmed
    // directly: the same file at the resolved path was found and loaded (RetroArch's own
    // "[Override] Game-specific overrides found" / "[Config] Appending override config" log lines).
    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_RetroArchCfgHasPortableRootConfigDirectory_ResolvesRelativeToExecutableDirectory()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        await File.WriteAllTextAsync(Path.Combine(dir, "retroarch.cfg"), "rgui_config_directory = \":\\config\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            Assert.True(File.Exists(Path.Combine(dir, "config", "FCEUmm", "Test Game.cfg")));
            Assert.False(File.Exists(Path.Combine(dir, "FCEUmm", "Test Game.cfg")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_RetroArchCfgHasAbsoluteConfigDirectory_UsesThatDirectoryDirectly()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        var customConfigDir = Path.Combine(Path.GetTempPath(), $"bridge_test_custom_config_{Guid.NewGuid()}");
        var escapedForCfg = customConfigDir.Replace("\\", "\\\\");
        await File.WriteAllTextAsync(Path.Combine(dir, "retroarch.cfg"), $"rgui_config_directory = \"{escapedForCfg}\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            Assert.True(File.Exists(Path.Combine(customConfigDir, "FCEUmm", "Test Game.cfg")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (Directory.Exists(customConfigDir))
            {
                Directory.Delete(customConfigDir, recursive: true);
            }
        }
    }

    // "default" is RetroArch's own sentinel for "not customized" (config_set_defaults clears it
    // back to empty) - must fall back exactly like a missing/empty value, not be treated as a
    // literal directory named "default".
    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_RetroArchCfgHasDefaultSentinelConfigDirectory_FallsBackToExecutableDirectory()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        await File.WriteAllTextAsync(Path.Combine(dir, "retroarch.cfg"), "rgui_config_directory = \"default\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            Assert.True(File.Exists(Path.Combine(dir, "FCEUmm", "Test Game.cfg")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCheatLaunchOverridesAsync_RetroArchCfgHasNoRguiConfigDirectoryKey_FallsBackToExecutableDirectory()
    {
        var service = CreateService(_ => throw new InvalidOperationException("HTTP should not be called"));
        var game = MakeGame("nes");
        var (dir, exePath) = CreateFakeRetroArchInstall();
        await File.WriteAllTextAsync(Path.Combine(dir, "retroarch.cfg"), "some_other_setting = \"true\"\n");

        try
        {
            await service.ApplyCheatLaunchOverridesAsync(game, exePath, TestCheatDirectory, autoApplyCheatsEnabled: true);

            Assert.True(File.Exists(Path.Combine(dir, "FCEUmm", "Test Game.cfg")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
