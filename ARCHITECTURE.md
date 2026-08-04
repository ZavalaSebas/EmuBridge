# Architecture Decision Records

This document records architectural decisions made during the development of Bridge.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│  Views (WPF — stock; WPF-UI theming pending)      │
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
| Styling/Theming | WPF-UI (lepo.co) — **decided, not yet integrated** | Mica, Fluent Design, already validated in a prior project (SteamManager). Confirmed via a documentation audit (2026-08-06) that Bridge ships on stock WPF today — zero `Wpf.Ui` package reference, no WPF-UI resource dictionaries in `App.xaml`. Tracked as an explicit Phase Polish item (`PLAN.md` → Roadmap), not assumed done just because it was decided early |
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

Error handling: a missing/inaccessible root folder or an unreadable individual file is caught per-item, logged (`LogWarning`), and skipped — never aborts the whole scan. A recognized-extension file with 0 bytes is excluded from being persisted as a `Game` (`LogWarning`) since it cannot be a valid ROM. **The `unknown` platform fallback below is for extensions not yet recognized — a file that might genuinely be a ROM for an unsupported system. It was never intended to catch emulator companion files (saves, save states) that are confidently *not* ROMs at all; that distinction wasn't drawn until a real bug surfaced it — see ADR-13 for the fix (a known-companion-extension check that excludes those files entirely, before the `unknown` fallback is ever reached).** Deeper content corruption is explicitly out of scope — `RomScannerService` only validates what's cheap to check from the filesystem (existence, extension, non-zero size); a corrupt-but-nonzero-size ROM is scanned normally and fails at launch time instead, which is `LaunchService`'s/the emulator's problem to surface. `ScanAsync` returns a structured `ScanResult` (counts added/updated/marked-missing, plus skipped-folders/skipped-files with reasons) so the caller can show a real status, not just a log line nobody reads — same "log AND surface visibly" standard as ADR-5.

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

**Status:** Accepted (mechanism); manifest data fully verified and all 15 of 15 seed platforms interactively install-confirmed — see Consequences and ADR-14's 2026-08-05 update

**Date:** 2026-07-31

**Context:**
Phase 2's "automatic emulator detection/download" (`PLAN.md`) needs Bridge to download and later execute third-party binaries (RetroArch + libretro cores) it didn't build — a materially different trust boundary than Phase 1's `ImageCacheService`, which only ever caches inert image bytes. RetroArch alone covers all 15 seed platforms via different cores, which the Phase 1 `EmulatorConfig` schema (1:1 with `Platform`, reviewed and deliberately scoped down from Playnite's `Emulator 1—* Profile *—* Platform` shape back in ADR-3) cannot express — one physical install needs to back many per-platform launch configs, not one. This ADR covers the schema migration and the download-verification mechanism; it does not build the actual install/extract orchestration (a future ADR) or a real per-platform core catalog (see Consequences).

**Decision — Emulator/EmulatorProfile split:** `EmulatorConfig` is replaced by `Emulator` (a physical install: `ExecutablePath`, `InstallSource` (`UserProvided`/`BridgeManaged`), `InstalledSha256`, optional `KnownEmulatorId`) and `EmulatorProfile` (per-platform launch config: `EmulatorId`, `PlatformId`, `ArgumentTemplate`) — realizing the split Decision #2 in `PLAN.md` already promised as a "mechanical one-time data migration, not a contract change." `EmulatorService.SaveProfileAsync` finds-or-creates the `Emulator` by `ExecutablePath` (case-insensitive) before upserting the `EmulatorProfile`, so two platforms pointed at the same executable share one `Emulator` row — the actual scenario RetroArch creates, not a hypothetical. `ResolvedEmulatorProfile` (the `Emulator`+`EmulatorProfile` join) is the only shape `LaunchService`/`SettingsViewModel` see; `EmulatorService` stays the sole place that knows the split exists, unchanged from ADR-3's original promise.

`Emulator.ExecutablePath` keeps a DB-level unique index (defense in depth backing the find-or-create logic, mirroring `Game.Path`/`BoxArt.GameId`). `EmulatorProfile.PlatformId` deliberately does **not** get a DB-level unique index — "one active profile per platform" is still enforced, but at the `EmulatorService` layer (find-then-replace), not by the schema, so a future many-profiles-per-platform UI doesn't need another migration. A one-time `LibraryRepository` migration converts any existing legacy `emulatorConfigs` rows into `Emulator`+`EmulatorProfile` on first open post-upgrade (deduping by `ExecutablePath` the same way going forward), then drops the legacy collection — existing Phase 1 configuration survives the upgrade without the user reconfiguring anything.

**Decision — KnownEmulator catalog:** `Resources/KnownEmulators.json` (embedded resource, same pattern as `SeedSystems.json`) holds Bridge's own curated list of installable emulators/cores — `KnownEmulator { Id, Name, Version, DownloadUrl, Sha256, ExpectedSizeBytes, ExecutableRelativePath, Cores: [KnownEmulatorCore { Id, PlatformId, DownloadUrl, Sha256, ExpectedSizeBytes, CoreFileName }] }`. This is **not** fetched live from any third party — reviewing Playnite's YAML emulator/profile catalog as prior art (ADR-3) informed the *shape* of this data, deliberately not its ~190-platform scope; Bridge's version stays small and hand-curated. Every `DownloadUrl`/`Sha256`/`ExpectedSizeBytes` is pinned to one specific, versioned build, captured by hand by a Bridge maintainer from the official source — never "latest", which would have no stable hash to pin against.

**Decision — checksum threat model, made explicit, not assumed:** the manifest's `Sha256` protects against transit corruption, a compromised CDN/mirror serving a different file than what was pinned, and MITM tampering on the download connection. It does **not** protect against the pinned source itself being malicious *at pin time* (a compromised libretro buildbot before the maintainer captured the hash) or against the maintainer capturing a wrong/compromised hash — the manifest is exactly as trustworthy as Bridge's own repo/release process, not a stronger trust root. This distinction is documented here rather than left implicit in a checksum's presence.

**Decision — `DownloadVerificationService`:** downloads to a staging path (`{file}.download` under `Config.EmulatorDownloadsPath`), never the final trusted name, so a failed/tampered download is never reachable at a path Bridge or the user would treat as installed. Size is enforced twice, both against `ExpectedSizeBytes` **within a small tolerance** (`SizeToleranceBytes = 32`; originally exact equality — see the 2026-08-02 Update below for why that didn't survive contact with a real rolling channel, and how the ±32 figure was calibrated): a `Content-Length`-header pre-check rejects a response outside that tolerance before downloading any bytes when the header is present; a running byte-count cutoff during streaming aborts once the byte count exceeds `ExpectedSizeBytes + SizeToleranceBytes`, bounding worst-case disk usage even when the server never sends `Content-Length`. Both checks use the same ceiling deliberately — a response accepted by the pre-check must not then get truncated mid-stream by a stricter one. The tolerance is a cheap first-line gate only, not the real protection: SHA256 is computed only after the size falls within tolerance, and the hash comparison itself is untouched — exact equality, no tolerance, exactly as it always has been. Any failure (hash mismatch, size exceeded, truncated download, network error) deletes the staging file immediately, `LogError`s (hash mismatch/size exceeded) or `LogWarning`s (network error) with expected-vs-actual detail, and returns a specific, non-generic `DownloadResult.ErrorMessage` — the same never-fail-silently principle already applied to `RomScannerService`/`MetadataService`/`EmulatorService`. Genuine caller-initiated cancellation (`ct.IsCancellationRequested`) is deliberately let propagate as `OperationCanceledException` rather than being caught and reported as `DownloadOutcome.NetworkError` — a stricter distinction than `ImageCacheService`'s existing `TaskCanceledException` handling, justified here because these are large, realistically cancellable downloads (hundreds of MB), not a single small image fetch. An `HttpClient`-level timeout (`OperationCanceledException` where `ct` was *not* the caller's cancellation) is still reported as `NetworkError`.

**Decision — real vs. placeholder manifest data:** RetroArch 1.22.2's win-x64 portable `.7z` was downloaded from the official source (`buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch.7z`) and hashed independently with both `sha256sum` and `certutil -hashfile` (cross-checked, matching) — `Sha256`/`ExpectedSizeBytes`/`DownloadUrl`/`Version` in the manifest are real, verified data, not searched-for or assumed.

**Update (2026-08-03): the third-party evidence was wrong, confirmed by real use.** `ExecutableRelativePath` originally shipped as `"retroarch.exe"` based on consistent third-party documentation (libretro community forums, dependent-project wikis) claiming the portable Windows `.7z` extracts flat. It doesn't. A real interactive test of the "Auto-Install" button (ADR-14) hit `InstallOutcome.ExecutableNotFoundAfterExtraction` on a real machine — the first genuine extraction of this archive anywhere, dev environment or user machine. Root-caused by opening the actual downloaded-and-verified `retroarch-1.22.2.7z` (still sitting in `%LocalAppData%\Bridge\Downloads\` from the failed attempt — the frontend's own extraction directory had already been cleaned up per ADR-14's failure handling, but the verified source archive is never deleted on a *later* step's failure) with the same `SharpCompress.ArchiveFactory.OpenArchive` code path Bridge itself uses, and listing all 15,646 entries directly. Real structure: everything nested under one top-level folder, `RetroArch-Win64/retroarch.exe` — not flat. `ExecutableRelativePath` is now `"RetroArch-Win64\\retroarch.exe"`, corrected against direct inspection of the exact file that failed, not reasoned about in the abstract. This is exactly the outcome the original "weaker evidence tier" framing anticipated and flagged as a real risk, not a hypothetical one — third-party corroboration was wrong, and only real use caught it.

One `KnownEmulatorCore` entry (`nes` → FCEUmm) is now real and verified, sourced from the actual distribution channel — confirmed there is no separate "stable" channel for cores at all; `buildbot.libretro.com/nightly/windows/x86_64/latest/` is the real one, per an official RetroArch repo issue. The index at that URL was navigated directly (not guessed) to confirm the exact filename (`fceumm_libretro.dll.zip`, not an assumed name); `Sha256`/`ExpectedSizeBytes` were captured the same double-method way as RetroArch (`sha256sum` + `certutil -hashfile`, matching); and because the archive is a `.zip` (unlike RetroArch's `.7z`), `CoreFileName` was confirmed by actually extracting it with `Expand-Archive` — one file, `fceumm_libretro.dll`, flat at the root — direct inspection, not the third-party-documentation tier `ExecutableRelativePath` relies on above.

Cores have no numbered-release equivalent to `KnownEmulator.Version` — "nightly" *is* the real channel, a rolling target. `KnownEmulatorCore.CapturedAt` records the date `Sha256`/`ExpectedSizeBytes` were pinned from that moving source; a later re-download of the same `DownloadUrl` producing a *different* hash is the expected, correct outcome of a rolling channel, not a regression to chase.

**Update (2026-08-04): second `KnownEmulatorCore` entry — `snes` → Snes9x.** Same process as `nes`/FCEUmm, repeated for the second seed platform in line (`snes`, next in `SeedSystems.json` order). Core choice justified against `docs.libretro.com` before downloading anything, not picked arbitrarily: Snes9x's own page describes it as "Most up-to-date libretro Snes9x core available", "Highly accurate SNES emulation", and explicitly "Recommended for netplay" (a claim that implies broad determinism/compatibility); the main alternative, bsnes-mercury, confirmed its own docs don't sacrifice default-setting accuracy either, but ships as three separate performance-tiered variants (performance/balanced/accuracy) that Bridge's single-entry-per-platform catalog shape would have to pick one of arbitrarily — Snes9x avoids that fork entirely as one core. Same editorial-language standard used to lean toward FCEUmm over Mesen/Nestopia/QuickNES for `nes`, not a stronger or different bar.

The real file was downloaded from `buildbot.libretro.com/nightly/windows/x86_64/latest/snes9x_libretro.dll.zip` (the same confirmed-real nightly channel as FCEUmm — RetroArch cores have no separate "stable" channel), hashed independently with `sha256sum` and `certutil -hashfile` (matching: `3e26cd5cc26d9d2ceb9c35fe91026dd56dd667e3cfab28653421de0c79da4156`), and — because it's a `.zip`, same as FCEUmm's archive — the internal `CoreFileName` was confirmed by listing the archive's actual contents (`unzip -l`) rather than assumed from the download URL: one file, `snes9x_libretro.dll`, flat at the root, matching FCEUmm's flat-zip shape (unlike RetroArch's own nested `.7z`, corrected in the update above).

**Update (2026-08-04): remaining 13 `KnownEmulatorCore` entries added in one batch — catalog now covers all 15 seed platforms.** Same process as `nes`/`snes`, run for all 13 remaining platforms before any download started: each core was chosen from `docs.libretro.com`'s own editorial language (explicit "recommended"/"most accurate"/"up-to-date" claims where present) and cross-checked against the real `buildbot.libretro.com/nightly/windows/x86_64/latest/` index — not the docs site — before being fixed as the choice, specifically because a docs-page 404 had already been shown (during this same session, for `snes`'s alternatives) not to mean "not distributed." Two real surprises turned up during that cross-check, both resolved with evidence rather than assumption:
- **PC Engine**: `docs.libretro.com/library/beetle_pce/` 404s, but `mednafen_pce_libretro.dll.zip` (the accurate, non-"Fast" variant) *is* on the buildbot, fresh-built same as everything else. Chosen over `mednafen_pce_fast_libretro.dll.zip` because Beetle PCE FAST's own docs page states it is "Mednafen PCE Fast with the PC Engine SuperGrafx module removed" — implying the base variant retains SuperGrafx compatibility, the same compatibility-first standard applied to Genesis Plus GX below.
- **Atari Lynx**: three differently-named files matched `holani` on the buildbot (`holani.dll.zip`, `holani_libretro.dll.zip`, `holani_retro.dll.zip`). `HEAD` requests on all three showed `holani.dll.zip` and `holani_retro.dll.zip` last-modified November 2024 — over a year stale, not part of the nightly rebuild — while `holani_libretro.dll.zip` was last-modified the same day as every other core captured here, and matches the `<core>_libretro.dll.zip` convention every other verified entry in this manifest follows. The two stale files were never downloaded or considered further; `holani_libretro.dll.zip` is the one in the manifest.

Also confirmed, not assumed: `stella2023_libretro.dll.zip` exists on the buildbot alongside `stella_libretro.dll.zip` and `stella2014_libretro.dll.zip`, but `docs.libretro.com/guides/core-list/`'s official Atari 2600 section only documents `Stella` and `Stella 2014` — `Stella 2023` isn't part of the documented catalog at all, a concrete reason (not just naming-convention inference) to exclude it and keep the original `Stella` pick.

Final mapping (one core reused across platforms where libretro's own core covers multiple systems, same shape as `Emulator`/`EmulatorProfile`'s reuse — Genesis Plus GX claims "100% compatibility with Genesis / Mega Drive... Master System, Game Gear, SG-1000 & Pico" so it backs all three Sega 8/16-bit platforms; SameBoy backs both `gb` and `gbc`):

| Platform | Core | Real filename | Justification |
|---|---|---|---|
| `gb`/`gbc` | SameBoy | `sameboy_libretro.dll.zip` | "extremely accurate"; built-in boot ROMs remove a firmware-sourcing step from Bridge's install flow entirely |
| `gba` | mGBA | `mgba_libretro.dll.zip` | docs explicitly steer users toward it as the accurate upgrade over gpSP |
| `n64` | Mupen64Plus-Next | `mupen64plus_next_libretro.dll.zip` | "latest upstream accuracy improvements... outstanding support of Hires Textures"; actively diverged, GLideN64-capable successor to Parallel-N64 |
| `nds` | melonDS DS | `melondsds_libretro.dll.zip` | explicit: "Use this one unless you're not ready to migrate" |
| `genesis`/`sms`/`gamegear` | Genesis Plus GX | `genesis_plus_gx_libretro.dll.zip` | "100% compatibility" claim spanning all three platforms |
| `atari2600` | Stella | `stella_libretro.dll.zip` | only Atari 2600 core in the official Core List guide without a legacy year suffix; `Stella 2023` confirmed to exist but confirmed *not* documented, excluded on that basis |
| `atari7800` | ProSystem | `prosystem_libretro.dll.zip` | only Atari 7800 core in libretro |
| `pcengine` | Beetle PCE (non-Fast) | `mednafen_pce_libretro.dll.zip` | retains SuperGrafx support the Fast variant explicitly drops, per Fast's own docs |
| `lynx` | Holani | `holani_libretro.dll.zip` | own docs: "get closer to the Lynx hardware and provide a better emulation experience"; confirmed the actively-built file among 3 same-named candidates |
| `wonderswan` | Beetle Cygne | `mednafen_wswan_libretro.dll.zip` | only WonderSwan/Color core in libretro |

Every file was downloaded from the confirmed-real nightly channel, hashed independently with `sha256sum` and `certutil -hashfile` (all 10 distinct downloads matched exactly — 13 manifest entries from 10 files, since Genesis Plus GX and SameBoy are each reused across 3 and 2 platforms respectively), and every archive's contents were listed directly (`unzip -l`) before trusting a `CoreFileName` — all 10 are flat single-file zips (`<name>_libretro.dll` at the root), the same shape already confirmed for `nes`/`snes`, no nested-folder surprise this time.

All 15 seed platforms now have a `KnownEmulatorCore` entry with real, double-hashed, content-inspected data — the catalog itself is complete. **This was not, at the time, the same as 15 platforms being interactively proven to install and launch a game** — only `nes` had been through a live "Auto-Install" click at this point (ADR-14). See ADR-14's 2026-08-04 and 2026-08-05 updates: all 15 platforms have since been interactively confirmed in two real sessions — the data-verified-but-not-click-tested gap this note originally flagged no longer exists. See `DEVELOPMENT.md` → Known Limitations for the current state.

`KnownEmulatorsManifestTests.KnownEmulators_NoUnverifiedPlaceholdersInReleaseBuild` (compiled only under `#if RELEASE`) passes with all 15 entries present (137 tests, Release) — none carry a placeholder value. As before, this test only rejects placeholder sentinel values on whatever entries exist; it has no assertion counting per-platform coverage, so "15 of 15 have a manifest entry" is tracked in prose here and in `DEVELOPMENT.md`, not enforced by a test.

**Update (2026-08-02): the exact-equality size guard broke against the rolling-channel behavior this ADR already documented — investigated, confirmed with real evidence, and revised.** Reported during real Auto-Install testing (re-verifying the per-game emulator override flow, ARCHITECTURE.md → ADR-24): SameBoy's core download failed with *"reported an unexpected size and was rejected before downloading."* Followed the Bug Investigation Process rather than assuming the cause. A real `HEAD` request to SameBoy's exact `buildbot.libretro.com` URL showed a genuine mismatch — 127315 bytes real vs. 127314 pinned, `Last-Modified` one day after the manifest's `CapturedAt: 2026-07-31` — consistent with, not contradicting, this ADR's own already-documented rolling-channel note above ("a later re-download of the same `DownloadUrl` producing a different hash is the expected, correct outcome... not a regression to chase"). Rather than stop at one core, all 13 unique download URLs in the catalog were checked directly (`HEAD`, all 15 `KnownEmulatorCore` entries plus RetroArch's own `.7z`): **11 of 15 core entries (73%) had already drifted from their pinned size**, by amounts from −3 to +2 bytes; RetroArch's versioned/stable-channel download and 4 of the 15 cores still matched exactly. `DownloadVerificationService`'s comparison code was read line-by-line against this evidence, not assumed correct — confirmed it was doing exactly what it was designed to do (exact equality); the data had moved, not the code.

Two actions followed, in order, not conflated:

**First — restored the catalog to today's reality.** All 11 drifted entries were re-downloaded, hashed independently with `sha256sum` and `certutil -hashfile` (matching, same double-method rigor as every entry already in this ADR), and their zip contents listed directly to confirm `CoreFileName` was unchanged — all 8 unique archives (`sameboy`/`sameboy_gbc`, `mupen64plus_next`, `melondsds`, `genesis_plus_gx`/`_sms`/`_gg`, `prosystem`, `mednafen_pce`, `holani`, `mednafen_wswan`) still flat, single-file zips, no structural surprise. `Sha256`/`ExpectedSizeBytes`/`CapturedAt` updated in the manifest for all 11 entries.

**Second — the actual design change: exact equality no longer fit a channel this ADR already knew was rolling.** `SizeToleranceBytes = 32`, calibrated from the real magnitude just observed (max 3 bytes, across 8 different cores in one rebuild cycle) — roughly 10x headroom for a future rebuild's variance to exceed today's sample without tripping the guard, while staying negligible (0.035% of the catalog's smallest file, `prosystem` at 90407 bytes) next to what an actually different or tampered file would realistically shift by. Applied consistently across all three places `expectedSizeBytes` gated a decision in `DownloadVerificationService` — the `Content-Length` pre-check, the streaming loop's early-abort ceiling, and the final post-download size check — deliberately, not just the first one: widening only the pre-check would have let a slightly-larger-than-pinned response start downloading, only to be truncated mid-stream by the old, still-exact ceiling — rejecting something the pre-check had just accepted. SHA256 is untouched: exact equality, no tolerance, still the actual security boundary. Size was always meant to be the cheap gate before spending bandwidth (per the original Decision above), and stays exactly that role — just calibrated to the channel's real, documented behavior instead of a value that only ever matched at the instant it was captured. 4 new boundary tests assert the literal edge, not an approximation: exactly `SizeToleranceBytes` bytes over/under succeeds, one more byte in either direction is rejected — tested through both the `Content-Length` pre-check path and the no-`Content-Length` streaming path, since the two are separate code paths that must agree.

**Same day, a second real failure — the two-layer verification caught a different kind of drift, exactly as designed.** Within the same ~2-hour working session, `stella` (one of the 4 cores that had matched exactly above, never touched in the recapture) failed Auto-Install again — this time a hash mismatch, not a size mismatch. Investigated the same way, not assumed: a real `HEAD` + download + double-hash against the live URL found the file had been rebuilt *again* since the check above, landing by coincidence on the exact same compressed size as the original `2026-07-31` pin (`1433565` bytes, so the size tolerance never even activated — delta was 0) while the actual content, and therefore the hash, genuinely differed (`c6b5b070...` pinned vs. `b9bd6061...` real, confirmed matching between `sha256sum` and `certutil`). Confirmed explicitly this was not a defect introduced by the tolerance change above: the hash comparison is untouched, exact equality, no tolerance — a real content difference was correctly rejected, which is the system working, not failing. Re-pinned `stella`'s `Sha256`/`CapturedAt` the same way as the 11 above.

Two real drift incidents, of two different kinds (size and hash), against two different cores, inside one ~2-hour session — stronger evidence than either incident alone that a hand-recaptured manifest cannot keep pace with this channel's real rebuild frequency. Rather than leave that as an open observation, it's recorded as a known limitation with a concrete (not-yet-built) proposal: see `DEVELOPMENT.md` → Known Limitations and `PLAN.md` → Phase 3 for a maintainer-only "manifest drift check" tool — the same `HEAD`+double-hash procedure just run twice by hand, scripted and run on demand, producing a diff for a maintainer to review and approve rather than auto-trusting a new hash (which would defeat the checksum's actual purpose).

**A third real failure, post-`v0.7.0` — this time investigated as a full-catalog sweep, not core-by-core.** `mgba` failed Auto-Install, reported alongside "some emulators have this problem" — a real signal there could be more than the one named core, not investigated one at a time a third time. All 13 unique download URLs in the catalog (covering all 15 `KnownEmulatorCore` entries) were checked in a single pass — real `HEAD` + download + double-hash for each, compared against the manifest as it stood at that exact moment — rather than repeating the reactive, one-core investigation used for the first two incidents. Found 3 more real mismatches, all the same pattern as `stella`: **`fceumm`** (`9382bc1b...` pinned vs. `1933f460...` real), **`snes9x`** (`3e26cd5c...` vs. `6ffaad03...`), **`mgba`** (`d68d551a...` vs. `a9d8a98c...`) — identical compressed size to their existing pin in all 3 cases (tolerance never activated), hash-only drift, internal archive structure unchanged (still flat, single-file, same `CoreFileName`). All 3 re-downloaded, double-hashed, and re-pinned together, in one pass, not sequentially.

**Full tally for the session: 15 of 15 catalog core entries have now drifted from their pin at least once** — the original 11, `stella`, and now `fceumm`/`snes9x`/`mgba` account for all 15; only the versioned/stable RetroArch frontend never moved, consistent with it not being on the rolling nightly channel at all. Three separate real incidents, each surfaced by a user-reported install failure, escalating from "investigate one core" to "sweep the whole catalog," is stronger evidence than either prior incident that reactive, by-hand maintenance has reached its limit for this catalog. The manifest drift-check tool proposed above — previously tracked as a low-priority Phase 3 "someday" item — is escalated to next priority, ahead of further feature work; see `PLAN.md` → Roadmap for where it now sits and the full reasoning. The tool's proposed shape is unchanged by this escalation — only its priority moved, not its design.

**Consequences:**
- ✅ Existing Phase 1 `EmulatorConfig` data survives the upgrade automatically — no re-configuration required, migration is additive and dedupes on the way in
- ✅ One RetroArch install can now back all 15 seed platforms as one `Emulator` row with 15 `EmulatorProfile` rows, the scenario the old 1:1 schema couldn't express
- ✅ The checksum's actual coverage (and non-coverage) is written down, not implied — a future contributor can't mistake "hash present" for "fully trusted at every layer"
- ✅ A hung, oversized, or corrupted download can't silently fill the user's disk or get treated as installed — verified end to end with fake-handler tests (Content-Length pre-check, no-Content-Length streaming cutoff, truncated download, hash mismatch, network error, genuine cancellation)
- ✅ **15 of 15 seed platforms now have a fully verified `KnownEmulatorCore`** — `DownloadUrl`, `Sha256`, `ExpectedSizeBytes`, and `CoreFileName` all confirmed from the real distribution channel, not assumed. The catalog-completeness gap this ADR originally flagged as open is closed.
- ✅ **All 15 of 15 seed platforms have now been through a real, interactive Auto-Install that succeeded end-to-end** — `nes` originally, 11 more in one session, and the final 3 (`snes`, `atari2600`, `atari7800`) in a later session (see ADR-14's 2026-08-04 and 2026-08-05 updates). The gap between "data-verified" and "interactively confirmed" that this ADR tracked since the catalog was first built no longer exists for any seed platform.
- ✅ `ExecutableRelativePath` was corrected (`RetroArch-Win64\retroarch.exe`, not `retroarch.exe`) after a real install failure, confirmed by directly inspecting the actual archive that failed — see the 2026-08-03 update above. The rest of the extraction/path-resolution code (`EmulatorInstallerService`) needed zero changes; the nested-folder structure was already handled correctly by `Path.Combine`/`ExtractFullPath = true`, it just had the wrong input string.
- ✅ **Size verification now matches the rolling-channel reality this ADR documented from the start**, instead of silently rejecting a legitimate rebuild it should have anticipated — the ±32-byte figure is calibrated from real observed drift (max 3 bytes across 8 cores in one rebuild cycle), not guessed. See the 2026-08-02 update above.
- ❌ The size guard is very slightly weaker in principle (±32 bytes instead of exact) — accepted because SHA256 remains the real, untouched verification boundary; 32 bytes is far too small relative to any of the catalog's files to hide a materially different one
- ❌ **All 15 of 15 catalog core entries drifted from their pin at least once within one working session** — hard evidence that hand-recaptured maintenance for this catalog has reached its practical limit; the manifest drift-check tool proposed above is no longer a low-priority "someday" item because of this, see `PLAN.md` → Roadmap

**Alternatives considered:**

- **Fetch the KnownEmulator manifest live from a Bridge-controlled backend at runtime:** rejected — adds server infrastructure Bridge doesn't have, and doesn't change the trust story (Bridge would still author the manifest); embedding it in the repo ties the pinned hash/URL to the same commit that ships the app code
- **Auto-install via RetroArch's own installer (`RetroArch-Win64-setup.exe`) instead of the portable `.7z`:** rejected for the eventual install step — needs UAC elevation and more failure surface, and fights Bridge's own single-file-no-installer philosophy; noted here because it shaped which artifact was verified (the portable `.7z`, not the installer)
- **Trust a third-party mirror's published hash instead of computing it independently:** rejected — every search result for a RetroArch 1.22.2 hash was a third-party "clean" badge, not an official publication; the same category of second-hand data this project already avoids (see the SteamGridDB `Retry-After` precedent in `DEVELOPMENT.md` → Known Limitations)
- **Catch `TaskCanceledException` broadly in `DownloadVerificationService` (matching `ImageCacheService`'s existing precedent):** rejected — would silently swallow a user's deliberate cancellation of a large in-progress download as a generic network failure instead of propagating it
- **Keep exact-equality size matching and just recapture the manifest whenever it drifts, instead of adding tolerance (2026-08-02):** rejected — treats the symptom, not the cause. The manifest is hand-curated and updated manually, not on any schedule; a rolling nightly channel will keep drifting between captures indefinitely, so this would just mean hitting the same false rejection again on whatever core rebuilds next, for as long as Bridge ships this catalog. The recapture done in this same update was still necessary (it fixes today's state), but tolerance is what stops the failure mode from recurring
- **A much larger or percentage-based tolerance (e.g. ±0.1% of file size):** rejected — not calibrated to anything real; a percentage would scale to thousands of bytes on the larger cores for no evidenced reason, while a small fixed byte count matches what a rebuild's incidental metadata changes (timestamps, embedded commit hashes) actually produce regardless of the binary's overall size

---

### ADR-12: Bundle native WPF dependencies into the single-file `.exe` (`IncludeNativeLibrariesForSelfExtract`)

**Status:** Accepted

**Date:** 2026-08-01

**Context:**
The `v0.1.0` GitHub Release's `Bridge.exe` did not open at all for a real user — no window, no dialog, no visible error, on a machine where it had never run before. Following `DEVELOPMENT.md` → Bug Investigation Process: the first specific hypothesis (an exception thrown during `App()`'s constructor, before `DispatcherUnhandledException` is wired — e.g. `LibraryRepository` failing to open LiteDB because `%LocalAppData%\Bridge\` doesn't exist yet on a clean machine) was tested with temporary file-based diagnostic logging and a simulated clean-machine run, and was **ruled out by direct evidence** — the full startup sequence completed with zero exceptions logged.

The real cause required questioning an assumption made when the release was originally cut: that `dotnet publish -p:PublishSingleFile=true` produces one file and nothing else worth checking. It doesn't, for a WPF app specifically. A full listing of a fresh publish output directory (not just `Bridge.exe`'s own byte size, which is all that had been checked before) showed `Bridge.exe` sitting next to `Bridge.pdb` and five native interop DLLs WPF depends on (`D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`) — `PublishSingleFile` bundles managed assemblies into the `.exe`, but leaves native (non-managed) libraries as loose sibling files by default. `gh release create v0.1.0 publish/Bridge.exe` uploaded only the named `.exe`, never these five files — the release asset was incomplete from the moment it was published, not corrupted afterward. Confirmed directly, not assumed: copying only `Bridge.exe` into an empty folder and running it reproduced the exact symptom — `System.DllNotFoundException` thrown from inside WPF's own native window-subclassing code (`MS.Win32.HwndSubclass`) before a single line of `App()`'s constructor ran (the diagnostic log file was never even created), an unhandled exception written only to stderr — invisible to a user double-clicking from Explorer, exactly matching "doesn't open at all."

**Decision:** `Bridge.csproj` sets `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`, which bundles those native libraries into the single-file `.exe` too, instead of leaving them as required sibling files.

**Trade-off, stated explicitly, not left implicit:** with this flag on, the bundled native libraries are extracted to `%TEMP%/.net` on every startup before the app can use them — the exact "startup extraction cost" already weighed and rejected for SQLite back in Decision #1 / ADR-3 (`LibraryRepository`'s storage choice), where it was one reason LiteDB (pure managed code, no native interop) won over `Microsoft.Data.Sqlite`. It's being accepted here for a different reason: WPF itself requires these native libraries to run at all — there is no "zero extraction" alternative available the way there was for storage (LiteDB vs. SQLite was a real choice; a WPF app either ships these DLLs as loose files or extracts them from the bundle, no third option). This was already true before the fix — the DLLs always had to end up on disk somewhere; the flag changes where they come from (bundled in `Bridge.exe`, extracted to `%TEMP%/.net`) rather than whether the cost exists at all.

Fix confirmed with the same reproduction method that found the bug, not assumed from the flag being set: republished with the flag on, copied only the resulting `Bridge.exe` into an empty folder (no sibling DLLs at all), ran it — it opened normally.

**Consequences:**
- ✅ `Bridge.exe` alone, with nothing else in the folder, now actually matches what "single-file" was always supposed to mean — confirmed by the same isolated-folder reproduction that found the bug, not just by re-running the publish command and assuming success
- ✅ The `v0.1.0` release asset was replaced in place (same tag, same commit) with a working build — no new tag/version, since the underlying commit and source didn't change, only the packaging flag
- ✅ Release Checklist (`DEVELOPMENT.md`) now requires verifying an isolated single-file `.exe` actually launches before it's attached to a release — checking the uploaded asset's byte size alone (what happened for `v0.1.0` originally) is no longer sufficient
- ❌ Slightly larger `.exe` and a small extraction cost on every cold start (`%TEMP%/.net`) — accepted as necessary, not free, per the trade-off above

**Alternatives considered:**

- **Distribute the whole publish folder (zip) instead of a bare `.exe`:** rejected — abandons the single-file, double-click-to-run distribution goal that motivated `PublishSingleFile` in the first place; the bug was an incomplete implementation of that goal, not a reason to give up on it
- **Manually attach the five native DLLs alongside `Bridge.exe` on the release page, no `.csproj` change:** rejected — fixes this one release asset without fixing the underlying publish command, so the same mistake (checking the `.exe`'s size, not the folder's contents) would silently reproduce on every future release unless the checklist also changes; `IncludeNativeLibrariesForSelfExtract` fixes the artifact itself, not just this one upload

---

### ADR-13: Exclude known emulator companion files (saves/save states) from scanning

**Status:** Accepted

**Date:** 2026-08-02

**Context:**
Found through real use (see `PLAN.md` → FR-01/02/03/06/07/09 interactive confirmation): a ROM folder containing `.sav` files (created by mGBA next to the ROM, same base filename) had each `.sav` scanned and persisted as its own `Game`, landing on the `unknown` platform sentinel — a working entry for the real ROM and a broken, unlaunchable second entry for its save file. `ADR-6`'s `unknown` fallback was never designed for this case: that decision covers "extension not yet recognized, might genuinely be a ROM for an unsupported system" — a save file is not an unrecognized ROM, it's confidently *not* a ROM at all. `RomScannerService.ProcessFileAsync` had no concept of a third category between "known ROM extension" and "unknown, could be anything."

**Decision:** A new check, `IsKnownCompanionExtension`, runs immediately after extension extraction and before the platform lookup. Files matching it are excluded entirely — not persisted as a `Game` under any `PlatformId`, unknown or otherwise — and recorded in `ScanResult.SkippedFiles` with an explicit reason, the same mechanism already used for empty/inaccessible files.

Extensions were confirmed against the two emulators actually relevant to Bridge today — mGBA (configured and used in the interactive test above) and RetroArch (Phase 2's install target) — not assumed or carried over from the user's initial guess unverified:
- `sav`, `srm` — battery/SRAM save files. Confirmed for both RetroArch and mGBA.
- `state` with an **optional** numeric suffix (`state`, `state1`, `state2`, ...) — RetroArch save states; RetroArch's own numbered-slot convention omits any separator between "state" and the slot number, and the unnumbered form (slot 0 / quick save) is bare `.state`.
- `ss` with a **required** numeric suffix (`ss0`, `ss1`, ...; bare `.ss` does *not* match) — mGBA's own save-state convention. Confirmed examples were always numbered; no evidence found of an unnumbered form, so none is matched — a deliberate asymmetry with `.state`, not an oversight or a "should be consistent" simplification.
- `rtc` was considered (the user's own suggestion) and explicitly **rejected** — checked directly against mGBA's actual behavior and found that RTC data is appended *inside* the `.sav` file (last 16 bytes) rather than written to a separate file. No standalone `.rtc` file exists for any emulator relevant to Bridge today. Matches the project's standing rule against including unverified data (same standard as the RetroArch/FCEUmm hash verification in ADR-11) — an extension nobody could confirm doesn't go in the list on the theory that it "probably" exists somewhere.

Matching a numeric suffix uses a manual prefix-then-all-digits check (`extension[5..].All(char.IsDigit)` for `state`, `extension[2..].All(char.IsDigit)` for `ss`), not a closed enumeration like `.ss0`...`.ss9` — a fixed list silently stops matching past whatever number was hardcoded (e.g. `.ss10`), where a suffix-shape check has no upper bound to outgrow. Checked for collisions against all 21 of the 15 seed platforms' extensions (`nes, sfc, smc, n64, z64, v64, gb, gbc, gba, nds, md, gen, smd, sms, gg, a26, a78, pce, lnx, ws, wsc`) character-by-character — none start with `sav`, `srm`, `state`, or `ss`, so no legitimate ROM extension is ever misclassified as a companion file.

The check deliberately does not touch `gamesByPath`/`unseenGameIds` — it returns before either is updated. This has a useful side effect, not separately engineered: a `Game` row already incorrectly persisted for a `.sav` file before this fix existed simply stops being "seen" on the next scan and falls into `ADR-6`'s existing mark-missing (not delete) sweep automatically. No migration code was needed or written.

**Consequences:**
- ✅ Save/save-state files no longer create bogus unlaunchable library entries
- ✅ A genuinely unrecognized extension still falls back to `unknown` exactly as before — `ADR-6`'s original intent (don't silently drop a possible ROM for an unsupported system) is untouched, only sharpened with a real third category
- ✅ Pre-existing bad data from before this fix self-heals via the existing mark-missing mechanism, no migration script needed
- ✅ Every included extension is backed by a checked source (RetroArch's/mGBA's actual documented behavior); `.rtc` was checked and excluded on the same standard, not included speculatively
- ❌ Scoped only to RetroArch's and mGBA's conventions — a future emulator with its own save-file naming scheme (e.g. a standalone core added in Phase 2) will need this list extended; not data-driven/user-editable the way `Platform.Extensions` is, since this list isn't expected to grow per-platform the way ROM extensions do

**Alternatives considered:**

- **Treat `.sav`/`.state`/etc. as just another "unknown" file (status quo):** rejected — this is the bug; `unknown` is supposed to mean "might be a real ROM for an unsupported system," and a save file is never that
- **Closed enumeration of exact save-state filenames (`.ss0` through `.ss9`, `.state1` through `.state9`):** rejected — silently stops working past whatever ceiling was hardcoded; a suffix-shape check has no such ceiling
- **Make the companion-extension list data-driven (embedded JSON, like `SeedSystems.json`/`KnownEmulators.json`):** rejected for now — this list isn't user-facing library data and isn't expected to grow with each new platform the way ROM extensions do; revisit if Phase 2's broader emulator catalog makes per-emulator companion extensions a real recurring need
- **Include `.rtc` on the user's original suggestion, without independent confirmation:** rejected — checked mGBA's actual behavior directly and found no standalone `.rtc` file exists; would have been exactly the kind of unverified data this project's standing rule already rejects elsewhere

---

### ADR-14: Emulator install orchestration (`EmulatorInstallerService`)

**Status:** Accepted

**Date:** 2026-08-02

**Context:**
ADR-11 built the mechanism (verified downloads, `Emulator`/`EmulatorProfile` split, `KnownEmulators.json` catalog) but not the orchestration that turns a catalog entry into a working, launchable emulator. With exactly one verified catalog pair (RetroArch + FCEUmm/`nes`), this pass builds and proves the orchestration end-to-end against that one pair — deliberately not adding more cores first, so a design gap in the orchestration itself would surface against a single known-good case rather than being masked or multiplied across many untested cores.

**Decision — extraction library:** `SharpCompress` (NuGet, MIT, pure managed, confirmed compatible with .NET 10, reads both `.7z` and `.zip` through one API) — chosen specifically to avoid repeating ADR-12's mistake of a native dependency sneaking into the single-file build. The real API differs from initial assumptions and was confirmed by reflecting on the installed assembly, not guessed: archives open via `ArchiveFactory.OpenArchive(path)` (not `.Open`), and `IArchiveExtensions.WriteToDirectory(IArchive, path, ExtractionOptions)` / `IArchiveEntryExtensions.WriteToDirectory(IArchiveEntry, path, ExtractionOptions)` extension methods do the actual extraction. Verified against real fixtures, not just compiled: `EmulatorInstallerServiceTests` builds genuine small `.zip` files with `System.IO.Compression.ZipFile` and extracts them through the real `SharpCompress` code path — not mocked.

**Decision — trigger location:** a "Auto-Install" button in `SettingsWindow`, next to manual configuration, visible only when `IEmulatorInstallerService.HasKnownInstallOptionAsync` confirms a fully-verified catalog entry exists for the selected platform (`PlatformConfigItem.HasKnownInstallOption`). Not offered inline on `LaunchService`'s `NoEmulatorConfigured` result — conflating "does the install mechanism work" with "is this the right UX moment to offer it" would make failures harder to isolate to one cause, which matters most while only one core has ever been exercised through this path.

**Decision — `{CorePath}` as a real resolver token, not a baked literal:** `EmulatorProfile`/`ResolvedEmulatorProfile` gained a nullable `CorePath`; `ArgumentTemplate.Expand` gained an optional `corePath` parameter and a `CorePathToken` constant. `LaunchService` now validates `File.Exists(profile.CorePath)` at launch time exactly like it already validates the emulator executable — a new `LaunchOutcome.CoreNotFound`. Baking the core's path as literal text into the stored `ArgumentTemplate` at install time was the simpler alternative, but it would lose that re-validation, the same never-fail-silently standard already applied to the executable path.

**Decision — dedup key for reused installs:** `IEmulatorService.GetInstalledKnownEmulatorAsync`/`RegisterInstalledEmulatorAsync` key by `KnownEmulatorId` (new `ILibraryRepository.GetEmulatorByKnownEmulatorIdAsync`), not `ExecutablePath` the way the manual-entry path (`SaveProfileAsync`) already does — the auto-install path doesn't know the eventual `ExecutablePath` until *after* extraction, so it needs to decide "already installed or not" *before* downloading anything. `EmulatorInstallerService` still re-validates `File.Exists` on the found row before trusting it (a DB row surviving a manually-deleted install folder shouldn't silently short-circuit a real re-install). Both new `IEmulatorService` methods stay the only way `EmulatorInstallerService` touches this data — it never calls `ILibraryRepository` directly, preserving ADR-11's "`EmulatorService` is the sole consumer" invariant.

**Decision — two-level failure handling, matching ADR-11's checksum design exactly:** extraction failure for the *frontend* deletes the entire partial install directory before returning — never leaves an ambiguous half-installed state a later attempt could mistake for real. Extraction failure for the *core*, after the frontend already installed successfully, does **not** roll back the frontend — a working frontend install is a valid, reusable state on its own (the actual reason the `Emulator`/`EmulatorProfile` split exists), so only the core's own partial file is cleaned up. Verified with real tests (`InstallAsync_FrontendExtractionFails_CleansUpPartialDirectory`, `InstallAsync_CoreDownloadFails_DoesNotRollBackAlreadyInstalledFrontend`), not just asserted in prose.

**Decision — progress:** `DownloadVerificationService.DownloadAndVerifyAsync` gained an optional `IProgress<long>?` reporting cumulative bytes — cheap to add, confirmed by reading the existing streaming loop (`totalRead` was already being accumulated per chunk; this is one added line, not a restructure). `EmulatorInstallerService` translates that into short staged status strings ("Downloading RetroArch... 45 / 193 MB") via a plain `IProgress<string>`, consumed by `SettingsViewModel` through the same `IsBusy`/`StatusMessage`/indeterminate-`ProgressBar` pattern `MainViewModel.RefreshLibraryCommand` already established, including a real Cancel button wired to a `CancellationTokenSource`, the same shape as `CancelScanCommand`.

**Consequences:**
- ✅ The install path is proven end-to-end against a real, verified catalog pair — not just designed on paper. `EmulatorInstallerServiceTests` extracts real archives, confirms the two-level failure/cleanup behavior, confirms the reuse-existing-install path skips a redundant download, confirms cancellation propagates, and confirms progress messages are actually reported at each stage.
- ✅ `ArgumentTemplate`'s token system absorbed a second token (`{CorePath}`) with a one-line dictionary addition and an optional parameter — exactly the extensibility ADR-4 designed it for, not a rework.
- ✅ DI resolution of `EmulatorInstallerService`'s two constructors (one for production, loading the embedded catalog; one for tests, accepting an injected catalog) was confirmed against a real `ServiceCollection`/`ServiceProvider`, not assumed from `LibraryRepository`'s similar-looking precedent — a genuinely new combination (skipping a `string` *and* an `IReadOnlyList<KnownEmulator>` parameter together) that hadn't been exercised in this exact shape before.
- ✅ **All 15 of 15 seed platforms have been through a real, interactive Auto-Install that succeeded end-to-end.** See the 2026-08-04 and 2026-08-05 updates below — no seed platform remains data-verified-only.
- ~~No visual/interactive confirmation that the "Auto-Install" button actually renders and behaves correctly in a real running window~~ — closed for all 15 platforms, see the updates below. **This is exactly the gap that surfaced ADR-11's `ExecutableRelativePath` bug** — every automated test used hand-built `.zip` fixtures matching the assumed (wrong) flat structure, so nothing caught the mismatch until a real click, on a real machine, against the real archive. That risk is now retired across the entire catalog, not just the platform where it was first found.

**Update (2026-08-04): 11 more platforms interactively confirmed in one real session.** Per the user's own direct report on their real machine (not reproduced by Claude): Auto-Install clicked and a real ROM launched successfully for `lynx` (Holani), `wonderswan` (Beetle Cygne), `gb`/`gbc` (SameBoy), `genesis`/`sms`/`gamegear` (Genesis Plus GX), `gba` (mGBA), `pcengine` (Beetle PCE), `n64` (Mupen64Plus-Next), and `nds` (melonDS DS) — 8 separate Auto-Install actions covering 11 seed platforms, since 3 of those cores each back multiple platforms (see ADR-11's 2026-08-04 update for why that's the designed shape, not a detection bug — confirmed directly against `KnownEmulators.json` during this same session: `genesis`/`sms`/`gamegear` each have their own `KnownEmulatorCore` entry, but all three point at the identical `genesis_plus_gx_libretro.dll.zip`/hash). Combined with `nes`'s original confirmation, **12 of 15 seed platforms are now interactively proven end-to-end, not just data-verified.** `snes`, `atari2600`, and `atari7800` remain data-verified only — no live Auto-Install click yet for those three.

Two things were investigated during this same session and confirmed, not assumed:
- The Genesis/SMS/Game Gear "same core" observation above is the documented design (ADR-11), not a system-detection bug — confirmed by direct inspection of the manifest, not by re-explaining the design from memory.
- A real gap was found and root-caused, not yet fixed pending a decision: two Atari 2600 ROMs (`.bin` extension — a common headerless dump format for that system) in the user's real `Downloads\ROMS` folder were not detected. Ruled out by direct code inspection, not assumption: `RomScannerService`'s extension matching is fully case-insensitive (`ToLowerInvariant()` + `StringComparer.OrdinalIgnoreCase` in `BuildExtensionMap`), so casing isn't the cause. Confirmed root cause: `SeedSystems.json`'s `atari2600` entry only lists `"a26"`, not `"bin"` — the files fall through to `Config.UnknownPlatformId` (ADR-6's existing, working fallback), not a scanner bug. Not yet resolved — `.bin` is a genuinely ambiguous extension across other systems' disc images, so extending `atari2600`'s `Extensions` array is a real design decision, not a trivial addition; tracked in `DEVELOPMENT.md` → Known Limitations pending that decision.

**Update (2026-08-05): final 3 platforms interactively confirmed — Auto-Install proven end-to-end for all 15 of 15 seed platforms.** Per the user's own direct report on their real machine: Auto-Install clicked and a real ROM launched successfully for `atari7800` (ProSystem), `snes` (Snes9x), and `atari2600` (Stella) — the three platforms that had been catalog-verified since the 2026-08-03/2026-08-04 batch work but never actually click-tested. **No seed platform remains data-verified-only.** The distinction this ADR and ADR-11 tracked throughout Phase 2's build-out — "the manifest has real data" vs. "a real click actually installs and launches a game" — is now fully closed, for every platform, not narrowed. This is the point Phase 2's install-mechanism work was aimed at from ADR-14's original context: proving the orchestration works, not just that it's plausible on paper.

**Alternatives considered:**

- **Offer auto-install inline when `LaunchService` returns `NoEmulatorConfigured`:** rejected for this pass — see trigger-location decision above; worth revisiting once the mechanism has more than one proven core behind it
- **Bake the resolved core path as literal text into `ArgumentTemplate` at install time (no `{CorePath}` token):** rejected — loses launch-time re-validation of the core file's existence, the exact protection already given to the executable path
- **Confirm dialog stating the exact download size:** rejected for this pass — would need `IEmulatorInstallerService` to expose size info just for that one UX nicety; the confirmation dialog is generic ("this may take a while") instead, not worth a new interface method yet
- **Roll back the frontend install if the core step fails:** rejected — directly contradicts the reason the `Emulator`/`EmulatorProfile` split exists; a working frontend is valuable on its own even without this one platform's core

### ADR-15: Remove a `Game` from the library — restricted to `IsMissing`, context-menu UI

**Status:** Accepted

**Date:** 2026-08-05

**Context:**
Phase 1 only ever marks a `Game` `IsMissing = true` (ADR-6) — deliberate, so a temporarily-unavailable folder never silently loses data. But there was genuinely no way to get rid of an entry once confirmed gone for good, found as a real gap during interactive use (the leftover `.sav`-as-Game entry from before the companion-files fix, ADR-13, self-healed to `IsMissing` but sat in the grid forever with no way to clear it). Designed first (New Feature Process), reviewed and approved before any code.

**Decision — scope: `IsMissing == true` only, not "hide any game":** the delete action is only exposed for missing entries, not for present ones. Two reasons, not one: (1) the motivating case, and everything ever tracked in `PLAN.md`'s backlog for this, was specifically about ghost/missing entries — extending to "hide a real game I don't want to see" is a different feature nobody asked for; (2) `RomScannerService.ScanAsync` (confirmed by reading it, not assumed) rebuilds its entire path→`Game` dedup map from the DB at the start of every scan — deleting a `Game` row whose file is still on disk means the next rescan can't tell the difference from a brand-new file, and silently re-adds it with a fresh `Guid` and no `BoxArt` row. Restricting to `IsMissing == true` sidesteps this entirely: a missing game's file isn't present *right now*, so there's no immediate reappearance to be surprised by. `RomScannerServiceTests.ScanAsync_GameDeletedButFileStillOnDisk_ReAddsAsNewGameWithFreshId` locks in and documents the underlying mechanism directly, even though it isn't reachable through the UI today.

**Decision — defense in depth, not just a hidden menu item:** `MainWindow.xaml`'s context menu item is only attached to a tile's `Button.ContextMenu` when `IsMissing == True` (a `Style.Triggers` `DataTrigger`, same one that already sets the missing-tile `Opacity`), but `MainViewModel.DeleteGameAsync` also re-checks `game.IsMissing` itself before proceeding — the same "re-validate at the point of action, not just where it was configured" principle already applied by `LaunchService` (re-checks ROM/executable existence at launch time) and `EmulatorInstallerService` (re-checks `File.Exists` on a found `KnownEmulatorId` row).

**Decision — UI surface: right-click context menu, not a new view or keyboard shortcut:** the entire game tile is already one `Button` fully consumed by `LaunchGameCommand` — no context menu, secondary button, or selection model existed before this. A context menu needed zero new state and doesn't compete with click-to-launch; a keyboard shortcut would have required inventing a "selected tile" concept the grid (`ItemsControl`, not `ListBox`) doesn't have; a separate "manage library" view was rejected as more surface than one action justifies. The `ContextMenu` is a single shared `Window.Resources` instance (`MissingGameContextMenu`) — WPF supports this since only one instance is ever visually open at a time; bindings inside it route through `PlacementTarget` (`Tag`/`DataContext`) rather than relying on `ContextMenu`'s otherwise-unreliable `DataContext` inheritance from its owning `Button`.

**Decision — what gets deleted, and the shared-cache-file edge case:** deleting a `Game` also deletes its `BoxArt` row (`ILibraryRepository.DeleteBoxArtAsync`, new) and the cached box-art file on disk (`IImageCacheService.DeleteCachedImageAsync`, new) — otherwise the file becomes permanent orphaned garbage in `ImageCache\`, accumulating forever. `ImageCacheService`'s cache filename is a hash of the source image *URL* plus target dimensions (confirmed by reading `GetCachePath`), not the `GameId` — meaning two different `Game`s could in theory share one cached file if they ever had identical box-art URLs. Before deleting the file, `MainViewModel` checks whether any *other* `BoxArt` row still references the same `LocalPath` (via the already-existing `GetAllBoxArtAsync`) and skips the file delete if so — cheap to add (one in-memory scan of a small collection), so added now rather than hand-waved as a theoretical risk. Covered directly by `MainViewModelTests.DeleteGameCommand_SharedCachedImage_DoesNotDeleteFileStillReferencedByAnotherGame`. Cache-file deletion itself is best-effort — `ImageCacheService.DeleteCachedImageAsync` logs and swallows `IOException`/`UnauthorizedAccessException` rather than throwing, so a locked or already-gone file never blocks the actual `Game`/`BoxArt` deletion.

**Consequences:**
- ✅ The exact motivating gap (the `.sav`-as-Game ghost entry, and any future case like it) now has a real fix, not just a documented limitation
- ✅ The re-scan-reappearance risk is avoided by scope restriction, not by a runtime check the user could still trigger accidentally — the "delete a present game" path doesn't exist in the UI at all
- ✅ The shared-cache-file edge case is closed by an actual check, not left as an unverified assumption that SteamGridDB URLs are always unique per game
- ✅ `ILibraryRepository.DeleteGameAsync`/`DeleteBoxArtAsync` are generic, policy-free CRUD methods (matching every other method in that class) — the `IsMissing` restriction lives entirely in `MainViewModel`, so a future feature needing unrestricted delete doesn't require touching the repository layer
- ❌ "Hide/remove a game I don't want to see" (present, not missing) is still not possible — deliberately out of scope; would need its own design if ever requested, specifically around the re-scan-reappearance question this ADR sidestepped rather than solved generally
- ❌ No visual/interactive confirmation that the context menu actually renders and behaves correctly in a real running window — same category of gap already noted for ADR-14's Auto-Install button before its first real click; covered here by `MainViewModelTests`/`LibraryRepositoryTests`/`ImageCacheServiceTests` at the ViewModel/service level, not by watching it on screen

**Alternatives considered:**

- **Allow deleting present games too, with a special warning dialog about reappearance on rescan:** rejected — expands scope beyond anything ever requested in the backlog, and adds a second confirmation-copy branch for a use case nobody asked for; revisit as its own design if requested
- **A dedicated "Manage Library" view/window:** rejected — too much surface for one action; nothing in Bridge today has a secondary view, and this doesn't justify being the first
- **Keyboard shortcut (e.g. Delete key) on a focused/selected tile:** rejected — the grid has no selection model today (`ItemsControl` of `Button`s, not `ListBox`); would require inventing that concept just for this one action
- **Cascade the delete inside `LibraryRepository` itself (one method deletes `Game` + `BoxArt`):** rejected — breaks the existing pattern where `Game` and `BoxArt` CRUD are always independent at the repository layer; orchestrating both (plus the cache file) belongs in `MainViewModel`, matching how `RefreshLibraryAsync` already orchestrates multiple services directly rather than through a dedicated facade

---

### ADR-16: Reconcile seed platform data on every open, not just first seed

**Status:** Accepted

**Date:** 2026-08-05

**Context:**
The `.bin`/Atari 2600 gap (found during interactive Auto-Install testing, ARCHITECTURE.md → ADR-14's 2026-08-04 update) needed `atari2600`'s `Extensions` in `SeedSystems.json` corrected from `["a26"]` to `["a26", "bin"]`. Before applying that one-line JSON fix, its actual propagation was checked, not assumed: `LibraryRepository.SeedPlatformsIfEmpty` (confirmed by reading it directly) gates on `platforms.Count() > 0` — the *entire* `Platform` collection, not per-row. That count stops being zero the moment any `bridge.db` is first opened, ever. Editing the embedded JSON alone would only ever reach brand-new databases; every already-seeded database — including the one used throughout this project's own interactive testing, and every real user's — would keep `atari2600.Extensions = ["a26"]` forever, with no code path that would ever revisit it. `Constructor_OnAlreadySeededDatabase_DoesNotReseed` already existed as a test proving this one-shot behavior, which is exactly what raised the question.

**Decision:** a new `ReconcileSeedPlatformExtensions()` runs on every `LibraryRepository` construction (not gated on "collection empty" — cheap enough, ~15 small list comparisons, that gating it wasn't worth the complexity), reconciling each seed platform against whatever's already in the database:
- If a seed platform's `Id` has no matching row at all, it's inserted. This isn't hypothetical for this fix specifically — it's the same one-shot-seeding gap applying to "a whole new platform added to the seed later," a broader instance of the exact bug just found for extensions, closed by the same mechanism rather than left for the next person to rediscover.
- If the row exists, its `Extensions` are unioned (case-insensitive) with the seed's. The union only ever grows the list, never shrinks it — a platform row is free to carry extensions the seed doesn't (e.g. a hypothetical future user-editable extension list) without this silently deleting them on the next startup. A row is only re-`Update`d if the union actually added something, avoiding a write (and a log line) on the common case where nothing changed.
- Deliberately scoped to `Extensions` only — `Name` is never touched. Only `Extensions` was ever the actual problem; syncing `Name` too would risk silently renaming something a user has already seen, a different risk not in scope for this fix.

The embedded-resource-loading code (`Assembly.GetManifestResourceStream` + JSON parse + error logging) was already duplicated once between this and `SeedPlatformsIfEmpty` in the first draft; extracted to a shared `LoadSeedPlatforms()` helper instead of copy-pasting the same try/catch a second time.

Verified against actual pre-existing data, not just a fresh database: `LibraryRepositoryTests` manipulates a raw `LiteDatabase` (same technique already used by `Constructor_LegacyEmulatorConfigsPresent_MigratesToEmulatorAndProfile`) to force an `atari2600` row back to the pre-fix `["a26"]` state, a row with an extra extension the seed doesn't know about, and a row deleted entirely — then reopens through `LibraryRepository` and confirms reconciliation actually reaches it.

**Consequences:**
- ✅ The `.bin` fix actually reaches every database that opens with this build, not just new ones — confirmed with a test that starts from simulated pre-existing (old) data, not assumed from the JSON change alone
- ✅ The same mechanism closes the broader "a whole new seed platform never appears in old databases" gap, not just the narrower extensions case that was actually reported
- ✅ A user's own additions to a platform's `Extensions` (if any future feature ever allows editing them) survive every future seed update — the union is additive-only, by design, not by accident
- ❌ `Name` changes to a seed platform still never propagate to existing databases — deliberately out of scope here; would need its own decision if it ever comes up, since renaming something a user has already seen carries different risk than adding a recognized extension

**Alternatives considered:**

- **Bump a schema/seed version number and gate reconciliation on it changing:** rejected — adds a new piece of state to track and keep in sync with every future `SeedSystems.json` edit, for a check (`platforms.Count()` vs. running the comparison) that's already cheap enough to just always run
- **Only fix the specific `atari2600` row via a one-off migration step (matching `MigrateLegacyEmulatorConfigsIfNeeded`'s shape):** rejected — solves today's instance but not the general class; the next time the seed data needs an update, the exact same investigation would have to happen again from scratch
- **Sync `Name` too, for full seed-data parity:** rejected — out of scope for what was actually broken, and carries a different, unaddressed risk (silently changing what a user already sees)

---

### ADR-17: v1.0 criterion — all of Phase 2 + Phase Polish, Phase 3 excluded by default, disc systems explicitly deferred to v2.0

**Status:** Accepted

**Date:** 2026-08-05

**Context:**
Today's audit work (ADR-15/ADR-16, the "Phase 2 complete" overclaim correction, adding Phase Polish to `PLAN.md`) left `PLAN.md` with an accurate picture of what's actually done vs. remaining for the first time, but no answer to "what does v1.0 actually mean for Bridge" — a real product decision, not something to assume silently the way "Phase 2 complete" was asserted earlier today without checking. Tracking also shifted from phase-gated releases to version cuts (`v0.1.0`/`v0.2.0` each already shipped a coherent chunk without waiting for a whole phase) — a v1.0 criterion needs to say what "coherent chunk" means at the scale of a 1.0, not just for the next small cut.

**Decision:** v1.0 = all of Phase 2 + all of Phase Polish. Phase 3 is excluded by default — not included unless a specific item is explicitly pulled in later, one at a time. Disc-based system support (PS1/PS2/Saturn/etc.) is explicitly named and excluded from the v1.0 path, not left as an open question.

**Reasoning — why Phase 2 is in, fully:** Bridge's core differentiator, per the foundation document's Vision section, is "zero-friction setup" — the auto-install mechanism already shipped in `v0.2.0` is the install-time half of that; the detail panel, favorites, refined "Library" view, "Big Picture" view, and per-game config are the browsing/usage-time half. A v1.0 missing either half reads as an extended MVP demo, not a finished product a stranger could be handed.

**Reasoning — why Phase 3 is out by default:** Phase 3 was scoped as "Could Have" in the original MoSCoW (`BRIDGE_PROJECT_FOUNDATION.md` section 2) — a meaningfully lower commitment tier than Phase 2's "Should Have," never promised as core. RetroAchievements, cheats/mods management, video previews, and a recommendation engine are legitimate post-1.0 growth — comparable tools in this space (early Playnite, EmulationStation) shipped stable, well-regarded versions without these before adding them later. "Excluded by default, not blanket-excluded forever" is deliberate phrasing — nothing stops a specific Phase 3 item from being pulled into the v1.0 path later through the same explicit decision process, just not assumed in by default.

**Reasoning — why disc-based systems are named and deferred, not just silently left in Phase 3:** this one got its own explicit call-out because it's a real, substantial capability gap — a large share of retro-gaming interest is disc-based (PS1 especially, per ADR-7's own Consequences) — not a minor nice-to-have like the rest of Phase 3. It would be tempting to fold it into v1.0 on that basis. Rejected: no checksum/DAT-based detection design exists yet (ADR-6/ADR-7 only note the idea as a natural future direction, never designed for real) — pulling it into the v1.0 path without that design done first is exactly the scope-creep pattern this entire session's audit work was aimed at catching, applied to the roadmap itself. Not dismissed, either — named explicitly as v2.0's first focus (`PLAN.md` → Roadmap) rather than left to blend into an undifferentiated backlog bucket.

**Reasoning — why Phase Polish is in, fully, not optional:** a v1.0 that still looks and behaves like Phase 1's functional-only grid contradicts what shipping a "1.0" signals to a user (stable, ready, finished) — visual/UX roughness is a real quality signal independent of whether anything is technically broken. Transition animations and the general UI pass specifically are load-bearing for that signal, not cosmetic extras that happen to be gated behind Phase 3-tier priority.

**Consequences:**
- ✅ "1.0" now has a real, written criterion instead of being whatever felt done when someone got there — the same standard this session already held every smaller claim to
- ✅ The disc-system capability gap is tracked with real weight (named as v2.0's explicit first focus) instead of disappearing into a generic backlog bucket, while still correctly kept out of v1.0's path given no design exists for it yet
- ❌ The intermediate path (`PLAN.md` → Roadmap → product-story groups, revised 2026-08-06 from a fixed `v0.4.0`-`v0.9.0` ladder to unnumbered thematic groups) is explicitly non-binding — each group earns its version number only once it ships, same discipline already applied to `v0.3.0` and to the Speculative Ideas pool. This ADR fixes the v1.0 *criterion*, not the exact path of versions that gets there.
- ❌ Whether any single Phase 3 item (RetroAchievements, cheats/mods, etc.) gets pulled into the v1.0 path remains genuinely undecided — this ADR deliberately doesn't pre-answer that per-item, only sets the default (excluded unless explicitly decided otherwise)

**Alternatives considered:**

- **v1.0 = Phase 1 + Phase 2 + Phase 3 + Phase Polish, everything:** rejected — Phase 3's "Could Have" items were never promised as core, and waiting for all of them (including a not-yet-designed recommendation engine and RetroAchievements integration) would indefinitely delay a 1.0 that could otherwise ship a genuinely complete, polished core experience
- **v1.0 = Phase 1 + Phase 2 only, Polish deferred to post-1.0:** rejected — a "1.0" that still looks like Phase 1's placeholder-grade grid undersells the actual functional completeness underneath it; the visual/UX bar is part of what "1.0" signals, not separable from it
- **Include disc-based system support in the v1.0 path given how significant a gap it is:** rejected — the design work (checksum/DAT identification) doesn't exist yet; including undesigned work in a version criterion is the same unverified-claim pattern this session's audit already corrected three times today, just applied one level up (to the roadmap instead of to a status line)
- **Leave disc-based systems in an undifferentiated Phase 3/backlog bucket instead of naming it as v2.0's explicit focus:** rejected — burying a capability gap this real in a generic "someday" bucket is how it would get lost the same way Phase Polish itself got lost earlier today; naming it explicitly is the same fix applied preventively

---

### ADR-18: Inline Auto-Install offer from the launch flow; core picker deferred

**Status:** Accepted

**Date:** 2026-08-06

**Correction (2026-08-07):** every `v0.3.0` reference below describes the working label at the time this ADR was written, kept as-is rather than rewritten. Two unrelated, already-committed items (Remove from Library, `.bin` fix — ADR-15/16) turned out to have been sitting unreleased since before this work started, and took the actual `v0.3.0` slot retroactively when the release was finally cut. This ADR's work shipped as `v0.4.0` instead — see `PLAN.md` → Roadmap for the full renumbering note.

**Context:**
`v0.3.0`'s two confirmed Roadmap items (`PLAN.md` → Roadmap) were a core picker UI (for platforms with more than one known-good `KnownEmulatorCore` candidate) and offering Auto-Install inline when `LaunchService` returns `NoEmulatorConfigured`, not just from Settings — both deliberately deferred during Phase 2's build-out (ADR-14) until the mechanism had more proven ground behind it. Investigated before designing either: `KnownEmulators.json` has exactly one `KnownEmulatorCore` per platform, for all 15 seed platforms, confirmed by inspecting the manifest directly — every platform's core was already narrowed to a single best choice during ADR-11's curation (e.g. Snes9x over bsnes-mercury for `snes`). The core picker's premise — a platform with more than one real candidate — doesn't exist anywhere in Bridge's data today.

**Decision — core picker deferred, not built:** Building a selector UI with no real multi-core case to exercise it against would mean testing it only against a synthetic fixture, never a real one — the same standard this project has held every catalog entry to since ADR-11 (independently verified data, not assumed). Removed from `v0.3.0`'s scope rather than forced in; `v0.3.0` ships as a single item, following the same precedent `v0.1.0`/`v0.2.0` already set that a version cut doesn't need a fixed size. Returns to the Roadmap once a real second core is added to some platform's catalog entry, or when disc-based systems (v2.0's named focus) plausibly introduce genuine multi-core choices.

**Decision — inline Auto-Install offer:** `MainViewModel.LaunchGameAsync`, on `LaunchOutcome.NoEmulatorConfigured`, now distinguishes the two cases that outcome already covers (differentiated only by `ErrorMessage` text, confirmed by reading `LaunchService.LaunchAsync` directly — no new `LaunchOutcome` value needed): if `game.PlatformId == Config.UnknownPlatformId`, behavior is unchanged (an unidentified system has nothing installable to offer); if it's a real, recognized platform and `IEmulatorInstallerService.HasKnownInstallOptionAsync` confirms a verified catalog entry exists, a Yes/No dialog offers to install automatically. Reuses `EmulatorInstallerService`/`IProgress<string>` exactly as `SettingsViewModel.AutoInstallAsync` already does — no new install logic. **On success, the game relaunches automatically** (`LaunchService.LaunchAsync` called a second time) rather than requiring a second click — the entire reason to offer this inline instead of only from Settings is collapsing "install, then separately relaunch" into one motion. A relaunch failure (e.g. `CoreNotFound`) surfaces through the same non-`Started` handling as any other launch attempt, with no retry loop.

**Decision — `IsBusy` becomes shared between scan and inline install, not accidental:** `RefreshLibraryAsync` was previously the only long-running `IsBusy`-gated operation in `MainViewModel`; `LaunchGameAsync` had no `IsBusy` guard at all. Adding a second long-running operation (install) without addressing this would let a scan and an install race each other against the same `LibraryRepository`/filesystem — a real latent bug, not specific to this feature. Both commands now guard on the same `IsBusy` flag and share one `CancellationTokenSource` field (renamed `_scanCts` → `_busyCts`, since it's no longer scan-specific), with one Cancel button in `MainWindow`'s existing status bar cancelling whichever operation is currently running. `CancelScanCommand` renamed to `CancelCommand` to match — a UI-facing rename justified by the same sharing, not a cosmetic one.

**Decision — no new progress UI:** `MainWindow`'s status bar (`ProgressBar` + `StatusMessage` + Cancel, `Grid.Row="2"`) was already bound to `IsBusy` generically for the scan flow; because install now shares `IsBusy`/`StatusMessage`/the cancellation pattern, it shows automatically during an inline install with zero new XAML — the existing pattern is reused exactly, not duplicated.

**Consequences:**
- ✅ `v0.3.0`'s scope is now real work with a real design behind it, not a placeholder for an unbuildable UI
- ✅ The unknown-vs-real-platform distinction for `NoEmulatorConfigured` — previously only encoded in `ErrorMessage` text — is now also a real branch point in `MainViewModel`, without needing to change `LaunchService`'s contract
- ✅ The latent scan/install race is closed generally, not just avoided for this one feature — `RefreshLibraryCommand_WhileInstallInProgress_DoesNotStartScan` and `LaunchGameCommand_WhileBusy_DoesNotLaunch` lock in both directions
- ✅ Auto-relaunch after a successful inline install is covered by a real two-call `FakeLaunchService` sequence (`ResultQueue`), not just asserted in prose
- ❌ Core picker remains genuinely undesigned — deferring it doesn't reduce `EmulatorInstallerService.FindKnownCore`'s existing silent-first-match behavior if a second core is ever added without the picker also landing; still only a logged warning (`EmulatorInstallerService.cs:232`). Tracked in `PLAN.md` → Speculative / Future Ideas, not as open Phase 2 scope, since 2026-08-04 — Phase 2 shipped complete without it.
- ✅ Interactively confirmed on a real running instance — main flow, decline, the "unknown"-platform case never offering, and no `IsBusy` conflict between launch and scan, all confirmed. See `PLAN.md` → Roadmap → `v0.4.0`.

**Alternatives considered:**

- **Build the core picker now against a synthetic multi-core test fixture:** rejected — would ship a UI element with zero real data ever exercising it, the same unverified-data risk this project's audit work has repeatedly caught and corrected today
- **Fill `v0.3.0`'s "empty" slot with a different Phase 2 item instead of shipping one item alone:** rejected — `v0.1.0`/`v0.2.0` already established that a version cut is whatever coherent, verified chunk is ready, not a fixed size
- **Show a confirmation dialog after install instead of auto-relaunching:** rejected — reintroduces the exact "click again" friction that offering Auto-Install inline (instead of only from Settings) was meant to remove in the first place
- **Add a new `LaunchOutcome` value to distinguish unknown-vs-real platform instead of checking `game.PlatformId` in the ViewModel:** rejected — `MainViewModel` already has `game` and the same `Config.UnknownPlatformId` constant `LaunchService` uses internally; changing `LaunchService`'s public contract for information the caller can already derive would be unnecessary surface area
- **Separate `CancellationTokenSource` fields for scan vs. install, two Cancel buttons:** rejected — `IsBusy` is already exclusive between the two operations (only one can run at a time), so two separate fields/buttons would just be two names for "the currently running operation," with no real independent state to justify the duplication

---

### ADR-19: Game detail panel — scoped to what SteamGridDB actually provides; new "View Details" context menu item

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
The "Full library" Roadmap group's first item is the game detail panel `BRIDGE_PROJECT_FOUNDATION.md` Section 2 describes: "short blurb/preview text, description, release year, console/system, additional screenshots, thumbnails." Investigated before designing anything: checked what SteamGridDB (Bridge's only integrated metadata source, ADR-8) actually exposes, against the official `node-steamgriddb` wrapper source rather than assumed. Its `SGDBGame` interface has `release_date: number` (Unix seconds) alongside `id`/`name`/`types`/`verified` — confirmed real and available. Every other wrapper method (`getGrids`/`getHeroes`/`getIcons`/`getLogos`) returns image assets only; **no method anywhere returns description text or gameplay screenshots.** SteamGridDB is an art-asset database, not a game-metadata database — a materially different service than what the foundation document's Section 2 wording assumed when it was written. `MetadataService`'s existing `SteamGridDbGame` DTO (ADR-8) only ever parsed `Id`/`Name`, discarding `release_date` from the same response it already fetches.

**Decision — scope the panel to what's real, not what was originally envisioned:** release year, game name, platform, and the existing box art. Description is shown as a static, explicit "Description: not available" — visible, not hidden or silently blank, same never-fail-silently standard applied to every other missing-data case this project has handled (SteamGridDB not-found, rate limits, etc.), just applied here to "feature scope" rather than "error." Screenshots are out of scope entirely for this item — not approximated with SteamGridDB's other grid results (multiple grids per game do come back from `/grids/game/{id}`, `GetFirstGridAsync` already discards all but the first), since those are alternate box art, not gameplay screenshots, and conflating the two would misrepresent what's actually shown. That data is left for "Choose cover," a separately-tracked Speculative idea, not folded into this one. Description/screenshots stay unbuilt until a real decision to add a different external metadata API is made — not designed here, not assumed to be "coming soon."

**Decision — data model: extend `BoxArt`, not a new entity:** `BoxArt.ReleaseYear` (`int?`) — exactly the shape ADR-8 pre-announced back in Phase 2 groundwork ("Phase 2's detail-panel metadata... is purely additive to this new entity, never touching `Game`"). `release_date` arrives in the same search response `FetchBoxArtForGameAsync` already makes; no new HTTP call. `SteamGridDbGame` gained a `[JsonPropertyName("release_date")] long? ReleaseDate` field (the real API key is snake_case; `PropertyNameCaseInsensitive` only handles casing, not the underscore, so this needed an explicit mapping — caught before it shipped as a silent-always-null bug) and a computed `ReleaseYear` (`DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime.Year`, treating `<= 0`/absent as unknown, not epoch 1970).

**Decision — UI entry point: generalize the existing context menu, not a new interaction pattern:** right-click → "View Details," available on every tile, reusing ADR-15's exact reasoning ("the whole tile is already one Button consumed by launch — a context menu doesn't compete with click-to-launch, needs no new state") rather than inventing a hover-icon overlay or a double-click gesture. Required generalizing `MissingGameContextMenu` (ADR-15) into `GameTileContextMenu`, now always attached (previously only attached via a `Style.Trigger` when `IsMissing == True`) with "Remove from Library" gated by per-item `Visibility` instead of whole-menu attachment. The tile's `Tag` — previously bound directly to one command (`DeleteGameCommand`) — now carries the whole `MainViewModel`, so any current or future context-menu item can reach whichever command it needs without adding another routing property.

**Decision — window, not a side panel:** `GameDetailWindow`/`GameDetailViewModel`, same modal-dialog shape as `SettingsWindow` (`Owner = this; ShowDialog();`, `Loaded` → `await viewModel.InitializeAsync()`). A slide-in side panel within `MainWindow` was explicitly not built — that's the kind of visual/animated work `PLAN.md` already scoped to Phase Polish, not something to build early just because this feature touches the main window.

**Consequences:**
- ✅ The panel ships honestly — every field shown is real, verified data; nothing is stubbed, guessed, or silently blank
- ✅ `BoxArt.ReleaseYear` costs zero new API calls — it was always in the response, just discarded before now
- ✅ The `[JsonPropertyName]` gap was caught by writing a real test with a realistic snake_case JSON fixture (`FetchMissingBoxArtAsync_SearchResultHasReleaseDate_PersistsReleaseYear`), not by inspection alone — the same class of bug ADR-11's `ExecutableRelativePath` was, caught this time before a real user's first click, not after
- ✅ `GameTileContextMenu`'s `Tag`-carries-the-whole-ViewModel generalization means the next context-menu item (if any) needs no further plumbing changes to `MainWindow.xaml`
- ❌ Description and screenshots remain genuinely unbuilt — the original foundation-document bullet is only partially delivered; revisiting either requires a new Open-Decision-weight choice of external metadata API, not scoped here
- ✅ Interactively confirmed on a real running instance — cover, name, year, and platform render correctly, "Description: not available" shows as expected, context menu works. See `DEVELOPMENT.md` → Current Status.

**Alternatives considered:**

- **Block the whole feature until a new metadata API (IGDB/TheGamesDB/RAWG) is researched and integrated:** rejected — would indefinitely stall a real, shippable improvement (release year, a real detail view) behind an unrelated, much larger decision; the honest-partial-panel approach ships what's real now and revisits the rest later, explicitly, not silently
- **Show SteamGridDB's additional grid results as "screenshots":** rejected — they're alternate cover art, not gameplay screenshots; presenting them as screenshots would misrepresent what SteamGridDB actually returns, the same category of overclaim this project's documentation audit spent a full session correcting
- **New `GameDetail` entity instead of extending `BoxArt`:** rejected — `ReleaseYear` is exactly the kind of "detail-panel metadata" ADR-8 already earmarked for `BoxArt`; a separate entity for one nullable `int` would be premature structure with no second field to justify it yet
- **Side panel inside `MainWindow` instead of a modal window:** rejected — real animated/layout work that belongs to Phase Polish (`PLAN.md`), not something to front-load into a Full-library-group item
- **Hover-icon overlay on the tile instead of a context-menu item:** rejected — `GameTileTemplate`'s tile is a single `Button` with no room for a second interactive element without restructuring the layout; the context-menu pattern already exists, is already approved (ADR-15), and needed no new state

---

### ADR-20: Favorites and Recently Played split into 2 items; Favorites shipped, embedded on `Game`

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
The "Full library" Roadmap group's second item, `PLAN.md`'s original "Favorites / recently played" bullet, bundles two mechanically different things: favorites is a manual, user-toggled flag; recently played is automatic, updated by the app itself when a game launches. Investigated before designing either, per the standing New Feature Process.

**Decision — split into 2 separately tracked/committed items, not designed or shipped as one:** their verification stories differ enough to justify it — favorites is fully interactive (toggle, see it change, confirm on a real click); recently played, as scoped below, has no consuming UI yet, so its only real verification is inspecting persisted data, not clicking anything. Bundling them would force one feature's commit to wait on the other's unrelated verification path. This ADR covers Favorites in full and Recently Played's design only — Recently Played's implementation is a separate, later commit.

**Decision — Favorites data model: `Game.IsFavorite` (`bool`), embedded, not a new entity:** matches `Game.IsMissing`'s precedent exactly — a simple, user/app-owned flag intrinsic to the game record itself, not provider-fetched metadata like `BoxArt` (ADR-3/ADR-8's reason for keeping `BoxArt` separate doesn't apply here — there's no external source, no fetch status, no cache path to track).

**Decision — Favorites UI: generalize `GameTileContextMenu` again, no new plumbing:** "Add to Favorites"/"Remove from Favorites" (`MenuItem.Style` + `DataTrigger` on `IsFavorite`, swapping the `Header` text) added to the same shared context menu ADR-19 already generalized from `MissingGameContextMenu`. Reuses the tile's `Tag`-carries-`MainViewModel` binding from that same ADR unchanged — the third context-menu item in a row added with zero routing changes, the exact payoff that generalization was built for. A small gold star (`★`) overlay on the tile's cover, visible only when `IsFavorite`, gives immediate visual feedback — without it, toggling would be unconfirmable by looking at the grid, and this project doesn't ship state changes with no visible effect.

**Decision — Recently Played design (approved, not built this pass):** trigger is `LaunchOutcome.Started`, not `GameSessionEndedTask` completing — waiting for session end would mean "recently played" never updates while an emulator stays open (common; could be hours, or indefinitely), and the launch attempt itself is the meaningful "played" event, the same point `MainViewModel` already logs `"Launched {GameName}."` at. Storage: a single nullable `Game.LastPlayedUtc`, not a play-history list — no requirement anywhere asks for history, and a single timestamp is sufficient to sort by "most recent."

**Decision — capture `LastPlayedUtc` ahead of any consuming view, deliberately distinguished from the core-picker deferral (ADR-18):** the core picker was deferred because its triggering condition (a platform with more than one real core) doesn't exist anywhere in Bridge's data — building it would be provably dead code. Recently Played's triggering condition (launching a game) happens constantly, in production, right now — what's missing is a *consumer* of the data, not the data's *source event*. Delaying capture until the "Library" view (next in this group) ships would create a permanent, unrecoverable gap: every game played in the meantime would read "never played" forever, with no way to reconstruct the real history after the fact. Capturing now costs one field write on a `Game` object already in memory (reusing `UpsertGameAsync`, same as Favorites) — cheap enough that the asymmetry with the core picker's situation matters more than the surface-level similarity ("building ahead of a consumer").

**Consequences:**
- ✅ Favorites ships as a fully real, interactively-verifiable feature — toggle, star indicator, persistence, all confirmed on a real click, not just unit-tested
- ✅ `GameTileContextMenu`'s ADR-19 generalization already paid for itself twice — Favorites needed zero changes to the `PlacementTarget`/`Tag` routing mechanism itself, only a new `MenuItem`
- ✅ Splitting the two items means neither commit blocks on the other's different verification path
- ❌ `GameTile.IsFavorite` requires a full `LoadGamesAsync()` rebuild to reflect a toggle (same "rebuilt wholesale, no per-tile live mutation" tradeoff already accepted for the whole `GameTile` design, not a new cost introduced here)
- ❌ `Game.LastPlayedUtc` still has no consuming UI — captured, not surfaced; the "Library" view remains the item that makes it visible

**Update (2026-08-07): Recently Played implemented, same session, separate commit from Favorites — as planned in the split decision above.** `Game.LastPlayedUtc` (`DateTime?`) added; a new `MainViewModel.RecordGamePlayedAsync(Game)` helper sets it and calls `UpsertGameAsync`, awaited (not fire-and-forget) so the write is guaranteed to have landed before the launch command returns — matters here specifically because the only verification available for this pass is inspecting `bridge.db` directly, not a visual confirmation, so persistence timing had to be provably synchronous, not just eventually consistent. Called from both real `LaunchOutcome.Started` sites in `MainViewModel` — the direct `LaunchGameAsync` path and the post-install relaunch inside `OfferInlineAutoInstallAsync` — since both are genuine "the user is now playing this game" events, not just the first one. 4 new tests (2 new, 2 added as extra assertions on existing install/relaunch tests) confirm `LastPlayedUtc` is set on every real `Started` path and stays `null` on every failure path, including the relaunch-fails-after-a-successful-install edge case. 181 Release / 180 Debug tests pass.

**Update (2026-08-07): interactive DB-inspection verification, and a real finding worth recording even though no code changed.** The first two verification attempts (inspecting a copy of the user's real `bridge.db` after a launch) showed `LastPlayedUtc` still `null`, twice, even after a confirmed fresh Debug rebuild. Followed the Bug Investigation Process rather than assuming a code defect: temporary file-based diagnostic instrumentation (same method as ADR-12) added to `MainViewModel.LaunchGameAsync`, `App.OnExit`, and `LibraryRepository.Dispose()`, confirmed step by step that `LaunchAsync` genuinely returned `Started`, `RecordGamePlayedAsync` completed without error, `OnExit` fired on a normal window close, and `LibraryRepository.Dispose()` → `LiteDatabase.Dispose()` ran — with the physical file's `LastWriteTime` only advancing at that final `Dispose()` moment, confirmed independently by the user checking the file's "Date modified" in Windows Explorer directly (not through Claude's own file-inspection tooling, which could not see the live file reliably for reasons not fully diagnosed). **Root cause of the apparent staleness: LiteDB checkpoints writes to the physical file on `Dispose()`/clean shutdown, not per-operation** — expected engine behavior, not a bug. `RecordGamePlayedAsync` was correct the entire time; the investigation was about verification methodology, not a defect. No code change resulted; instrumentation was added and then fully removed per the Bug Investigation Process's own cleanup step. Worth recording for future `bridge.db` investigations: inspecting the file while Bridge is still running, or after a forced process kill (not a clean window close), will not show recent writes — this isn't specific to Recently Played, it applies to every write `LibraryRepository` makes.

**Alternatives considered:**

- **Design and ship Favorites + Recently Played together since they share a `PLAN.md` bullet and a UI area:** rejected — see the split decision above; sharing a bullet in a planning document isn't a reason to couple two mechanically different features' verification and commit boundaries
- **New `Favorite` entity (mirroring `BoxArt`'s shape) instead of `Game.IsFavorite`:** rejected — no external source or fetch state to track, unlike `BoxArt`; would be structure for its own sake, contradicting the same `IsMissing`-precedent reasoning already applied everywhere else on `Game`
- **No visual indicator on the tile, defer to the "Library" view's dedicated favorites filter:** rejected — would ship a toggle with no way to see its own effect until a later, unrelated item ships; the star costs one `TextBlock` and reuses the existing `BoolToVisibilityConverter`
- **Update `LastPlayedUtc` when `GameSessionEndedTask` completes instead of on `Started`:** rejected — ties "recently played" to when the user closes the emulator, not when they chose to play, which could be arbitrarily delayed or never happen in a single session
- **Defer capturing `LastPlayedUtc` until the "Library" view exists to consume it, matching the core-picker precedent:** rejected — the two situations aren't actually analogous; see the capture-now decision above for the specific distinction (real recurring event today vs. zero real cases ever)

---

### ADR-21: "Full library" group's last item — sort/filter for the cover grid, no animation

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
The "Full library" group's last item is the refined "Library" view `BRIDGE_PROJECT_FOUNDATION.md` describes as "Playnite-style cover grid, refined from the Phase 1 functional version." By this point the group's first three items had already produced real, unused-until-now data — `Game.IsFavorite` (ADR-20), `Game.LastPlayedUtc` (ADR-20), `BoxArt.ReleaseYear` (ADR-19) — none of which the grid itself surfaced. Explicitly scoped against Phase Polish before designing: this item is "Full library," not Phase Polish — no transition animations, no theming, functional refinement only, per `PLAN.md`'s own phase boundary.

**Decision — scope: sorting, one filter, one added tile indicator:**
- **Sorting**, 3 modes: `Name` (new explicit default, replacing the previously undefined LiteDB return order), `RecentlyPlayed` (`Game.LastPlayedUtc` descending), `FavoritesFirst` (`Game.IsFavorite` descending, then name).
- **Filtering**: `ShowFavoritesOnly` only. A second candidate, "hide missing," was investigated and explicitly rejected — see Alternatives.
- **Tile indicator**: `BoxArt.ReleaseYear` shown directly on the tile (not just the ADR-19 detail panel) — real data already fetched, zero new calls.

Deliberately **not** in scope, matching what was actually asked rather than expanding it: platform filtering (a real Playnite feature, but never raised as a candidate — would need its own data plumbing and design pass) and a "played N days ago" tile badge (`ReleaseYear` was the specific example given; a relative-time badge wasn't).

**Decision — "hide missing" rejected, not built:** investigated as a real candidate, then rejected on a real edge case: a user with several ghost/missing entries would likely enable a hide filter for exactly that clutter — and then have no way to reach `ADR-15`'s "Remove from Library" context-menu action for those same entries while the filter stays on, since hidden tiles render nothing to right-click. Two fixes were possible (drop the filter entirely, or keep it and add a "N hidden" counter so the user knows to toggle it off before cleaning up) — dropped the filter entirely: `ADR-15`/`ADR-6` already established that `Remove` is the correct tool for confirmed-gone-forever entries, and the existing dimmed-opacity + "(missing)" badge treatment already de-emphasizes missing tiles without needing a second, filter-shaped path to the same declutter goal.

**Decision — nulls-last for `RecentlyPlayed`, alphabetical tiebreak:** never-played games (`LastPlayedUtc == null`) sort after every played game, not before — surfacing untouched games ahead of what was actually played recently would contradict the sort's own name. Within the never-played group, alphabetical by name, so that subset isn't left in an arbitrary/unstable order.

**Decision — in-memory rebuild, no repository round-trip for sort/filter changes:** `_gamesById` (already populated by `LoadGamesAsync`) is the source of truth; a new `_boxArtByGameId` cache was added alongside it (previously `BoxArt` was only used transiently inside `LoadGamesAsync`, never retained) so a sort/filter change can rebuild `GameTile`s purely from what's already in memory. `SortMode`/`ShowFavoritesOnly` are `[ObservableProperty]`s with `partial void On...Changed` hooks calling a new private `RebuildGameTiles()` — no `IsBusy`, no async, no new call to `ILibraryRepository`. `LoadGamesAsync` (used after a real reload — scan, favorite toggle, delete) populates the caches then calls the same `RebuildGameTiles()`, so there's one place, not two, that knows how to turn "the current set of games" into what the grid shows.

**Decision — toolbar controls, not a new view or panel:** a `ComboBox` (3 friendly-labeled `ComboBoxItem`s, `Tag` carrying the real `LibrarySortMode` enum value, `SelectedValuePath="Tag"`) and a `CheckBox`, added to the existing toolbar `StackPanel` next to Add Folder/Rescan/Settings — no new window, no new panel, matching the "functional, not elaborate" bar already applied to every control in `MainWindow`/`SettingsWindow` this phase.

**Consequences:**
- ✅ Every piece of data the "Full library" group produced (favorites, recently played, release year) is now actually visible/usable in the one place a user spends most of their time — the grid itself, not buried behind a detail-panel click
- ✅ Sort/filter changes are instant — no spinner, no `IsBusy`, no repository hit — because they operate on data already in memory
- ✅ The "hide missing" rejection is a real, evidenced decision (a specific interaction conflict with `ADR-15`), not a guess or a default-to-simpler choice
- ✅ `RebuildGameTiles()` being the single path both `LoadGamesAsync` and the property-changed hooks call means a future sort/filter addition doesn't need a second implementation
- ❌ `LibrarySortMode`'s `ComboBox` items store friendly display text a second time (in XAML `Content`), separate from the enum's own member names — acceptable for 3 fixed items, would need a real converter or view-model-exposed display list if this ever needed to be more dynamic
- ✅ Interactively confirmed on a real running instance — sorting in all 3 modes, recently-played prioritizing a same-day launch, the favorites filter in both directions, and the tile's release year, with every previously-built feature (context menu, star, missing badge, detail panel) still working under the new ordering. See `DEVELOPMENT.md` → Current Status.

**Alternatives considered:**

- **Add platform filtering:** rejected — never raised as a candidate for this pass; a real Playnite feature, but its own design/data-plumbing effort, not an opportunistic add-on
- **Add a "played N days ago" relative-time badge on the tile:** rejected for the same reason — `ReleaseYear` was the specific example given to investigate, a relative-time badge wasn't, and it's a meaningfully bigger scope (needs live/refreshing relative-time formatting, not a static string)
- **Keep "hide missing," add a "N hidden — missing" counter instead of dropping it:** rejected — adds a second piece of new UI (a counter) to work around a problem that dropping the filter avoids for free, and the counter itself doesn't restore access to `Remove`, only informs the user they'd need to toggle off first
- **`ICollectionView`/`CollectionViewSource` for sorting/filtering instead of manually rebuilding `Games`:** rejected — more idiomatic WPF for this exact problem, but the codebase doesn't use it anywhere yet, and a plain rebuild from an already-in-memory dictionary is simple enough not to need it; revisit if a future addition (e.g. live search-as-you-type) makes manual rebuilding awkward
- **Nulls-first for `RecentlyPlayed`:** rejected — would put never-played games at the top of a sort whose entire point is surfacing what was actually played recently

### ADR-22: "Big Picture" group — mode toggle on `MainWindow`, "Try Something New" resolves a Speculative idea

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
`BRIDGE_PROJECT_FOUNDATION.md` Section 2 describes the next Roadmap group as "'Big Picture' / streaming-style view with a recommended-games section." Investigated before designing, same standard as ADR-19: Bridge has no genre/tag/similarity data at all (confirmed there — SteamGridDB provides only assets + `release_date`), so any "recommended" content had to be built from real, existing fields (`IsFavorite`, `LastPlayedUtc`, `IsMissing`) or not built at all — a real recommendation engine (content-based scoring) stays out of scope, already tracked separately in `PLAN.md`'s Phase 3 list.

A second, more specific overlap surfaced during investigation, distinct from the recommendation-engine risk: `PLAN.md`'s Speculative/Future Ideas pool (Section 13) already carried its own line — *"'What to play next' — a section in the future Big Picture/streaming view (Phase 2) surfacing unplayed games"* — not yet designed, deliberately not folded into any shipped group. Silently building "unplayed games" as part of this cut would have skipped the same promotion discipline this session's own Roadmap governance rule requires (a Speculative idea needs its own real definition pass before entering a group that ships — see the "Numbering correction" note in `PLAN.md` → `## Roadmap`). Flagged to the user before designing further, per their own explicit instruction to pause on exactly this kind of signal; user confirmed promoting the idea now, with the definition below, as part of this same design pass.

**Decision — mode toggle on `MainWindow`, not a separate `Window`:**
`IsBigPictureMode` (`bool`, `MainViewModel`) — same shape as `ShowFavoritesOnly`, bound directly to a toolbar `CheckBox`, no `RelayCommand` needed. Big Picture is a different *presentation* of the same library (same `Games`, same `LaunchGameCommand`, same `GameTileContextMenu`), not a different data/action surface the way `SettingsWindow`/`GameDetailWindow` are — so it reuses the existing `MainViewModel` instance instead of a new `Window` + composition-root wiring (`OpenXRequested` action, owner/`ShowDialog` lifecycle) that would either duplicate `Games`/`SortMode`/`LaunchGameCommand` in a new `BigPictureViewModel` or need to juggle two top-level windows for one underlying library.

`Window.WindowState` binds to `IsBigPictureMode` through a new `BoolToWindowStateConverter` (`Converters/`, same one-way shape as `InverseBooleanToVisibilityConverter`) — maximizes on toggle, since a windowed "Big Picture" undercuts the point of the mode. Content swap uses two overlapping panels in the same content `Grid`, `Visibility` driven by `DataTrigger`/`MultiDataTrigger` (no new converter needed for that part — same style-trigger pattern already used for `ReleaseYearText`/the missing-tile dimming in `GameTileTemplate`), not `ICollectionView` or a new UserControl.

**Decision — "Try Something New" criterion, promoted from Speculative Ideas:**
Candidates = `Game.LastPlayedUtc is null && !Game.IsMissing` (a missing game can't be launched, so it can't honestly be "try this"), ordered alphabetically by `Name` — deterministic, not random (leaves the separate "Random game" Speculative idea untouched), capped at 10 so it reads as a curated section, not a second full catalog. Section title is **"Try Something New,"** not "Recommended for You" — same honesty standard as ADR-19's "Description: not available": the criterion is real (never played, still present) but isn't personalization or scoring, and the name doesn't claim otherwise. Computed in `MainViewModel.RebuildGameTiles()` alongside the main `Games` rebuild, from `_gamesById` directly (not the sorted/filtered `games` local used for the grid) — independent of `SortMode`/`ShowFavoritesOnly`, no repository round-trip, consistent with ADR-21's existing in-memory-rebuild design. When there are no candidates, the section is hidden entirely (a `DataTrigger` on `TrySomethingNewGames.Count == 0`) rather than shown empty — same reasoning as ADR-21's other empty-state calls elsewhere in this phase.

**Decision — controls: reuse click + context menu, no gamepad/keyboard nav:**
Launching stays a click on `LaunchGameCommand`, same `GameTile` binding as the normal grid. The `GameTileContextMenu` (View Details/Favorite/Remove) stays available in Big Picture mode too — stripping it for aesthetics would remove working functionality without a real reason. Keyboard/gamepad navigation investigated and explicitly deferred: confirmed via a repo-wide search that no gamepad/XInput/DirectInput code exists anywhere in Bridge today — this would be genuinely new input-handling capability, not reuse, and belongs in Phase Polish's "general UI pass" alongside the animation investment `BRIDGE_PROJECT_FOUNDATION.md` Section 5 already earmarks for that phase.

**Decision — favorites/year/missing shown identically, `BigPictureTileTemplate` scales `GameTileTemplate`:**
No new `GameTile` fields — the star, release year, and dimmed "(missing)" treatment already exist and carry over unchanged. `BigPictureTileTemplate` (new `DataTemplate` in `MainWindow.xaml`) is `GameTileTemplate` scaled up (`200`→`280` tile width, `300`→`420` cover height, same 2:3 ratio) sharing the same bindings and the same `GameTileContextMenu` resource — not a rewrite.

**Decision — no animation:** same rule as ADR-21 — functional, not elaborate. No transition/scale effects on mode entry or tile hover; that investment stays Phase Polish's, per `BRIDGE_PROJECT_FOUNDATION.md` Section 5's own note that a scale/`DropShadowEffect` treatment needs a pre-rendered-bitmap approach to avoid jank, not a naive per-frame shader — worth doing once, in Phase Polish, not twice.

**Real bug found on the first interactive run, fixed before confirmation:** `MainWindow`'s own `WindowState` attribute binding through `{StaticResource BoolToWindowStateConverter}`, with the converter declared in that same `Window`'s own `Window.Resources`, threw `XamlParseException` → `"Cannot find resource named 'BoolToWindowStateConverter'"` on launch. Root cause: `Window.Resources` is parsed as a later property element, not yet in scope when the `Window` tag's own opening attributes (including `WindowState`) are evaluated — a real WPF resource-resolution-order gap, not something the Debug/Release build+test pass (276 insertions, 0 warnings) could have caught, since it only surfaces when the BAML is actually loaded at runtime. Fixed by moving the converter to `Application.Resources` in `App.xaml` instead — application-level resources load before any `Window`, so they're already in scope by the time `MainWindow.xaml` parses. Found and fixed because the user ran the app before approving the commit, not because it was caught by review or the test suite.

**Consequences:**
- ✅ Zero new `Window`/`ViewModel`/`Service`/repository methods — every field "Try Something New" needs already existed before this ADR
- ✅ The Speculative-pool overlap was caught and resolved through an explicit pause-and-confirm, not silently — matches the same governance rule this session already applied to v2.0+ auto-promotion
- ✅ `RebuildGameTiles()` stays the single place that turns "the current set of games" into everything the UI shows (main grid + Big Picture's library section + "Try Something New") — no second parallel rebuild path
- ✅ Interactively confirmed on the user's real machine, all 5 points checked: toggle maximizes and restores correctly, "Try Something New" correctly filters to never-played games, click still launches, and the context menu (favorites/details/remove) works on the larger Big Picture tiles
- ❌ `BigPictureTileTemplate` duplicates `GameTileTemplate`'s XAML structure at a different scale rather than parameterizing one template — acceptable for two fixed sizes; would need a real templated-size mechanism (e.g. a shared `Style` with a bindable width) if a third size ever appears

**Alternatives considered:**

- **Separate `BigPictureWindow`/`BigPictureViewModel`:** rejected — real cost comparison during design showed it would either duplicate `Games`/`SortMode`/`LaunchGameCommand` in a new ViewModel or require passing/sharing the existing `MainViewModel` instance across two top-level windows with their own show/hide lifecycle, for a feature that is the same library, not a different data surface
- **Real content-based recommendation (genre/similarity):** rejected outright — no such data exists (ADR-19), and it's explicitly Phase 3 scope in `PLAN.md`, not touched here
- **Random selection for "Try Something New":** rejected — would overlap the separate, still-untouched "Random game" Speculative idea, and a randomized "recommendation" reads as more algorithmic than the honest, deterministic criterion actually justifies
- **Keyboard/gamepad navigation in this cut:** rejected — no existing input-handling code to build on, real new scope, correctly belongs with the rest of the input/animation investment in Phase Polish

### ADR-23: Vertical/poster-style box art for Big Picture mode

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
Big Picture's tiles are portrait-shaped (`BigPictureTileTemplate`, 280x420, ADR-22), but `MetadataService.GetFirstGridAsync` requested `/grids/game/{id}` with no dimension filter, so whichever grid SteamGridDB returned first — orientation unknown — was cached at that fixed portrait size, stretching non-uniformly if it happened to be horizontal (SteamGridDB's classic Steam-grid style). Investigated before designing, against the official `node-steamgriddb` wrapper source, same standard as ADR-8/19: `SGDBImageOptions` (the real type behind `getGridsById`) has a `dimensions?: string[]` filter on the same `/grids/game/{id}` endpoint — not a different asset type the way `getHeroes`/`getIcons`/`getLogos` are. The wrapper itself doesn't hardcode the valid dimension strings (same gap already noted for `release_date` in ADR-19); confirmed via SteamGridDB's own API documentation (the wrapper's `SGDBImage` interface does confirm real `width`/`height: number` fields on every returned grid): `460x215`/`920x430` are the horizontal/landscape pair, `600x900`/`342x482` the vertical/poster pair. None of the 4 is square (`Height == Width`) — confirmed by direct arithmetic on the 4 values, not assumed — so classifying a returned grid by `Height > Width` is exhaustive and unambiguous, given SteamGridDB filters server-side to only return grids matching one of exactly these 4 requested pairs.

The non-uniform-stretch finding itself is tracked separately in `DEVELOPMENT.md` → Known Limitations, not fixed here — a real but distinct problem (`ImageCacheService.ResizeAndSave`'s fixed-dimension, non-aspect-preserving resize), deliberately not folded into this item to avoid inflating one change into two.

**Decision — one combined `/grids/game/{id}` call, not two:** `GetGridsAsync` (renamed from `GetFirstGridAsync`) requests `?dimensions=460x215,920x430,600x900,342x482` and returns the full list; `FetchBoxArtForGameAsync` classifies it into `horizontalGrid = grids.FirstOrDefault(g => g.Height <= g.Width)` and `verticalGrid = grids.FirstOrDefault(g => g.Height > g.Width)`. Zero new HTTP calls per game over what already existed — matches ADR-8's original principle of not duplicating calls unnecessarily. `SteamGridDbGrid` gained `Width`/`Height` (confirmed real fields, not guessed).

**Decision — extend `BoxArt`, not a new entity:** `BoxArt.VerticalLocalPath`/`VerticalStatus`, mirroring `LocalPath`/`Status` — same reasoning as `ReleaseYear` in ADR-19 (metadata from the same fetch attempt for the same game). `VerticalStatus` reuses `BoxArtStatus` rather than a parallel enum — the state machine (`NotFetched`/`Cached`/`NotFoundOnProvider`/`FetchFailed`) is identical, just applied per orientation, and a game can legitimately have one orientation resolved and not the other (SteamGridDB's per-dimension coverage varies per game). No LiteDB migration needed — same as every other additive `BoxArt`/`Game` field added this project (`ReleaseYear`, `IsFavorite`, `LastPlayedUtc`): old rows deserialize with the new field's default (`VerticalStatus = NotFetched`), which is exactly the state that drives retroactive backfill below.

**Decision — retroactivity via a two-orientation skip guard, not a migration:** `FetchMissingBoxArtAsync`'s per-game skip condition changed from "`Status` is terminal" to "`Status` **and** `VerticalStatus` are both terminal." A game cached before this ADR has `Status = Cached` but `VerticalStatus = NotFetched` (the field's default) — not skipped, reprocessed once. `FetchBoxArtForGameAsync` and `PersistBoxArtAsync` were restructured to seed from whatever's already persisted (not a blank slate) on every exit path, including the error ones (rate limit/auth/network failure) — the pre-ADR-23 code built a brand-new `BoxArt` object on every persist call, safe only because the old single-orientation skip guard guaranteed it was never called for an already-`Cached` game; the new guard breaks that guarantee, so this had to change to avoid a rate-limited retroactive pass silently wiping an already-correct horizontal `LocalPath`. `needsHorizontal`/`needsVertical` flags (computed from the existing terminal state) gate which orientation actually gets (re)cached this call, so an already-resolved orientation's image is never re-downloaded just to backfill the other one — confirmed by a dedicated test (`HorizontalAlreadyCachedVerticalNotFetched...`) asserting the horizontal URL is never requested from `ImageCacheService` on a vertical-only backfill pass.

**Decision — fires in the same `FetchMissingBoxArtAsync` batch, no separate trigger:** no lazy/on-demand path for games that happen to appear in Big Picture — the marginal cost of also requesting vertical dimensions in the same already-happening call is small, and a second trigger path would be real, unjustified complexity for that small a saving.

**Decision — `GameTile.BigPictureCoverImagePath`, vertical-preferred with horizontal fallback:** computed in `MainViewModel.BuildTile`: `VerticalStatus == Cached ? VerticalLocalPath : (horizontal LocalPath if Cached, else null)`. `GameTile.CoverImagePath` (used by the normal grid's `GameTileTemplate`) is untouched — stays horizontal-only; only `BigPictureTileTemplate`'s `Image.Source` binding changed, so both "Try Something New" and Big Picture's full-library row pick up the vertical-preferred image automatically, since both already use that template. A real cover beats an empty placeholder — the "No Cover" placeholder only shows when neither orientation is cached, same as today's existing behavior for a game with zero box art at all.

**Consequences:**
- ✅ Zero new HTTP calls in the steady state — one `/grids/game/{id}` call already existed, this just adds a query-string filter and returns more of the same response
- ✅ Retroactive backfill is real, not a hole — a pre-existing `Cached` game gets its vertical orientation resolved automatically on the next scan/refresh, with its already-correct horizontal image preserved untouched
- ✅ `BoxArtStatus` reused rather than duplicated — no new enum for a state machine that's identical per orientation
- ✅ The square-dimension edge case was verified by direct arithmetic on the real requested values, not assumed away
- ❌ The retroactive backfill isn't free — a pre-existing game costs one extra *search* call (grids was always going to run) the first time it's reprocessed after this ships, since no `SteamGridDbGameId` is cached anywhere to skip re-searching by name; acceptable as a one-time cost, not recurring
- ✅ Interactively confirmed on a real running instance — see the Updates below, which found and fixed 3 real bugs during that same confirmation pass, with the final result confirmed by direct file comparison in both the normal grid and Big Picture

**Alternatives considered:**

- **A second, separate API call filtered to vertical dimensions only:** rejected — doubles the SteamGridDB call count per game in a batch this project already treats rate-limit budget as a real, scarce constraint for (ADR-8's early-stop-on-429 behavior); the combined single-call approach costs nothing extra
- **Lazy/on-demand vertical fetch only for games shown in Big Picture:** rejected — a second trigger path is real complexity the small marginal cost of the combined call doesn't justify
- **Cache the resolved `SteamGridDbGameId` on `BoxArt` to skip re-searching on retry/backfill:** rejected as out of scope — not needed to satisfy the 3 design decisions, and the existing code already re-searches by name on every retry of a previously-`FetchFailed` game, so this doesn't introduce a new inefficiency, only inherits an existing one
- **Show the "No Cover" placeholder in Big Picture when no vertical grid exists, instead of falling back to horizontal:** rejected — a real (if not perfectly framed) cover is strictly better than an empty tile, and this project consistently prefers showing real data over hiding it

**Update (2026-08-07) — interactive testing found two real issues, one was a false alarm:**

Two problems were reported after real use: (1) Adventure (Atari 2600) has a verified vertical grid on SteamGridDB, but Bridge didn't show it; (2) box art outside Big Picture looked visibly stretched. Followed the Bug Investigation Process on both — real evidence before any fix.

**Problem 1 — not a bug, a stale-database read (same class of gap as ADR-20's Recently Played investigation).** Made the exact live API call `GetGridsAsync` makes (decrypting the real DPAPI-protected key, same user context) for "Adventure" — SteamGridDB genuinely returns a `600x900` grid, explicitly noted `"Atari 2600 (Reconstructed)"`, inside the real 4-dimension filter. Inspected `bridge.db` directly: it hadn't been written since **before** today's testing (`LastWriteTime` ~16.5 hours older than the newest files in `ImageCache/`) — the same LiteDB Dispose()-only-checkpoint behavior already documented in ADR-20. Computed the real cache-key hash (`SHA256(url)[..16]_{w}x{h}.png`, `ImageCacheService`'s actual formula) for Adventure's real grid URLs and found **both the horizontal and vertical files already correctly cached on disk** — direct, irrefutable proof the fetch and classification worked correctly; the app process just was never closed cleanly, so the successful result never reached the file being inspected. Ruled out, not a code defect.

**Problem 2 — real regression, caused by this exact ADR.** Reviewed `PersistBoxArtAsync`/the classification predicates line by line — no field transposition, no logic bug. Confirmed visually instead: opened the actual cached files for the same game (Mario 64) at both sizes — the `200x300` (horizontal, normal grid) file was visibly squashed; the `280x420` (vertical, Big Picture) file, same source artwork, was correctly proportioned. Root cause: before this ADR, `GetFirstGridAsync` took whichever grid SteamGridDB returned first with **no filter at all** — occasionally that happened to be portrait-shaped and looked fine by luck. `GetGridsAsync`'s dimensions filter removed that luck: the horizontal bucket (`Height <= Width`) can now **only** ever contain a genuinely wide grid (`460x215`/`920x430`), which the pre-existing (already-documented) non-aspect-preserving `ResizeAndSave` then stretches into the normal grid's portrait tile — every time, for every freshly-processed game, not occasionally. A real, self-inflicted regression, not the pre-existing Known Limitation left alone as originally scoped.

**Decision — fix the actual cause (`ResizeAndSave`), not the symptom, closing the Known Limitation for good:** investigated the visual options before implementing, with a real source image (Adventure's actual `460x215` grid), not guessed:
- **Pure letterbox (Uniform-fit, transparent bars):** no distortion, never hides content — but for this specific mismatch (`2.14:1` source vs. `0.667:1` tile), the bars are large (the image only fills ~31% of the tile height).
- **Fill + center-crop:** computed the real numbers — filling the tile's height and cropping the overflow would discard ~69% of the source width (34.5% off each side), which would very likely cut off the title text or character art most SteamGridDB horizontal grids place near the edges.
- Chose **letterbox**, despite the larger-than-ideal bars, because it never risks hiding the exact content (the title) that makes a tile recognizable — same "show what's real, don't hide/fabricate" standard already applied throughout this project (ADR-19's "Description: not available," this same ADR's vertical→horizontal fallback). Background is **transparent**, not a baked-in solid color — the tile's own `Border Background="#333333"` already shows through identically, and staying transparent means the cache doesn't need invalidating if the placeholder color ever changes under a future Phase Polish theming pass.

`ImageCacheService.ResizeAndSave` rewritten: decodes at natural size (no forced `DecodePixelWidth`/`Height`), computes a Uniform-fit scale, renders onto a `DrawingVisual`/`RenderTargetBitmap` sized exactly to the target with the scaled image centered, encodes as PNG (`Pbgra32`, alpha-capable). Verified against a real, diverse sample of the catalog beyond the one Adventure case — Super Mario 64, Zelda: Ocarina of Time, Sonic the Hedgehog, Metroid, Chrono Trigger — all through the real `ImageCacheService`, all correctly proportioned, no distortion, consistent letterbox treatment across different real aspect ratios and source formats (PNG and JPEG alike). 3 new tests assert the letterbox behavior directly (output is always exactly the target size; a corner pixel inside the bar is transparent while the scaled image's center pixel is the real, undistorted color; a source that already matches the target ratio gets no bars at all). Known Limitations row removed from `DEVELOPMENT.md` — the cause is fixed, not documented as unresolved.

**Update (2026-08-07) — a second, real bug reported after the letterbox fix shipped, root-caused with runtime instrumentation, not code review:** the user reported the normal grid and Big Picture appearing to show the wrong orientation for several games, persisting even after a full `ImageCache` wipe and rescan. Three full passes of reading the actual code — `MainWindow.xaml`'s bindings, `MainViewModel.BuildTile`, the whole `MetadataService` classification/persistence chain — found no defect; every layer was provably correct by inspection. Followed the Bug Investigation Process's own instruction to escalate to real evidence rather than keep re-reading code: added temporary file-based diagnostic logging (same method as ADR-12/ADR-20) directly in `BuildTile`, confirmed `bigPicturePath` resolved correctly to the vertical file on every one of 8 real calls across a real rescan. Still visually wrong on screen. Added a second, binding-level temporary `IValueConverter` logging the exact string XAML's `Image.Source` binding received — also correct. With the ViewModel and the binding both proven correct by direct runtime evidence, the remaining candidate was WPF itself: `Image.Source="{Binding PathString}"` uses implicit `string`→`BitmapImage` conversion, which caches decoded bitmaps by URI at the *process* level, separate from `ImageCacheService`'s own file cache — a file deleted and rewritten at the same path (exactly what happened repeatedly during this investigation's own cache-clearing steps) can keep rendering the first bitmap WPF ever decoded for that path.

Two false signals along the way, disclosed rather than smoothed over: an initial visual read of a corrected file was wrongly judged "still stretched" (the transparent letterbox bars weren't visually obvious against the tool's preview background — resolved by reading real pixel alpha values and compositing onto a contrasting color, not trusting a thumbnail by eye); and a first round of testing the eventual fix produced a "still broken" report that turned out to be a mismatch between the real vertical grid's actual content (which includes the same "ADVENTURE" title text as the horizontal grid, just laid out for a portrait canvas) and an inaccurate description ("the castle one") used to ask about it — resolved by sending the actual two files for direct side-by-side comparison instead of describing them from memory.

**Fix:** `Converters/CachedImagePathConverter.cs` — builds the `BitmapImage` explicitly with `BitmapCreateOptions.IgnoreImageCache`, bypassing WPF's own cache entirely. Applied to **both** `GameTileTemplate`'s `CoverImagePath` binding and `BigPictureTileTemplate`'s `BigPictureCoverImagePath` binding — the same risk exists for the normal grid (e.g. a "Remove from Library" followed by the same game reappearing in a later rescan would rewrite a previously-cached path), not just Big Picture. See `DEVELOPMENT.md` → Image Loading for the standing guidance this leaves behind. Verified against the exact diagnosed case (Adventure) via direct file comparison in both the normal grid and Big Picture, confirmed by the user. All temporary instrumentation (the `BuildTile` file logging, the binding-level debug converter) removed per the Bug Investigation Process's own cleanup step. 4 new tests on `CachedImagePathConverter`, including one that reproduces the exact bug end-to-end (write a file, convert it, delete and rewrite the same path with different content, convert again, assert the second result reflects the new content) — the regression test that would have caught this before it ever reached a real session.

**Update (2026-08-07) — orientation preference swapped, a real design change, not a bug fix:** once the cache-bypass fix above was confirmed correct via direct file comparison, the user asked for the opposite of this ADR's original pairing — the normal grid (portrait tile, unchanged shape) showing the **vertical** grid, and Big Picture showing the **horizontal** grid in a landscape-shaped tile, not the portrait one originally designed. `Config.BigPictureCoverWidth`/`Height` changed from `280x420` (portrait) to `460x215` (landscape, matching SteamGridDB's real `460x215`/`920x430` horizontal ratio); `BigPictureTileTemplate`'s tile dimensions changed to match. `MainViewModel.BuildTile`'s preference logic flipped: `CoverImagePath` (normal grid) now prefers the vertical grid with horizontal fallback; `BigPictureCoverImagePath` (Big Picture) prefers horizontal with vertical fallback. The `FetchMissingBoxArtAsync` call site's argument order swapped to match — the horizontal-classified grid is now cached at the (landscape) `BigPictureCoverWidth`/`Height` size, the vertical-classified grid at the (portrait) `CoverWidth`/`Height` size, so each orientation is cached at a size matching where it's actually displayed, avoiding re-introducing the letterbox problem this same ADR already fixed once. `BoxArt.Status`/`LocalPath`/`VerticalStatus`/`VerticalLocalPath` keep their original meaning (still classified by real grid dimensions, `Height`/`Width`) — only which UI slot prefers which, and at what size each gets cached, changed. 3 tests updated to match the new preference direction.

---

### ADR-24: Per-game emulator override ("Rest of Phase 2")

**Status:** Accepted

**Date:** 2026-08-07

**Context:**
`PLAN.md`'s Phase 2 scope included per-game emulator configuration, not just the platform-wide one `EmulatorProfile` already supported. Investigated before designing, against the real code: traced `LaunchService.LaunchAsync` → `EmulatorService.GetProfileForPlatformAsync(platformId)` → `LibraryRepository.GetEmulatorProfileByPlatformIdAsync` (`FindOne(p => p.PlatformId == platformId)`) — confirmed `GameId` appeared nowhere in the resolution chain, and `UpsertEmulatorProfileAsync`'s existing-match also keyed purely on `PlatformId`. Concretely: 20 SNES games sharing one `Snes9x` profile, one needing a different argument (e.g. a graphics-compatibility flag) — today, saving a "per-game" config for that one game would silently overwrite the shared platform-wide profile for all 20.

Along the way, found the `EnsureIndexes()` comment claiming "no unique index on `PlatformId` alone... deliberate loosening for a future many-profiles-per-platform UI" was misleading — the actual `FindOne`/upsert logic already hard-enforced exactly one profile per `PlatformId`, contradicting the comment's own claim. Fixed the comment to be historically accurate as part of this change, not left for later.

UI placement evaluated with the same 3-way cost comparison used for ADR-22 (Big Picture): (1) a new dedicated modal window, (2) folding into the existing `GameDetailWindow`, (3) extending `SettingsWindow` with a per-game selector. `GameDetailWindow`/`GameDetailViewModel`/`GameDetailViewModelTests` are small, proven, and already establish the exact pattern needed (`SetGame` + `InitializeAsync`, `Owner=this; ShowDialog()`, wired via an `Action<Game>` property + `App.xaml.cs` composition root). `SettingsViewModel` carries its own code comment stating it's deliberately designed for "~15 platforms, always all shown" — explicitly not scaled for selecting among a potentially large, unbounded game list. A dedicated window reusing the `GameDetailWindow` pattern was clearly cheapest and most consistent; rejected folding into either existing window.

Re-confirmed (not assumed from earlier in the project) that the core picker still has zero real multi-core cases: `KnownEmulators.json` has 15 platforms, each with exactly 1 core entry, 0 duplicates.

**Decision — nullable `EmulatorProfile.GameId`, resolved with fallback to platform default:** `GameId` is `null` for the existing platform-wide default (unchanged behavior for every pre-existing row) or a real `Guid` for a per-game override. Uniqueness enforced as `(PlatformId, GameId)` via find-then-replace in `UpsertEmulatorProfileAsync`, the same "loosening" pattern (no DB unique index) already established in ADR-11 — this time the pattern is real, not just claimed in a comment. `EmulatorService.GetProfileForGameAsync(game)` tries the game-specific profile first, falls back to the platform default — the fallback means a per-game override window's fields are never blank for an already-configured platform, they just show what will actually be used. `GetEmulatorProfileForGameAsync` matches by `GameId` alone, not also `PlatformId` — sufficient because a `Game`'s `PlatformId` is fixed at scan time, so a `GameId` already uniquely identifies both.

**Decision — no Auto-Install in this flow:** this window is for a targeted per-game *adjustment* to an emulator the user has already configured/installed, not a new installation. Mixing "point this one game at different arguments" with "download and install a new emulator" would conflate two different actions in one window; Auto-Install stays exclusively in `SettingsWindow`.

**Decision — direct delete on `DeleteEmulatorProfileForGameAsync`, no "still referenced" check:** explicitly investigated whether a per-game `EmulatorProfile` row could ever be shared or referenced elsewhere, the same way `BoxArt`'s cached image files are (deduped by URL hash, so one file can be referenced by multiple `BoxArt` rows and needs a "still referenced by another game?" check before deletion in `DeleteGameAsync`). Confirmed by exhaustive `grep` across `Bridge/Models`, `Bridge/Services`, `Bridge/ViewModels` for every `EmulatorProfile` reference: no other entity stores `EmulatorProfile.Id` as a foreign key, and the `(PlatformId, GameId)` key with a real `GameId` uniquely identifies exactly one game's row — it can never be shared the way a deduped cache file can. `MainViewModel.DeleteGameAsync` calls `DeleteEmulatorProfileForGameAsync(game.Id)` unconditionally, right alongside the existing `BoxArt` cleanup, with no reference check — different from `BoxArt`'s pattern deliberately, not by oversight.

**Consequences:**
- ✅ The 20-SNES-games scenario works as expected: one game's override never touches the other 19's shared platform default, and vice versa — proven by dedicated `EmulatorServiceTests` exercising the real `EmulatorService` against `FakeLibraryRepository`, not just asserted by design
- ✅ A misleading doc-vs-code comment was caught and fixed as part of this change, not left to mislead the next reader
- ✅ Removing a game cleans up its override with no orphaned `EmulatorProfile` row left keyed to a deleted `GameId`
- ✅ UI reuses a proven, cheap pattern (`GameDetailWindow`'s) rather than inventing a new one or overloading an existing window built for a different shape of data
- ❌ No visual/interactive confirmation yet that the new window renders and behaves correctly in a real running instance — same category of gap noted at other UI features' first ship this phase; covered here by `EmulatorOverrideViewModelTests`/`MainViewModelTests`/`LaunchServiceTests` at the service/ViewModel level, not by watching it on screen

**Alternatives considered:**

- **Fold per-game override into `GameDetailWindow`:** rejected — `GameDetailWindow` is read-oriented (cover, release year, platform), adding editable emulator fields there mixes concerns and complicates a window that's currently simple by design
- **Extend `SettingsWindow` with a per-game selector:** rejected — `SettingsViewModel` is explicitly designed and documented for a small, always-fully-shown platform list, not for selecting among a potentially large game list
- **DB unique index on `(PlatformId, GameId)` instead of find-then-replace:** rejected for the same reason ADR-11 already rejected it for `PlatformId` alone — consistent with the existing pattern, no new migration machinery introduced for this feature alone
- **"Still referenced elsewhere?" check before deleting a per-game `EmulatorProfile`, matching `BoxArt`'s pattern:** rejected after explicit investigation — the `(PlatformId, GameId)` key already guarantees exclusivity to one game, so the check would be dead code that never trips

---

### ADR-25: Automated manifest drift detection + live catalog fetch

**Status:** Accepted

**Date:** 2026-08-02

**Context:**
Three real incidents in one working session (ADR-11's 2026-08-02 updates) showed the libretro nightly channel rebuilds far more often than a hand-maintained, hand-recaptured `KnownEmulators.json` can keep pace with — 15 of 15 catalog core entries drifted from their pin at least once that same session, one of them twice. Manually investigating and recapturing every time cost real, repeated effort and always started from a user-reported install failure. `DEVELOPMENT.md` → Known Limitations already logged this as a known, structural gap with a proposed (not built) direction: a maintainer-only drift-check tool.

Two related but distinct problems, addressed as two pieces:
1. **Keeping the source of truth (the repo's `KnownEmulators.json`) current** — a detection + correction mechanism.
2. **Getting a fix to users faster than Bridge's own release cadence** — today, even a same-day manifest fix only reaches users on the next tagged version.

**Decision — Piece 1, fully automated detection with a human-gated merge, not a local tool:** a scheduled GitHub Action (`.github/workflows/manifest-drift-check.yml`) runs the same real HEAD/download/double-verification procedure already performed by hand three times on 2026-08-02, and opens a pull request when it finds drift — merge stays a manual click, always. Explicitly **not** a console tool a maintainer runs locally: the goal is that the maintainer never has to run anything or remember to check — the bot notifies, a click approves. `GITHUB_TOKEN` (no new PAT) is sufficient for opening the PR, confirmed against GitHub's own documentation, but requires the repository's "Allow GitHub Actions to create and approve pull requests" setting enabled (`Settings → Actions → General`) — confirmed via `gh api repos/.../actions/permissions/workflow` that this repo has it disabled by default (the default for any repo created after February 2023), a one-time prerequisite, not a workflow bug.

Lives under `tools/ManifestDriftCheck/` — a plain console project (`net10.0-windows`, matching `Bridge.Tests`' own TFM so it can `ProjectReference` `Bridge.csproj` and reuse `KnownEmulator`/`KnownEmulatorCore` directly, avoiding a second, driftable copy of the schema), referenced only from the workflow, never documented as a maintainer command, never shipped in `Bridge.exe`'s publish output (`release.yml` publishes `Bridge/Bridge.csproj` alone).

**Decision — targeted text patching, not deserialize/reserialize:** `ManifestPatcher` locates each drifted entry by its unique `"Id"` line and replaces only `Sha256`/`ExpectedSizeBytes`/`CapturedAt` in place, preserving formatting and every untouched entry byte-for-byte — the same discipline already used for every manual edit to this file today. A structural anomaly (the archive no longer contains the expected `CoreFileName`/`ExecutableRelativePath` — the exact class of surprise ADR-11 already hit once for RetroArch's nested folder) is **never** patched automatically; it's reported and left for a human, exactly like that original finding was.

**Decision — bounded concurrency (2), 6-hour cadence, not a guess:** no documented rate limit was found for `buildbot.libretro.com`, but its response headers (`Server: cloudflare`, `cf-cache-status: HIT`/`REVALIDATED`, confirmed directly during the 2026-08-02 investigation) show a heavily-cached static host already serving RetroArch's own much larger real-world traffic — a handful of requests every few hours is negligible against that. Weighed against real evidence the same drift can recur within hours (not days), a daily check would leave too wide a window; 6 hours (4×/day, ~52 requests/day across 13 unique catalog URLs) is a deliberately conservative middle ground, not "as often as possible."

**Decision — Piece 2, `IManifestUpdateService` fetches Bridge's own `main` on every startup, fire-and-forget, silent fallback:** a new service fetches `Bridge/Resources/KnownEmulators.json` straight from `raw.githubusercontent.com/.../main/...` on every launch (the manifest itself is small — a few KB, not the multi-hundred-MB core archives Piece 1 downloads, so per-launch cost is negligible and the 6-hour-cadence reasoning above doesn't apply here). Never blocks startup or an Auto-Install attempt: the call is fire-and-forget, with a short (5s) timeout, and any failure — network down, timeout, malformed response, a response still carrying `Config.UnverifiedManifestPlaceholder` — falls back silently to whatever was already available (a previously fetched copy, or the embedded resource baked into this build). `EmulatorInstallerService`'s catalog is now a `Func<IReadOnlyList<KnownEmulator>>` provider rather than a fixed snapshot captured once at construction — it's registered as a singleton, resolved once early at startup (before a background refresh would have had time to complete), so a fixed list would never see a later-completed fetch for the rest of that session; the provider always reflects whatever `IManifestUpdateService.GetCatalog()` currently considers best, checked fresh on every install attempt.

**Decision — this is the one deliberate exception to "never fail silently," named explicitly:** every other network failure in Bridge (SteamGridDB, emulator downloads, install extraction) surfaces a specific, non-generic message per `DEVELOPMENT.md`'s standing rule. This one doesn't, on purpose: a failed background refresh the user never asked for, comparing a state they have no context for, has nothing actionable to show — the embedded fallback is exactly what shipped in this build and is already known-good. Interrupting the user over it would be worse than silence. The failure is still logged (`ILogger`, not swallowed entirely) for anyone inspecting logs later; only the user-facing surface is silent.

**Decision — reconciling with ADR-11's original rejection, not ignoring it:** ADR-11 explicitly considered and rejected "Fetch the KnownEmulator manifest live from a Bridge-controlled backend at runtime" — *"adds server infrastructure Bridge doesn't have, and doesn't change the trust story... embedding it in the repo ties the pinned hash/URL to the same commit that ships the app code."* This decision differs in the first half — no backend to build or host, just the same GitHub repo the compiled binary itself already comes from — but the second half is a real property that this decision does trade away: a given `Bridge.exe`'s catalog is no longer fully pinned to its own build; the same binary's behavior can change over time as `main` changes, decoupled from any new release. That's not a side effect, it's the actual point — closing the freshness gap without waiting to cut a version — but it has to be named in these terms, not glossed over. It is not a security regression: the trust chain becomes `main` (already Bridge's supply-chain root — the shipped binary comes from this same repo) → a human-reviewed, merged PR (Piece 1, merge never automatic) → JSON fetched over HTTPS, so nothing bypasses human review, only *when* a running instance sees the result changes. And critically, even a wrong or unexpected fetched manifest value is still caught by `DownloadVerificationService`'s own untouched, exact SHA256 check on the actual downloaded binary — the manifest only ever says what to fetch and check against, never a bypass of that check itself.

**Consequences:**
- ✅ The recurring-maintenance gap logged in `DEVELOPMENT.md` → Known Limitations is now addressed by a real, running mechanism, not just a documented "someday"
- ✅ A same-day catalog fix can reach a running Bridge instance without waiting for a new tagged release — closes the actual gap the 2026-08-02 incidents exposed, not just the symptom
- ✅ Merge stays a human decision at every layer — the drift-check PR, and by extension what a live-fetching Bridge instance will ever pick up, both require a click, never automatic (see the auto-merge idea explicitly deferred in `PLAN.md` → Speculative Ideas, pending real evidence)
- ✅ `EmulatorInstallerService`'s existing tests, contract, and install-time behavior are unchanged beyond the catalog becoming a provider delegate instead of a fixed list — a small, mechanical signature change (`() => catalog` in tests), not a rewrite
- ❌ A given `Bridge.exe` build's behavior is no longer fully reproducible from its own version alone — a real, named trade-off (see the reconciliation above), not an oversight
- ❌ The "silent on failure" exception for `IManifestUpdateService` is a deliberate deviation from Bridge's own standing rule — documented here and in the class's own doc comment specifically so it doesn't read as an inconsistency later
- ❌ Requires a one-time manual repository setting change (`Allow GitHub Actions to create and approve pull requests`) before the workflow's PR step will succeed — not something the workflow itself can bootstrap

**Alternatives considered:**

- **A local console tool a maintainer runs by hand, as an intermediate step before full automation:** rejected outright, not just deferred — the explicit goal is that the maintainer never runs anything locally or has to remember to check; building the manual version first would have been throwaway work
- **Auto-merge the drift-check PR when the change looks routine:** rejected for now, recorded in `PLAN.md` → Speculative Ideas as a real future candidate once there's accumulated evidence the drift pattern is consistently benign — not a decision to make without data
- **Deserialize/reserialize the whole manifest when patching:** rejected — same reasoning as every manual edit this session: reformatting untouched entries would bury real data changes in formatting noise
- **A percentage- or channel-aware variable cadence for the drift-check schedule:** rejected — no evidence justifies anything more elaborate than a single conservative fixed interval yet; revisit only if real operation shows 6 hours is wrong in either direction
- **Cache the live-fetched manifest with a longer TTL (e.g. 24h) instead of refetching every launch:** rejected — the fetch is KB-scale, not the multi-hundred-MB core downloads Piece 1 deals with, so the cost that would justify a TTL doesn't exist here; refetching every launch is simpler and always at least as fresh

---

### ADR-26: Hardcoded download-host allow-list

**Status:** Accepted

**Date:** 2026-08-02

**Context:**
ADR-25's live catalog fetch means `DownloadVerificationService` can now receive a `DownloadUrl`/`Sha256`/`ExpectedSizeBytes` triple sourced from a manifest fetched live from `main` on GitHub, not only the copy embedded at build time. ADR-25 argued this doesn't bypass human review, since `main` only changes via a merged PR — but that argument has a real gap it didn't close: **the exact SHA256 hash check that protects every download today only proves the downloaded bytes match what the manifest claims, never that the manifest's claim itself is legitimate.** If the source of the manifest were ever compromised (a subverted PR, a compromised maintainer account, a supply-chain attack on the repo itself), an attacker controls both the `DownloadUrl` and the `Sha256` it's checked against — the hash check would pass trivially for their own malicious binary, hashed against their own supplied value. This is not a new gap introduced by ADR-25; ADR-11 already named the same boundary for the original embedded manifest ("does NOT protect against the pinned source itself being malicious at pin time"). ADR-25 made it more visible by adding a second, live path into the same trust chain, but didn't add a mitigation for it — this ADR is that mitigation.

**Decision — a hardcoded allow-list of trusted download hosts, checked in `DownloadVerificationService`, never sourced from the manifest:** `Config.AllowedDownloadHosts` is a compiled `HashSet<string>` — today just `{ "buildbot.libretro.com" }`, confirmed by checking every one of the 16 real catalog entries (RetroArch frontend + all 15 cores), not assumed. `DownloadAndVerifyAsync` validates `sourceUrl` against it — `https` scheme required (not just an allowed host: a plain `http://` to the same host would reopen the MITM risk ADR-11's own threat model already covers) — **before** attempting any network connection, not after. A rejected source never generates an outbound request at all.

**Decision — enforced in `DownloadVerificationService`, not `EmulatorInstallerService` or any other caller:** this is the single chokepoint every emulator/core download already passes through, embedded catalog or live-fetched alike (ADR-25). Enforcing here, once, means no current or future caller of this service needs to remember to re-check it — the same centralization principle already used for the hash/size checks living in this class.

**Decision — the two external fetches Bridge now makes stay deliberately separate, not merged into one list:** `IManifestUpdateService` (ADR-25) fetches `Config.ManifestUpdateUrl` — `raw.githubusercontent.com`, a small JSON *description* of what to download, never executed itself — while `DownloadVerificationService` fetches and extracts *executable content* that gets run. `Config.ManifestUpdateUrl` is already itself a single fixed, compiled URL with no variability to constrain — there is no manifest-driven redirection possible for that fetch, so no allow-list concept even applies to it. Adding `raw.githubusercontent.com` to `AllowedDownloadHosts` would blur a distinction worth keeping sharp: one list gates what Bridge is willing to *run*, the other fetch has no list because it has no variable destination in the first place.

**Decision — adding a new host is a source-code change, not a data change, on purpose:** expanding `AllowedDownloadHosts` means editing `Config.cs` and shipping a new Bridge version through the normal build+test+review+commit process — never something the drift-check bot (ADR-25) can do, even indirectly. That bot only ever rewrites `Sha256`/`ExpectedSizeBytes`/`CapturedAt` on entries whose `DownloadUrl` already points at an already-trusted host; it has no path to introduce a new host into the allow-list. "Should Bridge trust downloads from a brand-new domain at all" is a categorically bigger decision than "is this pinned hash still correct" and deliberately doesn't get to ride through the same lightweight, largely-automated review the routine drift-check PRs do.

**Decision — a build-time guard, mirroring the existing placeholder guard:** `KnownEmulatorsManifestTests.KnownEmulators_AllDownloadUrlsUseAnAllowedHost` asserts every real `DownloadUrl` in the embedded manifest resolves to a host in `Config.AllowedDownloadHosts` over `https` — catches a manually-added untrusted host at development time, not in production against a real user. Unlike the placeholder guard, this isn't Release-gated: there's no equivalent "still being sourced" grace period for a trusted-host violation the way there legitimately is for placeholder data mid-catalog-work, so placeholder entries are simply skipped in this check rather than exempted by build configuration.

**Consequences:**
- ✅ Closes the actual gap ADR-25 left open: even a fully compromised `main` (and therefore a compromised live-fetched manifest) cannot direct Bridge to download and run a binary from anywhere outside a small, hardcoded, source-reviewed list — the hash check alone could never have provided this
- ✅ Rejected sources never generate a network request — a stronger property than catching the problem after downloading
- ✅ The distinction between "what Bridge is willing to run" (allow-listed, hardcoded) and "what Bridge is willing to read as data" (the manifest, JSON only, never executed) stays explicit rather than implicit
- ✅ `DownloadVerificationServiceTests`' existing tests needed only a mechanical constructor-injection change (`TestAllowedHosts` instead of the real `Config.AllowedDownloadHosts`) — the allow-list is injectable, not hardwired to the concrete `Config` reference, so tests never have to weaken the real production list to keep passing
- ❌ A genuinely new, legitimate download source (a different libretro mirror, a standalone emulator from a different vendor — see `PLAN.md` → Speculative Ideas) requires a Bridge source change and a new release before it can be used, not just a manifest update — an intentional friction, not an oversight

**Alternatives considered:**

- **Put the allow-list in `KnownEmulators.json` itself, as manifest data:** rejected — this is exactly the property this ADR exists to avoid; data that can arrive via a live fetch (ADR-25) cannot also be the thing deciding what that same fetch is allowed to point at
- **Check only the host, not the scheme:** rejected — an allowed host over plain `http://` reopens the exact MITM/tampering risk ADR-11's own threat model already accounts for at the transport level
- **Enforce the check in `EmulatorInstallerService` instead of `DownloadVerificationService`:** rejected — `DownloadVerificationService` is the one real chokepoint every download already passes through; checking one layer up risks a future caller bypassing it by mistake
- **Allow subdomain/wildcard matching (e.g. `*.libretro.com`) instead of an exact host match:** rejected for now — no evidence today's one real host needs that flexibility, and an exact match is simpler and less surprising; revisit only if a real second host under the same parent domain shows up

---

### ADR-27: Cheats management per game (Phase 3, item 1) — content source, activation mechanism, and three real bugs found correcting it

**Status:** Accepted

**Date:** 2026-08-04

**Context:**
`PLAN.md` → Phase 3's confirmed build order (2026-08-04 reorder) put cheats/mods first — lowest risk, reuses the per-game `EmulatorProfile` override pattern ADR-24 already built. "Mods" stayed unscoped (no standard format, no concrete use case — same standard already applied to the core picker's deferral in ADR-18); this ADR covers cheats only. RetroArch cheats are plain-text `.cht` files, a real, already-documented format — the open questions were where the *content* comes from, and how a per-game "auto-apply on launch" preference actually reaches RetroArch without Bridge either forcing a global RetroArch behavior change or silently corrupting the user's own `retroarch.cfg`.

**Decision — content source: fetch on demand from `libretro/libretro-database`, never curated or bundled:** unlike `KnownEmulators.json`'s hand-verified catalog, Bridge does not maintain its own cheat content. `CheatService.LoadCheatsAsync` fetches the specific file for one game, on first access, from `raw.githubusercontent.com/libretro/libretro-database/master/cht/{platform folder}/{game name}.cht` — the same real, public, community-maintained source RetroArch's own "Update Cheats" downloader uses. The repository's license (confirmed against its actual `LICENSE` file, not assumed) is CC BY-SA 4.0, which requires attribution and a link to the specific licensed material — `CheatsResult.SourceFileUrl` carries a per-file `github.com/.../blob/master/...` link back to the caller for exactly that, alongside a general attribution notice. `PlatformFolders`/`RetroArchCoreNames` in `CheatService` cover 14 of Bridge's 15 seed platforms — `wonderswan` is deliberately absent (see Known gaps below).

**Decision — activation mechanism 1: `cheat_database_path`, nested under a verified core-name subfolder, delivered via RetroArch's own override file (not an env var):** whenever `CheatService.GetCheatDirectoryIfExists` finds a Bridge-managed `.cht` file for a game, `LaunchService` needs RetroArch to look in that game's per-game root folder (`{Config.CheatsPath}/{game.Id}`) for it. The very first implementation wrote a flat file directly in that root; RetroArch never found it — root cause, confirmed against `cheat_manager.c`'s real source: `cheat_manager_get_game_specific_filename` requires `{cheat_database_path}/{core_name}/{game_name}.cht`, where `core_name` is the core's own self-reported `retro_get_system_info().library_name`, not a flat file. Fixed by nesting the `.cht` under a `RetroArchCoreNames`-verified subfolder. The *next* implementation delivered that per-game root via the `LIBRETRO_CHEATS_DIRECTORY` environment variable, confirmed working via real interactive use — but this itself turned out to have the same leak class as `--appendconfig` below (bug 3 in the list further down), and was replaced with the same override-file mechanism 2 already uses. `CheatService.ApplyCheatLaunchOverridesAsync` now writes `cheat_database_path` into that file unconditionally whenever it's called at all (LaunchService only calls it when a Bridge-managed cheat file exists for this game) — there's no "off" state for this one, matching the env var's original always-on behavior.

**Decision — activation mechanism 2, first attempt (rejected): `--appendconfig`:** the user asked, after mechanism 1 shipped and was confirmed working, for an "Auto-apply cheats on launch" toggle (Settings, default ON) so cheats already loaded for a game apply automatically instead of requiring a manual step in RetroArch's own Cheats menu. The first implementation generated a static override file (`cheat_apply_after_load = true`) and passed it via `--appendconfig` on launch. This was investigated and rejected, not shipped-then-patched: a real leaked line in the user's actual `retroarch.cfg` (`apply_cheats_after_load = "true"`, persisting even with the Bridge toggle off) proved `--appendconfig`'s injected value is **never reverted during the process's lifetime** — confirmed directly against `configuration.c`/`retroarch.c`: `RARCH_PATH_CONFIG_APPEND` is read and re-merged on every `config_load_file` call, including the one `config_unload_override()` itself performs to "restore" the original config before `config_save_on_exit` (RetroArch's own default) writes current settings back to the user's real config file. `--appendconfig` is not a session-scoped mechanism for this use case; RetroArch has no such flag.

**Decision — activation mechanism 2, real mechanism: RetroArch's own per-game "override" file — and the same file now also carries mechanism 1's `cheat_database_path`:** `config_load_override`/`config_unload_override` (`configuration.c`) is RetroArch's actual answer to "apply this setting for one session, never persist it" — overrides are explicitly reloaded away *before* `config_save_on_exit` runs, and RetroArch auto-discovers them itself (`auto_overrides_enable`, default true) at `{config directory}/{core_name}/{rom_basename_without_extension}.cfg`, no CLI flag needed. `CheatService.ApplyCheatLaunchOverridesAsync` writes/removes two lines in that one file — a targeted single-line patch per key (matching `CheatFileParser.SetEnabled`'s discipline for `.cht` files), preserving any other keys the user saved there themselves via RetroArch's own "Save Game/Core Override" menu action: `cheat_database_path` (always present whenever the method is called at all — mechanism 1, no toggle) and `apply_cheats_after_load` (present only when the Settings toggle is on — mechanism 2, removed otherwise). The file itself is never deleted outright by this method anymore — `cheat_database_path` having no "off" state means there's always at least one Bridge-owned line in it once a cheat file exists for the game. Verified end-to-end against a real interactive session: RetroArch's own log (`log_to_file`/`log_verbosity`, temporarily enabled for this investigation, then reverted) showed `[Override] Game-specific overrides found` / `[Config] Appending override config` / `[Override] Configuration overrides unloaded, original configuration restored` — both settings apply per-session and never touch the user's real `retroarch.cfg` (confirmed clean after exit: `apply_cheats_after_load = "false"` and `cheat_database_path = ""`, both matching their compiled defaults).

**The three real bugs found and corrected while building this:**

1. **`.cht` parser rejected RetroArch's own rewritten format.** `CheatFileParser.EnablePattern` only matched unquoted `cheat0_enable = false` (libretro-database's own distributed format). After the user toggled a cheat from *inside* RetroArch's own Cheats menu, RetroArch rewrote the entire file using its own save routine, which quotes every value including booleans (`cheat0_enable = "true"`) — Bridge's "Cheats..." window then reported the file as corrupted. Root-caused by reading the user's real, RetroArch-rewritten 5182-line file directly, not assumed. Fixed by making `EnablePattern` tolerate optional quotes on read while `SetEnabled` still only ever swaps the matched value in place, preserving whatever quoting was already there. Two regression tests use a real excerpt from that exact file.
2. **Override-directory resolution assumed the executable's own directory, not RetroArch's real configured one.** The override file above was correctly named and correctly formatted, but silently ignored — confirmed via RetroArch's own log showing no `[Override] ...` line at all for a file that existed at the path Bridge computed. Root cause, found only after enabling RetroArch's own file logging for direct evidence: the config key controlling this directory is `rgui_config_directory` (`configuration.c`'s `SETTING_PATH` binding), not `directory_menu_config` — the C struct field name, guessed instead of checked, the same class of mistake as the `apply_cheats_after_load` key name below. RetroArch's own portable-install default seeds `rgui_config_directory` to `":\config"` — a leading `:` is RetroArch's own "relative to the executable's own directory" notation (`fill_pathname_expand_special`, libretro-common) — so the real directory is `{executable directory}\config`, not the executable directory itself. `CheatService.ResolveConfigDirectory` now reads the user's actual `retroarch.cfg` and resolves this correctly (portable `:` notation, an absolute path, or RetroArch's own `"default"` sentinel), instead of assuming a fixed layout. Confirmed directly: the same file at the resolved path was found and loaded (RetroArch's own log lines above).
3. **Mechanism 1's `LIBRETRO_CHEATS_DIRECTORY` env var had the same leak class as `--appendconfig` did, initially scoped out of this ADR before being caught.** First flagged only as a known consequence, not fixed — an explicit follow-up review of that decision asked for it to actually be closed before release, not left as an accepted gap. Confirmed against `configuration.c`'s `config_load_file`: `getenv("LIBRETRO_CHEATS_DIRECTORY")` is read *after* the override file is merged, on every single call to that function — including the exact one `config_unload_override()` makes to "restore" the config before `config_save_on_exit` runs. Since the env var stays set for the process's whole lifetime, that "restore" call just re-derives and re-applies the same value, permanently leaking it — confirmed directly, a stale per-game cheat-folder path lingering in a real `retroarch.cfg`'s own `cheat_database_path` between sessions. This is a materially different problem from bugs 1–2 (not a wrong path/key — the env var mechanism *itself* is structurally incompatible with "never persist"), so simply adding `cheat_database_path` to the override file wasn't sufficient on its own: the env var would have kept silently overwriting it. Fixed by dropping `LIBRETRO_CHEATS_DIRECTORY` entirely and routing `cheat_database_path` through the same override file mechanism 2 already uses (see the mechanism 1/2 decisions above) — `ApplyCheatLaunchOverridesAsync` now owns both keys in one file, one method.

A related, smaller finding along the way: the `apply_cheats_after_load` config *key* itself was first guessed as `cheat_apply_after_load` (transposed from the `DEFAULT_APPLY_CHEATS_AFTER_LOAD` constant name) before being corrected, verified against `configuration.c`'s `SETTING_BOOL` binding. RetroArch ignores unknown config keys silently, so this failed the same way as bug 2 above — no error, just no effect — reinforcing the same lesson (see `DEVELOPMENT.md` → Bug Investigation Process for the process change this prompted).

**Decision — the toggle is scoped per game, not global, by design:** "Auto-apply cheats on launch" (Settings, default ON) only ever has an effect for a game that already has a Bridge-managed `.cht` file — `LaunchService` only calls `ApplyCheatLaunchOverridesAsync` inside the same `GetCheatDirectoryIfExists is not null` gate mechanism 1 already uses. A freshly-configured install has the toggle ON by default but no visible effect in RetroArch until the user opens "Cheats..." on a specific game at least once — confirmed, during real interactive testing, to be the intended scoped-per-game design (matching the original approved decision: "not a behavior forced by default for everyone"), not a bug to fix.

**Consequences:**
- ✅ Cheats work end-to-end, confirmed via real interactive use, not just unit tests: fetch, per-game enable/disable, auto-apply on launch, all without touching the user's real `retroarch.cfg`
- ✅ CC BY-SA 4.0 attribution is structurally per-file (`SourceFileUrl`), not just a general notice — satisfies "link to the specific licensed material" rather than a blanket disclaimer
- ✅ All three real bugs were root-caused against RetroArch's actual source and, for bugs 2 and 3, RetroArch's own log output — not fixed by trial and error or third-party documentation
- ✅ Both settings this feature needs (`cheat_database_path` and `apply_cheats_after_load`) go through RetroArch's own override-file mechanism now, not an env var or `--appendconfig` — neither can leak into the user's real `retroarch.cfg` anymore, verified structurally (RetroArch's own `config_unload_override` before `config_save_on_exit`) and empirically (confirmed clean after real sessions, both keys back to their compiled defaults)
- ❌ `RetroArchCoreNames`' `atari2600` (`"Stella"`) and `lynx` (`"Holani"`) entries are medium-confidence, not fully verified — see Known gaps
- ❌ `wonderswan` has no cheat coverage at all — see Known gaps

**Known gaps (not resolved, flagged rather than guessed):**
- **`atari2600` → `"Stella"`:** the official `libretro-core-info` entry says `"Stella"`, but the core's current source (`stella/src/os/libretro/StellaLIBRETRO.hxx`) self-reports `"Stella 2023"` via `getCoreName()`. Using the officially-published name since that's what RetroArch's own database ships/expects — genuinely unconfirmed against the exact binary `KnownEmulators.json` pins.
- **`lynx` → `"Holani"`:** the core's own Rust source (`LLeny/holani-retro`, `src/lib.rs`) reports lowercase `"holani"` via `SystemInfo::new()`, but the published `.info` corename says `"Holani"`. Two real sources disagree; using the published name as the more likely stable convention.
- **`wonderswan`:** no folder exists in the real `libretro/libretro-database` `cht/` directory (confirmed by listing it directly, not guessed from a naming convention) — a gap in the upstream source, not something Bridge's own code can work around. `LoadCheatsAsync` reports `PlatformNotSupported` for it rather than attempting a fetch that would always fail.

Any of these three, if it turns out wrong in real use, fails the same visible way bug 2 above did — RetroArch silently not finding what Bridge wrote — not a crash or data loss. Revisit with the same real-evidence standard (source + empirical confirmation) if a real report comes in.

**Alternatives considered:**

- **Curate/bundle cheat content the same way `KnownEmulators.json` curates emulators:** rejected — cheat databases are large, per-game, and change independently of Bridge's own release cycle; fetching on demand from the same source RetroArch itself uses keeps Bridge's own maintenance surface at zero
- **Force `cheat_apply_after_load = true` globally via a direct one-time write to the user's `retroarch.cfg`:** rejected — this is exactly the "global forced default" outcome the approved design explicitly ruled out; it would also affect every game/core, not just ones Bridge manages cheats for
- **Keep `--appendconfig`, and instead force `config_save_on_exit = false` for Bridge-launched sessions to prevent the leak:** rejected — this would silently discard *any* other setting change the user makes during that RetroArch session (video/audio tweaks, remaps), a much larger unwanted side effect than the one it would fix
- **Have Bridge track and actively revert the leaked `apply_cheats_after_load`/`cheat_database_path` values on every launch:** rejected — treats a symptom of using the wrong mechanism instead of using RetroArch's own real session-scoped mechanism, which needs no cleanup step at all
- **Leave mechanism 1 on the `LIBRETRO_CHEATS_DIRECTORY` env var, accepting the leak as a known limitation since it only affects RetroArch's own cheat-browsing UI between Bridge-driven launches, not Bridge's own cheat management:** rejected after explicit review — the fix is the same pattern already built for mechanism 2 (same file, same targeted-line-patch discipline), so there was no real cost/complexity reason to leave a known, real, root-caused leak unfixed for the version cut

---

## Creating a New ADR

1. Copy the ADR format block from the section above
2. Assign the next sequential number (e.g., `ADR-1`, `ADR-2`, …)
3. Paste it at the end of this document, before the "Creating a New ADR" section
4. Fill in the sections with concrete information
5. Add it as a new entry in the "Existing ADRs" section above
