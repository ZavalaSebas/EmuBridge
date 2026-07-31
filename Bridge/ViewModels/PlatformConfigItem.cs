namespace Bridge.ViewModels;

// Display DTO for the Settings platform list — Platform joined with its EmulatorConfig (if any).
public class PlatformConfigItem
{
    public string PlatformId { get; init; } = string.Empty;
    public string PlatformName { get; init; } = string.Empty;
    public bool IsConfigured { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ArgumentTemplate { get; init; }
}
