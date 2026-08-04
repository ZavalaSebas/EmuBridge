using System.IO;
using Bridge.Models;
using Bridge.ViewModels;

namespace Bridge.Tests.Services;

public class CheatsViewModelTests
{
    private readonly FakeCheatService _cheatService = new();
    private readonly FakeEmulatorService _emulatorService = new();
    private readonly FakeLibraryRepository _libraryRepository = new();
    private readonly FakeMessageBoxService _messageBoxService = new();
    private readonly CheatsViewModel _viewModel;

    public CheatsViewModelTests()
    {
        _viewModel = new CheatsViewModel(_cheatService, _emulatorService, _libraryRepository, _messageBoxService);
        _libraryRepository.Platforms.Add(new Platform { Id = "nes", Name = "Nintendo Entertainment System" });
    }

    private static Game MakeGame(string platformId = "nes") => new()
    {
        Id = Guid.NewGuid(),
        Path = @"C:\roms\Test Game.nes",
        Name = "Test Game",
        PlatformId = platformId
    };

    [Fact]
    public async Task InitializeAsync_NoResolvedProfile_ShowsRetroArchRequiredMessageWithoutCallingCheatService()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        // No profile registered in _emulatorService for this platform/game at all.

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Contains("RetroArch-installed emulator", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_ProfileHasNoCorePath_ShowsRetroArchRequiredMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\emu\manual.exe",
            ArgumentTemplate = "\"{RomPath}\""
            // No CorePath - a manually-configured emulator.
        };

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Contains("configured manually", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_PlatformNotSupported_ShowsExplicitMessage()
    {
        var game = MakeGame("wonderswan");
        _viewModel.SetGame(game);
        GiveRetroArchProfile(game.PlatformId);
        _cheatService.NextResult = new CheatsResult { Outcome = CheatFetchOutcome.PlatformNotSupported };

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Contains("public cheat database", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_NotFound_ShowsNoCheatsFoundMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        GiveRetroArchProfile(game.PlatformId);
        _cheatService.NextResult = new CheatsResult { Outcome = CheatFetchOutcome.NotFound };

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Contains("No cheats found", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_FetchFailed_ShowsServiceErrorMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        GiveRetroArchProfile(game.PlatformId);
        _cheatService.NextResult = new CheatsResult { Outcome = CheatFetchOutcome.FetchFailed, ErrorMessage = "Couldn't reach the cheat database. Check your connection and try again." };

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Equal("Couldn't reach the cheat database. Check your connection and try again.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_Corrupted_ShowsServiceErrorMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        GiveRetroArchProfile(game.PlatformId);
        _cheatService.NextResult = new CheatsResult { Outcome = CheatFetchOutcome.Corrupted, ErrorMessage = "This game's saved cheat file couldn't be read - it may be corrupted." };

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasCheats);
        Assert.Equal("This game's saved cheat file couldn't be read - it may be corrupted.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeAsync_Success_PopulatesCheatsAndSourceUrlAndClearsStatusMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        GiveRetroArchProfile(game.PlatformId);
        _cheatService.NextResult = new CheatsResult
        {
            Outcome = CheatFetchOutcome.Success,
            Cheats = [new Cheat { Index = 0, Description = "Infinite Lives", Enabled = false }],
            SourceFileUrl = "https://github.com/libretro/libretro-database/blob/master/cht/Nintendo%20-%20Nintendo%20Entertainment%20System/Test%20Game.cht"
        };

        await _viewModel.InitializeAsync();

        Assert.True(_viewModel.HasCheats);
        Assert.Empty(_viewModel.StatusMessage);
        Assert.Single(_viewModel.Cheats);
        Assert.Equal("Infinite Lives", _viewModel.Cheats[0].Description);
        Assert.False(_viewModel.Cheats[0].Enabled);
        Assert.Equal("Nintendo Entertainment System", _viewModel.PlatformName);
        Assert.NotNull(_viewModel.SourceFileUrl);
    }

    [Fact]
    public void ToggleCheatCommand_PersistsThroughCheatService()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        var item = new CheatItem(0, "Infinite Lives", true);

        _viewModel.ToggleCheatCommand.Execute(item);

        var call = Assert.Single(_cheatService.SetEnabledCalls);
        Assert.Equal(game.Id, call.GameId);
        Assert.Equal(0, call.Index);
        Assert.True(call.Enabled);
    }

    [Fact]
    public void ToggleCheatCommand_SaveFails_RevertsCheckboxAndShowsMessage()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        _cheatService.ThrowOnSetEnabled = new IOException("disk full");
        var item = new CheatItem(0, "Infinite Lives", true);

        _viewModel.ToggleCheatCommand.Execute(item);

        Assert.False(item.Enabled);
        Assert.True(_messageBoxService.ShowCalled);
    }

    private void GiveRetroArchProfile(string platformId)
    {
        _emulatorService.ProfilesByPlatformId[platformId] = new ResolvedEmulatorProfile
        {
            PlatformId = platformId,
            ExecutablePath = @"C:\emu\retroarch.exe",
            ArgumentTemplate = "-L {CorePath} {RomPath}",
            CorePath = @"C:\emu\cores\core.dll"
        };
    }
}
