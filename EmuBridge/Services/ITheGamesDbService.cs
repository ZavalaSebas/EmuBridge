using EmuBridge.Models;

namespace EmuBridge.Services;

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
