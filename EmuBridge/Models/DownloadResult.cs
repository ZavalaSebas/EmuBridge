namespace EmuBridge.Models;

public class DownloadResult
{
    public DownloadOutcome Outcome { get; set; }
    public string? ErrorMessage { get; set; }

    // Non-null only when Outcome == Success — the verified file's path in EmuBridge's managed
    // download directory. Never points at a file that failed hash/size verification: those are
    // deleted immediately (see ARCHITECTURE.md -> ADR-11, "never fail silently" checksum design).
    public string? FilePath { get; set; }
}
