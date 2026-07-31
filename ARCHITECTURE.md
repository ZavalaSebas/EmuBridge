# Architecture Decision Records

This document records architectural decisions made during the development of Bridge.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│  Views (WPF + WPF-UI)                            │
│  MainWindow · Library cover grid view            │
├─────────────────────────────────────────────────┤
│  ViewModels (CommunityToolkit.Mvvm)              │
│  LibraryViewModel · per-view ViewModels          │
├─────────────────────────────────────────────────┤
│  Services                                        │
│  RomScannerService · MetadataService ·           │
│  ImageCacheService · EmulatorService ·           │
│  LaunchService · LibraryRepository               │
├─────────────────────────────────────────────────┤
│  External / Data                                 │
│  SteamGridDB API · Local image cache ·           │
│  LiteDB (bridge.db) · Emulator processes         │
└─────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Choice | Why |
|----------|--------|-----|
| UI Framework | WPF (.NET 10) | Proven in production on prior projects (SteamManager, OrbSpoofer); single-file self-contained distribution already validated |
| Styling/Theming | WPF-UI (lepo.co) | Mica, Fluent Design, already validated in a prior project |
| WPF vs. WinUI 3 | WPF | WinUI 3's Composition API has a genuine edge for fluid animations, but WPF wins on proven real-world experience and reuse of the existing process/tooling template. If Phase 2/3 animation polish doesn't feel fluid enough with classic Storyboards, investigate Windows.UI.Composition interop as a targeted enhancement — not a reason to change the UI framework |
| MVVM | CommunityToolkit.Mvvm | Consistent with prior projects |
| DI | Microsoft.Extensions.DependencyInjection | Standard already in use |
| Logging | `ILogger<T>` | Standard for larger projects — Bridge's scope doesn't justify a `Debug.WriteLine` shortcut |
| Image scaling | Always resize/cache box art to the exact display pixel size; never scale full-resolution source images at render time | Real-time scaling of large images on the render thread is a common source of jank in cover-grid UIs |
| Selection/hover effects | Avoid broad `DropShadowEffect` on the selected cover; prefer caching the rendered result to a bitmap (`RenderTargetBitmap`) or a pre-rendered shadow asset | `DropShadowEffect` is a relatively expensive software-rendered shader in WPF, even applied to a single element |
| List virtualization | Use `VirtualizingStackPanel` (or equivalent) from the first cover grid implementation | The library can grow to thousands of ROMs; this must be a day-1 decision, not a later optimization |
| `EmulatorService` design | Data-driven configuration (JSON/DB), not a hardcoded platform→emulator mapping | Extensibility non-functional requirement: adding a platform or emulator must not require touching core code; also sets up Phase 2's auto-detect/download without a redesign |
| External Metadata API | SteamGridDB | Provides box art by game name; requires a user-supplied API key (DPAPI-encrypted at rest) and rate-limit handling — see ADR-5 |

---

## What is an ADR?

An Architecture Decision Record (ADR) documents a significant architectural decision: the context that led to it, the decision itself, and its consequences.

## When to Create an ADR

Create an ADR when:
- Choosing between multiple technical approaches
- Adopting a new library or framework
- Making a decision that affects multiple components
- Rejecting a proposed solution

This includes each of the 4 Open Decisions tracked in `PLAN.md` — once one is resolved, record it here as an ADR rather than only updating its status in PLAN.md.

## When NOT to Create an ADR

Don't create an ADR for:
- Trivial decisions (naming conventions, code style)
- Routine implementation choices
- Bug fixes that don't change architecture

## ADR Format

Copy this block and fill it in when adding a new ADR below:

```markdown
## ADR-{{ADR_NUMBER}}: {{ADR_TITLE}}

**Status:** Proposed | Accepted | Deprecated | Superseded by [ADR-XXX]

**Date:** {{DATE}}

**Context:**
{{CONTEXT}}

**Decision:**
{{DECISION}}

**Consequences:**
- ✅ {{POSITIVE_CONSEQUENCES}}
- ❌ {{NEGATIVE_CONSEQUENCES}}

**Alternatives considered:**

- **Alternative 1:** {{DESCRIPTION}} — rejected because {{REASON}}
- **Alternative 2:** {{DESCRIPTION}} — rejected because {{REASON}}
```

## Existing ADRs

### ADR-1: Phase 1 emulator exit detection uses direct process tracking, not Job Objects

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`LaunchService` needs to know when the launched emulator process has closed so Bridge can return control to the user (e.g. re-show the library view). This wasn't in the original foundation document — it surfaced as Open Decision #5 in `PLAN.md` while reviewing `LaunchService`'s requirements, and is directly motivated by a process-exit-detection bug already found in OrbSpoofer, where tracking the literal `Process` handle returned by `Process.Start()` failed for a launcher/wrapper process that spawned the real target and exited itself. Playnite's own `TrackingMode` (Process/Directory/OriginalProcess/ProcessName, configurable per emulator profile) was reviewed as prior art.

**Decision:**
Phase 1 tracks the process handle returned directly by `Process.Start()` and waits on it exiting — the simplest option. This is documented as a known limitation (see `DEVELOPMENT.md` → Known Limitations), not silently assumed to be correct for every emulator. Process-tree tracking via Windows Job Objects (`CreateJobObject`/`AssignProcessToJobObject`) is the noted improvement path if the wrapper/launcher problem is confirmed to occur frequently once real emulators are being configured and tested. A fully configurable per-emulator tracking-mode system (mirroring Playnite's approach) is explicitly not built in Phase 1 — more scope than a single-emulator-per-system phase needs.

**Consequences:**
- ✅ Minimal implementation for Phase 1 — no P/Invoke, no Job Object complexity, ships faster
- ✅ The limitation is documented up front rather than discovered as a surprise bug later, and the improvement path is already scoped
- ❌ Known to break for any emulator that launches via a wrapper/launcher process that exits before the real emulator does — Bridge would return control while the emulator is still running
- ❌ If this proves common, revisiting requires implementing Job Object tracking — a real, if well-documented, chunk of work

**Alternatives considered:**

- **Track by process name:** rejected — ambiguous with multiple instances of the same emulator running, and fragile if the executable name isn't reliably known upfront
- **Track by directory activity:** rejected as too heuristic/flaky for Phase 1, and solves a problem (no single trackable process) not yet confirmed to exist for the emulators Bridge targets first
- **Job Object process-tree tracking:** not rejected, deferred — the most robust option, but adds implementation complexity not justified until the direct-tracking limitation is confirmed to matter in practice
- **Fully configurable per-emulator tracking mode (Playnite's approach):** rejected for Phase 1 as more scope/flexibility than a single-emulator-per-system phase needs; revisit if/when Phase 2 introduces multiple emulator profiles per system

---

### ADR-2: Storage — LiteDB over SQLite or flat JSON

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`LibraryRepository` needs to persist Bridge's library (games, systems, emulator configs) locally. Bridge's NFR requires a single-file, self-contained `.exe`. Playnite (MIT-licensed, reviewed as prior art) uses LiteDB 4, split one physical file per collection (17 collections, matching its much larger taxonomy surface — genres, companies, tags, etc.), with an in-repo code comment justifying LiteDB v4 over v5 (disabling the memory cache, write speed) — not a LiteDB-vs-SQLite comparison.

**Decision:**
LiteDB (current 5.x, not Playnite's old v4 pin), a single `bridge.db` file with 2–3 named collections (not Playnite's one-file-per-collection split — Bridge has ~3 entities, not 17 taxonomies). Decisive factor, per official docs ([Create a single file for application deployment - .NET \| Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), "Native libraries"): SQLite's native interop (`Microsoft.Data.Sqlite` → SQLitePCLRaw) isn't bundled into a true single file by default — it ships as a separate file next to the `.exe` unless `IncludeNativeLibrariesForSelfExtract=true` is set, and even then it's extracted to `%TEMP%/.net` on every startup before the app can use it. LiteDB is pure managed code — no native interop, no extra packaging step.

**Consequences:**
- ✅ Genuinely single-file distribution, no extra packaging configuration needed
- ✅ Document model fits Bridge's small, mostly-lookup-shaped entity set better than normalizing into relational tables
- ❌ Weaker tooling/ecosystem than SQLite — no universal SQL client, LiteDB-specific viewers only
- ❌ If Phase 3's recommendation engine ever needs real relational queries, may need to reconsider — mitigated by `LibraryRepository` already encapsulating storage behind a service boundary

**Alternatives considered:**

- **SQLite (`Microsoft.Data.Sqlite`):** rejected primarily on single-file packaging friction (see Decision), despite stronger tooling/ecosystem
- **Flat JSON:** rejected — doesn't scale well to "thousands of ROMs" per the original foundation document's own concern; never seriously contended given LiteDB's document model already covers the same use case with proper indexing

---

### ADR-3: Extension→platform and platform→emulator schema

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`RomScannerService` needs to map file extensions to platforms (FR-02/FR-03, with an explicit "unknown" fallback requirement). `EmulatorService` needs a data-driven platform→emulator mapping — already settled as a principle in this document's Key Design Decisions, but never given a concrete schema. Playnite's real shape (`Emulator 1—* Profile *—* Platform`, with the many-to-many living on the profile, not a direct join) was reviewed as prior art but deliberately scoped down: Phase 1 is one manually-configured emulator per platform, not a catalog/profile system. The entity is named `Platform`, not `System` — `System` collides with the C# `System.*` namespace; Playnite independently hit and solved this exact naming problem the same way.

**Decision:**
Two LiteDB collections: `Platform { Id: string slug (+ reserved "unknown"), Name: string, Extensions: string[] }` and `EmulatorConfig { Id: Guid, PlatformId: string (FK, unique index), Name: string, ExecutablePath: string, ArgumentTemplate: string }`. The unknown-extension fallback assigns the reserved `Platform` sentinel (`Extensions: []`, so it's only ever reached via fallback, never matched directly) rather than leaving `Game.PlatformId` nullable.

**Consequences:**
- ✅ `Game.PlatformId` stays non-nullable everywhere; unmatched ROMs remain visible in the library grid instead of silently vanishing from scan results
- ✅ The unique index on `PlatformId` encodes the Phase 1 "one emulator per platform" constraint at the storage layer, and is a one-line change to relax for Phase 2's many-emulators-per-platform
- ✅ `EmulatorConfig` as its own entity (not fields embedded on `Platform`) keeps a future `Emulator`/`Profile` split (matching Playnite's shape) a mechanical one-time data migration, not a contract change — as long as `EmulatorService` stays the sole consumer of this data
- ❌ Phase 1 duplicates `ExecutablePath` if the same physical emulator install serves multiple platforms (e.g. one RetroArch install for both NES and SNES) — acceptable since Phase 1 has no reuse UX anyway; resolved properly when Phase 2 splits out a dedicated `Emulator` entity

**Alternatives considered:**

- **Nullable `Game.PlatformId` for unmatched ROMs, no sentinel:** rejected — pushes null-handling into every query/UI that groups by platform, and still needs a separate mechanism to surface unmatched files to the user
- **Embedding `ExecutablePath`/`ArgumentTemplate` directly on `Platform`:** rejected — blocks the Phase 2 many-to-many and `Emulator`/`Profile` split without a schema redesign
- **Full built-in YAML catalog + profile system (Playnite's actual shape), built now:** rejected as more scope than Phase 1 needs — noted as the Phase 2 evolution path, not built today
- **Naming the entity `System` (matching the original foundation document's wording):** rejected — collides with the C# `System.*` namespace; `Platform` avoids the collision and matches the term Playnite itself uses for the same concept

---

### ADR-4: Launch argument template — `{Token}` syntax, single-pass resolver, auto-quoting

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`LaunchService` needs to build the final command-line arguments for launching an emulator from `EmulatorConfig.ArgumentTemplate` plus the selected ROM's path. Playnite's real implementation (reviewed as prior art) uses `{Token}`-style placeholders expanded via a chain of `string.Replace` calls — a pattern Playnite's own code comments flag as tech debt (`// TODO rework this whole mess...`), and which risks double-substitution if an expanded value happens to contain another token's literal `{...}` text. Playnite also leaves argument quoting (for paths with spaces) entirely to the template author's manual discipline.

**Decision:**
`{Token}` syntax in PascalCase (e.g. `{RomPath}`, not the foundation document's illustrative `{ROM_PATH}`/`{FULLSCREEN}`). Phase 1 has exactly one token, `{RomPath}` — the only value that varies per launch; static flags (e.g. `-fullscreen`, a core path) are typed directly into `ArgumentTemplate` by the user, not modeled as tokens. Working directory isn't a token or schema field — `LaunchService` always sets it to `Path.GetDirectoryName(ExecutablePath)` automatically.

Resolver: a single `Regex.Replace` pass with a `MatchEvaluator`, looking up each matched token name in a `Dictionary<string, string>`. Two validations, both failing before `Process.Start` is ever called:
1. **Required token check:** `ArgumentTemplate` must contain `{RomPath}` literally, or expansion throws `BridgeException` — a template missing it would launch the emulator with no ROM argument (likely just opening its main menu), a silent functional failure even though the process technically starts.
2. **Unknown token check:** any `{Token}` in the template not found in the dictionary throws `BridgeException` during expansion.

Auto-quoting: the evaluator inspects the template characters immediately surrounding each match — if the resolved value contains a space and isn't already wrapped in manual `"..."` in the template, it gets quoted; if the user already wrote `"{RomPath}"`, Bridge doesn't double-quote.

**Consequences:**
- ✅ Single-pass expansion structurally avoids the double-substitution risk of chained `.Replace()` — a resolved value is never re-scanned for further token matches
- ✅ Both failure modes (missing required token, unknown token) surface as clear, immediate exceptions before any process launches, not as confusing runtime behavior (emulator opens to its menu) discovered later
- ✅ Auto-quoting removes a common first-run footgun (Windows paths almost always contain spaces) without breaking templates where the user already quoted manually, out of habit from seeing other launchers' conventions
- ❌ Adding new tokens later still requires care in `LaunchService`'s token-dictionary construction, though the resolver itself needs no changes — purely additive
- ❌ A literal `"` character inside a resolved value (theoretically possible, not just spaces) would still break quoting; not handled, and not expected to matter since `"` is an NTFS-reserved character no real Windows file path can contain

**Alternatives considered:**

- **Chained `string.Replace` calls (Playnite's actual approach):** rejected — self-acknowledged tech debt in the source we reviewed it from, plus the double-substitution risk
- **Blind auto-quoting on every path-like token, regardless of existing manual quotes:** rejected — would double-quote templates where the user already wrote `"{RomPath}"`, producing broken arguments
- **Silent fallback (empty string) for missing/unknown tokens instead of throwing:** rejected — contradicts the project's own "never swallow errors silently" rule (see `DEVELOPMENT.md` → Error Handling); a misconfigured template should fail loud, not launch incorrectly
- **Full Playnite-style token list (`{ImagePath}`, `{ImageNameNoExt}`, `{ImageDir}`, `{Name}`, `{Platform}`, etc.):** rejected for Phase 1 — none are needed by any current FR; adding one later is a one-line addition to the token dictionary, not a redesign

---

### ADR-5: SteamGridDB API key — user-supplied, DPAPI-encrypted at rest, never blocks first run

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`MetadataService` needs a SteamGridDB API key to fetch box art. The original foundation document leaned toward a user-supplied key in Phase 1 ("simpler, no cost to the developer") versus an embedded key ("more plug & play, but risks shared rate-limiting and key exposure"), without fully spelling out why embedding is actually untenable for this specific project. Bridge is a public, open-source (GPL-3.0) repository distributing a single-file `.exe`.

**Decision:**
User-supplied key, confirmed — and reinforced with a sharper reason than the original framing: since Bridge's source and published binary are both public, an "embedded" key isn't actually securable at all — it's readable directly from the repo or trivially recoverable from the compiled `.exe`. This makes it a structural non-starter for a public OSS project, not just a cost trade-off. (A server-side proxy holding a key secret behind Bridge's own backend would sidestep this, but that's a different architecture entirely — not considered here, noted only as a hypothetical future path if ever pursued.)

Storage: `%LocalAppData%\Bridge\settings.json`, with the key DPAPI-encrypted via `System.Security.Cryptography.ProtectedData` (`DataProtectionScope.CurrentUser`) — confirmed available as a current NuGet package ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata), [NuGet](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData)), Windows-only (fine — Bridge is Windows-only), ties decryption to the specific Windows account.

First run: never blocks ROM folder configuration or scanning (FR-01/02/03 need no key); missing key means placeholder covers, not a setup gate, matching the foundation document's zero-friction goal. Key can be added anytime in Settings, triggering a retroactive box art fetch for already-scanned games.

Invalid key / rate limit: both non-fatal to scanning (extends FR-04's "handle not-found gracefully" to these states too), but never silent — invalid key logged as `LogError` plus a persistent visible status; rate limit logged as `LogWarning`, with backoff-and-retry rather than failing the batch.

**Consequences:**
- ✅ No shared rate-limit risk across all Bridge installs, and no key-exposure liability for the developer — each user's key is theirs alone
- ✅ `CurrentUser`-scoped DPAPI protects against a common real OSS failure mode: a user pasting their settings file into a public bug report while troubleshooting
- ✅ Zero-friction first run preserved — box art is decoupled from the core detect/show/play loop, not a gate in front of it
- ✅ Failure states (invalid key, rate limited) are diagnosable via logs and a visible status, not collapsed into a silent "no box art, who knows why"
- ❌ Every user must obtain their own free SteamGridDB key before getting box art — some setup friction remains, just moved out of the critical path instead of eliminated
- ❌ Distinguishing "retry later" failures (no key / invalid key / rate limited) from "terminal" ones (genuinely not found on SteamGridDB) requires a small amount of persisted per-game fetch-status tracking in `MetadataService` — not designed here, deferred to that service's own implementation

**Alternatives considered:**

- **Embedded key shared across all installs:** rejected — not actually securable in a public OSS repo/binary, plus shared rate-limit exhaustion risk affecting every user at once
- **Plain-text key in the settings file:** rejected — trivially leaked if the user shares their config file (a real, common occurrence in OSS bug reports), with no protection benefit over DPAPI for effectively the same implementation cost
- **Environment variable for the key:** rejected — awkward for a GUI desktop app with a Settings screen; more setup friction than a text field, not less
- **Blocking first-run setup wizard requiring the key before any scanning:** rejected — directly contradicts the foundation document's zero-friction goal; box art has no functional dependency on scanning/detection succeeding

---

### ADR-6: LibraryRepository + RomScannerService design

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
First services built on top of the resolved schema (ADR-3). `RomScannerService` needs a minimal `Game` shape (FR-01/02/03/08/09), a dedup/upsert strategy for repeat scans, a defined behavior for ROMs that disappear from disk (FR-09), and error handling that never fails silently, consistent with every prior decision this session.

**Decision:**
`Game { Id: Guid, Path: string (unique index — Phase 1 identity key), Name: string (derived from filename at scan time), PlatformId: string, IsMissing: bool }`. No metadata fields (title/box art/etc.) — that's `MetadataService`'s domain, not scanned. `ScanFolder { Id: Guid, Path: string }` added to persist FR-01's configured root folders (structurally required for `RomScannerService` to know where to look; not separately deliberated as its own decision).

Identity/dedup is by normalized file `Path`, not content hash — hashing every ROM on every scan (including multi-GB disc images) would make FR-09's manual rescan slow, and no current FR requires recognizing a moved/renamed file as the same game. Noted as a natural Phase 2/3 addition (ties to the DAT/CRC identification idea surfaced during the Playnite research), purely additive since it doesn't change `Path`-based identity semantics.

Scan algorithm: single pass, no per-file DB round-trips — `LibraryRepository.GetAllGamesAsync()`/`GetPlatformsAsync()` load everything into memory once at scan start (dictionaries keyed by `Path` and by extension), each file is matched against these, and `Game`s not seen during the pass are marked `IsMissing = true` in one batch call at the end (never deleted — see Consequences).

Error handling: a missing/inaccessible root folder or an unreadable individual file is caught per-item, logged (`LogWarning`), and skipped — never aborts the whole scan. A recognized-extension file with 0 bytes is excluded from being persisted as a `Game` (`LogWarning`) since it cannot be a valid ROM. Deeper content corruption is explicitly out of scope — `RomScannerService` only validates what's cheap to check from the filesystem (existence, extension, non-zero size); a corrupt-but-nonzero-size ROM is scanned normally and fails at launch time instead, which is `LaunchService`'s/the emulator's problem to surface. `ScanAsync` returns a structured `ScanResult` (counts added/updated/marked-missing, plus skipped-folders/skipped-files with reasons) so the caller can show a real status, not just a log line nobody reads — same "log AND surface visibly" standard as ADR-5.

**Consequences:**
- ✅ Single in-memory pass scales cleanly to "thousands of ROMs" without N individual DB round-trips per file
- ✅ Mark-missing-not-delete means a temporarily unavailable folder (unplugged drive, unreachable network share) never silently destroys library data; the flag self-clears if the file reappears on a later scan, with no special-case code
- ✅ A folder disappearing entirely and a single file disappearing hit the exact same "not seen this scan → missing" code path — no separate handling needed
- ✅ `ScanResult` gives the UI layer enough to report real, specific outcomes ("2 folders inaccessible, 12 games now missing") instead of a generic pass/fail
- ❌ Path-based identity means a manually moved/renamed ROM is treated as a brand-new game (old entry marked missing, new entry created) until a future hash-based identity is added
- ❌ Corrupt-but-nonzero-size ROMs aren't caught at scan time — surfaces later as a launch failure instead

**Alternatives considered:**

- **Content-hash-based identity:** rejected for Phase 1 — reading every ROM's full contents on every scan is too slow for the stated "thousands of ROMs" scale; noted as a clean Phase 2/3 addition
- **Delete `Game` records no longer found on disk:** rejected — destroys data on a transient absence (unplugged drive, temporarily offline network share) with no confirmation
- **Leave stale `Game` records untouched with no flag:** rejected — a games stays visible and "launchable" with zero indication it will fail, violating the same never-fail-silently standard applied everywhere else this session
- **Deep ROM content/integrity validation during scanning:** rejected — disproportionate cost for a filesystem-level scanner; that validation naturally belongs to the emulator at launch time

---

### ADR-7: Platform seed data — curated cartridge/handheld list, disc-based platforms deliberately excluded

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
Phase 1 needs built-in `Platform` records so extension detection produces useful results without the user hand-authoring every entry first — otherwise the first scan classifies everything as `"unknown"`, working against the foundation document's zero-friction goal. Playnite's built-in catalog (~190 platforms, reviewed during the Playnite research) was explicitly ruled out as too much scope for Phase 1 back in ADR-3; this ADR defines what actually ships instead.

**Decision:**
15 hand-curated platforms, delivered as `Resources/SeedSystems.json`, an `EmbeddedResource` (same delivery mechanism already used for `WhatsNew.txt` in OrbSpoofer/SteamManager — `EmbeddedResource` + `Assembly.GetManifestResourceStream` + null-check fallback — adapted to JSON since the content is structured data, not prose). `LibraryRepository` seeds it at first run, in the same pass as the `"unknown"` sentinel:

| Id | Name | Extensions |
|---|---|---|
| `nes` | Nintendo Entertainment System | `nes` |
| `snes` | Super Nintendo Entertainment System | `sfc`, `smc` |
| `n64` | Nintendo 64 | `n64`, `z64`, `v64` |
| `gb` | Game Boy | `gb` |
| `gbc` | Game Boy Color | `gbc` |
| `gba` | Game Boy Advance | `gba` |
| `nds` | Nintendo DS | `nds` |
| `genesis` | Sega Genesis / Mega Drive | `md`, `gen`, `smd` |
| `sms` | Sega Master System | `sms` |
| `gamegear` | Sega Game Gear | `gg` |
| `atari2600` | Atari 2600 | `a26` |
| `atari7800` | Atari 7800 | `a78` |
| `pcengine` | PC Engine / TurboGrafx-16 | `pce` |
| `lynx` | Atari Lynx | `lnx` |
| `wonderswan` | WonderSwan / WonderSwan Color | `ws`, `wsc` |

Every extension in this table is checked pairwise and confirmed non-colliding, and each is specific to its platform's actual dump format — not a generic container. `WonderSwan`/`WonderSwan Color` are combined into one row (not split like `gb`/`gbc`/`gba`) because in practice they share the same emulator/core, matching the same reasoning already used for `snes`'s two extensions.

Disc-based platforms (PS1, PS2, Saturn, Dreamcast, GameCube, Wii, PSP, and similar) are **not** in the seed. Their common dump formats — `.iso`, `.bin`, `.cue`, `.chd`, `.gdi`, `.cdi`, `.wbfs`, `.pbp`, `.cso` — are shared across many different disc-based systems, and Phase 1's extension-only matching (ADR-3) has no mechanism to disambiguate which platform a given `.iso` actually belongs to. This is a **deliberate exclusion by Phase 1's detection mechanism, not an accepted limitation** — unlike ADR-1 (TrackingMode), where Option A was accepted because the more robust alternative (Job Objects) has real implementation cost that wasn't yet justified, there is no comparable cost being avoided here. The user can add any disc-based platform manually at any time through the same editable schema, consciously choosing which extension to risk assigning to it — Bridge's default posture for these platforms is "not seeded, so no default ambiguity," not "seeded with broken detection because building it right was too expensive."

**Consequences:**
- ✅ First scan on a fresh install produces useful, correctly-classified results for the most common cartridge/handheld platforms with zero configuration
- ✅ Zero risk of silent misclassification from the built-in seed itself — every included extension is unambiguous by construction
- ✅ The seed file is data, not code — editable/expandable without recompiling, even though Phase 1 has no in-app editor for it yet
- ❌ Popular disc-based platforms (PS1 especially) are not auto-detected out of the box; a user with a PS1 library gets zero seeded platforms for it and must configure one by hand, accepting the extension ambiguity themselves
- ❌ The list reflects one person's judgment of "common for a typical user" — reasonable disagreements about inclusion are possible and cheap to fix (it's a JSON file, not a schema change)

**Alternatives considered:**

- **Include disc-based platforms in the seed with their common extensions:** rejected — would silently misclassify any user with more than one disc-based system in their library, the exact class of silent failure this entire session has been designed to avoid
- **Include disc-based platforms with empty `Extensions` arrays, seeded but undetectable:** rejected as pointless — provides no real auto-detection benefit over the user creating the platform themselves when they actually need it, while implying a false sense of built-in support
- **Playnite's full ~190-platform catalog:** rejected in ADR-3 already; reconfirmed here — far more scope and maintenance surface than Phase 1's "one manually-configured emulator per platform" needs
- **A larger curated list (18-20) padded with rarer cartridge systems to hit a round number:** rejected — reliability of the included entries matters more than hitting an arbitrary count

---

### ADR-8: MetadataService + ImageCacheService design

**Status:** Accepted

**Date:** 2026-07-30

**Context:**
`MetadataService` needs to resolve box art for a `Game` via SteamGridDB (FR-04) and `ImageCacheService` needs to cache it locally, resized to the exact display size (FR-05), per the API key handling already decided in ADR-5. The actual SteamGridDB endpoints were confirmed from the official Node.js wrapper's source (`SteamGridDB/node-steamgriddb`, `src/index.ts`), not assumed: base URL `https://www.steamgriddb.com/api/v2`, `Authorization: Bearer {key}`, `GET /search/autocomplete/{query}` returning `{ success, data: [{ id, name, ... }], errors }`, `GET /grids/game/{id}` returning `{ success, data: [{ id, url, ... }], errors }`.

**Decision:**
New `BoxArt` entity (`Id`, `GameId` FK with unique index, `LocalPath`, `Status`, `LastAttemptUtc`), kept separate from `Game` for the same reason `EmulatorConfig` was kept separate from `Platform` in ADR-3 — Phase 2's detail-panel metadata (description, release year, screenshots) is purely additive to this new entity, never touching `Game`. `BoxArtStatus` has two terminal states (`Cached`, `NotFoundOnProvider` — never auto-retried) and one retry-worthy state (`FetchFailed`, covering missing key/invalid key/rate limit/network error uniformly — the specific reason lives in logs, not the persisted record, per ADR-5's own note that this distinction was deferred here).

`MetadataService.FetchMissingBoxArtAsync` is a single batch method (mirroring `RomScannerService.ScanAsync`'s shape), not a per-game call driven from outside. Game names are normalized before searching — common No-Intro/Redump parenthetical/bracketed tags (region, revision, etc.) are stripped via a simple regex, not fuzzy matching. The first search result and the first grid result are used with no scoring (approved as-is for Phase 1).

Error handling: a 429 (rate limit) or 401/403 (auth failure) response stops the rest of the batch immediately — both conditions predict every subsequent call will fail the same way, so continuing would just waste time; a network/parse error on a single game does not stop the batch, since it doesn't predict the next game will fail too. `MetadataFetchResult.StoppedEarlyDueToRateLimit` flags the rate-limit case specifically, per the explicit ask to surface it; auth-failure stopping early is visible via the `Failed` count plus a `LogError` (ADR-5's "persistent visible status" is a higher-level UI concern, not built here).

`ImageCacheService` is Game-agnostic (URL + target size → local path only), cache-keyed by `SHA256(url)[..16]_{width}x{height}.png` under `%LocalAppData%\Bridge\ImageCache\`, using WPF's native `BitmapImage` with `DecodePixelWidth`/`DecodePixelHeight` set before `EndInit()` — no new imaging library. Both dimensions are fixed to the exact target (a source with a different aspect ratio is stretched, not cropped) — matches the already-stated "resize to the exact display pixel size" principle without the extra complexity of aspect-preserving cropping, which no current requirement asks for. This deliberately does **not** replicate Playnite's decode-time-only resizing (rejected back when Playnite was first researched) — Bridge writes the resized bitmap to disk once, since box art here comes over the network, not from a local import.

**Consequences:**
- ✅ `BoxArt`'s terminal/retryable split means a confirmed "not found" is never retried forever, while a transient failure (no key yet, rate limit, network blip) naturally gets picked up again on a future batch run with zero extra bookkeeping
- ✅ Stopping early on rate-limit/auth-failure avoids burning through an entire library's worth of doomed API calls in one batch
- ✅ `ImageCacheService`'s Game-agnostic design is independently testable and reusable if Phase 2 needs differently-sized thumbnails for the same source image
- ❌ Name normalization is a simple tag-strip, not real fuzzy matching — a title with unusual formatting may still search poorly; acceptable for Phase 1, no FR asks for more
- ❌ Fixed-dimension resize can visually stretch box art with an unusual aspect ratio; acceptable simplification, revisit only if it proves to look bad in practice

**Alternatives considered:**

- **Embed box art fields directly on `Game`:** rejected — blocks Phase 2's larger metadata set without a redesign, same reasoning as `EmulatorConfig` vs `Platform` in ADR-3
- **Keep retrying rate-limited/auth-failed games one by one through the rest of the batch:** rejected — both failure modes predict repeat failures, so continuing wastes time without benefit
- **Replicate Playnite's decode-time image resizing (no disk cache of resized bitmaps):** rejected — Bridge's box art is fetched over the network, unlike Playnite's local imports, so re-decoding on every display wastes the original download; already the conclusion from the Playnite research pass
- **Full fuzzy/scored search matching:** rejected for Phase 1 — no FR requires it, and "first result" is an explicitly approved simplification

---

### ADR-9: EmulatorService + LaunchService design

**Status:** Accepted

**Date:** 2026-07-31

**Context:**
`EmulatorService` needs to validate and persist `EmulatorConfig` (schema from ADR-3), and `LaunchService` needs to actually launch a `Game` through the configured emulator, using the argument resolver already designed in ADR-4 but never implemented until now. The `EmulatorConfig.PlatformId` unique index was deliberately left out of `LibraryRepository` during the first implementation pass (ADR-6) specifically because nothing consumed it yet — this ADR is where it gets added back.

**Decision:**
A shared static `ArgumentTemplate` class (`Validate`/`Expand`) implements ADR-4's design exactly (single-pass `Regex.Replace`, dictionary lookup, context-aware quoting) and is called from two places: `EmulatorService.SaveEmulatorConfigAsync` (validates at config-save time) and `LaunchService.LaunchAsync` (validates again at launch time, then expands). Two entry points into the same data — a config could in principle be written directly to LiteDB bypassing `EmulatorService`, given the schema is deliberately editable — so both layers check independently rather than trusting the save-time gate alone.

`EmulatorService.SaveEmulatorConfigAsync` validates three things before persisting, throwing `BridgeException` on failure (same category as ADR-4's missing-token case — bad input from an active write, not an expected runtime outcome): the executable exists on disk, the argument template contains `{RomPath}`, and `PlatformId` references a real `Platform`.

`LaunchService.LaunchAsync` returns a `LaunchResult` (never throws for expected failure modes — an outcome of trying to fulfill a reasonable request, not bad input) with one of `Started`/`RomFileNotFound`/`NoEmulatorConfigured`/`ExecutableNotFound`/`LaunchFailed`. A missing `EmulatorConfig` for a platform is a single code path regardless of whether `PlatformId` is the `"unknown"` sentinel or a real, still-unconfigured platform — same unification already established in ADR-3/ADR-6 — only the surfaced message text differs. Both the ROM file and the emulator executable are re-checked with `File.Exists` immediately before launch, not trusted from `Game.IsMissing` or from whatever was true when the `EmulatorConfig` was saved — either can have moved or disappeared since.

`LaunchResult.GameSessionEndedTask` (built from `Process.WaitForExitAsync()`, per ADR-1's Option A) is the only way `LaunchService` exposes "the game ended" — never the raw `Process`. If ADR-1's improvement path (Job Object tracking) is ever implemented, this contract doesn't change, only what builds the `Task` internally does. `CancellationToken` is checked at method entry and again immediately before `Process.Start` — the two file-existence checks and argument expansion in between take real time, so a cancellation landing in that window is still honored instead of launching anyway.

**Consequences:**
- ✅ A misconfigured `ArgumentTemplate` is caught at config-save time in the common case (via `EmulatorService`), with `LaunchService`'s own check as a backstop for configs written some other way
- ✅ `NoEmulatorConfigured`'s unified code path means Phase 2's eventual "configure emulator" UI flow is identical whether the trigger was an unrecognized ROM or a recognized-but-unconfigured one
- ✅ Re-checking file existence at launch time (not trusting stale scan/config-time state) directly prevents a silent failure class: emulator or ROM moved after configuration
- ✅ The late cancellation check means a caller that cancels mid-`LaunchAsync` never accidentally launches a process it just tried to cancel
- ❌ Two validation call sites for the same `ArgumentTemplate` rule is minor duplication of *effort* (not logic — both call the same shared method), acceptable given the schema's own editability is what motivates it

**Alternatives considered:**

- **`LaunchService` throws exceptions for `NoEmulatorConfigured`/`ExecutableNotFound`/etc. instead of returning a result type:** rejected — these are expected, state-dependent outcomes of a reasonable request, not invalid input from the caller; matches the `ScanResult`/`MetadataFetchResult` precedent
- **Expose the raw `Process` from `LaunchResult` instead of a `Task`:** rejected — leaks `TrackingMode` implementation detail into the caller's contract, which ADR-1 already anticipated might change
- **Trust `EmulatorConfig`/`Game` state as of when they were last saved/scanned, skip the launch-time re-check:** rejected — exactly the silent-failure class this session has consistently avoided (moved emulator, removed ROM)
- **Only check `ct.ThrowIfCancellationRequested()` once, at method entry:** rejected per explicit review — file checks and argument expansion are real elapsed time, not free

---

### ADR-10: Composition root + Phase 1 minimal UI (MainWindow/MainViewModel, SettingsWindow/SettingsViewModel)

**Status:** Accepted

**Date:** 2026-07-31

**Context:**
`App.xaml.cs` had no DI wiring; `MainWindow.xaml`/`MainWindow.xaml.cs` were the untouched Visual Studio scaffold, still using the default `StartupUri="MainWindow.xaml"` mechanism. Phase 1's UI design (grid + empty state + progress + toolbar + Settings) was approved in a prior design pass, anchored to the already-implemented backend services (RomScannerService/MetadataService/LaunchService/EmulatorService), not inventing new behavior.

**Decision — composition root:**
`App`'s constructor builds the `ServiceCollection`/`ServiceProvider` before `OnStartup` runs. `OnStartup` now explicitly constructs `MainWindow`, resolves `MainViewModel` from DI, sets `DataContext`, and shows it — replacing the default `StartupUri` mechanism now that a real ViewModel exists to wire up (this was explicitly deferred, not skipped, when the composition root was first built). `StartupUri` was removed from `App.xaml`.

Every service lifetime is `Singleton`, chosen and reviewed field-by-field, not assumed from precedent: `LibraryRepository` needs it (owns the one `LiteDatabase` connection); the other nine registrations (`RomScannerService`, `SettingsService`, `ImageCacheService`, `MetadataService`, `EmulatorService`, `LaunchService`, plus the three dialog wrappers below) hold no mutable instance state at all — `Transient` would be equally correct, `Singleton` is a documented simplicity choice, not a technical requirement. `MainViewModel`/`SettingsViewModel` are `Transient`, matching SteamManager's ViewModel registration convention.

**Decision — testable dialog wrappers:** `IMessageBoxService`/`MessageBoxService` (message boxes), `IFolderPickerService`/`FolderPickerService` (`Microsoft.Win32.OpenFolderDialog`, WPF's native folder picker since .NET 8 — confirmed against official Microsoft docs, not the old WinForms interop), and `IFilePickerService`/`FilePickerService` (`Microsoft.Win32.OpenFileDialog`) — all mirroring `IMessageBoxService`'s exact shape from `SteamManager/Services/MessageBoxService.cs`. ViewModels depend on these interfaces, never on the WPF dialog types directly, so `MainViewModelTests`/`SettingsViewModelTests` drive dialog outcomes (folder chosen, file chosen, user cancelled) through `Fake*` doubles — consistent with every other Bridge test so far — without needing a real OS dialog to exist.

**Decision — MainViewModel:** `GameTile` is a flat, rebuilt-wholesale display DTO joining `Game` + `BoxArt` for the View — `NotFetched`/`NotFoundOnProvider`/`FetchFailed` all resolve to `CoverImagePath = null` (placeholder), no visual distinction, per the same reasoning as ADR-8. `RefreshLibraryCommand` is the orchestration glue explicitly flagged as pending in the prior session handoff (`PLAN.md` → Timeline) — it calls `RomScannerService.ScanAsync` then `MetadataService.FetchMissingBoxArtAsync` in sequence, guarded against concurrent execution (`if (IsBusy) return;`), with a shared `CancellationTokenSource` so `CancelScanCommand` can interrupt either phase. `LaunchGameCommand` shows `LaunchResult.ErrorMessage` via `IMessageBoxService` for any non-`Started` outcome and does not await `GameSessionEndedTask` inline (a background continuation just logs when the session ends) — awaiting it inside the command would keep the command's running state occupied for the emulator's entire lifetime.

Found and fixed during this pass: loading box art per-game in a loop (`GetBoxArtAsync` × N) would be the same N+1 pattern already avoided in `RomScannerService`/ADR-6 — added `ILibraryRepository.GetAllBoxArtAsync` (bulk fetch, dictionary lookup) instead, for the same reason (Games can scale to "thousands", per the NFR).

**Decision — SettingsViewModel:** the Platform list excludes the `"unknown"` sentinel — configuring an emulator for "couldn't identify this ROM's system" doesn't make sense, the fix there is the extension mapping, not an emulator assignment. Unlike `MainViewModel`'s box art loading, `SettingsViewModel` loads each platform's `EmulatorConfig` in an N-query loop (`GetEmulatorConfigForPlatformAsync` × ~15) without a bulk method — deliberately different from the `GetAllBoxArtAsync` decision above, because the platform list is seeded at ~15 rows, not "thousands"; a bulk method here would be optimizing a cost that doesn't exist.

**Consequences:**
- ✅ Every dialog-driven ViewModel behavior (folder/file picked or cancelled, message shown) is unit-tested without a real window or OS dialog
- ✅ `GetAllBoxArtAsync` keeps `MainViewModel`'s initial load and every refresh at two bulk repository calls, not `O(n)` round-trips, consistent with the NFR that already shaped `RomScannerService`
- ✅ Lifetime choices are individually justified, not copy-pasted from SteamManager's precedent — the review this ADR is based on went through it service-by-service
- ❌ `SettingsWindow`'s emulator-config `IsEnabled` gating on `SelectedPlatform` was attempted, then removed after referencing a converter that was never actually written — the `SaveEmulatorConfigCommand` guard (`if (SelectedPlatform is null) return;`) already prevents the only real failure mode, so the form simply stays interactively enabled with nothing selected; a cosmetic gap, not a functional one
- ❌ Same two-validation-call-sites duplication-of-effort tradeoff as ADR-9 applies again here, one layer up: `EmulatorService` validates at save time, `LaunchService` validates again at launch time — `SettingsViewModel` doesn't add a third check, it relies on `EmulatorService`'s

**Alternatives considered:**

- **Await `GameSessionEndedTask` inline inside `LaunchGameCommand`:** rejected — would keep the `IAsyncRelayCommand`'s running state occupied for the emulator's entire play session, a much longer window than "attempt to launch"
- **Bulk-fetch `EmulatorConfig` for `SettingsViewModel` the same way as `GetAllBoxArtAsync`:** rejected — no scale problem to solve at ~15 platforms; would be speculative optimization
- **Reference WPF dialog types directly from ViewModels (no `IMessageBoxService`-style wrappers):** rejected — makes the resulting dialog-driven behavior untestable without a real window, and SteamManager's own `IMessageBoxService` precedent already established the fix
- **Keep `StartupUri` and wire `DataContext` some other way (e.g. from `MainWindow`'s own constructor via a static locator):** rejected — explicit construction in `OnStartup`, resolving from `App.Services`, is the same shape SteamManager's `App.xaml.cs` already uses, and keeps the composition root as the single place that knows how everything is wired

---

### ADR-11: Emulator/EmulatorProfile split + verified-download mechanism (Phase 2 groundwork)

**Status:** Accepted (mechanism); manifest data partially unverified — see Consequences

**Date:** 2026-07-31

**Context:**
Phase 2's "automatic emulator detection/download" (`PLAN.md`) needs Bridge to download and later execute third-party binaries (RetroArch + libretro cores) it didn't build — a materially different trust boundary than Phase 1's `ImageCacheService`, which only ever caches inert image bytes. RetroArch alone covers all 15 seed platforms via different cores, which the Phase 1 `EmulatorConfig` schema (1:1 with `Platform`, reviewed and deliberately scoped down from Playnite's `Emulator 1—* Profile *—* Platform` shape back in ADR-3) cannot express — one physical install needs to back many per-platform launch configs, not one. This ADR covers the schema migration and the download-verification mechanism; it does not build the actual install/extract orchestration (a future ADR) or a real per-platform core catalog (see Consequences).

**Decision — Emulator/EmulatorProfile split:** `EmulatorConfig` is replaced by `Emulator` (a physical install: `ExecutablePath`, `InstallSource` (`UserProvided`/`BridgeManaged`), `InstalledSha256`, optional `KnownEmulatorId`) and `EmulatorProfile` (per-platform launch config: `EmulatorId`, `PlatformId`, `ArgumentTemplate`) — realizing the split Decision #2 in `PLAN.md` already promised as a "mechanical one-time data migration, not a contract change." `EmulatorService.SaveProfileAsync` finds-or-creates the `Emulator` by `ExecutablePath` (case-insensitive) before upserting the `EmulatorProfile`, so two platforms pointed at the same executable share one `Emulator` row — the actual scenario RetroArch creates, not a hypothetical. `ResolvedEmulatorProfile` (the `Emulator`+`EmulatorProfile` join) is the only shape `LaunchService`/`SettingsViewModel` see; `EmulatorService` stays the sole place that knows the split exists, unchanged from ADR-3's original promise.

`Emulator.ExecutablePath` keeps a DB-level unique index (defense in depth backing the find-or-create logic, mirroring `Game.Path`/`BoxArt.GameId`). `EmulatorProfile.PlatformId` deliberately does **not** get a DB-level unique index — "one active profile per platform" is still enforced, but at the `EmulatorService` layer (find-then-replace), not by the schema, so a future many-profiles-per-platform UI doesn't need another migration. A one-time `LibraryRepository` migration converts any existing legacy `emulatorConfigs` rows into `Emulator`+`EmulatorProfile` on first open post-upgrade (deduping by `ExecutablePath` the same way going forward), then drops the legacy collection — existing Phase 1 configuration survives the upgrade without the user reconfiguring anything.

**Decision — KnownEmulator catalog:** `Resources/KnownEmulators.json` (embedded resource, same pattern as `SeedSystems.json`) holds Bridge's own curated list of installable emulators/cores — `KnownEmulator { Id, Name, Version, DownloadUrl, Sha256, ExpectedSizeBytes, ExecutableRelativePath, Cores: [KnownEmulatorCore { Id, PlatformId, DownloadUrl, Sha256, ExpectedSizeBytes, CoreFileName }] }`. This is **not** fetched live from any third party — reviewing Playnite's YAML emulator/profile catalog as prior art (ADR-3) informed the *shape* of this data, deliberately not its ~190-platform scope; Bridge's version stays small and hand-curated. Every `DownloadUrl`/`Sha256`/`ExpectedSizeBytes` is pinned to one specific, versioned build, captured by hand by a Bridge maintainer from the official source — never "latest", which would have no stable hash to pin against.

**Decision — checksum threat model, made explicit, not assumed:** the manifest's `Sha256` protects against transit corruption, a compromised CDN/mirror serving a different file than what was pinned, and MITM tampering on the download connection. It does **not** protect against the pinned source itself being malicious *at pin time* (a compromised libretro buildbot before the maintainer captured the hash) or against the maintainer capturing a wrong/compromised hash — the manifest is exactly as trustworthy as Bridge's own repo/release process, not a stronger trust root. This distinction is documented here rather than left implicit in a checksum's presence.

**Decision — `DownloadVerificationService`:** downloads to a staging path (`{file}.download` under `Config.EmulatorDownloadsPath`), never the final trusted name, so a failed/tampered download is never reachable at a path Bridge or the user would treat as installed. Size is enforced twice: a `Content-Length`-header pre-check rejects an obviously-wrong-sized response before downloading any bytes when the header is present; a running byte-count cutoff during streaming aborts the instant `ExpectedSizeBytes` is exceeded, bounding worst-case disk usage even when the server never sends `Content-Length`. SHA256 is computed only after the size matches exactly. Any failure (hash mismatch, size exceeded, truncated download, network error) deletes the staging file immediately, `LogError`s (hash mismatch/size exceeded) or `LogWarning`s (network error) with expected-vs-actual detail, and returns a specific, non-generic `DownloadResult.ErrorMessage` — the same never-fail-silently principle already applied to `RomScannerService`/`MetadataService`/`EmulatorService`. Genuine caller-initiated cancellation (`ct.IsCancellationRequested`) is deliberately let propagate as `OperationCanceledException` rather than being caught and reported as `DownloadOutcome.NetworkError` — a stricter distinction than `ImageCacheService`'s existing `TaskCanceledException` handling, justified here because these are large, realistically cancellable downloads (hundreds of MB), not a single small image fetch. An `HttpClient`-level timeout (`OperationCanceledException` where `ct` was *not* the caller's cancellation) is still reported as `NetworkError`.

**Decision — real vs. placeholder manifest data:** RetroArch 1.22.2's win-x64 portable `.7z` was downloaded from the official source (`buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch.7z`) and hashed independently with both `sha256sum` and `certutil -hashfile` (cross-checked, matching) — `Sha256`/`ExpectedSizeBytes`/`DownloadUrl`/`Version` in the manifest are real, verified data, not searched-for or assumed.

`ExecutableRelativePath` is set to `"retroarch.exe"` on a **weaker evidence tier than the rest of this entry** — no `.7z`-capable extraction tool was available in this environment to inspect the downloaded archive directly, so this value comes from consistent, independent third-party documentation (libretro community forums, dependent-project wikis such as EmuVR's setup docs) agreeing the portable Windows `.7z` extracts flat, with `retroarch.exe` at the archive root, not nested in a subfolder. This is corroborating evidence, not the same standard as the hand-verified `Sha256`/`DownloadUrl` above — it should be confirmed by actually extracting the real downloaded archive the next time a `.7z`-capable tool is available, rather than treated as equivalent to direct inspection.

Every `KnownEmulatorCore` entry is **still fully unverified** — `PLACEHOLDER_NOT_VERIFIED` for both `DownloadUrl` and `Sha256`, `0` for `ExpectedSizeBytes` — the same "must be hand-verified from the official source before it's trusted" standard applied to the RetroArch entry itself, not yet done for any core. `KnownEmulatorsManifestTests.KnownEmulators_NoUnverifiedPlaceholdersInReleaseBuild` is compiled only under `#if RELEASE` (confirmed empirically: the SDK does define `RELEASE` for `-c Release`, `DEBUG` for `-c Debug`) — it fails `dotnet test Bridge.slnx -c Release` for as long as any placeholder remains, matching the existing Release Checklist gate, while leaving Debug-config iteration unblocked while cores are sourced and verified one at a time.

**Consequences:**
- ✅ Existing Phase 1 `EmulatorConfig` data survives the upgrade automatically — no re-configuration required, migration is additive and dedupes on the way in
- ✅ One RetroArch install can now back all 15 seed platforms as one `Emulator` row with 15 `EmulatorProfile` rows, the scenario the old 1:1 schema couldn't express
- ✅ The checksum's actual coverage (and non-coverage) is written down, not implied — a future contributor can't mistake "hash present" for "fully trusted at every layer"
- ✅ A hung, oversized, or corrupted download can't silently fill the user's disk or get treated as installed — verified end to end with fake-handler tests (Content-Length pre-check, no-Content-Length streaming cutoff, truncated download, hash mismatch, network error, genuine cancellation)
- ❌ **0 of N `KnownEmulatorCore` entries are verified.** The catalog has a real, hand-verified frontend (RetroArch) and zero real cores — Phase 2's auto-detect/download feature is **not functionally usable** in this state, since there's no complete, verified download-to-launch path for any platform yet. This is the actual remaining blocker, more than any UI or orchestration work — sourcing and verifying at least one core (same process already applied to RetroArch: official source, download, independent hash) is the next concrete step, not optional polish.
- ❌ `ExecutableRelativePath` for the RetroArch entry rests on consistent third-party documentation, not direct inspection of the archive Bridge actually downloaded — a materially weaker evidence tier than `Sha256`/`DownloadUrl`/`ExpectedSizeBytes`. Confirm by extracting the real file once a `.7z`-capable tool is available in this environment.

**Alternatives considered:**

- **Fetch the KnownEmulator manifest live from a Bridge-controlled backend at runtime:** rejected — adds server infrastructure Bridge doesn't have, and doesn't change the trust story (Bridge would still author the manifest); embedding it in the repo ties the pinned hash/URL to the same commit that ships the app code
- **Auto-install via RetroArch's own installer (`RetroArch-Win64-setup.exe`) instead of the portable `.7z`:** rejected for the eventual install step — needs UAC elevation and more failure surface, and fights Bridge's own single-file-no-installer philosophy; noted here because it shaped which artifact was verified (the portable `.7z`, not the installer)
- **Trust a third-party mirror's published hash instead of computing it independently:** rejected — every search result for a RetroArch 1.22.2 hash was a third-party "clean" badge, not an official publication; the same category of second-hand data this project already avoids (see the SteamGridDB `Retry-After` precedent in `DEVELOPMENT.md` → Known Limitations)
- **Catch `TaskCanceledException` broadly in `DownloadVerificationService` (matching `ImageCacheService`'s existing precedent):** rejected — would silently swallow a user's deliberate cancellation of a large in-progress download as a generic network failure instead of propagating it

---

## Creating a New ADR

1. Copy the ADR format block from the section above
2. Assign the next sequential number (e.g., `ADR-1`, `ADR-2`, …)
3. Paste it at the end of this document, before the "Creating a New ADR" section
4. Fill in the sections with concrete information
5. Add it as a new entry in the "Existing ADRs" section above
