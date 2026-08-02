using System.Windows;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Tests.Services;
using Bridge.ViewModels;

namespace Bridge.Tests.ViewModels;

public class EmulatorOverrideViewModelTests
{
    private readonly FakeLibraryRepository _repository = new();
    private readonly FakeEmulatorService _emulatorService = new();
    private readonly FakeFilePickerService _filePicker = new();
    private readonly FakeMessageBoxService _messageBox = new();
    private readonly EmulatorOverrideViewModel _viewModel;

    public EmulatorOverrideViewModelTests()
    {
        _repository.Platforms.Add(new Platform { Id = "snes", Name = "Super Nintendo Entertainment System", Extensions = ["sfc"] });
        _viewModel = new EmulatorOverrideViewModel(_emulatorService, _repository, _filePicker, _messageBox);
    }

    private static Game MakeGame(string platformId = "snes") => new()
    {
        Id = Guid.NewGuid(),
        Path = @"C:\roms\starfox2.sfc",
        Name = "Star Fox 2",
        PlatformId = platformId
    };

    [Fact]
    public void SetGame_SetsNameImmediately()
    {
        var game = MakeGame();

        _viewModel.SetGame(game);

        Assert.Equal("Star Fox 2", _viewModel.GameName);
    }

    [Fact]
    public async Task InitializeAsync_ResolvesPlatformName()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Super Nintendo Entertainment System", _viewModel.PlatformName);
    }

    [Fact]
    public async Task InitializeAsync_PlatformRowMissing_FallsBackToPlatformId()
    {
        var game = MakeGame("ghost-platform");
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("ghost-platform", _viewModel.PlatformName);
    }

    [Fact]
    public async Task InitializeAsync_OverrideExists_SetsHasOverrideTrueAndPrefillsOverrideValues()
    {
        var game = MakeGame();
        _emulatorService.ProfilesByPlatformId["snes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };
        _emulatorService.ProfilesByGameId[game.Id] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\" --gfx-compat"
        };
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.True(_viewModel.HasOverride);
        Assert.Equal("\"{RomPath}\" --gfx-compat", _viewModel.ArgumentTemplate);
    }

    [Fact]
    public async Task InitializeAsync_NoOverrideButPlatformDefaultExists_SetsHasOverrideFalseAndPrefillsPlatformDefault()
    {
        var game = MakeGame();
        _emulatorService.ProfilesByPlatformId["snes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasOverride);
        Assert.Equal(@"C:\emu\snes9x.exe", _viewModel.ExecutablePath);
        Assert.Equal("\"{RomPath}\"", _viewModel.ArgumentTemplate);
    }

    [Fact]
    public async Task InitializeAsync_NeitherExists_FieldsStayEmpty()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasOverride);
        Assert.Equal(string.Empty, _viewModel.ExecutablePath);
        Assert.Equal(string.Empty, _viewModel.ArgumentTemplate);
    }

    [Fact]
    public async Task InitializeAsync_NoGameSet_DoesNotThrow()
    {
        await _viewModel.InitializeAsync();

        Assert.Equal(string.Empty, _viewModel.GameName);
    }

    [Fact]
    public void BrowseExecutableCommand_SetsExecutablePathFromPicker()
    {
        _filePicker.NextResult = @"C:\emu\snes9x.exe";

        _viewModel.BrowseExecutableCommand.Execute(null);

        Assert.True(_filePicker.PickFileCalled);
        Assert.Equal(@"C:\emu\snes9x.exe", _viewModel.ExecutablePath);
    }

    [Fact]
    public void BrowseExecutableCommand_PickerCancelled_LeavesExecutablePathUnchanged()
    {
        _viewModel.ExecutablePath = @"C:\emu\original.exe";
        _filePicker.NextResult = null;

        _viewModel.BrowseExecutableCommand.Execute(null);

        Assert.Equal(@"C:\emu\original.exe", _viewModel.ExecutablePath);
    }

    [Fact]
    public async Task SaveCommand_ValidInput_PersistsOverrideAndSetsHasOverrideTrue()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        await _viewModel.InitializeAsync();
        _viewModel.ExecutablePath = @"C:\emu\snes9x.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\" --gfx-compat";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasOverride);
        Assert.Equal("Saved.", _viewModel.StatusMessage);
        Assert.True(_emulatorService.ProfilesByGameId.ContainsKey(game.Id));
        Assert.Equal("\"{RomPath}\" --gfx-compat", _emulatorService.ProfilesByGameId[game.Id].ArgumentTemplate);
    }

    [Fact]
    public async Task SaveCommand_InvalidInput_ShowsMessageBoxAndDoesNotSetHasOverride()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        await _viewModel.InitializeAsync();
        _viewModel.ExecutablePath = @"C:\does\not\exist.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\"";
        _emulatorService.ThrowOnSave = new BridgeException("Emulator executable not found.");

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(_messageBox.ShowCalled);
        Assert.False(_viewModel.HasOverride);
        Assert.Equal(string.Empty, _viewModel.StatusMessage);
    }

    [Fact]
    public async Task ClearOverrideCommand_UserConfirms_RemovesOverrideAndReloadsPlatformDefault()
    {
        var game = MakeGame();
        _emulatorService.ProfilesByPlatformId["snes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };
        _emulatorService.ProfilesByGameId[game.Id] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\" --gfx-compat"
        };
        _viewModel.SetGame(game);
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.ClearOverrideCommand.ExecuteAsync(null);

        Assert.False(_viewModel.HasOverride);
        Assert.False(_emulatorService.ProfilesByGameId.ContainsKey(game.Id));
        Assert.Equal("\"{RomPath}\"", _viewModel.ArgumentTemplate);
        Assert.Equal("Override removed.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task ClearOverrideCommand_UserDeclines_NoOp()
    {
        var game = MakeGame();
        _emulatorService.ProfilesByGameId[game.Id] = new ResolvedEmulatorProfile
        {
            PlatformId = "snes",
            ExecutablePath = @"C:\emu\snes9x.exe",
            ArgumentTemplate = "\"{RomPath}\" --gfx-compat"
        };
        _viewModel.SetGame(game);
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.No;

        await _viewModel.ClearOverrideCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasOverride);
        Assert.True(_emulatorService.ProfilesByGameId.ContainsKey(game.Id));
    }

    [Fact]
    public async Task ClearOverrideCommand_NoOverrideExists_NoOp()
    {
        var game = MakeGame();
        _viewModel.SetGame(game);
        await _viewModel.InitializeAsync();

        await _viewModel.ClearOverrideCommand.ExecuteAsync(null);

        Assert.False(_messageBox.ShowCalled);
        Assert.False(_viewModel.HasOverride);
    }
}
