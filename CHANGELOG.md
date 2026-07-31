# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
