using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeEmulatorService : IEmulatorService
{
    public Dictionary<string, ResolvedEmulatorProfile> ProfilesByPlatformId { get; } = [];
    public Exception? ThrowOnSave { get; set; }

    public Task SaveProfileAsync(string platformId, string emulatorName, string executablePath, string argumentTemplate, CancellationToken ct = default)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        ProfilesByPlatformId[platformId] = new ResolvedEmulatorProfile
        {
            PlatformId = platformId,
            ExecutablePath = executablePath,
            ArgumentTemplate = argumentTemplate
        };
        return Task.CompletedTask;
    }

    public Task<ResolvedEmulatorProfile?> GetProfileForPlatformAsync(string platformId, CancellationToken ct = default)
        => Task.FromResult(ProfilesByPlatformId.GetValueOrDefault(platformId));
}
