using System.Windows;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Tests.Services;
using Bridge.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly FakeRomScannerService _romScanner = new();
    private readonly FakeLibraryRepository _repository = new();
    private readonly FakeMetadataService _metadataService = new();
    private readonly FakeImageCacheService _imageCacheService = new();
    private readonly FakeLaunchService _launchService = new();
    private readonly FakeEmulatorInstallerService _installerService = new();
    private readonly FakeFolderPickerService _folderPicker = new();
    private readonly FakeMessageBoxService _messageBox = new();
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _viewModel = new MainViewModel(
            _romScanner,
            _repository,
            _metadataService,
            _imageCacheService,
            _launchService,
            _installerService,
            _folderPicker,
            _messageBox,
            NullLogger<MainViewModel>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_NoScanFoldersOrGames_SetsHasNoScanFoldersTrue()
    {
        await _viewModel.InitializeAsync();

        Assert.True(_viewModel.HasNoScanFolders);
        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public async Task InitializeAsync_ScanFolderConfigured_SetsHasNoScanFoldersFalse()
    {
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = @"C:\roms" });

        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.HasNoScanFolders);
    }

    [Fact]
    public async Task InitializeAsync_GameWithCachedBoxArt_TileHasCoverImagePath()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" });

        await _viewModel.InitializeAsync();

        var tile = Assert.Single(_viewModel.Games);
        Assert.Equal(@"C:\cache\a.png", tile.CoverImagePath);
    }

    [Fact]
    public async Task InitializeAsync_GameWithFetchFailedBoxArt_TileHasNullCoverImagePath()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.FetchFailed });

        await _viewModel.InitializeAsync();

        Assert.Null(Assert.Single(_viewModel.Games).CoverImagePath);
    }

    [Fact]
    public async Task InitializeAsync_MissingGame_TileHasIsMissingTrue()
    {
        _repository.Games.Add(new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes", IsMissing = true });

        await _viewModel.InitializeAsync();

        Assert.True(Assert.Single(_viewModel.Games).IsMissing);
    }

    [Fact]
    public async Task RefreshLibraryCommand_CallsScanThenFetchMissingBoxArt()
    {
        await _viewModel.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Equal(1, _romScanner.ScanAsyncCallCount);
        Assert.True(_metadataService.FetchMissingBoxArtCalled);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshLibraryCommand_WhileAlreadyBusy_DoesNotStartSecondScan()
    {
        var gate = new TaskCompletionSource<ScanResult>();
        _romScanner.ScanGate = gate;

        var firstCall = _viewModel.RefreshLibraryCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsBusy);

        await _viewModel.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Equal(1, _romScanner.ScanAsyncCallCount);

        gate.SetResult(new ScanResult());
        await firstCall;
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task CancelCommand_DuringScan_CancelsAndSetsStatusMessage()
    {
        var gate = new TaskCompletionSource<ScanResult>();
        _romScanner.ScanGate = gate;

        var refreshTask = _viewModel.RefreshLibraryCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsBusy);

        _viewModel.CancelCommand.Execute(null);
        await refreshTask;

        Assert.Equal("Cancelled.", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshLibraryCommand_WhileInstallInProgress_DoesNotStartScan()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" };
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        var installGate = new TaskCompletionSource<InstallResult>();
        _installerService.InstallGate = installGate;

        var launchTask = _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);
        Assert.True(_viewModel.IsBusy);

        await _viewModel.RefreshLibraryCommand.ExecuteAsync(null);
        Assert.Equal(0, _romScanner.ScanAsyncCallCount);

        installGate.SetResult(new InstallResult { Outcome = InstallOutcome.Success });
        await launchTask;
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task AddFolderCommand_UserCancelsDialog_DoesNotAddFolder()
    {
        _folderPicker.NextResult = null;
        var addCalled = false;
        _romScanner.AddScanFolderHandler = _ => { addCalled = true; return Task.CompletedTask; };

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.False(addCalled);
    }

    [Fact]
    public async Task AddFolderCommand_ServiceThrowsBridgeException_ShowsMessageAndDoesNotRefresh()
    {
        _folderPicker.NextResult = @"C:\typo-folder";
        _romScanner.AddScanFolderHandler = _ => throw new BridgeException("Folder not found.");

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.True(_messageBox.ShowCalled);
        Assert.Equal(0, _romScanner.ScanAsyncCallCount);
    }

    [Fact]
    public async Task AddFolderCommand_Success_TriggersRefresh()
    {
        _folderPicker.NextResult = @"C:\roms";
        _romScanner.AddScanFolderHandler = _ => Task.CompletedTask;

        await _viewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, _romScanner.ScanAsyncCallCount);
    }

    [Fact]
    public async Task LaunchGameCommand_UnknownTile_DoesNotCallLaunchService()
    {
        var tile = new GameTile { GameId = Guid.NewGuid(), Name = "Ghost" };

        await _viewModel.LaunchGameCommand.ExecuteAsync(tile);

        Assert.Null(_launchService.LastLaunchedGame);
    }

    [Fact]
    public async Task LaunchGameCommand_Success_DoesNotShowMessageBox()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.Started, GameSessionEndedTask = Task.CompletedTask };

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.False(_messageBox.ShowCalled);
        Assert.Equal(game.Id, _launchService.LastLaunchedGame?.Id);
    }

    [Fact]
    public async Task LaunchGameCommand_Failure_ShowsErrorMessage()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "Set one up in Settings." };

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.True(_messageBox.ShowCalled);
        Assert.Equal("Set one up in Settings.", _messageBox.LastMessage);
    }

    [Fact]
    public async Task LaunchGameCommand_WhileBusy_DoesNotLaunch()
    {
        var gate = new TaskCompletionSource<ScanResult>();
        _romScanner.ScanGate = gate;
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        var scanTask = _viewModel.RefreshLibraryCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsBusy);

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Null(_launchService.LastLaunchedGame);

        gate.SetResult(new ScanResult());
        await scanTask;
    }

    [Fact]
    public async Task LaunchGameCommand_NoEmulatorUnknownPlatform_NeverOffersInstall()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mystery.xyz", Name = "mystery", PlatformId = Config.UnknownPlatformId };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "unknown system" };
        _installerService.PlatformsWithKnownInstallOption.Add(Config.UnknownPlatformId);

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Empty(_installerService.InstalledPlatformIds);
        Assert.Equal("unknown system", _messageBox.LastMessage);
    }

    [Fact]
    public async Task LaunchGameCommand_NoEmulatorNoKnownInstallOption_ShowsGenericMessageNotInstallOffer()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "Set one up in Settings." };
        // _installerService.PlatformsWithKnownInstallOption deliberately left empty.

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Empty(_installerService.InstalledPlatformIds);
        Assert.Equal("Set one up in Settings.", _messageBox.LastMessage);
        Assert.Equal("Couldn't Launch Game", _messageBox.LastCaption);
    }

    [Fact]
    public async Task LaunchGameCommand_UserDeclinesInstallOffer_DoesNothingFurther()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" };
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _messageBox.NextResult = MessageBoxResult.No;

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Equal(1, _messageBox.ShowCallCount);
        Assert.Empty(_installerService.InstalledPlatformIds);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task LaunchGameCommand_UserAcceptsInstallOffer_InstallSucceeds_RelaunchesGameAutomatically()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.ResultQueue.Enqueue(new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" });
        _launchService.ResultQueue.Enqueue(new LaunchResult { Outcome = LaunchOutcome.Started, GameSessionEndedTask = Task.CompletedTask });
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.Success };
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Contains("nes", _installerService.InstalledPlatformIds);
        Assert.Equal(2, _launchService.LaunchAsyncCallCount);
        Assert.Equal(1, _messageBox.ShowCallCount); // only the Yes/No confirm — no error dialog
        Assert.False(_viewModel.IsBusy);
        Assert.Equal(string.Empty, _viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchGameCommand_UserAcceptsInstallOffer_InstallFails_ShowsInstallErrorMessage()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" };
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.DownloadFailed, ErrorMessage = "Download verification failed." };
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Equal(1, _launchService.LaunchAsyncCallCount); // no relaunch attempt after a failed install
        Assert.Equal("Download verification failed.", _messageBox.LastMessage);
        Assert.Equal("Couldn't Auto-Install", _messageBox.LastCaption);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task LaunchGameCommand_InstallSucceedsButRelaunchStillFails_ShowsLaunchErrorMessage()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.ResultQueue.Enqueue(new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" });
        _launchService.ResultQueue.Enqueue(new LaunchResult { Outcome = LaunchOutcome.CoreNotFound, ErrorMessage = "Core missing." });
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _installerService.NextResult = new InstallResult { Outcome = InstallOutcome.Success };
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Equal(2, _launchService.LaunchAsyncCallCount);
        Assert.Equal("Core missing.", _messageBox.LastMessage);
        Assert.Equal("Couldn't Launch Game", _messageBox.LastCaption);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task CancelCommand_DuringInlineInstall_CancelsAndSetsStatusMessage()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _launchService.NextResult = new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = "no emu" };
        _installerService.PlatformsWithKnownInstallOption.Add("nes");
        _messageBox.NextResult = MessageBoxResult.Yes;
        var installGate = new TaskCompletionSource<InstallResult>();
        _installerService.InstallGate = installGate;

        var launchTask = _viewModel.LaunchGameCommand.ExecuteAsync(_viewModel.Games[0]);
        Assert.True(_viewModel.IsBusy);

        _viewModel.CancelCommand.Execute(null);
        await launchTask;

        Assert.Equal("Cancelled.", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task DeleteGameCommand_UserConfirms_RemovesGameBoxArtAndCachedImage()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes", IsMissing = true };
        _repository.Games.Add(game);
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" });
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.DeleteGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Empty(_repository.Games);
        Assert.Empty(_repository.BoxArtRecords);
        Assert.Contains(@"C:\cache\a.png", _imageCacheService.DeletedPaths);
        Assert.Empty(_viewModel.Games);
        Assert.Equal("Removed.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteGameCommand_UserCancels_NothingHappens()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes", IsMissing = true };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.No;

        await _viewModel.DeleteGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Single(_repository.Games);
        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task DeleteGameCommand_NoBoxArt_DeletesGameOnly()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes", IsMissing = true };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.DeleteGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.Empty(_repository.Games);
        Assert.Empty(_imageCacheService.DeletedPaths);
    }

    [Fact]
    public async Task DeleteGameCommand_NotMissing_NoOp()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes", IsMissing = false };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.Yes;

        await _viewModel.DeleteGameCommand.ExecuteAsync(_viewModel.Games[0]);

        Assert.False(_messageBox.ShowCalled);
        Assert.Single(_repository.Games);
    }

    [Fact]
    public async Task DeleteGameCommand_SharedCachedImage_DoesNotDeleteFileStillReferencedByAnotherGame()
    {
        var missingGame = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\a.nes", Name = "a", PlatformId = "nes", IsMissing = true };
        var presentGame = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\b.nes", Name = "b", PlatformId = "nes", IsMissing = false };
        _repository.Games.Add(missingGame);
        _repository.Games.Add(presentGame);
        // Both games happen to share the same cached box-art file (same source URL).
        _repository.BoxArtRecords.Add(new BoxArt { GameId = missingGame.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\shared.png" });
        _repository.BoxArtRecords.Add(new BoxArt { GameId = presentGame.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\shared.png" });
        await _viewModel.InitializeAsync();
        _messageBox.NextResult = MessageBoxResult.Yes;
        var missingTile = _viewModel.Games.Single(t => t.GameId == missingGame.Id);

        await _viewModel.DeleteGameCommand.ExecuteAsync(missingTile);

        Assert.Empty(_imageCacheService.DeletedPaths);
        Assert.DoesNotContain(_repository.BoxArtRecords, b => b.GameId == missingGame.Id);
        Assert.Contains(_repository.BoxArtRecords, b => b.GameId == presentGame.Id);
    }

    [Fact]
    public void OpenSettingsCommand_InvokesOpenSettingsRequested()
    {
        var invoked = false;
        _viewModel.OpenSettingsRequested = () => invoked = true;

        _viewModel.OpenSettingsCommand.Execute(null);

        Assert.True(invoked);
    }

    [Fact]
    public async Task ViewGameDetailsCommand_ValidTile_InvokesOpenGameDetailsRequestedWithGame()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        _repository.Games.Add(game);
        await _viewModel.InitializeAsync();
        Game? requestedGame = null;
        _viewModel.OpenGameDetailsRequested = g => requestedGame = g;

        _viewModel.ViewGameDetailsCommand.Execute(_viewModel.Games[0]);

        Assert.Equal(game.Id, requestedGame?.Id);
    }

    [Fact]
    public void ViewGameDetailsCommand_UnknownTile_DoesNotInvoke()
    {
        var invoked = false;
        _viewModel.OpenGameDetailsRequested = _ => invoked = true;
        var tile = new GameTile { GameId = Guid.NewGuid(), Name = "Ghost" };

        _viewModel.ViewGameDetailsCommand.Execute(tile);

        Assert.False(invoked);
    }
}
