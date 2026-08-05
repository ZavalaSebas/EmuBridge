using System.Windows;
using EmuBridge.Exceptions;
using EmuBridge.Models;
using EmuBridge.Tests.Services;
using EmuBridge.ViewModels;

namespace EmuBridge.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly FakeLibraryRepository _repository = new();
    private readonly FakeEmulatorService _emulatorService = new();
    private readonly FakeEmulatorInstallerService _installerService = new();
    private readonly FakeSettingsService _settingsService = new();
    private readonly FakeFilePickerService _filePicker = new();
    private readonly FakeMessageBoxService _messageBox = new();
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _repository.Platforms.Add(new Platform { Id = Config.UnknownPlatformId, Name = Config.UnknownPlatformName, Extensions = [] });
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "NES", Extensions = ["nes"] });
        _repository.Platforms.Add(new Platform { Id = "snes", Name = "SNES", Extensions = ["sfc"] });

        _viewModel = new SettingsViewModel(_repository, _emulatorService, _installerService, _settingsService, _filePicker, _messageBox);
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
    public async Task SaveEmulatorProfileCommand_ServiceThrowsEmuBridgeException_ShowsMessage()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _viewModel.ExecutablePath = @"C:\does\not\exist.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\"";
        _emulatorService.ThrowOnSave = new EmuBridgeException("Emulator executable not found.");

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

    // Regression test: LoadPlatformsAsync rebuilds Platforms with brand-new PlatformConfigItem
    // instances every call. Before the fix, SelectedPlatform kept pointing at the stale,
    // now-orphaned pre-save instance, so IsConfigured here read false even though the save
    // succeeded and the reloaded Platforms list already reflected it correctly.
    [Fact]
    public async Task SaveEmulatorProfileCommand_Success_ReselectsPlatformSoSelectedPlatformReflectsSavedState()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _viewModel.ExecutablePath = @"C:\emu\nes.exe";
        _viewModel.ArgumentTemplate = "\"{RomPath}\"";

        await _viewModel.SaveEmulatorProfileCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.SelectedPlatform);
        Assert.Equal("nes", _viewModel.SelectedPlatform!.PlatformId);
        Assert.True(_viewModel.SelectedPlatform.IsConfigured);
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

    [Fact]
    public async Task InitializeAsync_PlatformWithKnownInstallOption_HasKnownInstallOptionTrue()
    {
        _installerService.PlatformsWithKnownInstallOption.Add("nes");

        await _viewModel.InitializeAsync();

        Assert.True(_viewModel.Platforms.Single(p => p.PlatformId == "nes").HasKnownInstallOption);
        Assert.False(_viewModel.Platforms.Single(p => p.PlatformId == "snes").HasKnownInstallOption);
    }

    [Fact]
    public async Task AutoInstallCommand_NoPlatformSelected_DoesNothing()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = null;

        await _viewModel.AutoInstallCommand.ExecuteAsync(null);

        Assert.False(_messageBox.ShowCalled);
        Assert.Empty(_installerService.InstalledPlatformIds);
    }

    [Fact]
    public async Task AutoInstallCommand_UserDeclinesConfirmation_DoesNotInstall()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _messageBox.NextResult = MessageBoxResult.No;

        await _viewModel.AutoInstallCommand.ExecuteAsync(null);

        Assert.Empty(_installerService.InstalledPlatformIds);
    }

    [Fact]
    public async Task AutoInstallCommand_UserConfirms_Success_SetsStatusMessageAndRefreshesList()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.Success };

        await _viewModel.AutoInstallCommand.ExecuteAsync(null);

        Assert.Equal("Installed.", _viewModel.StatusMessage);
        Assert.Contains("nes", _installerService.InstalledPlatformIds);
        Assert.False(_viewModel.IsBusy);
    }

    // Regression test for the exact bug reported from real interactive use: right after a
    // successful Auto-Install, the Executable field appeared empty — LoadPlatformsAsync rebuilds
    // Platforms with fresh PlatformConfigItem instances, but SelectedPlatform kept pointing at
    // the stale pre-install one, so OnSelectedPlatformChanged (the only thing that populates the
    // ExecutablePath/ArgumentTemplate text-box-bound properties) never re-fired. Leaving Settings
    // and re-entering "fixed" it only because re-selecting the platform manually re-triggered
    // that hook — this test exercises the same refresh without needing a real navigate-away.
    [Fact]
    public async Task AutoInstallCommand_Success_ReselectsPlatformSoExecutablePathFieldRefreshes()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.Success };
        // Simulates what a real install would have registered via EmulatorService by the time
        // LoadPlatformsAsync re-reads it — FakeEmulatorInstallerService doesn't touch
        // FakeEmulatorService itself, so this stands in for "the install already happened".
        _emulatorService.ProfilesByPlatformId["nes"] = new ResolvedEmulatorProfile
        {
            PlatformId = "nes",
            ExecutablePath = @"C:\emu\retroarch\RetroArch-Win64\retroarch.exe",
            ArgumentTemplate = "-L {CorePath} {RomPath}"
        };

        await _viewModel.AutoInstallCommand.ExecuteAsync(null);

        Assert.NotNull(_viewModel.SelectedPlatform);
        Assert.Equal("nes", _viewModel.SelectedPlatform!.PlatformId);
        Assert.True(_viewModel.SelectedPlatform.IsConfigured);
        Assert.Equal(@"C:\emu\retroarch\RetroArch-Win64\retroarch.exe", _viewModel.ExecutablePath);
        Assert.Equal("-L {CorePath} {RomPath}", _viewModel.ArgumentTemplate);
    }

    [Fact]
    public async Task AutoInstallCommand_UserConfirms_Failure_ShowsSpecificErrorMessage()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.DownloadFailed, ErrorMessage = "The download failed. Specific reason." };

        await _viewModel.AutoInstallCommand.ExecuteAsync(null);

        Assert.Equal("The download failed. Specific reason.", _messageBox.LastMessage);
        Assert.Equal(string.Empty, _viewModel.StatusMessage);
    }

    [Fact]
    public async Task CancelInstallCommand_DuringInstall_CancelsAndSetsStatusMessage()
    {
        await _viewModel.InitializeAsync();
        _viewModel.SelectedPlatform = _viewModel.Platforms.Single(p => p.PlatformId == "nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        _installerService.InstallGate = new TaskCompletionSource<InstallResult>();

        var installTask = _viewModel.AutoInstallCommand.ExecuteAsync(null);
        _viewModel.CancelInstallCommand.Execute(null);
        await installTask;

        Assert.Equal("Cancelled.", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsBusy);
    }
}
