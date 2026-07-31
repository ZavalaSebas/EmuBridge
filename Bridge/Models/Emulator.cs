namespace Bridge.Models;

// A physical emulator install, independent of any platform. One Emulator can back many
// EmulatorProfile rows (e.g. one RetroArch install serving all 15 seed platforms via different
// cores) — see ARCHITECTURE.md -> ADR-11 for the migration off the old 1:1 EmulatorConfig shape.
public class Emulator
{
    public Guid Id { get; set; }
    public string? KnownEmulatorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public InstallSource InstallSource { get; set; }
    public string? InstalledSha256 { get; set; }
}
