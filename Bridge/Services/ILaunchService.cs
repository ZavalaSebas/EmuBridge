using Bridge.Models;

namespace Bridge.Services;

public interface ILaunchService
{
    Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default);
}
