namespace EmuBridge.Models;

public enum CheatFetchOutcome
{
    Success,

    // The platform has no folder at all in libretro/libretro-database's cht/ directory —
    // confirmed by listing the real repo, not assumed (e.g. wonderswan, as of this check). A
    // distinct, honest outcome from NotFound: this game could never have been found, not just
    // "wasn't", the same distinction ADR-8 already draws for SteamGridDB lookups.
    PlatformNotSupported,

    // The platform has a real folder, but no file matching this game's exact name exists in it.
    NotFound,

    FetchFailed,

    // Either a local file already on disk, or one just fetched, doesn't match the expected
    // cheats=N / cheatN_desc / cheatN_enable grammar (CheatFileParser.Parse). Rejected whole,
    // never partially trusted.
    Corrupted
}
