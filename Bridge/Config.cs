using System.IO;

namespace Bridge;

public static class Config
{
    public const string AppName = "Bridge";

    public const string UnknownPlatformId = "unknown";
    public const string UnknownPlatformName = "Unknown System";

    public const string SeedSystemsResourceName = "Bridge.Resources.SeedSystems.json";
    public const string KnownEmulatorsResourceName = "Bridge.Resources.KnownEmulators.json";

    // Sentinel value for any KnownEmulators.json field whose real data hasn't been independently
    // verified yet (see ARCHITECTURE.md -> ADR-11). Must never reach a Release build unreplaced —
    // enforced by Bridge.Tests -> KnownEmulatorsManifestTests.
    public const string UnverifiedManifestPlaceholder = "PLACEHOLDER_NOT_VERIFIED";

    // Live catalog refresh (ARCHITECTURE.md -> ADR-25): fetched fresh on every startup, fire-and-
    // forget, from the same repo the app itself ships from — never a separate backend. Points at
    // `main` specifically, never an unreviewed branch: the drift-check PR mechanism (same ADR)
    // only ever writes to `main` after a human merges it.
    public const string ManifestUpdateUrl = "https://raw.githubusercontent.com/ZavalaSebas/Bridge/main/Bridge/Resources/KnownEmulators.json";

    // Short deliberately: this fetch must never make startup or an Auto-Install attempt feel
    // slow. Missing this window just means falling back to the cache or the embedded copy.
    public static readonly TimeSpan ManifestUpdateTimeout = TimeSpan.FromSeconds(5);

    // Hosts DownloadVerificationService will ever download+extract+run content from, regardless
    // of where the manifest entry pointing at them came from (embedded or live-fetched, ADR-25).
    // Deliberately a compiled constant, never manifest data — a compromised KnownEmulators.json
    // (embedded or fetched) could otherwise redirect a DownloadUrl anywhere and supply a matching
    // hash for its own malicious payload, since the hash check alone can't catch a source that
    // controls both the file and the pin. Confirmed against the real catalog, not guessed: every
    // one of the 15 seed cores plus the RetroArch frontend itself uses exactly this one host.
    // Adding a new one is deliberately a source-code change, reviewed like any other Bridge
    // commit — not something the drift-check bot (which only ever updates Sha256/
    // ExpectedSizeBytes/CapturedAt on already-trusted entries) can add on its own. Distinct from
    // ManifestUpdateUrl above: that fetch has no variable destination to restrict in the first
    // place, so no allow-list applies to it.
    public static readonly IReadOnlySet<string> AllowedDownloadHosts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "buildbot.libretro.com" };

    public const string SteamGridDbBaseUrl = "https://www.steamgriddb.com/api/v2";

    // Phase 1 cover grid tile size — a placeholder default (2:3, matching typical box art
    // proportions), not a final UI decision. Tune when the actual grid cell size is designed.
    public const int CoverWidth = 200;
    public const int CoverHeight = 300;

    // Big Picture mode's tile size (MainWindow.xaml -> BigPictureTileTemplate) — landscape, not
    // portrait: Big Picture shows the horizontal grid (matches this shape closely, ~2.14:1, the
    // same ratio as SteamGridDB's real 460x215/920x430 dimensions), while the normal grid shows
    // the vertical grid (matches CoverWidth/CoverHeight's 2:3 shape instead). See ARCHITECTURE.md
    // -> ADR-23 (Update).
    public const int BigPictureCoverWidth = 460;
    public const int BigPictureCoverHeight = 215;

    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    public static string LibraryDbPath => Path.Combine(AppDataPath, "bridge.db");
    public static string SettingsPath => Path.Combine(AppDataPath, "settings.json");
    public static string ImageCachePath => Path.Combine(AppDataPath, "ImageCache");

    // Staging/final location for auto-downloaded emulators/cores (ADR-11). Verified files land
    // here; anything that fails hash/size verification is deleted before it ever gets this far
    // as a non-".download"-suffixed name.
    public static string EmulatorDownloadsPath => Path.Combine(AppDataPath, "Downloads");

    // Extracted, ready-to-run emulator installs (ADR-14) — distinct from EmulatorDownloadsPath,
    // which only holds the raw downloaded archives before/after verification.
    public static string EmulatorInstallPath => Path.Combine(AppDataPath, "Emulators");
}
