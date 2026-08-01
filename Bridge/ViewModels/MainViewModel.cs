using System.Collections.ObjectModel;
using System.Windows;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Bridge.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IRomScannerService _romScannerService;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IMetadataService _metadataService;
    private readonly IImageCacheService _imageCacheService;
    private readonly ILaunchService _launchService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly ILogger<MainViewModel> _logger;

    private readonly Dictionary<Guid, Game> _gamesById = new();
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private ObservableCollection<GameTile> _games = new();

    [ObservableProperty]
    private bool _hasNoScanFolders;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Invoked by MainWindow to open the Settings window — wired from the composition
    /// root, not referenced directly here, so this ViewModel doesn't need to know about
    /// SettingsWindow/SettingsViewModel.</summary>
    public Action? OpenSettingsRequested { get; set; }

    public MainViewModel(
        IRomScannerService romScannerService,
        ILibraryRepository libraryRepository,
        IMetadataService metadataService,
        IImageCacheService imageCacheService,
        ILaunchService launchService,
        IFolderPickerService folderPickerService,
        IMessageBoxService messageBoxService,
        ILogger<MainViewModel> logger)
    {
        _romScannerService = romScannerService;
        _libraryRepository = libraryRepository;
        _metadataService = metadataService;
        _imageCacheService = imageCacheService;
        _launchService = launchService;
        _folderPickerService = folderPickerService;
        _messageBoxService = messageBoxService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadGamesAsync(ct);
    }

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        _scanCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Scanning...";
            var scanProgress = new Progress<int>(count => StatusMessage = $"Scanning... {count} files found");
            var scanResult = await _romScannerService.ScanAsync(scanProgress, _scanCts.Token);
            _logger.LogInformation(
                "Scan complete: {Added} added, {Updated} updated, {Missing} newly missing, {SkippedFolders} folders skipped, {SkippedFiles} files skipped.",
                scanResult.GamesAdded,
                scanResult.GamesUpdated,
                scanResult.GamesMarkedMissing,
                scanResult.SkippedFolders.Count,
                scanResult.SkippedFiles.Count);

            StatusMessage = "Fetching box art...";
            var metadataProgress = new Progress<int>(count => StatusMessage = $"Fetching box art... {count} processed");
            await _metadataService.FetchMissingBoxArtAsync(Config.CoverWidth, Config.CoverHeight, metadataProgress, _scanCts.Token);

            await LoadGamesAsync(_scanCts.Token);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = _folderPickerService.PickFolder("Select ROM Folder");
        if (path is null)
        {
            return;
        }

        try
        {
            await _romScannerService.AddScanFolderAsync(new ScanFolder { Id = Guid.NewGuid(), Path = path });
        }
        catch (BridgeException ex)
        {
            _messageBoxService.Show(ex.Message, "Couldn't Add Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }

    [RelayCommand]
    private async Task LaunchGameAsync(GameTile? tile)
    {
        if (tile is null || !_gamesById.TryGetValue(tile.GameId, out var game))
        {
            return;
        }

        var result = await _launchService.LaunchAsync(game);

        if (result.Outcome != LaunchOutcome.Started)
        {
            _messageBoxService.Show(
                result.ErrorMessage ?? "Launch failed.",
                "Couldn't Launch Game",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _logger.LogInformation("Launched {GameName}.", game.Name);
        _ = TrackSessionEndAsync(game.Name, result.GameSessionEndedTask!);
    }

    [RelayCommand]
    private async Task DeleteGameAsync(GameTile? tile)
    {
        // Defense in depth: MainWindow.xaml only shows the "Remove from Library" context menu item
        // for IsMissing tiles (avoids the re-scan-reappearance confusion for present games, ADR-15),
        // but re-check here too in case a future binding regression exposes the command incorrectly.
        if (tile is null || !_gamesById.TryGetValue(tile.GameId, out var game) || !game.IsMissing)
        {
            return;
        }

        var confirmed = _messageBoxService.Show(
            $"Remove \"{game.Name}\" from your library? This can't be undone.",
            "Remove Game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var boxArt = await _libraryRepository.GetBoxArtAsync(game.Id);
        if (boxArt?.LocalPath is not null)
        {
            // ImageCacheService dedupes cache files by image URL, not by GameId (ARCHITECTURE.md),
            // so two Games could in theory share the same cached file if they ever had identical
            // box-art URLs. Only delete the file if no other BoxArt row still points at it.
            var allBoxArt = await _libraryRepository.GetAllBoxArtAsync();
            var stillReferencedByAnotherGame = allBoxArt.Any(b => b.GameId != game.Id && b.LocalPath == boxArt.LocalPath);
            if (!stillReferencedByAnotherGame)
            {
                await _imageCacheService.DeleteCachedImageAsync(boxArt.LocalPath);
            }
        }

        await _libraryRepository.DeleteBoxArtAsync(game.Id);
        await _libraryRepository.DeleteGameAsync(game.Id);

        _logger.LogInformation("Removed {GameName} from the library.", game.Name);

        await LoadGamesAsync();
        StatusMessage = "Removed.";
    }

    private async Task TrackSessionEndAsync(string gameName, Task sessionEndedTask)
    {
        try
        {
            await sessionEndedTask;
            _logger.LogInformation("{GameName} session ended.", gameName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while waiting for {GameName} session to end.", gameName);
        }
    }

    private async Task LoadGamesAsync(CancellationToken ct = default)
    {
        var games = await _libraryRepository.GetAllGamesAsync(ct);
        var boxArtByGameId = (await _libraryRepository.GetAllBoxArtAsync(ct))
            .ToDictionary(b => b.GameId);

        _gamesById.Clear();
        var tiles = new List<GameTile>();
        foreach (var game in games)
        {
            _gamesById[game.Id] = game;
            boxArtByGameId.TryGetValue(game.Id, out var boxArt);

            tiles.Add(new GameTile
            {
                GameId = game.Id,
                Name = game.Name,
                CoverImagePath = boxArt?.Status == BoxArtStatus.Cached ? boxArt.LocalPath : null,
                IsMissing = game.IsMissing
            });
        }

        Games = new ObservableCollection<GameTile>(tiles);

        var scanFolders = await _libraryRepository.GetScanFoldersAsync(ct);
        HasNoScanFolders = scanFolders.Count == 0;
    }
}
