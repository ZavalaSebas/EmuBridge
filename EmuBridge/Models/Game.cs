namespace EmuBridge.Models;

public class Game
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public bool IsMissing { get; set; }

    // Manual user toggle, embedded like IsMissing — not provider-fetched metadata like BoxArt,
    // so it doesn't belong in that separate entity (see ARCHITECTURE.md -> ADR-20).
    public bool IsFavorite { get; set; }

    // Set on LaunchOutcome.Started, not on session end — see ARCHITECTURE.md -> ADR-20. No
    // consuming UI yet (that's the "Library" view, a separate Roadmap item); captured now so no
    // game played in the meantime reads as "never played" once that view exists.
    public DateTime? LastPlayedUtc { get; set; }
}
