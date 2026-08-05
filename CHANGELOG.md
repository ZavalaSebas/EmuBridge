# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- Renamed the project from Bridge to EmuBridge, code and GitHub repo alike, to free the "Bridge" name for a future, larger version of the app. Existing installs migrate their `%LOCALAPPDATA%\Bridge` data automatically on first launch after updating — box art cache and auto-installed emulator paths are rewritten in place. See ARCHITECTURE.md → ADR-30.

## [0.10.0] - 2026-08-04

TheGamesDB as a second, additive metadata source for the game detail panel — chosen against real evidence from a deliberately hard coverage test (13/15 titles), not a flagship-only sample. 327 unit tests pass in Release, 326 in Debug (`EmuBridge.Tests`); 16 in `ManifestDriftCheck.Tests` (unchanged).

### Added
- TheGamesDB as a second metadata source (RetroArch and other emulators alike) — the game detail panel now shows real descriptions and screenshots, filling the gap SteamGridDB structurally can't (SteamGridDB keeps owning box art, unchanged). Chosen after evaluating IGDB/TheGamesDB/RAWG/ScreenScraper/LaunchBox Games Database against primary sources and a real, deliberately hard coverage test (13/15 titles, including 2 ROM hacks and 3 region-exclusives) rather than a flagship-only sample. New API key setting in Settings (project-level, same DPAPI-encrypted pattern as SteamGridDB). Fetched on demand when a game's detail view opens, not during the library scan, to conserve the real ~1000/month key allowance. See ARCHITECTURE.md → ADR-28.

## [0.9.0] - 2026-08-04

Cheats management per game for RetroArch-backed profiles — three real bugs found and fixed correcting the mechanism along the way, all root-caused against RetroArch's actual source (and, for two of them, its own log output) rather than assumed. 301 unit tests pass in Release, 300 in Debug (`EmuBridge.Tests`); 16 in `ManifestDriftCheck.Tests` (unchanged).

### Added
- Cheats management per game (RetroArch only) — right-click a game → "Cheats..." fetches its `.cht` file on demand from `libretro/libretro-database` (CC BY-SA 4.0, with a per-file attribution link), lets you toggle individual cheats on/off, and persists the file locally so it never re-fetches on later opens. A new "Auto-apply cheats on launch" toggle in Settings (default ON) applies a game's enabled cheats automatically when it starts. Both this and pointing RetroArch at a game's cheat file at all go through RetroArch's own per-game config override mechanism — scoped to games that already have a EmuBridge-managed cheat file, never a global RetroArch behavior change, and never written to the user's own `retroarch.cfg`. See ARCHITECTURE.md → ADR-27. New `CheatService`/`CheatFileParser`/`CheatsViewModel` and tests.

### Fixed
- Automated manifest drift-check (ADR-25) ran on its scheduled 6-hour cycle and opened its first real pull request, re-verifying all 15 `KnownEmulators.json` core entries against the real libretro buildbot channel and re-pinning 15 of 15 (`Sha256`/`ExpectedSizeBytes`/`CapturedAt`) — no structural anomalies found. Confirms the mechanism (ADR-25/ADR-26) works unattended, end-to-end, against a real drift event, not just in a manual dry run. See PR #1 (`chore/manifest-drift-check`), merged 2026-08-04.
- A saved cheat file could be reported as "corrupted" after RetroArch itself rewrote it from its own Cheats menu — RetroArch's own save routine quotes every value, including booleans (`cheat0_enable = "true"`), which `CheatFileParser` didn't tolerate on read. Found and fixed against a real file RetroArch had actually rewritten, not a hypothetical format. See ARCHITECTURE.md → ADR-27.
- The auto-apply-cheats override file was written to the emulator executable's own directory, but RetroArch was looking for it elsewhere — the real config key controlling where RetroArch looks (`rgui_config_directory`) was assumed unset when it actually pointed at a `\config` subfolder for this portable install (a key name guessed from a related field name, `directory_menu_config`, instead of checked). Confirmed via RetroArch's own log output after enabling its file logging for direct evidence, then fixed to read and resolve the real setting. An earlier design for this same toggle (`--appendconfig`) was investigated and abandoned first, after a real leaked setting in the user's actual `retroarch.cfg` proved that mechanism's injected values are never reverted and get permanently saved by RetroArch's own `config_save_on_exit` default. See ARCHITECTURE.md → ADR-27.
- Pointing RetroArch at a game's cheat file (via the `LIBRETRO_CHEATS_DIRECTORY` environment variable) had the same leak class as the `--appendconfig` bug above, initially left as a known consequence rather than fixed — a real stale cheat-folder path lingered in the user's own `cheat_database_path` setting between sessions. Confirmed against RetroArch's source that the env var is read after the override file merges, on every config reload, including RetroArch's own pre-save "restore" step — so it kept re-leaking regardless. Fixed by dropping the env var and routing this setting through the same override file the auto-apply toggle already uses. See ARCHITECTURE.md → ADR-27.

## [0.8.0] - 2026-08-03

A complete catalog-maintenance system, built the same day the recurring drift problem it solves was found and confirmed real three separate times. 256 unit tests in Release, 255 in Debug (`EmuBridge.Tests`); 16 in the new `ManifestDriftCheck.Tests` project (up from 243/242 in `v0.7.1`).

### Added
- Automated manifest drift detection — a scheduled GitHub Action re-verifies `KnownEmulators.json` against the real libretro buildbot every 6 hours and opens a pull request with any fix; merge always stays a manual human decision, never automatic. See ARCHITECTURE.md → ADR-25. 16 new tests
- EmuBridge now fetches its own emulator catalog fresh from GitHub on every startup, so a merged catalog fix reaches a running install without waiting for a new release. Fire-and-forget with a silent fallback to the build's embedded copy on any failure (no internet, slow network, bad response) — the one deliberate exception to EmuBridge's usual never-fail-silently rule, since a background refresh failure has nothing actionable to show the user. See ARCHITECTURE.md → ADR-25. 8 new tests
- A hardcoded allow-list of trusted download hosts (`buildbot.libretro.com`, today the only real one) — closes a real gap the live catalog fetch above opened: the existing exact-hash check alone can't tell a legitimate download apart from one where a compromised manifest supplied both a malicious URL and a matching hash for it. The list lives in EmuBridge's own compiled code, never in the manifest itself, so nothing fetched live can expand what EmuBridge is willing to download and run; adding a new trusted host requires an actual source change and release. See ARCHITECTURE.md → ADR-26. 5 new tests

## [0.7.1] - 2026-08-02

A same-day data-only patch, cut on its own rather than bundled with other work — a third real drift incident left an insignia feature (Auto-Install) effectively broken for multiple platforms, the same urgency standard as the `v0.1.0` `.exe` fix. 243 unit tests in Release, 242 in Debug (unchanged from `v0.7.0` — data-only fix, no test changes needed).

### Fixed
- A third real emulator-catalog drift incident, post-`v0.7.0`: `fceumm`, `snes9x`, and `mgba` had each drifted from their pinned hash (same compressed size, different content — the same pattern as `stella`'s fix in `v0.7.0`). Found by sweeping all 15 catalog entries in one pass instead of investigating the one reported core in isolation, since the report suggested more than one core could be affected. All 3 re-verified with the established double-hash method and re-pinned together. See ARCHITECTURE.md → ADR-11 (2026-08-02 update). With this, all 15 of 15 catalog core entries have now drifted from their pin at least once in one working session — the underlying maintenance gap (`DEVELOPMENT.md` → Known Limitations) has been escalated to next priority in `PLAN.md` → Roadmap, no longer a low-priority "someday" item

## [0.7.0] - 2026-08-02

"Rest of Phase 2": per-game emulator configuration, plus a real download-verification hardening pass found while re-testing it. 243 unit tests in Release, 242 in Debug (up from 209 in `v0.6.0`).

### Added
- Per-game emulator configuration — right-click a tile → "Configure Emulator..." opens a dedicated window to set an executable/argument override for just that one game, without touching the platform's shared default. Falls back to showing the platform default when no override exists yet, so the fields are never blank for an already-configured platform. Removing a game also removes its per-game override, if any. No Auto-Install in this flow — it's for adjusting an already-configured emulator, not installing a new one. See ARCHITECTURE.md → ADR-24. 30 new tests

### Fixed
- Auto-Install could reject a core download as an "unexpected size" even though nothing was wrong — the libretro nightly build channel rebuilds regularly, and a routine rebuild can shift a core's size by a few bytes with no functional change. 11 of the catalog's 15 core entries had already drifted from their pinned size at the time this was found. Re-verified and re-pinned all 11 against the real, current files, and changed the size guard from exact equality to a small (±32 byte) tolerance, calibrated from the real drift observed — the SHA256 hash check, the actual security boundary, is untouched and still exact. See ARCHITECTURE.md → ADR-11 (2026-08-02 update). 4 new tests
- `stella` (Atari 2600) failed Auto-Install a second time, same session, with a hash mismatch rather than a size mismatch — the nightly channel rebuilt it again, landing on the exact same compressed size as the prior pin but genuinely different content. Confirmed real (not a bug in today's tolerance change: size matched exactly, so tolerance never applied; the hash check, always exact, correctly caught a real content difference) and re-pinned. See ARCHITECTURE.md → ADR-11 (2026-08-02 update) and `DEVELOPMENT.md` → Known Limitations for the underlying recurring-maintenance gap this confirms

## [0.6.0] - 2026-08-02

The "Big Picture" group, complete: a streaming-style mode with a maximized window, landscape tiles, a "Try Something New" section, and real box art per view (vertical for the normal grid, horizontal for Big Picture) instead of one orientation stretched to fit both. Three real bugs were found and fixed during interactive testing, each investigated with real evidence before any fix — see ARCHITECTURE.md → ADR-22/ADR-23 for the full design and investigation record. 209 unit tests in Release, 208 in Debug (up from 187 in `v0.5.0`).

### Added
- "Big Picture" mode (toolbar checkbox) — a maximized, larger-tile presentation of the same library, plus a "Try Something New" section surfacing never-played, still-present games alphabetically (no scoring, no randomness — capped at 10, hidden entirely when there are no candidates). Reuses the existing library/launch/context-menu behavior rather than a separate window. No genre/similarity-based recommendations — SteamGridDB has no such data (ADR-19); "Try Something New" resolves a previously-unscoped idea from the Speculative Ideas pool, promoted with the user's explicit confirmation before being folded in. Keyboard/gamepad navigation deferred to Phase Polish — no such input handling exists in EmuBridge today. See ARCHITECTURE.md → ADR-22. 6 new tests
- Vertical/poster-style box art for the normal library grid's portrait tiles, and landscape/streaming-style box art for Big Picture's tiles — each orientation falls back to the other when one isn't available for a game. One combined SteamGridDB request (no new HTTP calls) classifies grids by real aspect ratio; games cached before this change get the missing orientation backfilled automatically on the next scan, without re-downloading the cover they already have. See ARCHITECTURE.md → ADR-23. 8 new tests

### Fixed
- Box art outside Big Picture (the normal library grid) could be visibly stretched non-uniformly. Root cause: `ImageCacheService.ResizeAndSave` forced both target dimensions regardless of the source image's real aspect ratio — always distorting a SteamGridDB horizontal grid (e.g. 460x215) squeezed into the grid's portrait tile shape. A regression made worse by the vertical-art work above: narrowing the horizontal fetch to a real dimensions filter removed the lucky cases where an unfiltered "first result" happened to already be portrait-shaped. Fixed by preserving aspect ratio (Uniform-fit, letterboxed with a transparent background rather than a baked-in color) instead of stretching. A center-crop alternative was evaluated with real numbers and rejected — it would discard roughly a third of the source width, risking cutting off the title text most box art places near the edges. See ARCHITECTURE.md → ADR-23 (Update). 3 new tests
- Cover art could still show stale/wrong content for a game after its cached image file was deleted and rewritten at the same path (e.g. during the investigation above, or a real "Remove from Library" followed by the same game reappearing in a later rescan) — both `MainViewModel` and the XAML binding were provably correct, but WPF's own implicit `string`→`BitmapImage` conversion caches decoded bitmaps by URI at the process level, separate from and invisible to `ImageCacheService`'s file cache, and kept serving the first bitmap it ever decoded for that path. Fixed with an explicit `CachedImagePathConverter` (`BitmapCreateOptions.IgnoreImageCache`) applied to both the normal grid and Big Picture's image bindings. See ARCHITECTURE.md → ADR-23 (Update), `DEVELOPMENT.md` → Image Loading. 4 new tests

## [0.5.0] - 2026-08-02

The "Full library" group, complete: the main library stops feeling like Phase 1's functional-only grid and starts feeling finished. All 4 items are interactively confirmed on the user's real machine, not just unit-tested. 187 unit tests in Release, 186 in Debug (up from 161 in `v0.4.0`).

### Added
- Game detail panel (right-click a tile → "View Details") — shows release year, name, platform, and the existing box art. Scoped down from the original plan after confirming against SteamGridDB's real API that it has no description/blurb or screenshot data at all — shows "Description: not available" explicitly rather than a blank or fabricated field; screenshots are out of scope until a different metadata source is decided on. `BoxArt` gained a nullable `ReleaseYear`, sourced from data `MetadataService` already fetches (no new API call). See ARCHITECTURE.md → ADR-19. 14 new tests
- Favorites (right-click a tile → "Add to Favorites"/"Remove from Favorites") — `Game.IsFavorite`, embedded like `IsMissing`, with a star (★) indicator on the tile's cover when favorited. Split out from "recently played," which shares the same `PLAN.md` bullet but has different mechanics (automatic, not a manual toggle) and ships separately. See ARCHITECTURE.md → ADR-20. 4 new tests
- Recently played, data only — `Game.LastPlayedUtc` is now set whenever a launch actually starts (`LaunchOutcome.Started`), not when the emulator session ends. No UI reads it yet; captured now so no game played before the future "Library" view ships reads as "never played" forever. See ARCHITECTURE.md → ADR-20 (Update). 4 new tests
- Refined "Library" view — sort (Name / Recently Played / Favorites First), a Favorites-only filter, and the release year shown directly on each tile, finally putting the "Full library" group's earlier data (favorites, recently played, release year) to use in the grid itself. No animation — that's Phase Polish's scope. A "hide missing" filter was investigated and deliberately not built: it would block access to the existing "Remove from Library" action for exactly the entries a user would most want hidden. See ARCHITECTURE.md → ADR-21. 6 new tests

## [0.4.0] - 2026-08-02

Offering Auto-Install inline from the launch flow itself — the item originally scoped as `v0.3.0`, retroactively renumbered when two smaller unrelated items (Remove from Library, `.bin` fix) turned out to have taken that slot first. See `PLAN.md` → Roadmap for the full renumbering note. 161 unit tests in Release, 160 in Debug (up from 152 in `v0.3.0`).

### Added
- Offer Auto-Install inline when launching a game whose platform has no emulator configured yet (`LaunchService` → `NoEmulatorConfigured`), not just from Settings — only for a real, recognized platform with a verified catalog entry, never for the unidentified `"unknown"` platform. Reuses `EmulatorInstallerService` and its existing progress-reporting mechanism; on a successful install, the game relaunches automatically instead of requiring a second click. Also closes a related latent bug: `IsBusy` is now shared between the scan and install flows (previously `LaunchGameAsync` had no busy guard at all), so the two can no longer race each other. See ARCHITECTURE.md → ADR-18. 9 new tests

## [0.3.0] - 2026-08-02

Two small items left over from the `v0.2.0` auto-install work, cut as their own release rather than bundled silently into whatever shipped next — a real capability gap (no way to remove a confirmed-gone game) and a real data gap (`.bin` Atari 2600 ROMs undetected), unrelated to each other and to the auto-install mechanism itself. Retroactively version-bumped: both had been sitting committed but unreleased since shortly after `v0.2.0`. 152 unit tests in Release, 151 in Debug (up from 137 in `v0.2.0`).

### Added
- Remove a `Game` from the library (right-click a missing tile → "Remove from Library") — deletes the `Game` row, its `BoxArt` row, and the cached box-art file (skipping the file delete if another `Game` still references the same cached file). Scoped to `IsMissing == true` only, not any game — see ARCHITECTURE.md → ADR-15 for why. Closes the gap first found during the Pokémon Emerald `.sav`-as-Game interactive session. 12 new tests

### Fixed
- Atari 2600 ROMs using the common headerless `.bin` extension weren't detected — `SeedSystems.json`'s `atari2600.Extensions` only had `.a26`. The JSON fix alone wouldn't have reached any already-seeded `bridge.db`, confirmed by reading `LibraryRepository.SeedPlatformsIfEmpty`: seeding is one-shot, gated on the whole `Platform` collection being non-empty. A new `ReconcileSeedPlatformExtensions()` now runs on every database open, unioning each seed platform's extensions into whatever's already stored (additive only — never removes anything) and inserting any seed platform whose row is missing entirely, closing the same gap for future seed changes generally, not just this one extension. See ARCHITECTURE.md → ADR-16. 3 new tests simulate real pre-existing (pre-fix) data, not just a fresh database.

## [0.2.0] - 2026-07-31

Phase 2 emulator auto-install: fully automatic download, verification, and installation of emulators/cores, replacing Phase 1's fully-manual per-system executable configuration. Catalog covers all 15 seed platforms, and every one of them has been interactively confirmed end-to-end — a real Auto-Install click that installed the emulator and launched a game. 137 unit tests in Release, 136 in Debug (up from 83 in `v0.1.0`).

### Added
- `Emulator`/`EmulatorProfile` split, replacing the 1:1 `EmulatorConfig` — one physical emulator install can now back many per-platform launch configs; existing `EmulatorConfig` data migrates automatically on first open (see ARCHITECTURE.md → ADR-11)
- `DownloadVerificationService` — downloads to a staging path, verifies exact size (`Content-Length` pre-check + streaming cutoff, no unbounded downloads even without a reported size) and SHA256 before a file is ever treated as installed; deletes and reports specifically (not generically) on any mismatch
- `KnownEmulators.json` catalog (embedded resource) — RetroArch 1.22.2 entry and all 15 of 15 platform cores independently verified: `nes`→FCEUmm, `snes`→Snes9x, `gb`/`gbc`→SameBoy, `gba`→mGBA, `n64`→Mupen64Plus-Next, `nds`→melonDS DS, `genesis`/`sms`/`gamegear`→Genesis Plus GX, `atari2600`→Stella, `atari7800`→ProSystem, `pcengine`→Beetle PCE, `lynx`→Holani, `wonderswan`→Beetle Cygne. Each downloaded from the official nightly buildbot, hashed by hand with two independent methods (`sha256sum` + `certutil -hashfile`), and the internal DLL filename confirmed by actually listing the archive's contents. **All 15 platforms have since been interactively confirmed too** — a real Auto-Install click that installed the emulator and launched a game, across three separate real sessions (`nes` alone, then 11 more, then the final 3). No seed platform remains data-verified only; see ARCHITECTURE.md → ADR-11/ADR-14 for the full confirmation history
- Release-build-only guard test (`KnownEmulatorsManifestTests`) rejecting any unverified placeholder value in the manifest
- `EmulatorInstallerService` — orchestrates the full auto-install: downloads and extracts (`SharpCompress`, pure managed, no native dependency) a known emulator + core, registers the resulting `Emulator`/`EmulatorProfile`. Exposed via a new "Auto-Install" button in Settings, gated on catalog availability per platform, including reuse-existing-install, two-level failure cleanup, cancellation, and staged progress reporting (see ARCHITECTURE.md → ADR-14)
- `{CorePath}` as a real `ArgumentTemplate` resolver token (alongside `{RomPath}`) — `EmulatorProfile`/`ResolvedEmulatorProfile` gained a nullable `CorePath`; `LaunchService` re-validates it at launch time, new `LaunchOutcome.CoreNotFound`
- Granular byte-progress reporting (`IProgress<long>`) in `DownloadVerificationService.DownloadAndVerifyAsync`, translated into staged status messages by `EmulatorInstallerService`
- 53 new unit tests since `v0.1.0` (`EmulatorInstallerServiceTests`, `KnownEmulatorsManifestTests`, plus additions to `ArgumentTemplateTests`, `LaunchServiceTests`, `LibraryRepositoryTests`, `EmulatorServiceTests`, `DownloadVerificationServiceTests`, `SettingsViewModelTests`) — 136 total in Debug, 137 in Release

### Fixed
- The published self-contained single-file `.exe` didn't open at all — `PublishSingleFile` bundles managed assemblies but not WPF's native interop DLLs by default, so the app crashed with `System.DllNotFoundException` before a single line of application code ran, silently (no dialog, since it happens before any exception handler is wired). `EmuBridge.csproj` now sets `IncludeNativeLibrariesForSelfExtract=true`. See ARCHITECTURE.md → ADR-12. **The `v0.1.0` release asset on GitHub was replaced in place (same tag, same commit) with a working build** — the tagged source is unchanged; only the uploaded binary was corrected, since it had shipped incomplete from the start.
- `RomScannerService` was scanning emulator companion files (`.sav`/`.srm` save files, `.state`/`.ss` save states created by mGBA/RetroArch next to a ROM) as if they were ROMs, landing them on the `unknown` platform as broken, unlaunchable library entries alongside the real ROM. Root cause: the `unknown` fallback (ADR-6) was designed for "extension not yet recognized, might be a ROM for an unsupported system," never for files confidently known not to be ROMs at all — no distinction existed between the two. Known companion extensions (confirmed against RetroArch's and mGBA's actual behavior, not assumed) are now excluded entirely before the `unknown` fallback is reached; a genuinely unrecognized extension is unaffected. Pre-existing bogus entries from before this fix self-heal via the existing mark-missing sweep on the next rescan, no migration needed. See ARCHITECTURE.md → ADR-13.
- The first real, interactive "Auto-Install" click failed with `ExecutableNotFoundAfterExtraction` — `KnownEmulator.ExecutableRelativePath` for RetroArch was `"retroarch.exe"`, sourced from third-party documentation claiming the portable `.7z` extracts flat. It doesn't: the real archive nests everything under `RetroArch-Win64/`. Corrected to `"RetroArch-Win64\\retroarch.exe"`, confirmed by opening the actual downloaded-and-verified `.7z` (recovered from `%LocalAppData%\EmuBridge\Downloads\`, left behind by the failed attempt) with the same `SharpCompress` code path EmuBridge itself uses and listing its real entries. `EmulatorInstallerServiceTests`' shared fixture now mirrors this real nested structure instead of the flat one that let the wrong value pass every automated test. See ARCHITECTURE.md → ADR-11 (2026-08-03 update).
- After a successful "Auto-Install" (or a manual "Save Emulator Config"), `SettingsViewModel`'s Executable/Argument Template fields kept showing stale (pre-change) data until Settings was closed and reopened — the underlying `Emulator`/`EmulatorProfile` data was correct the whole time (confirmed: the game launched fine), but `LoadPlatformsAsync` rebuilds `Platforms` with brand-new `PlatformConfigItem` instances every call, and `SelectedPlatform` was never re-pointed at the matching new one, so `OnSelectedPlatformChanged` (the only thing that refreshes those fields) never re-fired. Both commands now explicitly reselect the matching platform after reloading. Caught mid-fix by the regression test itself: the first attempt reselected *before* setting the final "Installed."/"Saved." status message, and `OnSelectedPlatformChanged`'s own status-clearing side effect wiped it out immediately — reordered so the final message is set last.

### Known Issues
- `RomScannerService` doesn't detect Atari 2600 ROMs distributed with the common headerless `.bin` extension — only `.a26` is in `SeedSystems.json`'s `atari2600.Extensions`. Not a scanner bug (case-insensitive matching confirmed working); a data gap, not yet fixed pending a scoping decision since `.bin` is ambiguous across other systems' disc images. See DEVELOPMENT.md → Known Limitations. **(Resolved in v0.3.0.)**
- No UI action to remove a `Game` from the library once it's confirmed gone for good (only mark-missing exists) — see DEVELOPMENT.md → Known Limitations, `PLAN.md` → Phase 2 backlog. **(Resolved in v0.3.0.)**

## [0.1.0] - 2026-07-31

Phase 1 MVP: a functional, minimal library manager covering all 9 FR milestones — scan, catalog, fetch box art, configure emulators, launch games — backed by 3 core services and a composition-root-wired WPF UI. 83 unit tests.

### Added
- Initial project scaffold from project-template
- `LibraryRepository` (LiteDB-backed persistence: platforms, games, scan folders) and `RomScannerService` (recursive folder scan, extension→platform detection, missing-ROM tracking on rescan) — covered by 15 unit tests
- Built-in seed data for 15 common cartridge/handheld platforms (`Resources/SeedSystems.json`)
- `MetadataService` (SteamGridDB search + box art lookup, batch fetch with terminal/retryable status tracking) and `ImageCacheService` (download, WPF-native decode-time resize, on-disk caching keyed by URL + target size) — covered by 19 unit tests
- `SettingsService` for DPAPI-encrypted local storage of the user's SteamGridDB API key
- `BoxArt` persistence (LiteDB) tracking per-game box art status
- `EmulatorService` (validates and persists `EmulatorConfig`: executable exists, `{RomPath}` token present, platform reference valid) and `LaunchService` (launches a `Game` through its configured emulator, re-validates ROM/executable existence at launch time, exposes process exit as a `Task`) — covered by 20 unit tests
- `ArgumentTemplate` shared `{Token}` resolver (single-pass expansion, context-aware quoting), used by both new services
- DI composition root in `App.xaml.cs` (all services registered, lifetimes individually justified — see ARCHITECTURE.md → ADR-10), replacing the default `StartupUri` mechanism
- `MainWindow`/`MainViewModel`: functional cover grid, empty-state guidance, scan/box-art progress with cancel, add-folder/rescan/settings toolbar, click-to-launch
- `SettingsWindow`/`SettingsViewModel`: per-platform emulator configuration, SteamGridDB API key entry
- `IMessageBoxService`/`IFolderPickerService`/`IFilePickerService` testable dialog wrappers, covered by 27 new ViewModel/dialog unit tests (83 total)
- `DispatcherUnhandledException` global handler in `App.xaml.cs` — logs, shows the user a Yes/No dialog, shuts down cleanly on No

### Known Issues
- The UI has not yet been manually run/observed in a real window from this environment

---

*This changelog follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)*
