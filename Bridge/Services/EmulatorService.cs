using System.IO;
using Bridge.Exceptions;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

public class EmulatorService : IEmulatorService
{
    private readonly ILibraryRepository _repository;
    private readonly ILogger<EmulatorService> _logger;

    public EmulatorService(ILibraryRepository repository, ILogger<EmulatorService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task SaveProfileAsync(string platformId, string emulatorName, string executablePath, string argumentTemplate, Guid? gameId = null, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
        {
            throw new BridgeException($"Emulator executable not found at '{executablePath}'.");
        }

        ArgumentTemplate.Validate(argumentTemplate);

        var platforms = await _repository.GetPlatformsAsync(ct);
        if (platforms.All(p => p.Id != platformId))
        {
            throw new BridgeException($"Unknown platform id '{platformId}' — no matching Platform exists.");
        }

        // Find-or-create by ExecutablePath (ADR-11): two platforms pointed at the same physical
        // install (e.g. RetroArch configured for both nes and snes) share one Emulator row and
        // get separate EmulatorProfile rows, rather than duplicating the Emulator.
        var emulator = await _repository.UpsertEmulatorAsync(new Emulator
        {
            Name = emulatorName,
            ExecutablePath = executablePath,
            InstallSource = InstallSource.UserProvided
        }, ct);

        await _repository.UpsertEmulatorProfileAsync(new EmulatorProfile
        {
            EmulatorId = emulator.Id,
            PlatformId = platformId,
            ArgumentTemplate = argumentTemplate,
            GameId = gameId
        }, ct);

        if (gameId is null)
        {
            _logger.LogInformation("Saved emulator profile for platform {PlatformId}: {ExecutablePath}", platformId, executablePath);
        }
        else
        {
            _logger.LogInformation("Saved per-game emulator override for game {GameId} (platform {PlatformId}): {ExecutablePath}", gameId, platformId, executablePath);
        }
    }

    public async Task<ResolvedEmulatorProfile?> GetProfileForPlatformAsync(string platformId, CancellationToken ct = default)
        => await ResolveAsync(await _repository.GetEmulatorProfileByPlatformIdAsync(platformId, ct), platformId, ct);

    public async Task<ResolvedEmulatorProfile?> GetProfileForGameAsync(Game game, CancellationToken ct = default)
    {
        var overrideProfile = await _repository.GetEmulatorProfileForGameAsync(game.Id, ct);
        return overrideProfile is not null
            ? await ResolveAsync(overrideProfile, game.PlatformId, ct)
            : await GetProfileForPlatformAsync(game.PlatformId, ct);
    }

    public async Task<bool> HasGameOverrideAsync(Guid gameId, CancellationToken ct = default)
        => await _repository.GetEmulatorProfileForGameAsync(gameId, ct) is not null;

    public Task ClearGameOverrideAsync(Guid gameId, CancellationToken ct = default)
        => _repository.DeleteEmulatorProfileForGameAsync(gameId, ct);

    private async Task<ResolvedEmulatorProfile?> ResolveAsync(EmulatorProfile? profile, string platformId, CancellationToken ct)
    {
        if (profile is null)
        {
            return null;
        }

        var emulator = await _repository.GetEmulatorByIdAsync(profile.EmulatorId, ct);
        if (emulator is null)
        {
            _logger.LogError(
                "EmulatorProfile for platform {PlatformId} references missing Emulator {EmulatorId}.",
                platformId,
                profile.EmulatorId);
            return null;
        }

        return new ResolvedEmulatorProfile
        {
            PlatformId = platformId,
            ExecutablePath = emulator.ExecutablePath,
            ArgumentTemplate = profile.ArgumentTemplate,
            CorePath = profile.CorePath
        };
    }

    public Task<Emulator?> GetInstalledKnownEmulatorAsync(string knownEmulatorId, CancellationToken ct = default)
        => _repository.GetEmulatorByKnownEmulatorIdAsync(knownEmulatorId, ct);

    public async Task<Emulator> RegisterInstalledEmulatorAsync(string knownEmulatorId, string name, string executablePath, string installedSha256, CancellationToken ct = default)
    {
        var emulator = await _repository.UpsertEmulatorAsync(new Emulator
        {
            KnownEmulatorId = knownEmulatorId,
            Name = name,
            ExecutablePath = executablePath,
            InstallSource = InstallSource.BridgeManaged,
            InstalledSha256 = installedSha256
        }, ct);

        _logger.LogInformation("Registered auto-installed emulator {KnownEmulatorId} at {ExecutablePath}.", knownEmulatorId, executablePath);
        return emulator;
    }

    public async Task RegisterCoreProfileAsync(string platformId, Guid emulatorId, string corePath, string argumentTemplate, CancellationToken ct = default)
    {
        await _repository.UpsertEmulatorProfileAsync(new EmulatorProfile
        {
            EmulatorId = emulatorId,
            PlatformId = platformId,
            ArgumentTemplate = argumentTemplate,
            CorePath = corePath
        }, ct);

        _logger.LogInformation("Registered auto-installed core profile for platform {PlatformId}: {CorePath}", platformId, corePath);
    }
}
