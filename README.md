<div align="center">

# Bridge

### Point it at your ROMs. Bridge does the rest.

[![License: GPL v3](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white&labelColor=1a1a2e)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows-00a4ef?style=flat-square&logo=windows&logoColor=white&labelColor=1a1a2e)](https://github.com/ZavalaSebas/Bridge)
[![Version](https://img.shields.io/badge/Version-0.10.0-57F287?style=flat-square&labelColor=1a1a2e)](https://github.com/ZavalaSebas/Bridge/releases)

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

Under the hood, Bridge is built around a small set of focused services. A `RomScannerService` recursively scans your configured folders and detects valid ROM files, mapping each one to a known system by file extension. For every detected game, a `MetadataService` looks up box art on SteamGridDB, and an `ImageCacheService` downloads and resizes it locally to the exact resolution it will be displayed at — never scaling large source images at render time — so the library view stays smooth even with thousands of covers. A `TheGamesDbService` separately fetches a real description and screenshots for a game's detail view, on demand, from TheGamesDB.

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
- A free TheGamesDB API key for the game detail panel's description and screenshots (optional — the panel still shows cover art, name, platform, and release year without one); same DPAPI-encrypted storage, entered in Settings
- Emulators for the systems you want to play — Bridge can auto-install RetroArch + the right core for any of its 15 supported platforms with one click (Settings → Auto-Install), or you can point it at an emulator you already have

---

## Features

> Bridge is functional and shipping — v0.10.0. Phase 1 (scan ROMs, fetch box art, configure and launch emulators) is complete. Phase 2 is complete too. `v0.3.0` shipped removing a confirmed-gone game from the library and a ROM-detection fix; `v0.4.0` added offering Auto-Install inline right from the launch flow; `v0.5.0` completed the "Full library" group — a refined library view with sorting and filtering, favorites, recently played, and a game detail panel; `v0.6.0` completed the "Big Picture" group — a streaming-style mode with real box art per view; `v0.7.0` added per-game emulator configuration and hardened the download-verification guard against the libretro nightly channel's real rebuild frequency; `v0.7.1` was a same-day patch re-verifying the entire emulator catalog against that same channel; `v0.8.0` closed the loop with a full catalog-maintenance system — automated drift detection with a human-reviewed pull request, Bridge fetching its own catalog fresh on every startup, and a hardcoded allow-list of trusted download hosts as a second line of defense; `v0.9.0` was the first item of Phase 3 — cheats management per game for RetroArch-based platforms; `v0.10.0` is Phase 3's second item — TheGamesDB as a second, additive metadata source for the game detail panel's description and screenshots.

- Scan your ROM folders and automatically detect which system each game belongs to
- Fetch box art automatically from SteamGridDB
- Local image cache, resized to the exact size used on screen — no runtime scaling
- Automatically download, install, and configure the right emulator for any of 15 supported systems — one click, no manual setup (or point Bridge at an emulator you already have)
- Launching a game with no emulator configured yet offers to install one automatically, right there — not just from Settings
- Launch games with the correct emulator and arguments, automatically
- Set a different emulator/arguments for one specific game without touching the platform's shared default (right-click a tile → "Configure Emulator...")
- Mark games as favorites and see when you last played each one, right on the cover grid
- Sort the library by name, recently played, or favorites first, and filter to favorites only
- A game detail panel (right-click → "View Details") with release year, platform, cover art, and — for games TheGamesDB has catalogued — a real description and screenshots
- Remove a game from the library once it's confirmed gone for good (right-click a missing game → "Remove from Library")
- "Big Picture" mode — a maximized, streaming-style view with a "Try Something New" section surfacing games you haven't played yet
- Real box art per view — vertical/poster-style covers in the normal grid, landscape covers in Big Picture, matching each view's real tile shape instead of stretching one orientation to fit both
- Library persists between sessions — no full re-scan on every launch
- The built-in emulator catalog stays current on its own — a scheduled check re-verifies it against the real libretro build channel and opens a human-reviewed pull request if anything drifted, and Bridge fetches the latest verified catalog on every startup so a fix reaches you without waiting for a new release
- Cheats for RetroArch-based platforms (right-click a game → "Cheats...") — fetched on demand from the same public cheat database RetroArch itself uses, with an optional "Auto-apply cheats on launch" toggle so enabled cheats apply automatically without a manual step in RetroArch's own menu
- Real game descriptions and screenshots in the detail panel, fetched on demand from TheGamesDB — SteamGridDB keeps handling box art, TheGamesDB fills the gap it doesn't cover

**Known limitations** (see [DEVELOPMENT.md](DEVELOPMENT.md#known-limitations) for full detail):
- Removing a game from the library only works for entries already marked "missing" — there's no way to remove a game that's still present but you no longer want tracked
- TheGamesDB doesn't catalog ROM hacks — a game sourced from the ROM-hacking community will always show "Description: not available" in the detail panel, confirmed via a real coverage test, not a bug
- A core picker UI, the rest of Phase 3 (achievements, mods, video previews, recommendations, disc-based systems), and Phase Polish (animations, theming, welcome sentinel, auto-updater, sponsor/credits, general UI pass) haven't been started

---

## Architecture

Bridge is organized around eleven focused services — scanning (`RomScannerService`), box-art lookup (`MetadataService`), description/screenshot lookup (`TheGamesDbService`), image caching (`ImageCacheService`), settings (`SettingsService`), emulator configuration (`EmulatorService`), launching (`LaunchService`), verified downloads (`DownloadVerificationService`), automatic emulator installation (`EmulatorInstallerService`), catalog freshness (`ManifestUpdateService`), and cheats (`CheatService`) — following a standard Services/Models/ViewModels/Views separation. See [ARCHITECTURE.md](ARCHITECTURE.md) for the full breakdown and the reasoning behind each decision.

---

## Development

See [DEVELOPMENT.md](DEVELOPMENT.md) for the full project guide, architecture, and workflow rules.

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

---

## Acknowledgments

- [SteamGridDB](https://www.steamgriddb.com/) — box art and metadata
- [TheGamesDB](https://thegamesdb.net/) — game descriptions and screenshots
- [libretro/libretro-database](https://github.com/libretro/libretro-database) — RetroArch cheat files (CC BY-SA 4.0)
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
