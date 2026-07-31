using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeEmulatorService : IEmulatorService
{
    public Dictionary<string, EmulatorConfig> ConfigsByPlatformId { get; } = [];

    public Task SaveEmulatorConfigAsync(EmulatorConfig config, CancellationToken ct = default)
    {
        ConfigsByPlatformId[config.PlatformId] = config;
        return Task.CompletedTask;
    }

    public Task<EmulatorConfig?> GetEmulatorConfigForPlatformAsync(string platformId, CancellationToken ct = default)
        => Task.FromResult(ConfigsByPlatformId.GetValueOrDefault(platformId));
}
