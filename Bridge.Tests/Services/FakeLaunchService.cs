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

    /// <summary>When non-empty, each call dequeues one result instead of returning NextResult —
    /// lets a test set up a distinct result per call (e.g. NoEmulatorConfigured, then Started
    /// after an inline Auto-Install), without disturbing tests that only ever set NextResult.</summary>
    public Queue<LaunchResult> ResultQueue { get; } = new();

    public Game? LastLaunchedGame { get; private set; }
    public int LaunchAsyncCallCount { get; private set; }

    public Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default)
    {
        LastLaunchedGame = game;
        LaunchAsyncCallCount++;
        var result = ResultQueue.Count > 0 ? ResultQueue.Dequeue() : NextResult;
        return Task.FromResult(result);
    }
}
