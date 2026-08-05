using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

// Downloads a file to a EmuBridge-managed staging area and verifies it against a pinned SHA256 +
// expected size (within a small tolerance, see SizeToleranceBytes) before it's ever treated as
// installed. See ARCHITECTURE.md -> ADR-11 for the full threat model: this protects against
// transit corruption, a compromised CDN serving a different file than what was pinned, and a
// hung/oversized download filling the user's disk. It does NOT protect against the pinned source
// itself being malicious at pin time — that's a property of EmuBridge's own manifest-authoring
// process, not of this verification step.
public class DownloadVerificationService : IDownloadVerificationService
{
    private const int BufferSize = 81920;

    // The size check is a cheap first-line gate, not the real verification — SHA256 (below) is the
    // one that actually decides trust, exact comparison, no tolerance. This margin exists only
    // because libretro's nightly core builds are a rolling channel: a routine rebuild can shift a
    // binary's size by a few bytes (build timestamp/commit hash embedded in the output) with no
    // functional change at all. Calibrated against real evidence, not guessed: re-verifying all 15
    // seed-platform cores on 2026-08-02 found 11 of 15 had drifted from their pinned
    // ExpectedSizeBytes, by -3 to +2 bytes. 32 bytes is ~10x that observed worst case — comfortable
    // headroom for future rebuilds — while staying negligible (0.035% of the smallest catalog file,
    // ARCHITECTURE.md -> ADR-11) next to what an actually different or tampered file would shift by.
    private const long SizeToleranceBytes = 32;

    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly IReadOnlySet<string> _allowedHosts;
    private readonly ILogger<DownloadVerificationService> _logger;

    public DownloadVerificationService(HttpClient httpClient, ILogger<DownloadVerificationService> logger)
        : this(httpClient, Config.EmulatorDownloadsPath, Config.AllowedDownloadHosts, logger)
    {
    }

    public DownloadVerificationService(HttpClient httpClient, string downloadDirectory, ILogger<DownloadVerificationService> logger)
        : this(httpClient, downloadDirectory, Config.AllowedDownloadHosts, logger)
    {
    }

    // Allowed hosts injected, not read from Config directly, so tests can point at a test-only
    // host (e.g. example.com) without weakening the real production allow-list (Config.cs ->
    // AllowedDownloadHosts, ARCHITECTURE.md -> ADR-26) to accommodate test fixtures.
    public DownloadVerificationService(HttpClient httpClient, string downloadDirectory, IReadOnlySet<string> allowedHosts, ILogger<DownloadVerificationService> logger)
    {
        _httpClient = httpClient;
        _downloadDirectory = downloadDirectory;
        _allowedHosts = allowedHosts;
        _logger = logger;
    }

    public async Task<DownloadResult> DownloadAndVerifyAsync(
        string sourceUrl,
        string destinationFileName,
        string expectedSha256,
        long expectedSizeBytes,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        if (!TryValidateSource(sourceUrl, out var host))
        {
            _logger.LogError("Refusing to download {FileName} from untrusted source {Url} (host: {Host}) — not in EmuBridge's allowed download hosts. No connection attempted.", destinationFileName, sourceUrl, host);
            return UntrustedSourceResult(destinationFileName, host);
        }

        Directory.CreateDirectory(_downloadDirectory);
        var stagingPath = Path.Combine(_downloadDirectory, $"{destinationFileName}.download");
        var finalPath = Path.Combine(_downloadDirectory, destinationFileName);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to start download from {Url}.", sourceUrl);
            return NetworkErrorResult(destinationFileName, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Download from {Url} timed out.", sourceUrl);
            return NetworkErrorResult(destinationFileName, "The request timed out.");
        }

        using (response)
        {
            var reportedLength = response.Content.Headers.ContentLength;
            if (reportedLength is not null && !IsWithinSizeTolerance(reportedLength.Value, expectedSizeBytes))
            {
                _logger.LogError(
                    "Download size mismatch for {Url}: server reported {Reported} bytes, expected {Expected} bytes (tolerance +/-{Tolerance}). Rejected before downloading.",
                    sourceUrl,
                    reportedLength.Value,
                    expectedSizeBytes,
                    SizeToleranceBytes);
                return SizeMismatchResult(destinationFileName, "reported an unexpected size and was rejected before downloading");
            }

            var streamFailure = await StreamToStagingFileAsync(response, stagingPath, destinationFileName, expectedSizeBytes, sourceUrl, progress, ct);
            if (streamFailure is not null)
            {
                return streamFailure;
            }
        }

        var actualHash = await ComputeSha256Async(stagingPath, ct);
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(stagingPath);
            _logger.LogError("Hash mismatch for {Url}: expected {Expected}, got {Actual}.", sourceUrl, expectedSha256, actualHash);
            return new DownloadResult
            {
                Outcome = DownloadOutcome.HashMismatch,
                ErrorMessage = $"The download for {destinationFileName} didn't match what was expected — it may be corrupted or tampered with. It was not installed."
            };
        }

        File.Move(stagingPath, finalPath, overwrite: true);
        _logger.LogInformation("Downloaded and verified {FileName} from {Url}.", destinationFileName, sourceUrl);
        return new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = finalPath };
    }

    // Streams the response body to disk, cutting off the instant the byte count exceeds
    // expectedSizeBytes + SizeToleranceBytes rather than waiting for the transfer to finish — bounds
    // worst-case disk usage even when the server never sends Content-Length. The ceiling must match
    // the tolerance used elsewhere in this class, or a file that's legitimately a few bytes over
    // (and would pass the final size check below) would get truncated mid-stream instead. Returns
    // null on success (file fully staged, size within tolerance); returns the terminal
    // DownloadResult directly on any failure.
    private async Task<DownloadResult?> StreamToStagingFileAsync(
        HttpResponseMessage response,
        string stagingPath,
        string destinationFileName,
        long expectedSizeBytes,
        string sourceUrl,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        long totalRead = 0;
        try
        {
            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            await using (var destinationStream = File.Create(stagingPath))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead > expectedSizeBytes + SizeToleranceBytes)
                    {
                        break;
                    }

                    await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    progress?.Report(totalRead);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            DeleteIfExists(stagingPath);
            _logger.LogWarning(ex, "Download failed mid-transfer for {Url}.", sourceUrl);
            return NetworkErrorResult(destinationFileName, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            DeleteIfExists(stagingPath);
            _logger.LogWarning("Download from {Url} timed out mid-transfer.", sourceUrl);
            return NetworkErrorResult(destinationFileName, "The request timed out.");
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(stagingPath);
            throw;
        }

        if (totalRead > expectedSizeBytes + SizeToleranceBytes)
        {
            DeleteIfExists(stagingPath);
            _logger.LogError(
                "Download for {Url} exceeded expected size ({Expected} bytes, tolerance +/-{Tolerance}) — stopped early at {Actual} bytes.",
                sourceUrl,
                expectedSizeBytes,
                SizeToleranceBytes,
                totalRead);
            return SizeMismatchResult(destinationFileName, "was larger than expected and was stopped");
        }

        if (!IsWithinSizeTolerance(totalRead, expectedSizeBytes))
        {
            DeleteIfExists(stagingPath);
            _logger.LogError("Download for {Url} ended early: got {Actual} of {Expected} expected bytes (tolerance +/-{Tolerance}).", sourceUrl, totalRead, expectedSizeBytes, SizeToleranceBytes);
            return new DownloadResult
            {
                Outcome = DownloadOutcome.SizeExceeded,
                ErrorMessage = $"The download for {destinationFileName} did not complete as expected. It was not installed."
            };
        }

        return null;
    }

    private static bool IsWithinSizeTolerance(long actual, long expected) => Math.Abs(actual - expected) <= SizeToleranceBytes;

    // https required, not just an allowed host — a plain http:// URL to the same host would
    // reopen exactly the MITM/tampering risk ADR-11's threat model already covers for the
    // download itself. host carries either the real parsed host (for the log/error message) or
    // the raw input when it isn't even a valid absolute URL — either way, never trusted.
    private bool TryValidateSource(string sourceUrl, out string host)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            return uri.Scheme == Uri.UriSchemeHttps && _allowedHosts.Contains(uri.Host);
        }

        host = sourceUrl;
        return false;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static DownloadResult NetworkErrorResult(string destinationFileName, string reason) => new()
    {
        Outcome = DownloadOutcome.NetworkError,
        ErrorMessage = $"The download for {destinationFileName} failed. {reason}"
    };

    private static DownloadResult SizeMismatchResult(string destinationFileName, string reason) => new()
    {
        Outcome = DownloadOutcome.SizeExceeded,
        ErrorMessage = $"The download for {destinationFileName} {reason} — this may indicate a compromised or misconfigured source. It was not installed."
    };

    private static DownloadResult UntrustedSourceResult(string destinationFileName, string host) => new()
    {
        Outcome = DownloadOutcome.UntrustedSource,
        ErrorMessage = $"The download for {destinationFileName} was blocked because its source ({host}) isn't in EmuBridge's list of trusted download hosts. It was not installed."
    };
}
