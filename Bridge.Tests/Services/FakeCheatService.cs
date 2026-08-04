using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeCheatService : ICheatService
{
    public CheatsResult NextResult { get; set; } = new() { Outcome = CheatFetchOutcome.NotFound };
    public Exception? ThrowOnSetEnabled { get; set; }
    public List<(Guid GameId, int Index, bool Enabled)> SetEnabledCalls { get; } = [];
    public string? CheatDirectoryToReturn { get; set; }
    public List<(Guid GameId, string RetroArchExecutablePath, string CheatDirectory, bool Enabled)> ApplyCheatLaunchOverridesCalls { get; } = [];

    public Task<CheatsResult> LoadCheatsAsync(Game game, string platformId, CancellationToken ct = default)
        => Task.FromResult(NextResult);

    public Task SetCheatEnabledAsync(Game game, int cheatIndex, bool enabled, CancellationToken ct = default)
    {
        if (ThrowOnSetEnabled is not null)
        {
            throw ThrowOnSetEnabled;
        }

        SetEnabledCalls.Add((game.Id, cheatIndex, enabled));
        return Task.CompletedTask;
    }

    public string? GetCheatDirectoryIfExists(Game game) => CheatDirectoryToReturn;

    public Task ApplyCheatLaunchOverridesAsync(Game game, string retroArchExecutablePath, string cheatDirectory, bool autoApplyCheatsEnabled, CancellationToken ct = default)
    {
        ApplyCheatLaunchOverridesCalls.Add((game.Id, retroArchExecutablePath, cheatDirectory, autoApplyCheatsEnabled));
        return Task.CompletedTask;
    }
}
