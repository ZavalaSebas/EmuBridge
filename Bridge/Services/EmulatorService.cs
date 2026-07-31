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

    public async Task SaveEmulatorConfigAsync(EmulatorConfig config, CancellationToken ct = default)
    {
        if (!File.Exists(config.ExecutablePath))
        {
            throw new BridgeException($"Emulator executable not found at '{config.ExecutablePath}'.");
        }

        ArgumentTemplate.Validate(config.ArgumentTemplate);

        var platforms = await _repository.GetPlatformsAsync(ct);
        if (platforms.All(p => p.Id != config.PlatformId))
        {
            throw new BridgeException($"Unknown platform id '{config.PlatformId}' — no matching Platform exists.");
        }

        await _repository.UpsertEmulatorConfigAsync(config, ct);
        _logger.LogInformation(
            "Saved emulator config for platform {PlatformId}: {ExecutablePath}",
            config.PlatformId,
            config.ExecutablePath);
    }

    public Task<EmulatorConfig?> GetEmulatorConfigForPlatformAsync(string platformId, CancellationToken ct = default)
        => _repository.GetEmulatorConfigByPlatformIdAsync(platformId, ct);
}
