using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeEmulatorService : IEmulatorService
{
    public Dictionary<string, ResolvedEmulatorProfile> ProfilesByPlatformId { get; } = [];
    public List<Emulator> InstalledEmulators { get; } = [];
    public Exception? ThrowOnSave { get; set; }
    public Exception? ThrowOnRegisterEmulator { get; set; }
    public Exception? ThrowOnRegisterCoreProfile { get; set; }

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

    public Task<Emulator?> GetInstalledKnownEmulatorAsync(string knownEmulatorId, CancellationToken ct = default)
        => Task.FromResult(InstalledEmulators.FirstOrDefault(e => e.KnownEmulatorId == knownEmulatorId));

    public Task<Emulator> RegisterInstalledEmulatorAsync(string knownEmulatorId, string name, string executablePath, string installedSha256, CancellationToken ct = default)
    {
        if (ThrowOnRegisterEmulator is not null)
        {
            throw ThrowOnRegisterEmulator;
        }

        var emulator = new Emulator
        {
            Id = Guid.NewGuid(),
            KnownEmulatorId = knownEmulatorId,
            Name = name,
            ExecutablePath = executablePath,
            InstallSource = InstallSource.BridgeManaged,
            InstalledSha256 = installedSha256
        };
        InstalledEmulators.Add(emulator);
        return Task.FromResult(emulator);
    }

    public Task RegisterCoreProfileAsync(string platformId, Guid emulatorId, string corePath, string argumentTemplate, CancellationToken ct = default)
    {
        if (ThrowOnRegisterCoreProfile is not null)
        {
            throw ThrowOnRegisterCoreProfile;
        }

        var executablePath = InstalledEmulators.FirstOrDefault(e => e.Id == emulatorId)?.ExecutablePath ?? string.Empty;
        ProfilesByPlatformId[platformId] = new ResolvedEmulatorProfile
        {
            PlatformId = platformId,
            ExecutablePath = executablePath,
            ArgumentTemplate = argumentTemplate,
            CorePath = corePath
        };
        return Task.CompletedTask;
    }
}
