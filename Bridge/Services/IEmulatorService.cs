using Bridge.Models;

namespace Bridge.Services;

public interface IEmulatorService
{
    Task SaveProfileAsync(string platformId, string emulatorName, string executablePath, string argumentTemplate, CancellationToken ct = default);
    Task<ResolvedEmulatorProfile?> GetProfileForPlatformAsync(string platformId, CancellationToken ct = default);
}
