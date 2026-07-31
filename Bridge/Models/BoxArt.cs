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
}
