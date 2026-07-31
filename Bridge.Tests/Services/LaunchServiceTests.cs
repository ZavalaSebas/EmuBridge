using System.IO;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class LaunchServiceTests : IDisposable
{
    private readonly string _romPath;
    private readonly FakeEmulatorService _emulatorService;
    private readonly LaunchService _launchService;

    public LaunchServiceTests()
    {
        _romPath = Path.Combine(Path.GetTempPath(), $"bridge_test_rom_{Guid.NewGuid()}.nes");
        File.WriteAllBytes(_romPath, [1, 2, 3]);

        _emulatorService = new FakeEmulatorService();
        _launchService = new LaunchService(_emulatorService, NullLogger<LaunchService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_romPath))
        {
            File.Delete(_romPath);
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
        var corePath = Path.Combine(Path.GetTempPath(), $"bridge_test_core_{Guid.NewGuid()}.dll");
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

    [Fact]
    public async Task LaunchAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var game = MakeGame("nes");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _launchService.LaunchAsync(game, cts.Token));
    }
}
