using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bridge.Exceptions;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

public class LaunchService : ILaunchService
{
    private readonly IEmulatorService _emulatorService;
    private readonly ILogger<LaunchService> _logger;

    public LaunchService(IEmulatorService emulatorService, ILogger<LaunchService> logger)
    {
        _emulatorService = emulatorService;
        _logger = logger;
    }

    public async Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(game.Path))
        {
            _logger.LogWarning("ROM file not found at {Path} for {GameName}.", game.Path, game.Name);
            return new LaunchResult
            {
                Outcome = LaunchOutcome.RomFileNotFound,
                ErrorMessage = $"ROM file not found at '{game.Path}'. It may have been moved or removed since the last scan."
            };
        }

        var profile = await _emulatorService.GetProfileForGameAsync(game, ct);
        if (profile is null)
        {
            var message = game.PlatformId == Config.UnknownPlatformId
                ? "Bridge couldn't identify this ROM's system, so no emulator is configured for it. Fix the extension mapping in Settings, then rescan."
                : "No emulator is configured for this game's system yet. Set one up in Settings.";
            _logger.LogWarning("No EmulatorProfile for platform {PlatformId} ({GameName}).", game.PlatformId, game.Name);
            return new LaunchResult { Outcome = LaunchOutcome.NoEmulatorConfigured, ErrorMessage = message };
        }

        if (!File.Exists(profile.ExecutablePath))
        {
            _logger.LogWarning("Configured emulator executable not found at {ExecutablePath}.", profile.ExecutablePath);
            return new LaunchResult
            {
                Outcome = LaunchOutcome.ExecutableNotFound,
                ErrorMessage = $"The configured emulator can't be found at '{profile.ExecutablePath}' — it may have been moved or uninstalled."
            };
        }

        if (profile.CorePath is not null && !File.Exists(profile.CorePath))
        {
            _logger.LogWarning("Configured core not found at {CorePath}.", profile.CorePath);
            return new LaunchResult
            {
                Outcome = LaunchOutcome.CoreNotFound,
                ErrorMessage = $"The configured emulator core can't be found at '{profile.CorePath}' — it may have been moved or uninstalled. Re-install it in Settings."
            };
        }

        string expandedArguments;
        try
        {
            expandedArguments = ArgumentTemplate.Expand(profile.ArgumentTemplate, game.Path, profile.CorePath);
        }
        catch (BridgeException ex)
        {
            _logger.LogError(ex, "Invalid argument template for platform {PlatformId}.", game.PlatformId);
            return new LaunchResult { Outcome = LaunchOutcome.LaunchFailed, ErrorMessage = ex.Message };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            Arguments = expandedArguments,
            WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? string.Empty,
            UseShellExecute = false
        };

        // Explicit late check: everything above (file-existence checks, argument expansion) takes
        // real time, so re-check right here, immediately before the process actually launches,
        // instead of trusting the ct.ThrowIfCancellationRequested() call at the top of the method.
        ct.ThrowIfCancellationRequested();

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to launch emulator {ExecutablePath} for {GameName}.", profile.ExecutablePath, game.Name);
            return new LaunchResult { Outcome = LaunchOutcome.LaunchFailed, ErrorMessage = ex.Message };
        }

        _logger.LogInformation("Launched {GameName} via {ExecutablePath}.", game.Name, profile.ExecutablePath);

        return new LaunchResult
        {
            Outcome = LaunchOutcome.Started,
            GameSessionEndedTask = process.WaitForExitAsync()
        };
    }
}
