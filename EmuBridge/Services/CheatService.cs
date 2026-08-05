using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

// Fetches/manages per-game RetroArch .cht files (ARCHITECTURE.md -> ADR-27). EmuBridge never curates
// cheat content itself (unlike KnownEmulators.json's hand-verified entries) - fetches the specific
// file for one game, on demand, from libretro/libretro-database, the same real, public,
// community-maintained source RetroArch's own "Update Cheats" downloader uses. CC BY-SA 4.0
// (confirmed against the repo's real LICENSE file) requires attribution and a link to the specific
// licensed material - SourceFileUrl on CheatsResult carries that per-file link back to the caller.
//
// Real-use finding (2026-08-04): RetroArch's own game-specific-cheat auto-load
// (cheat_manager_get_game_specific_filename, cheat_manager.c) looks for
// "{cheat_database_path}/{core_name}/{game_name}.cht" - a subfolder named after the CORE's own
// self-reported retro_get_system_info library_name, not a flat file. The first implementation
// wrote a flat file and pointed LIBRETRO_CHEATS_DIRECTORY straight at it, so RetroArch never found
// it (confirmed: toggled cheats had no effect, and nothing showed in RetroArch's own Cheats menu -
// exactly what "never loaded" looks like, not "loaded but not applied"). RetroArchCoreNames below
// holds the real library_name per platform, verified against each core's own source (or the
// official libretro-core-info .info file where the source used an indirect macro/constant) - not
// guessed from KnownEmulators.json's "friendly" branding names, which don't always match (e.g.
// Stella's current source self-reports "Stella 2023", not "Stella").
public class CheatService : ICheatService
{
    // Confirmed by listing the real cht/ directory of libretro/libretro-database (gh api), not
    // guessed from a naming convention. "wonderswan" is deliberately absent - no folder exists for
    // it in the real repo as of this check; LoadCheatsAsync reports PlatformNotSupported for it
    // rather than attempting (and always failing) a fetch.
    private static readonly IReadOnlyDictionary<string, string> PlatformFolders = new Dictionary<string, string>
    {
        ["nes"] = "Nintendo - Nintendo Entertainment System",
        ["snes"] = "Nintendo - Super Nintendo Entertainment System",
        ["n64"] = "Nintendo - Nintendo 64",
        ["gb"] = "Nintendo - Game Boy",
        ["gbc"] = "Nintendo - Game Boy Color",
        ["gba"] = "Nintendo - Game Boy Advance",
        ["nds"] = "Nintendo - Nintendo DS",
        ["genesis"] = "Sega - Mega Drive - Genesis",
        ["sms"] = "Sega - Master System - Mark III",
        ["gamegear"] = "Sega - Game Gear",
        ["atari2600"] = "Atari - 2600",
        ["atari7800"] = "Atari - 7800",
        ["pcengine"] = "NEC - PC Engine - TurboGrafx 16",
        ["lynx"] = "Atari - Lynx"
    };

    // The real value each core reports via retro_get_system_info().library_name - the exact
    // subfolder name RetroArch's own cheat_manager_get_game_specific_filename requires. Verified
    // per platform:
    //  - nes/snes/gb/gbc/gba/n64/nds/genesis/sms/gamegear/atari7800: confirmed directly in each
    //    core's own libretro.c/.cpp source (info->library_name = "...").
    //  - atari2600: official libretro-core-info says "Stella", but the core's current source
    //    (stella/src/os/libretro/StellaLIBRETRO.hxx) self-reports "Stella 2023" via getCoreName().
    //    Using the officially-published "Stella" here since it's what RetroArch's own database
    //    ships/expects; genuinely unconfirmed against the exact binary KnownEmulators.json pins -
    //    flagged for real interactive confirmation, not assumed correct.
    //  - pcengine: official libretro-core-info corename ("Beetle PCE") - the source uses an
    //    indirect MEDNAFEN_CORE_NAME macro whose exact definition wasn't found in a quick search.
    //  - lynx: the core's own Rust source (LLeny/holani-retro, src/lib.rs) reports "holani"
    //    (lowercase) via SystemInfo::new(), but the published .info corename says "Holani". Two
    //    real sources disagree - using the published "Holani" as the more likely stable convention,
    //    flagged for real interactive confirmation like atari2600 above.
    //  - wonderswan: deliberately absent, matches PlatformFolders - moot, no cheat coverage exists
    //    for it regardless of this mapping.
    private static readonly IReadOnlyDictionary<string, string> RetroArchCoreNames = new Dictionary<string, string>
    {
        ["nes"] = "FCEUmm",
        ["snes"] = "Snes9x",
        ["gb"] = "SameBoy",
        ["gbc"] = "SameBoy",
        ["gba"] = "mGBA",
        ["n64"] = "Mupen64Plus-Next",
        ["nds"] = "melonDS DS",
        ["genesis"] = "Genesis Plus GX",
        ["sms"] = "Genesis Plus GX",
        ["gamegear"] = "Genesis Plus GX",
        ["atari2600"] = "Stella",
        ["atari7800"] = "ProSystem",
        ["pcengine"] = "Beetle PCE",
        ["lynx"] = "Holani"
    };

    private const string SourceSidecarFileName = "source.txt";

    // RetroArch's own override-file naming convention (config_load_override, configuration.c) only
    // ever writes/reads this one key here - the regex tolerates whatever quoting is already present
    // (RetroArch's own "Save Game Override" menu action quotes everything, same lesson as the .cht
    // quoting fix) but always normalizes to unquoted on write, matching the rest of this codebase's
    // convention.
    private static readonly Regex ApplyCheatsAfterLoadLinePattern =
        new(@"^[ \t]*apply_cheats_after_load[ \t]*=.*\r?\n?", RegexOptions.Multiline);

    // Same targeted-line convention as ApplyCheatsAfterLoadLinePattern, for the other key this
    // override file now carries (see ApplyCheatLaunchOverridesAsync for why cheat_database_path
    // moved here too, off the LIBRETRO_CHEATS_DIRECTORY env var).
    private static readonly Regex CheatDatabasePathLinePattern =
        new(@"^[ \t]*cheat_database_path[ \t]*=.*\r?\n?", RegexOptions.Multiline);

    // Real bug found via a live test + RetroArch's own log file (log_verbosity/log_to_file): a
    // correctly-placed, correctly-named override file at the executable's own directory was
    // silently ignored - no "[Override] ..." log line at all. Root cause verified against
    // configuration.c's SETTING_PATH bindings: the config key here is "rgui_config_directory", NOT
    // "directory_menu_config" (the C struct field name - the same class of mistake as
    // "cheat_apply_after_load" earlier, a key name guessed from a field/constant name instead of
    // checked). RetroArch's own portable/self-contained install default seeds this to ":\config" -
    // ":" is RetroArch's own "relative to the executable's own directory" notation (verified in
    // libretro-common's fill_pathname_expand_special) - so the real override directory is
    // "{executable directory}\config", not the executable directory itself. Confirmed directly: the
    // same file at "{executable directory}\config\{core}\{game}.cfg" was found and loaded (RetroArch's
    // own "[Override] Game-specific overrides found" / "[Config] Appending override config" log
    // lines).
    private static readonly Regex RguiConfigDirectoryPattern =
        new(@"^[ \t]*rgui_config_directory[ \t]*=[ \t]*""?([^""\r\n]*)""?[ \t]*$", RegexOptions.Multiline);

    private readonly HttpClient _httpClient;
    private readonly string _cheatsDirectory;
    private readonly ILogger<CheatService> _logger;

    public CheatService(HttpClient httpClient, ILogger<CheatService> logger)
        : this(httpClient, Config.CheatsPath, logger)
    {
    }

    // Injectable root directory, same two-constructor shape as EmulatorInstallerService (ADR-14) -
    // tests point this at a temp directory instead of the real %LocalAppData%, and can exercise
    // "local file already exists" without a real prior fetch.
    public CheatService(HttpClient httpClient, string cheatsDirectory, ILogger<CheatService> logger)
    {
        _httpClient = httpClient;
        _cheatsDirectory = cheatsDirectory;
        _logger = logger;
    }

    public async Task<CheatsResult> LoadCheatsAsync(Game game, string platformId, CancellationToken ct = default)
    {
        var localPath = GetCheatFilePath(game, platformId);
        if (localPath is not null && File.Exists(localPath))
        {
            return await LoadLocalAsync(localPath, ct);
        }

        if (!PlatformFolders.TryGetValue(platformId, out var platformFolder))
        {
            return new CheatsResult { Outcome = CheatFetchOutcome.PlatformNotSupported };
        }

        var encodedFolder = Uri.EscapeDataString(platformFolder);
        var encodedName = Uri.EscapeDataString(game.Name);
        var rawUrl = $"{Config.CheatDatabaseRawBaseUrl}/{encodedFolder}/{encodedName}.cht";
        var blobUrl = $"{Config.CheatDatabaseBlobBaseUrl}/{encodedFolder}/{encodedName}.cht";

        string content;
        try
        {
            using var response = await _httpClient.GetAsync(rawUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new CheatsResult { Outcome = CheatFetchOutcome.NotFound };
            }

            response.EnsureSuccessStatusCode();
            content = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch cheats for {GameName} ({PlatformId}).", game.Name, platformId);
            return new CheatsResult
            {
                Outcome = CheatFetchOutcome.FetchFailed,
                ErrorMessage = "Couldn't reach the cheat database. Check your connection and try again."
            };
        }

        var parseResult = CheatFileParser.Parse(content);
        if (!parseResult.IsValid)
        {
            _logger.LogWarning("Fetched cheat file for {GameName} did not match the expected format ({Url}).", game.Name, rawUrl);
            return new CheatsResult
            {
                Outcome = CheatFetchOutcome.Corrupted,
                ErrorMessage = "The cheat file for this game couldn't be read - its format wasn't recognized."
            };
        }

        // Platform is in PlatformFolders (checked above) but not RetroArchCoreNames would be a
        // real data-consistency bug in this class, not a user-facing state - the two dictionaries
        // are meant to cover the same 14 platforms 1:1.
        if (localPath is null)
        {
            _logger.LogError("Platform {PlatformId} has a libretro-database folder but no known RetroArch core name - fix RetroArchCoreNames.", platformId);
            return new CheatsResult { Outcome = CheatFetchOutcome.PlatformNotSupported };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, content, ct);
        await File.WriteAllTextAsync(GetSourceSidecarPath(game, platformId)!, blobUrl, ct);

        return new CheatsResult { Outcome = CheatFetchOutcome.Success, Cheats = parseResult.Cheats, SourceFileUrl = blobUrl };
    }

    public async Task SetCheatEnabledAsync(Game game, int cheatIndex, bool enabled, CancellationToken ct = default)
    {
        var localPath = GetCheatFilePath(game, game.PlatformId)
            ?? throw new InvalidOperationException($"No known RetroArch core name for platform '{game.PlatformId}'.");
        var content = await File.ReadAllTextAsync(localPath, ct);
        var updated = CheatFileParser.SetEnabled(content, cheatIndex, enabled);
        await File.WriteAllTextAsync(localPath, updated, ct);
    }

    private async Task<CheatsResult> LoadLocalAsync(string localPath, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(localPath, ct);
        var parseResult = CheatFileParser.Parse(content);
        if (!parseResult.IsValid)
        {
            _logger.LogWarning("Local cheat file at {Path} did not match the expected format.", localPath);
            return new CheatsResult
            {
                Outcome = CheatFetchOutcome.Corrupted,
                ErrorMessage = "This game's saved cheat file couldn't be read - it may be corrupted."
            };
        }

        var sourcePath = Path.Combine(Path.GetDirectoryName(localPath)!, SourceSidecarFileName);
        var sourceUrl = File.Exists(sourcePath) ? await File.ReadAllTextAsync(sourcePath, ct) : null;

        return new CheatsResult { Outcome = CheatFetchOutcome.Success, Cheats = parseResult.Cheats, SourceFileUrl = sourceUrl };
    }

    // Returns the per-game ROOT folder (never the core-name subfolder) - LaunchService passes this
    // straight into ApplyCheatLaunchOverridesAsync, which writes it as this game's
    // cheat_database_path in the override file, so RetroArch's own {dir}/{core_name}/{game_name}.cht
    // lookup resolves to the real file underneath it.
    public string? GetCheatDirectoryIfExists(Game game)
    {
        var path = GetCheatFilePath(game, game.PlatformId);
        return path is not null && File.Exists(path) ? GetGameRootDirectory(game) : null;
    }

    // Key name verified against RetroArch's own real source, not assumed: configuration.c's
    // SETTING_BOOL binds the config key "apply_cheats_after_load" (not "cheat_apply_after_load" -
    // the DEFAULT_APPLY_CHEATS_AFTER_LOAD constant name reads the other way round and is easy to
    // transpose) to settings->bools.apply_cheats_after_load, which retroarch.c passes straight
    // into command_event_init_cheats() on content load - that's the actual auto-apply trigger.
    //
    // Uses RetroArch's real "override" mechanism (config_load_override/config_unload_override in
    // configuration.c), not --appendconfig - a real leaked line in an actual retroarch.cfg proved
    // --appendconfig values are never reverted during the process lifetime and get permanently
    // baked in by config_save_on_exit (RetroArch's own default). Override files are explicitly
    // reloaded away *before* that save happens, and RetroArch auto-discovers them itself
    // (auto_overrides_enable, default true) at
    // "{real config directory}/{core_name}/{rom_basename_without_extension}.cfg" - no CLI flag
    // needed. retroArchExecutablePath is only the starting point for finding that directory - see
    // ResolveConfigDirectory for why it isn't simply the executable's own directory.
    //
    // cheat_database_path lives in this SAME override file too, not the LIBRETRO_CHEATS_DIRECTORY
    // env var mechanism 1 used originally - verified against configuration.c's config_load_file:
    // the env var (getenv("LIBRETRO_CHEATS_DIRECTORY")) is read AFTER the override file is merged
    // on every single call to that function, including the exact one config_unload_override()
    // itself makes to "restore the original configuration" before config_save_on_exit runs. As
    // long as the env var was set, that "restore" call would just re-derive and re-apply the same
    // value, permanently leaking it into the user's real retroarch.cfg the same way --appendconfig
    // leaked apply_cheats_after_load - confirmed directly (a stale per-game GUID path lingering in
    // a real retroarch.cfg's own cheat_database_path between sessions). Routing it through the
    // override file instead closes that gap the same way it's already closed for
    // apply_cheats_after_load. Unlike apply_cheats_after_load, cheat_database_path is not
    // toggle-gated - it's written whenever a EmuBridge-managed cheat file exists for this game,
    // matching mechanism 1's original always-on behavior exactly.
    //
    // That exact file may already hold other keys the user saved themselves via RetroArch's own
    // "Save Game/Core Override" menu action (a shader or resolution tweak, for example) - only
    // these two lines are ever touched here, matching the same targeted single-line patch
    // discipline CheatFileParser.SetEnabled already uses for .cht files. cheat_database_path is
    // never removed once written (there's no "off" state for it - RetroArch always needs to be
    // pointed at this game's cheat folder for GetCheatDirectoryIfExists to have meant anything),
    // so the file itself is never deleted by this method; turning the auto-apply toggle off only
    // ever removes the apply_cheats_after_load line.
    public async Task ApplyCheatLaunchOverridesAsync(Game game, string retroArchExecutablePath, string cheatDirectory, bool autoApplyCheatsEnabled, CancellationToken ct = default)
    {
        if (!RetroArchCoreNames.TryGetValue(game.PlatformId, out var coreName))
        {
            return;
        }

        var retroArchConfigDirectory = ResolveConfigDirectory(retroArchExecutablePath);
        var overridePath = Path.Combine(retroArchConfigDirectory, coreName, $"{game.Name}.cfg");
        var existing = File.Exists(overridePath) ? await File.ReadAllTextAsync(overridePath, ct) : string.Empty;

        // Path-type settings are always quoted in RetroArch's own real config files (confirmed
        // against several real entries, including this exact key after it had leaked) - unlike
        // apply_cheats_after_load's boolean, which RetroArch's own shipped retroarch.cfg writes
        // unquoted.
        var cheatDatabasePathLine = $"cheat_database_path = \"{cheatDirectory}\"\n";
        var withCheatDatabasePath = CheatDatabasePathLinePattern.IsMatch(existing)
            ? CheatDatabasePathLinePattern.Replace(existing, cheatDatabasePathLine)
            : existing + (existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "") + cheatDatabasePathLine;

        string updated;
        if (autoApplyCheatsEnabled)
        {
            const string applyLine = "apply_cheats_after_load = true\n";
            updated = ApplyCheatsAfterLoadLinePattern.IsMatch(withCheatDatabasePath)
                ? ApplyCheatsAfterLoadLinePattern.Replace(withCheatDatabasePath, applyLine)
                : withCheatDatabasePath + applyLine;
        }
        else
        {
            updated = ApplyCheatsAfterLoadLinePattern.Replace(withCheatDatabasePath, "");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        await File.WriteAllTextAsync(overridePath, updated, ct);
    }

    // The real bug this fixes: RetroArch's override auto-discovery does NOT simply use the
    // executable's own directory - it uses settings->paths.directory_menu_config, bound to config
    // key "rgui_config_directory" (verified against configuration.c's SETTING_PATH; the field name
    // doesn't match the config key, same trap as "cheat_apply_after_load" earlier). RetroArch's own
    // portable-install default seeds that to ":\config" - a leading ":" is RetroArch's own "relative
    // to the executable's own directory" notation (fill_pathname_expand_special, libretro-common) -
    // so the real directory is "{executable directory}\config" for any install that hasn't had this
    // customized, not the executable directory itself. Reads the user's own retroarch.cfg (plain
    // text, sitting next to the executable for any portable install) rather than hardcoding
    // "\config" - a user who customized RetroArch's own "Config" directory setting, or set an
    // absolute path there, must still be respected.
    private static string ResolveConfigDirectory(string retroArchExecutablePath)
    {
        var executableDirectory = Path.GetDirectoryName(retroArchExecutablePath) ?? string.Empty;
        var mainConfigPath = Path.Combine(executableDirectory, "retroarch.cfg");
        if (!File.Exists(mainConfigPath))
        {
            return executableDirectory;
        }

        var match = RguiConfigDirectoryPattern.Match(File.ReadAllText(mainConfigPath));
        var configuredValue = match.Success ? match.Groups[1].Value.Trim() : string.Empty;

        // Empty and the literal string "default" both mean "not customized" - config_set_defaults
        // (configuration.c) treats them identically, clearing the setting back to unset.
        if (configuredValue.Length == 0 || configuredValue == "default")
        {
            return executableDirectory;
        }

        return configuredValue[0] == ':'
            ? Path.Combine(executableDirectory, configuredValue[1..].TrimStart('\\', '/'))
            : configuredValue;
    }

    private string GetGameRootDirectory(Game game) => Path.Combine(_cheatsDirectory, game.Id.ToString());

    // Null when platformId has no known RetroArch core name (see RetroArchCoreNames) - distinct
    // from PlatformNotSupported at the libretro-database level, but in practice the two
    // dictionaries cover the same platforms, so this only trips for a genuine mapping gap.
    private string? GetCheatFilePath(Game game, string platformId) =>
        RetroArchCoreNames.TryGetValue(platformId, out var coreName)
            ? Path.Combine(GetGameRootDirectory(game), coreName, $"{game.Name}.cht")
            : null;

    private string? GetSourceSidecarPath(Game game, string platformId) =>
        RetroArchCoreNames.TryGetValue(platformId, out var coreName)
            ? Path.Combine(GetGameRootDirectory(game), coreName, SourceSidecarFileName)
            : null;
}
