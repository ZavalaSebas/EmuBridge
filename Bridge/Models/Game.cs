namespace Bridge.Models;

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
}
