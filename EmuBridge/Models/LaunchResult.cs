namespace EmuBridge.Models;

public enum LaunchOutcome
{
    Started,
    RomFileNotFound,
    NoEmulatorConfigured,
    ExecutableNotFound,
    CoreNotFound,
    LaunchFailed
}

public class LaunchResult
{
    public LaunchOutcome Outcome { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Completes when the launched emulator process exits. Non-null only when Outcome == Started.
    /// See ADR-1 (TrackingMode) — this Task is built from Process.WaitForExitAsync() today; the
    /// contract stays the same if the underlying tracking mechanism ever changes.
    /// </summary>
    public Task? GameSessionEndedTask { get; set; }
}
