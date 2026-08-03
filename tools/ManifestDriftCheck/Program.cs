using System.Net.Http;
using System.Text.Json;
using Bridge.Models;
using ManifestDriftCheck;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ManifestDriftCheck <path-to-KnownEmulators.json> <report-output-path>");
    return 2;
}

var manifestPath = args[0];
var reportPath = args[1];

var rawJson = await File.ReadAllTextAsync(manifestPath);
var catalog = JsonSerializer.Deserialize<List<KnownEmulator>>(rawJson) ?? [];

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var checker = new DriftChecker(httpClient);
var results = await checker.CheckAsync(catalog);

var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
var updates = results
    .Where(r => r.Status == EntryStatus.Drifted)
    .Select(r => new ManifestPatcher.EntryUpdate(
        r.Target.Id,
        r.ActualSha256!,
        r.ActualSizeBytes!.Value,
        r.Target.HasCapturedAt ? today : null))
    .ToList();

if (updates.Count > 0)
{
    var patched = ManifestPatcher.ApplyUpdates(rawJson, updates);
    await File.WriteAllTextAsync(manifestPath, patched);
}

var report = ReportBuilder.Build(results);
await File.WriteAllTextAsync(reportPath, report);
Console.WriteLine(report);

// Non-zero when something needs a human beyond a routine PR review (ARCHITECTURE.md -> ADR-25):
// a verification failure (network) or a structural change (never auto-applied). Routine drift,
// applied or not found at all, is not itself a failure — the PR step downstream decides whether
// there's anything to open a PR about, based on whether KnownEmulators.json actually changed.
var needsAttention = results.Any(r => r.Status is EntryStatus.VerificationFailed or EntryStatus.StructuralChange);
return needsAttention ? 1 : 0;
