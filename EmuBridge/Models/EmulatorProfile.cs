namespace EmuBridge.Models;

// Launch config for an Emulator, scoped to either a whole Platform (GameId == null, the default
// every game on that platform uses) or a single Game (GameId set — an override, see
// ARCHITECTURE.md -> ADR-24). Uniqueness is (PlatformId, GameId) at the repository layer, not a
// DB unique index — same deliberate loosening ADR-11 already established for PlatformId alone.
public class EmulatorProfile
{
    public Guid Id { get; set; }
    public Guid EmulatorId { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public string ArgumentTemplate { get; set; } = string.Empty;

    // Non-null only for auto-installed core-based profiles (ADR-14) — resolved to {CorePath} at
    // launch time, alongside {RomPath}. Null for manually-configured profiles.
    public string? CorePath { get; set; }

    // Null (the default for every row before ADR-24, and for every platform-wide profile since)
    // means this is the platform's shared default. A real Guid means this row overrides that
    // default for one specific Game only — never referenced from anywhere else, so it can't be
    // shared the way BoxArt's cached image files can (ADR-24).
    public Guid? GameId { get; set; }
}
