using Bridge.Models;

namespace Bridge.Services;

public interface IEmulatorService
{
    Task SaveEmulatorConfigAsync(EmulatorConfig config, CancellationToken ct = default);
    Task<EmulatorConfig?> GetEmulatorConfigForPlatformAsync(string platformId, CancellationToken ct = default);
}
