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
    private readonly IEmulatorInstallerService _installerService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly ILogger<MainViewModel> _logger;

    private readonly Dictionary<Guid, Game> _gamesById = new();
    private readonly Dictionary<Guid, BoxArt> _boxArtByGameId = new();

    // Shared across RefreshLibraryAsync (scan) and OfferInlineAutoInstallAsync (install) —
    // IsBusy is exclusive between the two, so only one of them is ever running, and one Cancel
    // button in MainWindow's status bar cancels whichever is currently in flight.
    private CancellationTokenSource? _busyCts;

    [ObservableProperty]
    private ObservableCollection<GameTile> _games = new();

    [ObservableProperty]
    private bool _hasNoScanFolders;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private LibrarySortMode _sortMode = LibrarySortMode.Name;

    [ObservableProperty]
    private bool _showFavoritesOnly;

    // "Big Picture" mode — a different presentation of the same Games/TrySomethingNewGames data,
    // not a separate data set (see ARCHITECTURE.md -> ADR-22). Plain bound bool, same shape as
    // ShowFavoritesOnly; MainWindow binds WindowState to this too, so toggling it also maximizes.
    [ObservableProperty]
    private bool _isBigPictureMode;

    [ObservableProperty]
    private ObservableCollection<GameTile> _trySomethingNewGames = new();

    partial void OnSortModeChanged(LibrarySortMode value) => RebuildGameTiles();

    partial void OnShowFavoritesOnlyChanged(bool value) => RebuildGameTiles();

    /// <summary>Invoked by MainWindow to open the Settings window — wired from the composition
    /// root, not referenced directly here, so this ViewModel doesn't need to know about
    /// SettingsWindow/SettingsViewModel.</summary>
    public Action? OpenSettingsRequested { get; set; }

    /// <summary>Same wiring shape as OpenSettingsRequested, parameterized with the specific Game
    /// to show — this ViewModel doesn't need to know about GameDetailWindow/GameDetailViewModel.</summary>
    public Action<Game>? OpenGameDetailsRequested { get; set; }

    public MainViewModel(
        IRomScannerService romScannerService,
        ILibraryRepository libraryRepository,
        IMetadataService metadataService,
        IImageCacheService imageCacheService,
        ILaunchService launchService,
        IEmulatorInstallerService installerService,
        IFolderPickerService folderPickerService,
        IMessageBoxService messageBoxService,
        ILogger<MainViewModel> logger)
    {
        _romScannerService = romScannerService;
        _libraryRepository = libraryRepository;
        _metadataService = metadataService;
        _imageCacheService = imageCacheService;
        _launchService = launchService;
        _installerService = installerService;
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
        _busyCts = new CancellationTokenSource();

        try
        {
            StatusMessage = "Scanning...";
            var scanProgress = new Progress<int>(count => StatusMessage = $"Scanning... {count} files found");
            var scanResult = await _romScannerService.ScanAsync(scanProgress, _busyCts.Token);
            _logger.LogInformation(
                "Scan complete: {Added} added, {Updated} updated, {Missing} newly missing, {SkippedFolders} folders skipped, {SkippedFiles} files skipped.",
                scanResult.GamesAdded,
                scanResult.GamesUpdated,
                scanResult.GamesMarkedMissing,
                scanResult.SkippedFolders.Count,
                scanResult.SkippedFiles.Count);

            StatusMessage = "Fetching box art...";
            var metadataProgress = new Progress<int>(count => StatusMessage = $"Fetching box art... {count} processed");
            // The horizontal-classified grid (targetWidth/Height) is cached at Big Picture's
            // landscape size, and the vertical-classified grid (verticalTargetWidth/Height) at the
            // normal grid's portrait size — Big Picture shows horizontal, the normal grid shows
            // vertical (see ARCHITECTURE.md -> ADR-23 Update; parameter names are still about grid
            // orientation, not which UI slot uses them).
            await _metadataService.FetchMissingBoxArtAsync(
                Config.BigPictureCoverWidth, Config.BigPictureCoverHeight,
                Config.CoverWidth, Config.CoverHeight,
                metadataProgress, _busyCts.Token);

            await LoadGamesAsync(_busyCts.Token);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _busyCts?.Dispose();
            _busyCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _busyCts?.Cancel();
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
        // Guards against racing a scan or an in-flight Auto-Install (OfferInlineAutoInstallAsync
        // below shares IsBusy with RefreshLibraryAsync) — both touch LibraryRepository/the
        // filesystem, so letting them overlap was a latent bug even before this method could
        // itself trigger a long-running install.
        if (IsBusy || tile is null || !_gamesById.TryGetValue(tile.GameId, out var game))
        {
            return;
        }

        var result = await _launchService.LaunchAsync(game);

        if (result.Outcome == LaunchOutcome.Started)
        {
            _logger.LogInformation("Launched {GameName}.", game.Name);
            await RecordGamePlayedAsync(game);
            _ = TrackSessionEndAsync(game.Name, result.GameSessionEndedTask!);
            return;
        }

        // Only offer Auto-Install for a real, recognized-but-unconfigured platform — never for
        // the "unknown" sentinel (NoEmulatorConfigured covers both cases, distinguished only by
        // ErrorMessage text; installing an emulator for a system Bridge never identified makes no
        // sense) — and only when the catalog actually has a verified entry for it.
        if (result.Outcome == LaunchOutcome.NoEmulatorConfigured
            && game.PlatformId != Config.UnknownPlatformId
            && await _installerService.HasKnownInstallOptionAsync(game.PlatformId))
        {
            await OfferInlineAutoInstallAsync(game);
            return;
        }

        _messageBoxService.Show(
            result.ErrorMessage ?? "Launch failed.",
            "Couldn't Launch Game",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task OfferInlineAutoInstallAsync(Game game)
    {
        var confirmed = _messageBoxService.Show(
            $"No emulator is configured for \"{game.Name}\"'s system yet. Install one automatically now?",
            "Install Emulator?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        _busyCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(message => StatusMessage = message);
            var installResult = await _installerService.InstallAsync(game.PlatformId, progress, _busyCts.Token);

            if (installResult.Outcome != InstallOutcome.Success)
            {
                _messageBoxService.Show(
                    installResult.ErrorMessage ?? "Install failed.",
                    "Couldn't Auto-Install",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = string.Empty;
                return;
            }

            // Collapses "install, then separately relaunch" into one motion — the whole point of
            // offering Auto-Install inline from the launch flow instead of only from Settings.
            StatusMessage = "Installed. Launching...";
            var relaunchResult = await _launchService.LaunchAsync(game, _busyCts.Token);

            if (relaunchResult.Outcome == LaunchOutcome.Started)
            {
                _logger.LogInformation("Launched {GameName} after Auto-Install.", game.Name);
                await RecordGamePlayedAsync(game);
                _ = TrackSessionEndAsync(game.Name, relaunchResult.GameSessionEndedTask!);
                StatusMessage = string.Empty;
            }
            else
            {
                // Relaunch failed for some other reason (e.g. CoreNotFound) — surface it through
                // the same non-Started handling a normal launch attempt already uses. No retry loop.
                _messageBoxService.Show(
                    relaunchResult.ErrorMessage ?? "Launch failed.",
                    "Couldn't Launch Game",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _busyCts?.Dispose();
            _busyCts = null;
        }
    }

    [RelayCommand]
    private void ViewGameDetails(GameTile? tile)
    {
        if (tile is null || !_gamesById.TryGetValue(tile.GameId, out var game))
        {
            return;
        }

        OpenGameDetailsRequested?.Invoke(game);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(GameTile? tile)
    {
        if (tile is null || !_gamesById.TryGetValue(tile.GameId, out var game))
        {
            return;
        }

        game.IsFavorite = !game.IsFavorite;
        await _libraryRepository.UpsertGameAsync(game);

        await LoadGamesAsync();
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

    // Recorded on LaunchOutcome.Started, not on session end — see ARCHITECTURE.md -> ADR-20. No
    // consuming UI reads this yet (that's the "Library" view, a separate Roadmap item); awaited,
    // not fire-and-forget, so the write is guaranteed to have landed before the command returns.
    private async Task RecordGamePlayedAsync(Game game)
    {
        game.LastPlayedUtc = DateTime.UtcNow;
        await _libraryRepository.UpsertGameAsync(game);
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
        _boxArtByGameId.Clear();
        foreach (var game in games)
        {
            _gamesById[game.Id] = game;
            if (boxArtByGameId.TryGetValue(game.Id, out var boxArt))
            {
                _boxArtByGameId[game.Id] = boxArt;
            }
        }

        RebuildGameTiles();

        var scanFolders = await _libraryRepository.GetScanFoldersAsync(ct);
        HasNoScanFolders = scanFolders.Count == 0;
    }

    // Reorders/filters what's already cached in _gamesById/_boxArtByGameId — no repository
    // round-trip, no IsBusy needed. Called after a real reload (LoadGamesAsync) and whenever
    // SortMode/ShowFavoritesOnly change, so both paths share one place that knows how to turn
    // "the current set of Games" into what the grid actually displays.
    private void RebuildGameTiles()
    {
        IEnumerable<Game> games = _gamesById.Values;

        if (ShowFavoritesOnly)
        {
            games = games.Where(g => g.IsFavorite);
        }

        // Nulls (never played) sort last, alphabetically among themselves — not first, which
        // would surface games nobody has touched ahead of what was actually played recently.
        games = SortMode switch
        {
            LibrarySortMode.RecentlyPlayed => games
                .OrderByDescending(g => g.LastPlayedUtc.HasValue)
                .ThenByDescending(g => g.LastPlayedUtc)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            LibrarySortMode.FavoritesFirst => games
                .OrderByDescending(g => g.IsFavorite)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ => games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        };

        Games = new ObservableCollection<GameTile>(games.Select(BuildTile));

        // "Try Something New" (Big Picture mode, ARCHITECTURE.md -> ADR-22): never-played, still-
        // present games, alphabetical — no scoring, no randomness, honestly named for what it
        // actually is. Independent of SortMode/ShowFavoritesOnly, which only affect the main grid.
        // Sourced from _gamesById directly (not the filtered/sorted `games` above), capped to 10.
        var trySomethingNew = _gamesById.Values
            .Where(g => g.LastPlayedUtc is null && !g.IsMissing)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10);
        TrySomethingNewGames = new ObservableCollection<GameTile>(trySomethingNew.Select(BuildTile));
    }

    private GameTile BuildTile(Game game)
    {
        _boxArtByGameId.TryGetValue(game.Id, out var boxArt);

        var horizontalPath = boxArt?.Status == BoxArtStatus.Cached ? boxArt.LocalPath : null;
        var verticalPath = boxArt?.VerticalStatus == BoxArtStatus.Cached ? boxArt.VerticalLocalPath : null;

        // Normal grid: vertical preferred (matches its 2:3 portrait tile), horizontal fallback.
        // Big Picture: horizontal preferred (matches its landscape tile), vertical fallback. Either
        // way, null only if neither is cached — never the "No Cover" placeholder when some cover
        // already exists (ARCHITECTURE.md -> ADR-23 Update).
        var coverPath = verticalPath ?? horizontalPath;
        var bigPicturePath = horizontalPath ?? verticalPath;

        return new GameTile
        {
            GameId = game.Id,
            Name = game.Name,
            CoverImagePath = coverPath,
            BigPictureCoverImagePath = bigPicturePath,
            IsMissing = game.IsMissing,
            IsFavorite = game.IsFavorite,
            ReleaseYearText = boxArt?.ReleaseYear?.ToString()
        };
    }
}
