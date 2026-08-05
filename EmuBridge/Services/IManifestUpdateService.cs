using EmuBridge.Models;

namespace EmuBridge.Services;

public interface IManifestUpdateService
{
    // Fire-and-forget from startup. Never throws — a failed/slow fetch falls back to whatever
    // was already available (see ARCHITECTURE.md -> ADR-25 for why this is the one deliberate
    // exception to EmuBridge's usual never-fail-silently rule).
    Task RefreshAsync(CancellationToken ct = default);

    // Always returns something usable: the most recently and successfully fetched catalog if
    // RefreshAsync has ever succeeded, otherwise the embedded fallback. Synchronous, no I/O.
    IReadOnlyList<KnownEmulator> GetCatalog();
}
