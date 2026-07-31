using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeLaunchService : ILaunchService
{
    public LaunchResult NextResult { get; set; } = new()
    {
        Outcome = LaunchOutcome.Started,
        GameSessionEndedTask = Task.CompletedTask
    };

    public Game? LastLaunchedGame { get; private set; }

    public Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default)
    {
        LastLaunchedGame = game;
        return Task.FromResult(NextResult);
    }
}
