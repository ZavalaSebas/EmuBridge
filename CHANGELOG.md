# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `Emulator`/`EmulatorProfile` split, replacing the 1:1 `EmulatorConfig` — one physical emulator install can now back many per-platform launch configs; existing `EmulatorConfig` data migrates automatically on first open (see ARCHITECTURE.md → ADR-11)
- `DownloadVerificationService` — downloads to a staging path, verifies exact size (`Content-Length` pre-check + streaming cutoff, no unbounded downloads even without a reported size) and SHA256 before a file is ever treated as installed; deletes and reports specifically (not generically) on any mismatch
- `KnownEmulators.json` catalog (embedded resource) — RetroArch 1.22.2 entry and 1 of 15 platform cores (`nes` → FCEUmm) independently verified: downloaded from the official source, hashed by hand with two independent methods (`sha256sum` + `certutil -hashfile`), and — for the core — the internal DLL filename confirmed by actually extracting the archive
- Release-build-only guard test (`KnownEmulatorsManifestTests`) rejecting any unverified placeholder value in the manifest
- 14 new unit tests (`EmulatorServiceTests`, `LibraryRepositoryTests`, `DownloadVerificationServiceTests`, `KnownEmulatorsManifestTests`) — 97 total, passing in both Debug and Release

### Fixed
- The published self-contained single-file `.exe` didn't open at all — `PublishSingleFile` bundles managed assemblies but not WPF's native interop DLLs by default, so the app crashed with `System.DllNotFoundException` before a single line of application code ran, silently (no dialog, since it happens before any exception handler is wired). `Bridge.csproj` now sets `IncludeNativeLibrariesForSelfExtract=true`. See ARCHITECTURE.md → ADR-12. **The `v0.1.0` release asset on GitHub was replaced in place (same tag, same commit) with a working build** — the tagged source is unchanged; only the uploaded binary was corrected, since it had shipped incomplete from the start.
- `RomScannerService` was scanning emulator companion files (`.sav`/`.srm` save files, `.state`/`.ss` save states created by mGBA/RetroArch next to a ROM) as if they were ROMs, landing them on the `unknown` platform as broken, unlaunchable library entries alongside the real ROM. Root cause: the `unknown` fallback (ADR-6) was designed for "extension not yet recognized, might be a ROM for an unsupported system," never for files confidently known not to be ROMs at all — no distinction existed between the two. Known companion extensions (confirmed against RetroArch's and mGBA's actual behavior, not assumed) are now excluded entirely before the `unknown` fallback is reached; a genuinely unrecognized extension is unaffected. Pre-existing bogus entries from before this fix self-heal via the existing mark-missing sweep on the next rescan, no migration needed. See ARCHITECTURE.md → ADR-13.

### Known Issues
- 14 of 15 seed platforms have no `KnownEmulatorCore` entry yet — Phase 2's auto-detect/download isn't functionally usable end-to-end (see DEVELOPMENT.md → Known Limitations)
- Archive-extraction/install orchestration that would consume `DownloadVerificationService`'s output isn't built yet

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
