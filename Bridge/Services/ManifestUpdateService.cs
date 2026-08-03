using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

// Keeps Bridge's emulator/core catalog current between releases without waiting for a new
// Bridge.exe build — see ARCHITECTURE.md -> ADR-25 for the full design and the reconciliation
// with ADR-11's original rejection of a live-fetched manifest. Fetches Bridge's own
// KnownEmulators.json straight from `main` on GitHub (never a separate backend, never an
// unreviewed branch — the drift-check mechanism, same ADR, only ever writes to `main` after a
// human merges a PR). A failed or slow fetch is never visible to the user: this is the one
// deliberate exception to Bridge's usual never-fail-silently rule (see DEVELOPMENT.md -> Bug
// Investigation Process) — the embedded copy this class falls back to is exactly what shipped
// in this build and is known-good, so there is nothing actionable to show the user, and
// interrupting them over a background refresh they never asked for would be worse than silence.
public class ManifestUpdateService : IManifestUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ManifestUpdateService> _logger;
    private List<KnownEmulator>? _fetchedCatalog;
    private List<KnownEmulator>? _embeddedCatalog;

    public ManifestUpdateService(HttpClient httpClient, ILogger<ManifestUpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(Config.ManifestUpdateTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var json = await _httpClient.GetStringAsync(Config.ManifestUpdateUrl, linkedCts.Token);
            var parsed = JsonSerializer.Deserialize<List<KnownEmulator>>(json);

            if (!IsUsable(parsed))
            {
                _logger.LogWarning("Fetched catalog from {Url} failed validation; keeping the previous catalog.", Config.ManifestUpdateUrl);
                return;
            }

            _fetchedCatalog = parsed;
            _logger.LogInformation("Refreshed emulator catalog from {Url} ({Count} entries).", Config.ManifestUpdateUrl, parsed!.Count);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Catalog refresh from {Url} timed out; keeping the previous catalog.", Config.ManifestUpdateUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Catalog refresh from {Url} failed; keeping the previous catalog.", Config.ManifestUpdateUrl);
        }
    }

    public IReadOnlyList<KnownEmulator> GetCatalog() => _fetchedCatalog ?? (_embeddedCatalog ??= LoadEmbeddedCatalog());

    // Same shape of check as EmulatorInstallerService.IsUnverified, applied here to a whole
    // fetched catalog rather than one entry at install time — a fetch that deserializes cleanly
    // but still carries placeholder/empty data is treated as unusable, not cached over a good copy.
    private static bool IsUsable(List<KnownEmulator>? catalog)
    {
        if (catalog is null || catalog.Count == 0)
        {
            return false;
        }

        return catalog.All(emulator =>
            emulator.Sha256 != Config.UnverifiedManifestPlaceholder
            && emulator.DownloadUrl != Config.UnverifiedManifestPlaceholder
            && emulator.ExecutableRelativePath != Config.UnverifiedManifestPlaceholder
            && emulator.Cores.All(core =>
                core.Sha256 != Config.UnverifiedManifestPlaceholder
                && core.DownloadUrl != Config.UnverifiedManifestPlaceholder));
    }

    private static List<KnownEmulator> LoadEmbeddedCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(Config.KnownEmulatorsResourceName);
        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<KnownEmulator>>(stream) ?? [];
    }
}
