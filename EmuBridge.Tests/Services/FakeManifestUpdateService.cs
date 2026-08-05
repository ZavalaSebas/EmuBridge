using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeManifestUpdateService : IManifestUpdateService
{
    public List<KnownEmulator> Catalog { get; set; } = [];
    public bool RefreshCalled { get; private set; }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        RefreshCalled = true;
        return Task.CompletedTask;
    }

    public IReadOnlyList<KnownEmulator> GetCatalog() => Catalog;
}
