namespace EmuBridge.ViewModels;

// Flat display DTO for the cover grid — combines Game + its BoxArt (if any) into what the View
// actually binds to. Rebuilt wholesale on every refresh (Phase 1: no per-tile live mutation,
// see MainViewModel design notes) rather than an ObservableObject with property-level notifications.
public class GameTile
{
    public Guid GameId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Null means: no cached box art yet — the View shows a placeholder. Covers
    /// NotFetched, NotFoundOnProvider, and FetchFailed alike; Phase 1 doesn't distinguish them
    /// visually (see ARCHITECTURE.md -> ADR-8). Always the horizontal grid — used by the normal
    /// grid's tile template.</summary>
    public string? CoverImagePath { get; init; }

    /// <summary>Vertical grid if cached, falling back to the horizontal <see cref="CoverImagePath"/>
    /// if not, null if neither is cached — used by Big Picture mode's larger tiles, which are
    /// portrait-shaped and would otherwise stretch a horizontal source image (see ARCHITECTURE.md
    /// -> ADR-23).</summary>
    public string? BigPictureCoverImagePath { get; init; }

    public bool IsMissing { get; init; }
    public bool IsFavorite { get; init; }

    /// <summary>Null means BoxArt has no release year (not fetched, or SteamGridDB had none) —
    /// the View hides this line entirely rather than showing an empty one.</summary>
    public string? ReleaseYearText { get; init; }
}

// UI-only display concern — where to sort a Game in the library grid. Not a domain model, so it
// doesn't live in EmuBridge.Models alongside things like LaunchOutcome/BoxArtStatus.
public enum LibrarySortMode
{
    Name,
    RecentlyPlayed,
    FavoritesFirst
}
