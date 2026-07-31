namespace Bridge.Models;

// Emulator + EmulatorProfile joined for a single platform — what LaunchService and
// SettingsViewModel actually need at the point of use. EmulatorService is the sole place that
// performs this join, so callers never need to know about the Emulator/EmulatorProfile split.
public class ResolvedEmulatorProfile
{
    public string PlatformId { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgumentTemplate { get; set; } = string.Empty;
}
