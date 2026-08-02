<div align="center">

# Bridge

### Point it at your ROMs. Bridge does the rest.

[![License: GPL v3](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white&labelColor=1a1a2e)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows-00a4ef?style=flat-square&logo=windows&logoColor=white&labelColor=1a1a2e)](https://github.com/ZavalaSebas/Bridge)
[![Version](https://img.shields.io/badge/Version-0.7.0-57F287?style=flat-square&labelColor=1a1a2e)](https://github.com/ZavalaSebas/Bridge/releases)

A retro emulation launcher that detects your ROMs, fetches box art, and launches everything — zero manual configuration.

[Get Started](#get-started) · [Features](#features) · [How It Works](#how-it-works) · [Build from Source](#build-from-source)

</div>

---

## What is Bridge?

Bridge is an all-in-one retro emulation launcher built around a single idea: point it at your ROM folders and it takes care of the rest. It scans your library, detects which system each game belongs to, fetches box art automatically, and launches everything through the right emulator — no manual configuration, no hunting for cover art, no memorizing which core goes with which extension. Architecturally, Bridge takes cues from Playnite's centralized library and metadata model, trimmed down to emulation only; visually, it aims for EmulationStation's fluid, console-style presentation.

> **Disclaimer:** Bridge manages ROMs and emulators you already own. It does not include, facilitate, or link to ROM downloads.

---

## Screenshot

<div align="center">

> Screenshot coming soon

</div>

---

## How It Works

Under the hood, Bridge is built around a small set of focused services. A `RomScannerService` recursively scans your configured folders and detects valid ROM files, mapping each one to a known system by file extension. For every detected game, a `MetadataService` looks up box art on SteamGridDB, and an `ImageCacheService` downloads and resizes it locally to the exact resolution it will be displayed at — never scaling large source images at render time — so the library view stays smooth even with thousands of covers.

Launching a game goes through `EmulatorService` and `LaunchService`: the system-to-emulator mapping and the command-line argument template for each emulator are data-driven rather than hardcoded, so adding a new system or emulator doesn't require touching core code. Everything detected, cached, and configured is persisted locally by `LibraryRepository`, so Bridge doesn't need to re-scan your entire library on every launch.

---

## Get Started

**Download a Release**

Grab the latest `Bridge.exe` from [Releases](https://github.com/ZavalaSebas/Bridge/releases). Self-contained — no .NET required. Just run it.

**Build from Source**

```bash
git clone https://github.com/ZavalaSebas/Bridge.git
cd Bridge
dotnet publish Bridge -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## Requirements

- Windows 10/11
- A free SteamGridDB API key for box art (optional — games still appear without one, shown with a "No Cover" placeholder); user-supplied, stored locally with DPAPI encryption, entered in Settings
- Emulators for the systems you want to play — Bridge can auto-install RetroArch + the right core for any of its 15 supported platforms with one click (Settings → Auto-Install), or you can point it at an emulator you already have

---

## Features

> Bridge is functional and shipping — v0.7.0. Phase 1 (scan ROMs, fetch box art, configure and launch emulators) is complete. Phase 2 is effectively complete too — the only remaining piece is a core picker UI, deliberately deferred until a platform actually has more than one known-good core to choose between. `v0.3.0` shipped removing a confirmed-gone game from the library and a ROM-detection fix; `v0.4.0` added offering Auto-Install inline right from the launch flow; `v0.5.0` completed the "Full library" group — a refined library view with sorting and filtering, favorites, recently played, and a game detail panel; `v0.6.0` completed the "Big Picture" group — a streaming-style mode with real box art per view; `v0.7.0` adds per-game emulator configuration and hardens the download-verification guard against the libretro nightly channel's real rebuild frequency.

- Scan your ROM folders and automatically detect which system each game belongs to
- Fetch box art automatically from SteamGridDB
- Local image cache, resized to the exact size used on screen — no runtime scaling
- Automatically download, install, and configure the right emulator for any of 15 supported systems — one click, no manual setup (or point Bridge at an emulator you already have)
- Launching a game with no emulator configured yet offers to install one automatically, right there — not just from Settings
- Launch games with the correct emulator and arguments, automatically
- Set a different emulator/arguments for one specific game without touching the platform's shared default (right-click a tile → "Configure Emulator...")
- Mark games as favorites and see when you last played each one, right on the cover grid
- Sort the library by name, recently played, or favorites first, and filter to favorites only
- A game detail panel (right-click → "View Details") with release year, platform, and cover art
- Remove a game from the library once it's confirmed gone for good (right-click a missing game → "Remove from Library")
- "Big Picture" mode — a maximized, streaming-style view with a "Try Something New" section surfacing games you haven't played yet
- Real box art per view — vertical/poster-style covers in the normal grid, landscape covers in Big Picture, matching each view's real tile shape instead of stretching one orientation to fit both
- Library persists between sessions — no full re-scan on every launch

**Known limitations** (see [DEVELOPMENT.md](DEVELOPMENT.md#known-limitations) for full detail):
- Removing a game from the library only works for entries already marked "missing" — there's no way to remove a game that's still present but you no longer want tracked
- The game detail panel has no description/blurb — SteamGridDB (Bridge's only metadata source today) doesn't provide one
- The built-in emulator catalog needs occasional manual maintenance to stay in sync with the libretro nightly build channel, which rebuilds frequently — Bridge's download verification always catches and rejects a stale entry rather than installing something unverified, so this affects availability (a real install can fail until the catalog is updated), never safety
- A core picker UI, Phase 3 (achievements, cheats, video previews, recommendations), and Phase Polish (animations, theming, welcome sentinel, auto-updater, sponsor/credits, general UI pass) haven't been started

---

## Architecture

Bridge is organized around eight focused services — scanning (`RomScannerService`), metadata lookup (`MetadataService`), image caching (`ImageCacheService`), settings (`SettingsService`), emulator configuration (`EmulatorService`), launching (`LaunchService`), verified downloads (`DownloadVerificationService`), and automatic emulator installation (`EmulatorInstallerService`) — following a standard Services/Models/ViewModels/Views separation. See [ARCHITECTURE.md](ARCHITECTURE.md) for the full breakdown and the reasoning behind each decision.

---

## Development

See [DEVELOPMENT.md](DEVELOPMENT.md) for the full project guide, architecture, and workflow rules.

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

---

## Acknowledgments

- [SteamGridDB](https://www.steamgriddb.com/) — box art and metadata
- [Playnite](https://playnite.link/) — architectural inspiration for the library/metadata model
- [EmulationStation](https://emulationstation.org/) — visual and animation inspiration
- [Emutastic](https://github.com/codingncaffeine/Emutastic) — architecture reference (WPF/.NET, libretro cores)

---

## Sponsor

If you find Bridge useful, consider supporting the project:

[Ko-fi](https://ko-fi.com/sebastianzavala82573) · [GitHub Sponsors](https://github.com/sponsors/ZavalaSebas)

---

<div align="center">

Made with care by [ZavalaSebas](https://github.com/ZavalaSebas)

</div>
