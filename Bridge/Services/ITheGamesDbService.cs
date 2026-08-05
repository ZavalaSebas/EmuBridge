using Bridge.Models;

namespace Bridge.Services;

public enum TheGamesDbOutcome
{
    Cached,
    NotFound,
    RateLimited,
    Failed,
    NoKeyConfigured
}

public interface ITheGamesDbService
{
    Task<TheGamesDbOutcome> FetchDescriptionAndScreenshotsAsync(Game game, CancellationToken ct = default);
}
