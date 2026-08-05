namespace EmuBridge.ViewModels;

// Display DTO for the Settings platform list — Platform joined with its resolved EmulatorProfile (if any).
public class PlatformConfigItem
{
    public string PlatformId { get; init; } = string.Empty;
    public string PlatformName { get; init; } = string.Empty;
    public bool IsConfigured { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ArgumentTemplate { get; init; }

    // True if KnownEmulators.json has a fully-verified core for this platform — drives whether
    // Settings offers the "Auto-Install" option at all (see ARCHITECTURE.md -> ADR-14).
    public bool HasKnownInstallOption { get; init; }
}
