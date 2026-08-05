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

    // TheGamesDB (metadata-source decision, 2026-08-04 — see PLAN.md -> Timeline). Kept on BoxArt
    // rather than a new entity, same reasoning ADR-19 already used for ReleaseYear: this is
    // detail-panel metadata, not a separate concept with its own lifecycle. Fetched on demand when
    // GameDetailWindow opens, not during the library scan (unlike Status/VerticalStatus) — a
    // ~1000/month key allowance would be exhausted fast if every game's description/screenshots
    // were pre-fetched for a library nobody has looked at yet.
    public string? Description { get; set; }
    public BoxArtStatus DescriptionStatus { get; set; } = BoxArtStatus.NotFetched;

    // Set only when a fetch attempt returned a real rate-limit response (distinct from a generic
    // FetchFailed) so the UI can show "resets at HH:mm" instead of a generic unavailable message —
    // TheGamesDB's own response includes this, unlike SteamGridDB (see DEVELOPMENT.md -> Known
    // Limitations). Null whenever DescriptionStatus isn't a rate-limited FetchFailed.
    public DateTime? DescriptionRateLimitResetUtc { get; set; }

    // Screenshots ride on the same DescriptionStatus gate (one TheGamesDB game lookup covers both),
    // but degrade independently within a Cached status — the game can be found with a real overview
    // and still end up with zero screenshots, either because TheGamesDB has none for it or because
    // individual image downloads failed. No separate status field for that: a shorter, real list is
    // itself the signal, same as CoverImagePath being null already means "nothing to show" elsewhere.
    public List<string> ScreenshotLocalPaths { get; set; } = [];
}
