namespace Bridge.Models;

// One cheat entry from a RetroArch .cht file (see ARCHITECTURE.md -> ADR-27). Deliberately does
// not carry the "code" value (the actual Game Genie/address-poke payload) — Bridge never
// interprets or generates that, only toggles cheatN_enable, so CheatFileParser patches the raw
// file text directly rather than round-tripping through this DTO.
public class Cheat
{
    public int Index { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}
