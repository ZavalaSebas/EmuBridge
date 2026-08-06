using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public interface IUpdateService
{
    // Queries the GitHub Releases API for the latest tagged release and compares it against the
    // running version. Never throws on network/parse failures — an update check must never break
    // startup or the Settings screen; it returns "no update" and logs instead.
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default);

    // Downloads, SHA-256-verifies, and hot-swaps the running exe with the release asset, then
    // relaunches and exits the current process. Only supported when running from a real exe file
    // (the single-file self-contained publish); returns NotSupported otherwise.
    Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateCheckResult update, IProgress<string>? progress = null, CancellationToken ct = default);

    // Deletes a leftover current-exe.old from a previous update, if any. Called once at startup:
    // the fact that this process is running (the new version) is the signal that the old exe is
    // no longer needed — a new-version process that never starts leaves the .old in place for
    // manual recovery instead of deleting it blindly.
    void CleanupOldExecutable();
}

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly Version _currentVersion;
    private readonly string _updateCheckUrl;

    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger)
        : this(httpClient, Config.UpdateCheckUrl, CurrentAssemblyVersion(), logger)
    {
    }

    public UpdateService(HttpClient httpClient, string updateCheckUrl, Version currentVersion, ILogger<UpdateService> logger)
    {
        _httpClient = httpClient;
        _updateCheckUrl = updateCheckUrl;
        _currentVersion = currentVersion;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _updateCheckUrl);
            // The GitHub API rejects requests without a User-Agent (403), and prefers the JSON
            // media type explicitly.
            request.Headers.UserAgent.ParseAdd("EmuBridge");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Update check failed with HTTP {Status} for {Url}.", response.StatusCode, _updateCheckUrl);
                return NoUpdateAvailable();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<ReleaseApiResponse>(stream, cancellationToken: ct).ConfigureAwait(false);

            var latestTag = release?.tag_name?.TrimStart('v');
            if (latestTag is null || !Version.TryParse(latestTag, out var latestVersion))
            {
                _logger.LogWarning("Update check: latest release tag '{Tag}' is not a parseable version.", release?.tag_name);
                return NoUpdateAvailable();
            }

            var asset = release?.assets?.FirstOrDefault(a => a.name == Config.UpdateAssetName);
            if (asset?.browser_download_url is null)
            {
                _logger.LogWarning("Update check: latest release has no {AssetName} asset.", Config.UpdateAssetName);
                return NoUpdateAvailable();
            }

            var currentText = _currentVersion.ToString(3);
            var latestText = latestVersion.ToString(3);
            if (latestVersion <= _currentVersion)
            {
                _logger.LogInformation("Update check: already up to date ({CurrentText}).", currentText);
                return NoUpdateAvailable();
            }

            _logger.LogInformation("Update available: {CurrentText} -> {LatestText}.", currentText, latestText);
            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = latestVersion,
                CurrentVersionText = currentText,
                LatestVersionText = latestText,
                DownloadUrl = asset.browser_download_url,
                ReleaseNotesUrl = release?.html_url ?? Config.UpdateReleasesPageUrl,
                Sha256Digest = ParseDigest(asset.digest)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Update check failed unexpectedly; treating as no update.");
            return NoUpdateAvailable();
        }
    }

    public async Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateCheckResult update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || update.DownloadUrl is null)
        {
            return new UpdateApplyResult
            {
                Outcome = UpdateApplyOutcome.NotSupported,
                ErrorMessage = "EmuBridge isn't running from a standalone exe, so it can't update itself in place."
            };
        }

        var tempExe = Path.Combine(Path.GetTempPath(), $"emubridge_update_{Guid.NewGuid()}.exe");
        try
        {
            progress?.Report("Downloading update...");
            await DownloadToFileAsync(update.DownloadUrl, tempExe, progress, ct).ConfigureAwait(false);

            if (!HashMatches(tempExe, update.Sha256Digest))
            {
                _logger.LogError("Update download failed SHA-256 verification; refusing to apply.");
                return new UpdateApplyResult
                {
                    Outcome = UpdateApplyOutcome.VerificationFailed,
                    ErrorMessage = "The downloaded update failed verification and was not applied."
                };
            }

            progress?.Report("Applying update...");
            ApplyExecutableSwap(currentExe, tempExe);
            return new UpdateApplyResult { Outcome = UpdateApplyOutcome.Success };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to apply the update.");
            return new UpdateApplyResult
            {
                Outcome = UpdateApplyOutcome.DownloadFailed,
                ErrorMessage = $"The update could not be applied: {ex.Message}"
            };
        }
        finally
        {
            // The swap only succeeds by restarting the process; reaching this finally block with
            // the process still alive means the swap failed (or never started), so the temp file
            // is safe to clean up.
            if (File.Exists(tempExe))
            {
                try
                {
                    File.Delete(tempExe);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not clean up the temporary update file {TempExe}.", tempExe);
                }
            }
        }
    }

    // Streams the release asset to disk. On an unexpected size or failed copy, the file is
    // deleted so a half-downloaded exe can never be swapped in.
    private async Task DownloadToFileAsync(string url, string destinationPath, IProgress<string>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("EmuBridge");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The update download returned HTTP {response.StatusCode}.");
        }

        await using (var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = File.Create(destinationPath))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }
    }

    public void CleanupOldExecutable()
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe))
            {
                return;
            }

            var oldExe = currentExe + ".old";
            if (File.Exists(oldExe))
            {
                File.Delete(oldExe);
                _logger.LogInformation("Cleaned up the previous version's {OldExe}.", oldExe);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bookkeeping only — a leftover .old is harmless and gets retried next launch.
            _logger.LogWarning(ex, "Could not clean up the previous version's exe.");
        }
    }

    // internal for testability: asserts whether a downloaded file's SHA-256 matches GitHub's
    // reported digest. Null/malformed digest is treated as "match" (GitHub has always supplied
    // one for release assets, but a format change must not brick the updater — it degrades to a
    // logged warning instead, and the swap still proceeds). See ADR-26 for the security stance.
    internal static bool HashMatches(string filePath, string? sha256Digest)
    {
        var hex = ParseDigest(sha256Digest);
        if (hex is null)
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return hash.Equals(hex, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        // GitHub reports assets as "sha256:<hex>" — strip the algorithm prefix, keep the hex.
        if (digest.StartsWith(Config.UpdateDigestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            digest = digest[Config.UpdateDigestPrefix.Length..];
        }

        return digest.Length == 64 && digest.All(Uri.IsHexDigit) ? digest : null;
    }

    // The safe executable swap (DEVELOPMENT.md -> Version Management -> "Updater pattern"):
    // never overwrite the running exe directly — rename it aside (.old), move the new one in,
    // relaunch, then exit so the new process takes over. On the failure of any move, roll the
    // .old back before returning (the temp file cleanup happens in the caller's finally).
    private void ApplyExecutableSwap(string currentExe, string tempExe)
    {
        var oldExe = currentExe + ".old";

        File.Delete(oldExe); // discard any stale .old from a previous update
        File.Move(currentExe, oldExe);
        try
        {
            File.Move(tempExe, currentExe);
        }
        catch
        {
            // Roll back so a failed swap leaves the app runnable, not deleted.
            File.Move(oldExe, currentExe);
            throw;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true
        });

        // The running exe is now renamed/moved on disk — exit immediately so the new instance
        // takes the file handle. Never reached in a way that skips the swap above.
        Environment.Exit(0);
    }

    private static UpdateCheckResult NoUpdateAvailable() => new() { IsUpdateAvailable = false };

    private static Version CurrentAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version ?? new Version(0, 0, 0);
    }

    private sealed class ReleaseApiResponse
    {
        public string? tag_name { get; set; }
        public string? html_url { get; set; }
        public List<ReleaseAsset>? assets { get; set; }
    }

    private sealed class ReleaseAsset
    {
        public string? name { get; set; }
        public string? browser_download_url { get; set; }
        public string? digest { get; set; }
    }
}
