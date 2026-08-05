using EmuBridge.Models;

namespace EmuBridge.Services;

public interface ILaunchService
{
    Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default);
}
