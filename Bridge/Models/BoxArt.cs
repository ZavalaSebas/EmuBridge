namespace Bridge.Models;

public enum BoxArtStatus
{
    NotFetched,
    Cached,
    NotFoundOnProvider,
    FetchFailed
}

public class BoxArt
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string? LocalPath { get; set; }
    public BoxArtStatus Status { get; set; } = BoxArtStatus.NotFetched;
    public DateTime? LastAttemptUtc { get; set; }

    // Sourced from SteamGridDB's search response (release_date, Unix seconds) — confirmed
    // available there even though description/screenshots are not (see ARCHITECTURE.md ADR-19).
    // Null means SteamGridDB has no release date for this game, not "not fetched yet".
    public int? ReleaseYear { get; set; }

    // Vertical/poster-style grid, for Big Picture mode — same /grids/game/{id} response as
    // LocalPath/Status, classified by aspect ratio, not a separate fetch (see ARCHITECTURE.md ->
    // ADR-23). Own status, not reused from Status: a game can have a horizontal grid cached and no
    // vertical one (or vice versa), since SteamGridDB's coverage per dimension varies per game.
    public string? VerticalLocalPath { get; set; }
    public BoxArtStatus VerticalStatus { get; set; } = BoxArtStatus.NotFetched;
}
