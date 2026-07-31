using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

// Downloads a file to a Bridge-managed staging area and verifies it against a pinned SHA256 +
// exact expected size before it's ever treated as installed. See ARCHITECTURE.md -> ADR-11 for
// the full threat model: this protects against transit corruption, a compromised CDN serving a
// different file than what was pinned, and a hung/oversized download filling the user's disk.
// It does NOT protect against the pinned source itself being malicious at pin time — that's a
// property of Bridge's own manifest-authoring process, not of this verification step.
public class DownloadVerificationService : IDownloadVerificationService
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly ILogger<DownloadVerificationService> _logger;

    public DownloadVerificationService(HttpClient httpClient, ILogger<DownloadVerificationService> logger)
        : this(httpClient, Config.EmulatorDownloadsPath, logger)
    {
    }

    public DownloadVerificationService(HttpClient httpClient, string downloadDirectory, ILogger<DownloadVerificationService> logger)
    {
        _httpClient = httpClient;
        _downloadDirectory = downloadDirectory;
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
            if (reportedLength is not null && reportedLength.Value != expectedSizeBytes)
            {
                _logger.LogError(
                    "Download size mismatch for {Url}: server reported {Reported} bytes, expected {Expected} bytes. Rejected before downloading.",
                    sourceUrl,
                    reportedLength.Value,
                    expectedSizeBytes);
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
    // expectedSizeBytes rather than waiting for the transfer to finish — bounds worst-case disk
    // usage even when the server never sends Content-Length. Returns null on success (file fully
    // staged, size matched exactly); returns the terminal DownloadResult directly on any failure.
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
                    if (totalRead > expectedSizeBytes)
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

        if (totalRead > expectedSizeBytes)
        {
            DeleteIfExists(stagingPath);
            _logger.LogError(
                "Download for {Url} exceeded expected size ({Expected} bytes) — stopped early at {Actual} bytes.",
                sourceUrl,
                expectedSizeBytes,
                totalRead);
            return SizeMismatchResult(destinationFileName, "was larger than expected and was stopped");
        }

        if (totalRead != expectedSizeBytes)
        {
            DeleteIfExists(stagingPath);
            _logger.LogError("Download for {Url} ended early: got {Actual} of {Expected} expected bytes.", sourceUrl, totalRead, expectedSizeBytes);
            return new DownloadResult
            {
                Outcome = DownloadOutcome.SizeExceeded,
                ErrorMessage = $"The download for {destinationFileName} did not complete as expected. It was not installed."
            };
        }

        return null;
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
}
