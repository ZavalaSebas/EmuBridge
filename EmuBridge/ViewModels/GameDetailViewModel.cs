using System.Collections.ObjectModel;
using EmuBridge.Models;
using EmuBridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuBridge.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    private readonly ILibraryRepository _libraryRepository;
    private readonly ITheGamesDbService _theGamesDbService;
    private Game? _game;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _platformName = string.Empty;

    [ObservableProperty]
    private string _releaseYearText = "Release year: unknown";

    [ObservableProperty]
    private string? _coverImagePath;

    // Static default, same "never silent" standard ADR-19 already applied to this exact text -
    // overwritten once TheGamesDbService resolves a real outcome, including the "not available"
    // cases, so the window never shows a stale/misleading default if InitializeAsync is still
    // running when the user looks at it.
    [ObservableProperty]
    private string _descriptionText = "Description: not available";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshots))]
    private ObservableCollection<string> _screenshotPaths = [];

    public bool HasScreenshots => ScreenshotPaths.Count > 0;

    public GameDetailViewModel(ILibraryRepository libraryRepository, ITheGamesDbService theGamesDbService)
    {
        _libraryRepository = libraryRepository;
        _theGamesDbService = theGamesDbService;
    }

    /// <summary>Set synchronously before the window shows, so the title/name render immediately —
    /// InitializeAsync then fills in the parts that need a repository round-trip.</summary>
    public void SetGame(Game game)
    {
        _game = game;
        Name = game.Name;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_game is null)
        {
            return;
        }

        var platforms = await _libraryRepository.GetPlatformsAsync(ct);
        PlatformName = platforms.FirstOrDefault(p => p.Id == _game.PlatformId)?.Name ?? _game.PlatformId;

        // Fetch first (persists Description/ScreenshotLocalPaths onto BoxArt), then one fresh
        // GetBoxArtAsync read covers cover art, release year, and the TheGamesDB fields together -
        // avoids a second, redundant repository round-trip.
        var outcome = await _theGamesDbService.FetchDescriptionAndScreenshotsAsync(_game, ct);

        var boxArt = await _libraryRepository.GetBoxArtAsync(_game.Id, ct);
        CoverImagePath = boxArt?.Status == BoxArtStatus.Cached ? boxArt.LocalPath : null;
        ReleaseYearText = boxArt?.ReleaseYear is { } year ? $"Release year: {year}" : "Release year: unknown";

        DescriptionText = outcome switch
        {
            TheGamesDbOutcome.Cached when !string.IsNullOrWhiteSpace(boxArt?.Description) => $"Description: {boxArt!.Description}",
            TheGamesDbOutcome.RateLimited when boxArt?.DescriptionRateLimitResetUtc is { } resetUtc
                => $"Description: rate limit reached (resets {resetUtc.ToLocalTime():t})",
            _ => "Description: not available"
        };

        ScreenshotPaths = new ObservableCollection<string>(boxArt?.ScreenshotLocalPaths ?? []);
    }
}
