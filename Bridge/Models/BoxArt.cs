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
}
