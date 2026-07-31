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
    private readonly FakeLaunchService _launchService = new();
    private readonly FakeFolderPickerService _folderPicker = new();
    private readonly FakeMessageBoxService _messageBox = new();
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _viewModel = new MainViewModel(
            _romScanner,
            _repository,
            _metadataService,
            _launchService,
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
    public async Task CancelScanCommand_DuringScan_CancelsAndSetsStatusMessage()
    {
        var gate = new TaskCompletionSource<ScanResult>();
        _romScanner.ScanGate = gate;

        var refreshTask = _viewModel.RefreshLibraryCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsBusy);

        _viewModel.CancelScanCommand.Execute(null);
        await refreshTask;

        Assert.Equal("Cancelled.", _viewModel.StatusMessage);
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
    public void OpenSettingsCommand_InvokesOpenSettingsRequested()
    {
        var invoked = false;
        _viewModel.OpenSettingsRequested = () => invoked = true;

        _viewModel.OpenSettingsCommand.Execute(null);

        Assert.True(invoked);
    }
}
