using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Tests.Services;
using Bridge.ViewModels;

namespace Bridge.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly FakeLibraryRepository _repository = new();
    private readonly FakeEmulatorService _emulatorService = new();
    private readonly FakeSettingsService _settingsService = new();
    private readonly FakeFilePickerService _filePicker = new();
    private readonly FakeMessageBoxService _messageBox = new();
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _repository.Platforms.Add(new Platform { Id = Config.UnknownPlatformId, Name = Config.UnknownPlatformName, Extensions = [] });
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "NES", Extensions = ["nes"] });
        _repository.Platforms.Add(new Platform { Id = "snes", Name = "SNES", Extensions = ["sfc"] });

        _viewModel = new SettingsViewModel(_repository, _emulatorService, _settingsService, _filePicker, _messageBox);
    }

    [Fact]
    public async Task InitializeAsync_ExcludesUnknownPlatformSentinel()
    {
        await _viewModel.InitializeAsync();

        Assert.DoesNotContain(_viewModel.Platforms, p => p.PlatformId == Config.UnknownPlatformId);
        Assert.Equal(2, _viewModel.Platforms.Count);
    }

    [Fact]
    public async Task InitializeAsync_LoadsApiKey()
    {
        _settingsService.ApiKey = "existing-key";

        await _viewModel.InitializeAsync();

        Assert.Equal("existing-key", _viewModel.SteamGridDbApiKey);
    }

    [Fact]
    public async Task InitializeAsync_PlatformWithExistingConfig_IsConfiguredTrue()
    {
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\emu\nes.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };

        await _viewModel.InitializeAsync();

        var nes = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        Assert.True(nes.IsConfigured);
        Assert.Equal(@"C:\emu\nes.exe", nes.ExecutablePath);
    }

    [Fact]
    public async Task InitializeAsync_PlatformWithoutConfig_IsConfiguredFalse()
    {
        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.Platforms.Single(p => p.PlatformId == "nes").IsConfigured);
    }

    [Fact]
    public async Task SelectingPlatform_PrefillsExecutablePathAndArgumentTemplate()
    {
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\emu\nes.exe",
            ArgumentTemplate = "\"{RomPath}\""
        };
        await _viewModel.InitializeAsync();

        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");

        Assert.Equal(@"C:\emu\nes.exe", _viewModel.ExecutablePath);
        Assert.Equal("\"{RomPath}\"", _viewModel.ArgumentTemplate);
    }

    [Fact]
    public async Task SaveEmulatorProfileCommand_NoPlatformSelected_DoesNothing()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = null;
        _viewModel.ExecutablePath = @"C:\emu\nes.exe";

        await _viewModel.SaveEmulatorProfileCommand.ExecuteAsync(null);

        Assert.Empty(_emulatorService.ProfilesByPlatformId);
    }

    [Fact]
    public async Task SaveEmulatorProfileCommand_ServiceThrowsBridgeException_ShowsMessage()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _viewModel.ExecutablePath = @"C:\does\not\exist.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\"";
        _emulatorService.ThrowOnSave = new BridgeException("Emulator executable not found.");

        await _viewModel.SaveEmulatorProfileCommand.ExecuteAsync(null);

        Assert.True(_messageBox.ShowCalled);
        Assert.Equal(string.Empty, _viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveEmulatorProfileCommand_Success_SetsStatusMessageAndRefreshesList()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _viewModel.ExecutablePath = @"C:\emu\nes.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\"";

        await _viewModel.SaveEmulatorProfileCommand.ExecuteAsync(null);

        Assert.Equal("Saved.", _viewModel.StatusMessage);
        Assert.True(_viewModel.Platforms.Single(p => p.PlatformId == "nes").IsConfigured);
    }

    [Fact]
    public async Task SaveApiKeyCommand_EmptyKey_DoesNotSave()
    {
        _viewModel.SteamGridDbApiKey = "   ";

        await _viewModel.SaveApiKeyCommand.ExecuteAsync(null);

        Assert.Null(_settingsService.ApiKey);
    }

    [Fact]
    public async Task SaveApiKeyCommand_ValidKey_SavesAndSetsStatusMessage()
    {
        _viewModel.SteamGridDbApiKey = "new-key";

        await _viewModel.SaveApiKeyCommand.ExecuteAsync(null);

        Assert.Equal("new-key", _settingsService.ApiKey);
        Assert.Equal("API key saved.", _viewModel.StatusMessage);
    }

    [Fact]
    public void BrowseExecutableCommand_SetsExecutablePathFromPicker()
    {
        _filePicker.NextResult = @"C:\emu\picked.exe";

        _viewModel.BrowseExecutableCommand.Execute(null);

        Assert.Equal(@"C:\emu\picked.exe", _viewModel.ExecutablePath);
    }

    [Fact]
    public void BrowseExecutableCommand_UserCancels_DoesNotChangeExecutablePath()
    {
        _viewModel.ExecutablePath = @"C:\existing.exe";
        _filePicker.NextResult = null;

        _viewModel.BrowseExecutableCommand.Execute(null);

        Assert.Equal(@"C:\existing.exe", _viewModel.ExecutablePath);
    }
}
