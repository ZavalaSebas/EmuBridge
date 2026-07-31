# Bridge - Project Plan

> **Status:** Phase 1 (MVP) — backend services + composition root (with global exception handling) + minimal UI all implemented and tested (9/9 FRs wired end-to-end in code, 83 unit tests, 10 ADRs). **Not yet manually run/verified in a real window** — see `DEVELOPMENT.md` → Current Status. See `## Timeline` below for the exact handoff state.
>
> **Last updated:** 2026-07-31

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

These reshape the architecture if resolved late, so they were closed in Phase 0, before implementation started, rather than mid-way through building the services they block. All 5 are now resolved — see the corresponding ADRs in `ARCHITECTURE.md` (ADR-1 through ADR-5) for the full context/consequences/alternatives behind each. Kept here, not deleted, as the record of what was decided and why at this specific point in the project.

| # | Decision | Status | Blocks |
|---|---|---|---|
| 1 | Storage: SQLite vs. flat JSON vs. LiteDB | **Resolved — LiteDB** (current 5.x, one `bridge.db` file with 2–3 named collections — not Playnite's one-file-per-collection split, and not their old LiteDB 4 pin). Decisive factor: Bridge's NFR requires a single-file self-contained `.exe`. Per official docs ([Create a single file for application deployment - .NET \| Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), "Native libraries"), only managed DLLs are bundled by default — SQLite's native interop (`Microsoft.Data.Sqlite` → SQLitePCLRaw) would ship as a separate file next to the `.exe` unless `IncludeNativeLibrariesForSelfExtract=true` is set, and even then it's extracted to `%TEMP%/.net` on every startup before the app can use it. LiteDB is pure managed code — no native interop, no extra packaging step, genuinely one file. Secondary factors: Bridge has ~3 entities (games, systems, emulator configs) vs. Playnite's 17 taxonomy collections — LiteDB's document model fits simple key/system lookups better than normalizing into relational tables; box art already lives on disk as separate files regardless of engine choice, so that consideration is neutral, not a differentiator. SQLite would still win if Phase 3's recommendation engine ever needs real relational queries, but `LibraryRepository` already encapsulates storage, so that stays a contained swap if it's ever needed, not a rewrite. | `LibraryRepository` |
| 2 | Extension→platform mapping: hardcoded in config or data-driven (editable JSON)? | **Resolved — data-driven**, stored in LiteDB (not hardcoded in C#), scoped to Phase 1's actual need (one emulator per platform, manually configured) rather than Playnite's full profile/catalog system. Entity named `Platform`, not `System` — `System` collides with the C# `System.*` namespace; Playnite hit and solved the same naming problem the same way. Two collections: `Platform { Id: string (slug, e.g. "nes"; reserved value "unknown"), Name: string, Extensions: string[] }` and `EmulatorConfig { Id: Guid, PlatformId: string (FK → Platform.Id, unique index — encodes "one emulator per platform" as a real Phase 1 constraint), Name: string (denormalized on purpose, so logs/error messages are readable without a join), ExecutablePath: string, ArgumentTemplate: string (syntax defined by Decision #3) }`. **Unknown-platform fallback (FR-03):** a reserved `Platform` sentinel (`Id: "unknown"`, `Extensions: []` so it's never matched directly, only ever assigned as the explicit fallback) is seeded once at first run, referenced via a `Config.cs` constant. `RomScannerService` assigns it when no extension matches, so `Game.PlatformId` stays non-nullable everywhere — no scattered null-handling, and unmatched ROMs still show up in the library grid (transparent to the user) instead of silently vanishing from scan results; this also reuses the same "no emulator configured yet" UI state that legitimately-known-but-unconfigured platforms already need in Phase 1, rather than a separate case. **Why this shape doesn't block Phase 2:** dropping the unique index on `PlatformId` is the entire migration needed to go from 1:1 to many-emulators-per-platform; splitting `EmulatorConfig` into a separate `Emulator` (physical install, reusable across platforms) + `EmulatorProfile` (platform + argument template, matching Playnite's `Emulator 1—* Profile *—* Platform` shape) is a mechanical one-time data migration, not a contract change — as long as `EmulatorService` stays the sole consumer of this data, `RomScannerService`/`LaunchService` keep asking the same question ("resolved launch config for platform X") across both phases. **Seed data and the LibraryRepository/RomScannerService implementation built on this schema are detailed in ARCHITECTURE.md → ADR-6 and ADR-7.** | `RomScannerService`, `EmulatorService` |
| 3 | Emulator argument template format (e.g. `{ROM_PATH}`, `{FULLSCREEN}`) | **Resolved — `{Token}` syntax, PascalCase** (e.g. `{RomPath}` — the `{ROM_PATH}` in the original foundation document was illustrative, not a decision). **Phase 1 token list: just `{RomPath}`** (full path to the selected ROM/disc image) — the only value that actually varies per launch. `{FULLSCREEN}` from the original example is not a token: it doesn't vary per invocation in Phase 1, so the user just types the flag (e.g. `-fullscreen`) directly into `ArgumentTemplate` as static text, same as a core path (`-L cores\snes9x_libretro.dll`). Working directory is not a token or a schema field either — `LaunchService` always sets `WorkingDirectory = Path.GetDirectoryName(ExecutablePath)` automatically (many emulators, e.g. RetroArch, need their own install dir as CWD to find `cores/`/`system/` by relative path; unset, `Process.Start` defaults to Bridge's own CWD, a common silent launcher bug). **Resolver:** single-pass `Regex.Replace` with a `MatchEvaluator` over a `Dictionary<string, string>` token map — not Playnite's chained `.Replace()` calls (which Playnite's own code flags as tech debt, and which risk double-substitution if an expanded value happens to contain another token's literal `{...}` text). The evaluator throws `BridgeException` on any `{Token}` in the template not present in the dictionary. **Required-token validation:** before expansion runs, `ArgumentTemplate` is checked for `{RomPath}`'s literal presence and throws `BridgeException` if missing — a template without it would launch the emulator with no ROM argument (likely opening its main menu), which is a silent functional failure from the user's perspective even though the process technically launched successfully; this check fails before `Process.Start` is ever called, not after. **Auto-quoting:** the evaluator inspects the template characters immediately before/after each token match — if the resolved value contains a space and the match isn't already wrapped in manual `"..."`, it gets auto-quoted; if the user already wrote `"{RomPath}"` themselves, Bridge doesn't double-quote it. Improves on Playnite's approach, where quoting is entirely the template author's manual responsibility. | `LaunchService` |
| 4 | SteamGridDB API key handling: user-supplied vs. embedded | **Resolved — user-supplied, confirmed** (not just carried over from the foundation document's initial lean — reinforced by a sharper, Bridge-specific reason it didn't spell out: Bridge is public/open-source (GPL-3.0, public GitHub repo), so an "embedded" key can't actually stay secret — it's readable straight from the source or a trivial decompile of the published `.exe`. This isn't just extra cost to the developer as originally framed, it's a structural non-starter for a public OSS project specifically. A server-side proxy that keeps a key secret behind Bridge's own backend would be a fundamentally different architecture, not "embedded vs. user-supplied" — noted only as a hypothetical, out of scope unless actually pursued later). **Storage:** `%LocalAppData%\Bridge\settings.json`, key stored DPAPI-encrypted via `System.Security.Cryptography.ProtectedData` (`Protect`/`Unprotect`, `DataProtectionScope.CurrentUser` — [ProtectedData Class \| Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata), [NuGet package](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData)). `CurrentUser` scope ties decryption to the specific Windows account — protects against another local account reading it, and against a common OSS failure mode: a user pasting their settings file into a public GitHub issue while troubleshooting, where the encrypted blob is useless outside their own Windows account. **First run / zero-friction:** the key is never required to configure ROM folders or scan (FR-01/FR-02/FR-03 need no key at all) — box art is a separate, independent concern (FR-04/FR-05). No key configured → games still appear in the grid with a placeholder cover, not a blocking setup prompt; the key can be entered later in Settings at any time, and `MetadataService` retroactively fetches box art for already-scanned games once a key becomes available. **Invalid key / rate limit — never silent:** both are non-fatal to the scan flow, extending FR-04's "handle not-found gracefully" to these cases too, but neither is swallowed silently either: an invalid key (401/403) is `LogError`'d and surfaced as a persistent, visible status (not just a log line) so the user isn't left staring at blank covers with no explanation; a rate limit (429) is `LogWarning`'d as an expected/transient condition, backing off and retrying later rather than failing the whole scan. Distinguishing these retry-worthy states from a game genuinely not found on SteamGridDB (which shouldn't be retried indefinitely) is a `MetadataService` implementation detail for later, not scoped into this decision. | `MetadataService` |
| 5 | Emulator exit detection ("TrackingMode"): how does Bridge know the emulator process has closed, so control returns to the launcher? | **Resolved** — Phase 1 tracks the process handle returned directly by `Process.Start()` (the simplest option). Documented explicitly as a known limitation (see DEVELOPMENT.md → Known Limitations), not assumed to cover every emulator — it does not detect exit correctly if the launched process is a wrapper/launcher that spawns the real emulator and exits itself. Process-tree tracking via Windows Job Objects is the noted improvement path if that turns out to be common in practice; the fully configurable per-emulator tracking-mode system (Playnite's approach) is explicitly not built in Phase 1 — more scope than a single-emulator-per-system phase needs. See ARCHITECTURE.md → ADR-1. | `LaunchService` |

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
├── Bridge/                     # Main WPF project
│   ├── Converters/
│   │   ├── InverseBooleanConverter.cs
│   │   └── InverseBooleanToVisibilityConverter.cs
│   ├── Exceptions/
│   │   └── BridgeException.cs
│   ├── Models/
│   │   ├── Platform.cs
│   │   ├── Game.cs
│   │   ├── EmulatorConfig.cs
│   │   ├── ScanFolder.cs
│   │   ├── ScanResult.cs
│   │   ├── BoxArt.cs
│   │   ├── MetadataFetchResult.cs
│   │   └── LaunchResult.cs
│   ├── Resources/
│   │   └── SeedSystems.json    # EmbeddedResource — 15 built-in platforms
│   ├── Services/
│   │   ├── ILibraryRepository.cs / LibraryRepository.cs
│   │   ├── IRomScannerService.cs / RomScannerService.cs
│   │   ├── ISettingsService.cs / SettingsService.cs
│   │   ├── IImageCacheService.cs / ImageCacheService.cs
│   │   ├── IMetadataService.cs / MetadataService.cs
│   │   ├── IEmulatorService.cs / EmulatorService.cs
│   │   ├── ArgumentTemplate.cs     # shared {Token} resolver, used by EmulatorService + LaunchService
│   │   ├── ILaunchService.cs / LaunchService.cs
│   │   ├── MessageBoxService.cs      # IMessageBoxService/MessageBoxService
│   │   ├── FolderPickerService.cs    # IFolderPickerService/FolderPickerService
│   │   └── FilePickerService.cs      # IFilePickerService/FilePickerService
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   ├── GameTile.cs
│   │   ├── SettingsViewModel.cs
│   │   └── PlatformConfigItem.cs
│   ├── App.xaml / App.xaml.cs        # composition root — DI wiring, no StartupUri
│   ├── MainWindow.xaml / .xaml.cs
│   ├── SettingsWindow.xaml / .xaml.cs
│   └── Config.cs
├── Bridge.Tests/
│   ├── ViewModels/
│   │   ├── MainViewModelTests.cs
│   │   └── SettingsViewModelTests.cs
│   └── Services/
│       ├── LibraryRepositoryTests.cs
│       ├── RomScannerServiceTests.cs
│       ├── SettingsServiceTests.cs
│       ├── ImageCacheServiceTests.cs
│       ├── MetadataServiceTests.cs
│       ├── ArgumentTemplateTests.cs
│       ├── EmulatorServiceTests.cs
│       ├── LaunchServiceTests.cs
│       ├── FakeLibraryRepository.cs
│       ├── FakeSettingsService.cs
│       ├── FakeImageCacheService.cs
│       ├── FakeEmulatorService.cs
│       ├── FakeRomScannerService.cs
│       ├── FakeMetadataService.cs
│       ├── FakeLaunchService.cs
│       ├── FakeMessageBoxService.cs
│       ├── FakeFolderPickerService.cs
│       ├── FakeFilePickerService.cs
│       └── FakeHttpMessageHandler.cs
├── docs/
├── README.md
├── PLAN.md
├── DEVELOPMENT.md
├── ARCHITECTURE.md
└── Bridge.slnx
```

---

## Development Phases

### Phase 1: MVP
> **Objective:** Detect → show → play, using manually-configured emulators.

**Milestones:**

| Milestone | Description | Status |
|-----------|-------------|--------|
| FR-01 | User can add one or more root ROM folders | UI wired — "Add Folder" button (`MainWindow`) → `OpenFolderDialog` → `RomScannerService.AddScanFolderAsync`. Not yet manually verified running (no way to launch a real WPF window from this environment — see PLAN.md → Timeline) |
| FR-02 | System recursively scans and detects valid ROM files | UI wired — "Rescan" button / triggered automatically after "Add Folder". Same unverified-running caveat as FR-01 |
| FR-03 | Each detected ROM is automatically associated with a known system/console | Implemented and tested at the service layer; surfaced in the grid via `GameTile`/`IsMissing`. Same unverified-running caveat |
| FR-04 | Each detected ROM looks up its box art on SteamGridDB | UI wired — `RefreshLibraryCommand` calls `FetchMissingBoxArtAsync` after every scan. Same unverified-running caveat |
| FR-05 | Box art is cached locally, resized to the exact size it's displayed at | UI wired — grid tiles bind to `GameTile.CoverImagePath` (`Config.CoverWidth`/`CoverHeight` placeholder values, not a final UI decision). Same unverified-running caveat |
| FR-06 | User configures, per system, which emulator (.exe) to use | UI wired — `SettingsWindow`, platform list + executable/argument form. Same unverified-running caveat |
| FR-07 | Selecting and confirming a ROM launches the emulator with correct arguments | UI wired — clicking a grid tile calls `LaunchGameCommand` directly, no confirmation step (see UI design notes). Same unverified-running caveat |
| FR-08 | The library persists between sessions (no full re-scan on every launch) | Implemented and tested at the service layer (LiteDB) |
| FR-09 | User can trigger a manual re-scan | UI wired — "Rescan" button, disabled while a scan is already running. Same unverified-running caveat |

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

No fixed dates yet. Progress so far, and the exact handoff state for whoever (or whichever session) picks this up next:

1. ~~Create the Bridge repo from `project-template/`, following `project-template/NEW_PROJECT_CHECKLIST.md`~~ — done
2. ~~Resolve the 5 Open Decisions above in Phase 0, before writing the first service~~ — done, see ADR-1 through ADR-5 in `ARCHITECTURE.md`
3. ~~`LibraryRepository` + `RomScannerService`~~ — done (commit `a4e781a`), ADR-1/2/3/6/7
4. ~~`MetadataService` + `ImageCacheService` (+ `SettingsService`)~~ — done (commit `2b7cd10`), ADR-4/5/8
5. ~~`EmulatorService` + `LaunchService`~~ — done (commit `f87d516`), ADR-9
6. ~~Close the `AddScanFolderAsync` validation gap found during the status review~~ — done
7. ~~Composition root (`App.xaml.cs` DI wiring, lifetimes justified field-by-field)~~ — done
8. ~~Phase 1 minimal UI — `MainWindow`/`MainViewModel` (grid, empty state, progress, toolbar) and `SettingsWindow`/`SettingsViewModel` (emulator config, API key)~~ — done, ADR-10
9. ~~Global unhandled-exception handler (`DispatcherUnhandledException`) in `App.xaml.cs`~~ — done, closed in the same session it was found rather than left as a Known Limitation (the code genuinely didn't exist yet, unlike the two remaining Known Limitations rows, which are both "code exists and is correct, just untested")

**All of Phase 1's 9 FRs are wired end-to-end in code — services, composition root (with global exception handling), and UI — and covered by 83 unit tests (`dotnet test` green).** All 5 original Open Decisions plus the TrackingMode addition are resolved (ADR-1 through ADR-10).

**Next, in order:**

10. **Manually run the app and actually look at it** — nothing in this repo has confirmed the UI *renders* correctly or behaves as designed; `dotnet build`/`dotnet test` passing is necessary but not sufficient. This is the first real gate before Phase 1 can be called done.
11. Whatever the manual run in step 10 surfaces — expect UI bugs on first real render (untested XAML bindings, layout issues) that unit tests structurally can't catch.

---

*This document is a living plan. Update as the project evolves.*
