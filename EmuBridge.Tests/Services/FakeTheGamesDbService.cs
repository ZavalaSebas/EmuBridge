using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeTheGamesDbService : ITheGamesDbService
{
    public TheGamesDbOutcome NextOutcome { get; set; } = TheGamesDbOutcome.NoKeyConfigured;
    public bool ThrowOperationCanceled { get; set; }
    public List<Guid> CalledForGameIds { get; } = [];

    public Task<TheGamesDbOutcome> FetchDescriptionAndScreenshotsAsync(Game game, CancellationToken ct = default)
    {
        if (ThrowOperationCanceled)
        {
            throw new OperationCanceledException();
        }

        CalledForGameIds.Add(game.Id);
        return Task.FromResult(NextOutcome);
    }
}
