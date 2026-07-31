# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial project scaffold from project-template
- `LibraryRepository` (LiteDB-backed persistence: platforms, games, scan folders) and `RomScannerService` (recursive folder scan, extension→platform detection, missing-ROM tracking on rescan) — no UI yet, covered by 15 unit tests
- Built-in seed data for 15 common cartridge/handheld platforms (`Resources/SeedSystems.json`)
- `MetadataService` (SteamGridDB search + box art lookup, batch fetch with terminal/retryable status tracking) and `ImageCacheService` (download, WPF-native decode-time resize, on-disk caching keyed by URL + target size) — no UI yet, covered by 19 unit tests
- `SettingsService` for DPAPI-encrypted local storage of the user's SteamGridDB API key
- `BoxArt` persistence (LiteDB) tracking per-game box art status

---

*This changelog follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)*
