using Bridge.Models;

namespace Bridge.Services;

public interface ICheatService
{
    // Checks the local per-game file first (Config.CheatsPath); only fetches from
    // libretro/libretro-database when nothing local exists yet. A local file that fails to parse
    // is Corrupted, never silently re-fetched over top of it.
    Task<CheatsResult> LoadCheatsAsync(Game game, string platformId, CancellationToken ct = default);

    // Only ever called after a successful LoadCheatsAsync for the same game (CheatsViewModel's
    // contract) — patches the one cheatN_enable line in the already-local file.
    Task SetCheatEnabledAsync(Game game, int cheatIndex, bool enabled, CancellationToken ct = default);

    // Cheap, synchronous File.Exists check — LaunchService uses this at launch time to decide
    // whether this game has a Bridge-managed cheat file at all. Never triggers a fetch; null means
    // no Bridge-managed cheat file exists for this game yet, so RetroArch's own defaults are left
    // completely alone. The non-null return value is the per-game root folder, passed straight into
    // ApplyCheatLaunchOverridesAsync's cheatDirectory parameter.
    string? GetCheatDirectoryIfExists(Game game);

    // Writes both settings this feature needs into RetroArch's own per-game/per-core "override"
    // config file (ARCHITECTURE.md -> ADR-27) — the one config mechanism RetroArch itself
    // explicitly reloads away before saving on exit, unlike --appendconfig or the
    // LIBRETRO_CHEATS_DIRECTORY env var mechanism 1 originally used (both confirmed, separately, to
    // leak permanently into the user's actual retroarch.cfg via config_save_on_exit — see
    // CheatService.ApplyCheatLaunchOverridesAsync for the real evidence on each). cheatDirectory
    // becomes this game's cheat_database_path, always written whenever this method is called
    // (no on/off state — mirrors mechanism 1's original always-on behavior). autoApplyCheatsEnabled
    // controls whether apply_cheats_after_load is present at all. retroArchExecutablePath is the
    // emulator's own executable — CheatService reads its real retroarch.cfg to find RetroArch's
    // actual configured "Config" directory (verified NOT to always be the executable's own
    // directory — see CheatService.ResolveConfigDirectory). The file may already contain other keys
    // the user saved themselves from RetroArch's own "Save Game/Core Override" menu action — only
    // these two lines are ever touched.
    Task ApplyCheatLaunchOverridesAsync(Game game, string retroArchExecutablePath, string cheatDirectory, bool autoApplyCheatsEnabled, CancellationToken ct = default);
}
