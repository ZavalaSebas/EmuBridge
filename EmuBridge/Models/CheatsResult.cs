namespace EmuBridge.Models;

public class CheatsResult
{
    public CheatFetchOutcome Outcome { get; set; }
    public IReadOnlyList<Cheat> Cheats { get; set; } = [];
    public string? ErrorMessage { get; set; }

    // Link to the specific file in libretro/libretro-database this game's cheats came from —
    // CC BY-SA 4.0 requires a link to the licensed material itself, not just a generic project
    // credit (see ARCHITECTURE.md -> ADR-27). Null when Outcome isn't Success, or when a local
    // file exists but its provenance sidecar wasn't found (a game whose .cht predates this field,
    // or was placed by hand) — the UI falls back to a generic project credit in that case, not an
    // error.
    public string? SourceFileUrl { get; set; }
}
