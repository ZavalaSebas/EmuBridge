using Bridge.Models;
using Bridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bridge.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    private readonly ILibraryRepository _libraryRepository;
    private Game? _game;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _platformName = string.Empty;

    [ObservableProperty]
    private string _releaseYearText = "Release year: unknown";

    [ObservableProperty]
    private string? _coverImagePath;

    public GameDetailViewModel(ILibraryRepository libraryRepository)
    {
        _libraryRepository = libraryRepository;
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

        var boxArt = await _libraryRepository.GetBoxArtAsync(_game.Id, ct);
        CoverImagePath = boxArt?.Status == BoxArtStatus.Cached ? boxArt.LocalPath : null;
        ReleaseYearText = boxArt?.ReleaseYear is { } year ? $"Release year: {year}" : "Release year: unknown";
    }
}
