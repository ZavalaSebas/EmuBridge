namespace Bridge.Models;

// Per-platform launch config for an Emulator — replaces the old EmulatorConfig (see ADR-11).
// PlatformId is kept effectively unique per profile at the service layer (EmulatorService),
// not via a DB unique index — a deliberate loosening so a future many-profiles-per-platform UI
// doesn't need another schema migration.
public class EmulatorProfile
{
    public Guid Id { get; set; }
    public Guid EmulatorId { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public string ArgumentTemplate { get; set; } = string.Empty;
}
