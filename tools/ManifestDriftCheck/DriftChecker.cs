using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using Bridge.Models;
using SharpCompress.Archives;

namespace ManifestDriftCheck;

public enum EntryStatus
{
    Match,
    Drifted,
    StructuralChange,
    VerificationFailed
}

// One thing this tool checks: either the RetroArch frontend itself, or one KnownEmulatorCore.
// ExpectedInternalPath is CoreFileName for a core, ExecutableRelativePath for the frontend —
// generalized to one concept ("does the archive still contain this expected path") so both
// share the same structural check.
public record CheckTarget(
    string Id,
    string DisplayLabel,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string ExpectedInternalPath,
    bool HasCapturedAt);

public record CheckResult(
    CheckTarget Target,
    EntryStatus Status,
    long? ActualSizeBytes,
    string? ActualSha256,
    string? Detail);

// Same procedure already run by hand three times on 2026-08-02 (ARCHITECTURE.md -> ADR-11):
// real HEAD/download, real SHA256, real archive-contents check against every catalog URL.
// Bounded concurrency, not full-parallel — no documented rate limit on buildbot.libretro.com was
// found, but a handful of requests every few hours is a deliberately conservative default, not a
// guess (see ARCHITECTURE.md -> ADR-25 for the full reasoning).
public class DriftChecker
{
    private readonly HttpClient _httpClient;
    private readonly int _maxConcurrency;

    public DriftChecker(HttpClient httpClient, int maxConcurrency = 2)
    {
        _httpClient = httpClient;
        _maxConcurrency = maxConcurrency;
    }

    public async Task<List<CheckResult>> CheckAsync(List<KnownEmulator> catalog, CancellationToken ct = default)
    {
        var targets = BuildTargets(catalog);
        var uniqueUrls = targets.Select(t => t.DownloadUrl).Distinct().ToList();

        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var resultsByUrl = new ConcurrentDictionary<string, UrlProbeResult>();

        await Task.WhenAll(uniqueUrls.Select(async url =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                resultsByUrl[url] = await ProbeUrlAsync(url, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        return targets.Select(target => Classify(target, resultsByUrl[target.DownloadUrl])).ToList();
    }

    private async Task<UrlProbeResult> ProbeUrlAsync(string url, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var destination = File.Create(tempFile);
                await source.CopyToAsync(destination, ct);
            }

            var sizeBytes = new FileInfo(tempFile).Length;

            string sha256;
            await using (var stream = File.OpenRead(tempFile))
            {
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            }

            HashSet<string> entries;
            using (var archive = ArchiveFactory.OpenArchive(tempFile))
            {
                entries = archive.Entries
                    .Where(e => !e.IsDirectory && e.Key is not null)
                    .Select(e => e.Key!.Replace('/', '\\'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return new UrlProbeResult(sizeBytes, sha256, entries, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UrlProbeResult(null, null, null, ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static CheckResult Classify(CheckTarget target, UrlProbeResult probe)
    {
        if (probe.Error is not null)
        {
            return new CheckResult(target, EntryStatus.VerificationFailed, null, null, probe.Error);
        }

        var expectedPath = target.ExpectedInternalPath.Replace('/', '\\');
        if (!probe.ArchiveEntries!.Contains(expectedPath))
        {
            return new CheckResult(
                target,
                EntryStatus.StructuralChange,
                probe.SizeBytes,
                probe.Sha256,
                $"Expected path '{target.ExpectedInternalPath}' was not found inside the archive.");
        }

        var matches = probe.SizeBytes == target.ExpectedSizeBytes
            && string.Equals(probe.Sha256, target.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

        return new CheckResult(target, matches ? EntryStatus.Match : EntryStatus.Drifted, probe.SizeBytes, probe.Sha256, null);
    }

    private static List<CheckTarget> BuildTargets(List<KnownEmulator> catalog)
    {
        var targets = new List<CheckTarget>();
        foreach (var emulator in catalog)
        {
            targets.Add(new CheckTarget(
                emulator.Id,
                emulator.Name,
                emulator.DownloadUrl,
                emulator.Sha256,
                emulator.ExpectedSizeBytes,
                emulator.ExecutableRelativePath,
                HasCapturedAt: false));

            foreach (var core in emulator.Cores)
            {
                targets.Add(new CheckTarget(
                    core.Id,
                    $"{core.Id} ({core.PlatformId})",
                    core.DownloadUrl,
                    core.Sha256,
                    core.ExpectedSizeBytes,
                    core.CoreFileName,
                    HasCapturedAt: true));
            }
        }

        return targets;
    }

    private record UrlProbeResult(long? SizeBytes, string? Sha256, HashSet<string>? ArchiveEntries, string? Error);
}
