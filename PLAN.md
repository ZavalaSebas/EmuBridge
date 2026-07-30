# Bridge - Project Plan

> **Status:** Pre-implementation — Phase 1 (MVP) not started
>
> **Last updated:** 2026-07-30

## Project Overview

Bridge is a Windows retro emulation launcher/frontend that eliminates the manual setup friction of managing ROMs, box art, and emulator configuration by hand. The user points Bridge at their ROM folders; Bridge detects the system for each file, fetches box art from SteamGridDB, and launches each game through the correct emulator with the correct arguments — without the user needing to know which emulator or core belongs to which system. Bridge manages ROMs and emulators the user already owns; it does not include, facilitate, or link to ROM acquisition, keeping it in the same legitimate category as RetroArch, EmulationStation, and Playnite.

## Current State

### Phase 1 (MVP) — Not Started
Goal: detect → show → play, using emulators the user has already installed manually.

- Scan user-selected ROM folders
- Detect system/console by file extension (extension to system mapping)
- Fetch box art from SteamGridDB (user-provided API key)
- Local image cache, resized to the exact display resolution used in the UI — never scale large source images at render time
- Manual emulator path configuration per system (user points to the .exe for each system)
- Launch a ROM with the correct emulator and arguments
- One functional view (simple cover grid) — no elaborate animations yet, functional only
- Local library persistence (which ROMs, which system, which emulator assigned)

Explicitly out of scope for this phase: multiple views, video previews, cheats/mods, social features, RetroAchievements, automatic emulator download, recommendations, editable per-game emulator settings.

### Phase 2 (Should Have) — Not Started
Once the MVP works end-to-end.

- Game detail panel: short blurb/preview text, description, release year, console/system, additional screenshots, thumbnails (distinct from the main box art)
- Favorites / recently played
- "Library" view (Playnite-style cover grid, refined from the Phase 1 functional version)
- "Big Picture" / streaming-style view with a recommended-games section
- Polished transition animations (this is where the EmulationStation inspiration gets invested in)
- Automatic emulator detection/download for known systems (e.g. RetroArch cores, PCSX2) — replaces the fully manual Phase 1 configuration
- Per-game emulator configuration editable directly from the launcher (not just per-system defaults)

### Phase 3 (Could Have) — Not Started
Once the base is solid and stable.

- RetroAchievements integration
- Cheats/mods management per game
- Video previews / trailers
- Recommendation engine ("similar games")
- Additional views, theme customization

**Won't Have (for now, not permanently ruled out):** any ROM discovery/acquisition feature; social features.

---

## Problem Statement

Setting up a retro emulation library today means manually sorting ROMs by system, hunting down and installing the right emulator or core for each console, learning each emulator's command-line syntax to launch games from a frontend, and separately tracking down box art and metadata for every game. This is enough friction that many users either give up on a unified library or spend hours on setup before playing a single game. Existing frontends either require this manual configuration up front (most libretro-based frontends) or are scoped to a broader, non-emulation use case (Playnite, built primarily around Steam/Epic/etc. integration).

---

## Solution Overview

Bridge automates the parts of this setup that don't need to be manual. Extension-to-system detection, box art lookup, and image caching happen automatically once the user points Bridge at their ROM folders. Emulator configuration remains manual in Phase 1 (one emulator path per system) but is designed as data-driven configuration from the start — both the extension-to-system mapping and the emulator launch-argument templates — so Phase 2 can add automatic emulator detection/download without a redesign. The result is a shorter path from "open Bridge for the first time" to "play a ROM" than existing tools offer, without expanding scope into ROM acquisition or non-emulation library management.

---

## Technical Decisions

| Aspect | Decision |
|--------|----------|
| Runtime | .NET 10, self-contained single-file publish (win-x64) |
| UI Framework | WPF + WPF-UI (lepo.co) for Mica/Fluent styling |
| Architecture | MVVM (CommunityToolkit.Mvvm) + DI (Microsoft.Extensions.DependencyInjection) |
| External Metadata API | SteamGridDB (box art) — API key-handling approach pending, see Open Decisions #4 below |
| Packaging | PublishSingleFile, self-contained |

---

## Open Decisions

These reshape the architecture if resolved late — closed in Phase 0, before implementation starts. Not resolved here; tracked as pending on purpose.

| # | Decision | Status | Blocks |
|---|---|---|---|
| 1 | Storage: SQLite vs. flat JSON vs. LiteDB | Pending — leaning SQLite given potential ROM volume plus metadata | `LibraryRepository` |
| 2 | Extension→system mapping: hardcoded in config or data-driven (editable JSON)? | Pending — leaning data-driven, per the extensibility non-functional requirement | `RomScannerService` |
| 3 | Emulator argument template format (e.g. `{ROM_PATH}`, `{FULLSCREEN}`) | Pending | `LaunchService` |
| 4 | SteamGridDB API key handling: user-supplied vs. embedded | Pending — leaning user-supplied for Phase 1, revisit in Phase 2 | `MetadataService` |

---

## Scope: Current vs Future

### Current Version (0.1.0) — In Progress
Version 0.1.0 targets the full Phase 1 (MVP) scope listed above: detect → show → play. No feature from Phase 2 or Phase 3 is in scope for this version. `<Version>0.1.0</Version>` is the value to use once `Bridge.csproj` is created (see DEVELOPMENT.md → Version Management) — keep it consistent with this document, README.md, and docs/index.html.

### Future Versions — Backlog
Phase 2 and Phase 3 scope (see above) are explicitly deferred and tracked as backlog — not started, not scheduled. The "Won't Have" list (any ROM discovery/acquisition feature; social features) is out of scope indefinitely, not just for this version.

---

## Project Structure

```
Bridge/
├── Bridge/                 # Main WPF project (not yet created)
│   ├── Models/
│   ├── Services/
│   │   ├── RomScannerService
│   │   ├── MetadataService
│   │   ├── ImageCacheService
│   │   ├── EmulatorService
│   │   ├── LaunchService
│   │   └── LibraryRepository
│   ├── ViewModels/
│   ├── Views/
│   └── Config.cs
├── Bridge.Tests/            # (not yet created)
├── docs/
├── README.md
├── PLAN.md
├── DEVELOPMENT.md
├── ARCHITECTURE.md
└── Bridge.slnx              # (not yet created)
```

---

## Development Phases

### Phase 1: MVP
> **Objective:** Detect → show → play, using manually-configured emulators.

**Milestones:**

| Milestone | Description | Status |
|-----------|-------------|--------|
| FR-01 | User can add one or more root ROM folders | Not started |
| FR-02 | System recursively scans and detects valid ROM files | Not started |
| FR-03 | Each detected ROM is automatically associated with a known system/console | Not started |
| FR-04 | Each detected ROM looks up its box art on SteamGridDB | Not started |
| FR-05 | Box art is cached locally, resized to the exact size it's displayed at | Not started |
| FR-06 | User configures, per system, which emulator (.exe) to use | Not started |
| FR-07 | Selecting and confirming a ROM launches the emulator with correct arguments | Not started |
| FR-08 | The library persists between sessions (no full re-scan on every launch) | Not started |
| FR-09 | User can trigger a manual re-scan | Not started |

**Deliverables:**

- Functional cover grid view (Phase 1 — no elaborate animations)
- Working scan → detect → fetch art → cache → launch pipeline, end-to-end, for at least one system

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Storage choice undecided before `LibraryRepository` work starts | Medium | High — wrong choice forces a rewrite | Resolve Open Decision #1 in Phase 0 before implementation |
| Emulator argument syntax varies widely per emulator | High | Medium — `LaunchService` breaks for edge-case emulators | Resolve Open Decision #3 (argument template schema) before implementing `LaunchService` |
| SteamGridDB rate limits or API key exposure | Medium | Medium — box art fetch fails or key leaks | Resolve Open Decision #4; add rate-limit handling in `MetadataService` |
| UI jank with large libraries (thousands of ROMs) if virtualization is skipped | Medium | High — core UX requirement | Use `VirtualizingStackPanel` (or equivalent) from the first cover grid implementation, not as a later optimization |
| WPF Storyboards don't reach EmulationStation-level animation fluidity | Low (Phase 2/3 concern) | Low | Investigate Windows.UI.Composition interop as a targeted enhancement if it becomes a real limitation |

---

## Dependencies

| Dependency | Version | Purpose | Notes |
|-----------|---------|---------|-------|
| WPF-UI (lepo.co) | TBD | Mica/Fluent theming | Validated in a prior project |
| CommunityToolkit.Mvvm | TBD | MVVM (ObservableObject, RelayCommand) | Standard for this template |
| Microsoft.Extensions.DependencyInjection | TBD | DI container | Standard for this template |
| Microsoft.Extensions.Logging | TBD | `ILogger<T>` logging | Standard for this template |
| Storage library (SQLite / LiteDB — TBD) | TBD | Library persistence | Pending Open Decision #1 |
| SteamGridDB API | N/A (external HTTP) | Box art metadata | Requires an API key (handling approach pending, see Open Decisions #4); rate-limit handling required |

---

## Success Criteria

- A user can point Bridge at a ROM folder and, without touching any config file, see their games in a cover grid with box art after one scan.
- Selecting a game and confirming launches it through the correct emulator, using arguments defined in a configurable template — not hardcoded per emulator.
- Re-opening Bridge does not re-scan the full library or re-fetch already-cached box art.
- The cover grid stays responsive with a library in the thousands of ROMs.

---

## Timeline

No fixed dates yet. Immediate next steps, in order:

1. Create the Bridge repo from `project-template/`, following `project-template/NEW_PROJECT_CHECKLIST.md` (this scaffold — that checklist lives in `project-template/`, not in this repo)
2. Resolve the 4 Open Decisions above in Phase 0, before writing the first service
3. Start with `RomScannerService` + minimal persistence — it's the foundation everything else depends on

---

*This document is a living plan. Update as the project evolves.*
