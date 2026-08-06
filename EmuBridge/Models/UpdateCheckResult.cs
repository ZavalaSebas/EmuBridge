namespace EmuBridge.Models;

// Result of an update check against the GitHub Releases API (Phase Polish -> "Auto-updater").
// IsUpdateAvailable is the one thing the UI branches on; the rest carries enough detail to show
// the user what's being offered and, on confirmation, to download and verify the right asset.
public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }

    public Version? LatestVersion { get; init; }

    public string? CurrentVersionText { get; init; }

    public string? LatestVersionText { get; init; }

    // browser_download_url of the EmuBridge.exe release asset (the single-file self-contained
    // build — the only artifact a swap of the running exe actually supports).
    public string? DownloadUrl { get; init; }

    // The GitHub releases page for this specific tag (html_url), opened by the UI as the
    // "See what's new" affordance.
    public string? ReleaseNotesUrl { get; init; }

    // GitHub's per-asset SHA-256 digest ("sha256:<hex>", present on every release asset) —
    // used to verify the download before the exe swap, the same never-fail-silently security
    // posture DownloadVerificationService already applies to emulator downloads (ADR-11/ADR-26).
    public string? Sha256Digest { get; init; }
}
