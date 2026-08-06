namespace EmuBridge.Models;

public enum UpdateApplyOutcome
{
    // Update downloaded, verified, swapped, and the app relaunched itself. Nothing more to do.
    Success = 0,

    // The release asset couldn't be downloaded (network failure, 404, empty response).
    DownloadFailed,

    // The downloaded file's SHA-256 didn't match the digest GitHub reported for the asset —
    // refused before anything was swapped, matching ADR-11/ADR-26's exact-verification boundary.
    VerificationFailed,

    // The update can't be applied in this running context — e.g. there's no current exe path
    // (not running from a real file), or the release had no downloadable asset at all.
    NotSupported,
}

public class UpdateApplyResult
{
    public UpdateApplyOutcome Outcome { get; init; }

    public string? ErrorMessage { get; init; }
}
