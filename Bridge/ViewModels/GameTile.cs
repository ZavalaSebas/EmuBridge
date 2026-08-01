namespace Bridge.ViewModels;

// Flat display DTO for the cover grid — combines Game + its BoxArt (if any) into what the View
// actually binds to. Rebuilt wholesale on every refresh (Phase 1: no per-tile live mutation,
// see MainViewModel design notes) rather than an ObservableObject with property-level notifications.
public class GameTile
{
    public Guid GameId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Null means: no cached box art yet — the View shows a placeholder. Covers
    /// NotFetched, NotFoundOnProvider, and FetchFailed alike; Phase 1 doesn't distinguish them
    /// visually (see ARCHITECTURE.md -> ADR-8).</summary>
    public string? CoverImagePath { get; init; }

    public bool IsMissing { get; init; }
    public bool IsFavorite { get; init; }
}
