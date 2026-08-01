# Bridge - Project Plan

> **Status:** Phase 1 (MVP) shipped as tagged release `v0.1.0`. **The app has been launched, fixed, and interactively used end-to-end** — launch itself confirmed during the ADR-12 investigation (which found and fixed a real release-breaking packaging bug: `v0.1.0`'s published `.exe` didn't open at all, fixed in place, same tag), and separately, per the user's own direct report on their real machine: add folder → Pokémon Emerald detected → mGBA configured → rescan → launched successfully (FR-01/02/03/06/07/09), plus a full close-and-reopen confirming FR-08 (cross-session persistence). FR-04/FR-05 (SteamGridDB box art lookup + caching) are now confirmed too — box art was seen actually rendered in the cover grid after a rescan, not just present as a cached file on disk. **Phase 2's install orchestration is now built, proven end-to-end, and interactively tested** (`EmulatorInstallerService` — download, extract via `SharpCompress`, register a working `Emulator`/`EmulatorProfile` — see ARCHITECTURE.md → ADR-14), exposed via a new "Auto-Install" button in Settings; 152 tests pass in Release, 151 in Debug. The first real interactive click found and fixed two real bugs — a wrong `ExecutableRelativePath` (ARCHITECTURE.md → ADR-11's 2026-08-03 update) and a stale-`SelectedPlatform` refresh bug in `SettingsViewModel` — proof the mechanism now works end-to-end for the first fully-verified catalog pair (`nes` → FCEUmm). **The catalog itself is complete, and all 15 of 15 seed platforms have now been clicked and proven end-to-end** — `nes` first, then 11 more (`lynx`, `wonderswan`, `gb`/`gbc`, `genesis`/`sms`/`gamegear`, `gba`, `pcengine`, `n64`, `nds`) in one session, then the final 3 (`snes`, `atari2600`, `atari7800`) in a later session — see Timeline items 22–24. **Phase 2's install mechanism is no longer just data-verified for any platform — every seed platform has a real, interactive Auto-Install confirmation.** What's left open is a single, separate, newly-found (not yet fixed) `.bin`/Atari-2600 extension-matching gap, unrelated to the install mechanism. See `## Timeline` below for the exact handoff state.
>
> **Last updated:** 2026-08-05

## Project Overview

Bridge is a Windows retro emulation launcher/frontend that eliminates the manual setup friction of managing ROMs, box art, and emulator configuration by hand. The user points Bridge at their ROM folders; Bridge detects the system for each file, fetches box art from SteamGridDB, and launches each game through the correct emulator with the correct arguments — without the user needing to know which emulator or core belongs to which system. Bridge manages ROMs and emulators the user already owns; it does not include, facilitate, or link to ROM acquisition, keeping it in the same legitimate category as RetroArch, EmulationStation, and Playnite.

## Current State

### Phase 1 (MVP) — Complete (shipped v0.1.0)
Goal: detect → show → play, using emulators the user has already installed manually.

- ~~Scan user-selected ROM folders~~ — done
- ~~Detect system/console by file extension (extension to system mapping)~~ — done
- ~~Fetch box art from SteamGridDB (user-provided API key)~~ — done
- ~~Local image cache, resized to the exact display resolution used in the UI — never scale large source images at render time~~ — done
- ~~Manual emulator path configuration per system (user points to the .exe for each system)~~ — done
- ~~Launch a ROM with the correct emulator and arguments~~ — done
- ~~One functional view (simple cover grid) — no elaborate animations yet, functional only~~ — done
- ~~Local library persistence (which ROMs, which system, which emulator assigned)~~ — done

Explicitly out of scope for this phase: multiple views, video previews, cheats/mods, social features, RetroAchievements, automatic emulator download, recommendations, editable per-game emulator settings.

### Phase 2 (Should Have) — Partially Started
Once the MVP works end-to-end. 1 of the 6 items originally scoped here shipped in `v0.2.0`; the other 5 haven't been started. (Four more items below — a core picker UI, offering Auto-Install inline from the launch flow, removing a `Game` from the library, and the `.bin`/Atari 2600 extension fix — were never part of Phase 2's original scope; they're improvements/gaps surfaced while building and using the auto-install mechanism, added here opportunistically rather than tracked as a separate list.)

- Game detail panel: short blurb/preview text, description, release year, console/system, additional screenshots, thumbnails (distinct from the main box art)
- Favorites / recently played
- "Library" view (Playnite-style cover grid, refined from the Phase 1 functional version)
- "Big Picture" / streaming-style view with a recommended-games section
- ~~Automatic emulator detection/download for known systems (e.g. RetroArch cores, PCSX2) — replaces the fully manual Phase 1 configuration~~ — done, shipped in `v0.2.0` (see ARCHITECTURE.md → ADR-11/ADR-14, `CHANGELOG.md`). **Caveat, not silently dropped:** only the 15 cartridge/handheld seed platforms (RetroArch cores) are covered — `PCSX2` was always just an illustrative example in this bullet's original wording, never actually implemented; PS2 and other disc-based systems were never in the seed list to begin with (ADR-7's cartridge/handheld scoping). This is a completed item, not a completed phase — see the header above.
- Per-game emulator configuration editable directly from the launcher (not just per-system defaults)
  - Core picker UI when a platform has more than one known-good core candidate — today `EmulatorInstallerService.FindKnownCore` silently picks the first match and just logs a warning (`EmulatorInstallerService.cs:230`, `"...using the first. A core picker UI isn't built yet."`); never made it past a code comment into either an ADR or this plan until now. **Scope note:** this is specifically about picking between multiple *RetroArch cores* for one platform. It's related to, but narrower than, the speculative "standalone emulator" selection idea below (choosing RetroArch-via-core vs. a standalone emulator entirely) — worth clarifying the distinction when either gets designed for real, not assuming they're the same feature.
- Offer Auto-Install inline from the launch flow itself (when `LaunchService` returns `NoEmulatorConfigured`), not just from Settings — deliberately deferred in ADR-14 "worth revisiting once the mechanism has more than one proven core behind it"; that condition is now met (15/15)
- ~~Manually remove/hide a `Game` from the library~~ — done, see Timeline below (ARCHITECTURE.md → ADR-15). Scoped to `IsMissing == true` only, not "hide any game" — see ADR-15 for why.
- ~~Extend `atari2600`'s recognized extensions to include `.bin`~~ — done, ARCHITECTURE.md → ADR-16. Confirmed no collision with any of the 15 seed platforms' extensions before applying. Required a real migration, not just a JSON edit — see Timeline below.

### Phase 3 (Could Have) — Not Started
Once the base is solid and stable.

- RetroAchievements integration
- Cheats/mods management per game
- Video previews / trailers
- Recommendation engine ("similar games")
- Additional views (beyond Library and Big Picture, already covered in Phase 2)
- Disc-based system support (PS1/PS2/Saturn/etc.) via checksum/DAT-CRC identification — Phase 1's extension-only matching has no way to disambiguate shared disc formats (`.iso`/`.bin`/`.cue`, ...) between different disc-based platforms (ADR-7), which is why none are in the seed today; hash/DAT-based identification (noted as a natural addition in ADR-6) is the mechanism that would make this safe to add
- Check for and offer updates to already-installed emulator cores — today, once a core is installed it's reused forever (`EmulatorInstallerService`'s dedup-by-`KnownEmulatorId` never re-checks the nightly channel for a newer build, ADR-11/ADR-14); a maintenance improvement, not a blocker — all 15 seed platforms were proven end-to-end without needing this

### Phase Polish — Not Started
Non-content work — do after Phase 2's remaining scope and Phase 3 are settled, not before: the core/foundation should stop changing shape before investing in how it looks and feels, so this work doesn't get redone against a moving target. This is everything that makes Bridge feel like a finished product rather than everything that makes it *work*.

- **Integrate WPF-UI (lepo.co) for Mica/Fluent theming** — decided in the original foundation document (WPF + WPF-UI over WinUI 3, SteamManager as precedent) but never actually installed; confirmed via the 2026-08-06 documentation audit that Bridge ships on stock WPF today, with zero `Wpf.Ui` package reference and no WPF-UI resource dictionaries merged into `App.xaml`. Called out as its own item, not folded into "general UI pass" below — this is adding the theming stack itself, a prerequisite for the animation/UI-pass items, not just polish
- Polished transition animations (moved from Phase 2 — this is where the EmulationStation inspiration gets invested in)
- Theme customization / visual personalization (moved from Phase 3)
- Welcome sentinel + "what's new" dialog on first run / after updates — reference pattern already sketched in DEVELOPMENT.md → Welcome Sentinel, not yet wired into the actual app
- Auto-updater for Bridge itself, via GitHub Releases — distinct from Phase 2's emulator auto-install; reference pattern already sketched in DEVELOPMENT.md → Version Management ("Updater pattern"), not yet wired in
- Sponsor/support icon (Ko-fi/GitHub Sponsors link) + Credits/About dialog with disclaimer — reference XAML/code already sketched in DEVELOPMENT.md → Branding & Sponsorship, not yet wired in
- General UI pass on what already exists from Phase 1/2 (the functional-only grid, Settings screen) to match the visual bar the rest of this phase sets

**Won't Have (for now, not permanently ruled out):** any ROM discovery/acquisition feature; social features; storefront integration (Steam/Epic/etc.) — not planned, not being designed for, far future only if ever revisited.

### Speculative / Future Ideas — Not Scoped, No Version Assigned
Came up while documenting Phase 2's work. Deliberately not assigned to Phase 2/3/Polish above — either they need real design work first, or they're genuinely "someday, maybe" with no committed shape. Recorded so they're not lost, not because they're promised.

**Far future / speculative — no clear purpose or design yet:**
- Distribution as an installer, instead of a single-file `.exe` — no clear reason yet, just a possibility being kept in mind; a distribution-model change, not a code change, if it ever happens
- "All games" library, Playnite-style — beyond just ROMs/emulation. Not in tension with the "Won't Have: storefront integration" line above — it's the concrete example of exactly that far-future scope-permanence note
- Discord Rich Presence — show the currently-playing game in Discord
- Full first-run onboarding wizard (animated, step-by-step: name/avatar, ROM folder, suggested emulators based on detected ROMs, SteamGridDB key) — implies a real user-profile system (persisting name/avatar) that doesn't exist today. Distinct from Phase Polish's much simpler "Welcome sentinel + what's new dialog" above — not the same feature, don't conflate when scoping either one
- Standalone emulator auto-download/configure (not just RetroArch/libretro cores) — extending `EmulatorInstallerService`'s auto-install mechanism (ARCHITECTURE.md → ADR-11/ADR-14) to arbitrary standalone emulators (e.g. mGBA) the same way it works for RetroArch today. Real new work — each standalone emulator has its own install format, argument syntax, and update channel; the far-future half of the standalone-emulator idea below

**Nearer-term, simple — not yet designed, but low complexity:**
- Choose cover — let the user pick among SteamGridDB's multiple candidate images instead of Bridge silently taking the first result. Directly ties to the existing documented simplification in ARCHITECTURE.md ("The first search result and the first grid result are used with no scoring (approved as-is for Phase 1)") — this idea is the eventual revisit of that simplification, not a new decision
- "What to play next" — a section in the future Big Picture/streaming view (Phase 2) surfacing unplayed games
- Random game — a button that picks and offers to launch a random game from the library
- Drag-and-drop ROM import — drop a ROM file onto the app window, Bridge detects it and copies/moves it into a scanned ROM folder automatically
- New-ROM-detected prompt — when a scan finds a ROM for a platform with no emulator configured, proactively offer to install one right there. The scan-triggered version; the launch-triggered version is already tracked above (Phase 2, "Offer Auto-Install inline from the launch flow")
- Standalone emulator suggestions (links only, no auto-install) — the lighter-weight near-term half of the standalone-emulator idea above: suggest a good standalone emulator and where to get it, without downloading/configuring it automatically

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
| UI Framework | WPF; WPF-UI (lepo.co) decided for Mica/Fluent styling but not yet integrated — Bridge ships on stock WPF today, WPF-UI is an explicit Phase Polish item (see `## Roadmap`) |
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

### Current Version (0.2.0) — Shipped
`v0.1.0` shipped the full Phase 1 (MVP) scope: detect → show → play. `v0.2.0` shipped exactly one item out of Phase 2's scope — automatic emulator detection/download (see above) — not the rest of Phase 2. Keep `<Version>` in `Bridge/Bridge.csproj` consistent with this document, `README.md`, and `docs/index.html` (see DEVELOPMENT.md → Version Management).

### Future Versions — Backlog
The remaining Phase 2 scope (game detail panel, favorites/recently-played, refined "Library" view, "Big Picture" view, per-game emulator configuration, a core picker UI, offering Auto-Install inline from the launch flow), all of Phase 3 (RetroAchievements, cheats/mods, video previews, recommendations, additional views, disc-based system support, emulator core update checks), and all of Phase Polish (transition animations, theming, welcome sentinel, auto-updater, sponsor/credits, general UI pass) remain deferred — not started, not scheduled. The "Won't Have" list (any ROM discovery/acquisition feature; social features; storefront integration) is out of scope indefinitely, not just for this version.

---

## Roadmap

Tracking approach as of `v0.2.0`: version cuts, not phase completion. `v0.1.0` and `v0.2.0` each shipped a coherent, tested, real-use-verified chunk without waiting for an entire phase to finish — the same pattern continues going forward. Phase 1/2/3/Polish above remain useful as *thematic buckets* (what kind of work something is), not as "when do we ship" gates.

### v0.3.0 — Next confirmed cut
The 2 small Phase 2 items left over from today's auto-install work, not the rest of Phase 2:
- Core picker UI when a platform has more than one known-good core candidate
- Offer Auto-Install inline from the launch flow (`LaunchService` → `NoEmulatorConfigured`), not just from Settings

**Why these two, not something else:** both extend the exact mechanism (`EmulatorInstallerService`) that was today's entire focus, with the freshest possible context; neither needs a new view — they extend UI that already exists (the Settings Auto-Install button, the launch-failure dialog) — unlike the detail panel/Library view/Big Picture items, which do; and they're genuinely small, matching "one coherent, verified chunk," not a multi-feature bundle.

### Path to v1.0

**Criterion, decided (not assumed):** v1.0 = all of Phase 2 + all of Phase Polish. Phase 3 is excluded by default, not included unless a specific item is explicitly decided on later.

**Reasoning:**
- **All of Phase 2** — Bridge's core differentiator ("zero-friction setup," per the foundation document's Vision) lives largely here. A 1.0 missing the refined Library view, detail panel, or per-game config reads as an extended MVP, not a finished product.
- **Phase 3 excluded by default** — it's "Could Have" in the original MoSCoW, a meaningfully lower commitment tier than Phase 2's "Should Have." RetroAchievements, cheats/mods, video previews, and a recommendation engine are legitimately post-1.0 growth, the same way comparable tools (early Playnite, EmulationStation) shipped without them initially.
- **Disc-based system support (PS1/PS2/Saturn/etc.) explicitly excluded from the v1.0 path** — confirmed decision, not left as an open question: no checksum/DAT detection design exists yet (it's speculative, ARCHITECTURE.md → ADR-6/ADR-7 only note it as a natural future direction), and pulling it into the 1.0 path without that design done first would be exactly the kind of scope creep this whole session was built to avoid. It's real, substantial, user-facing capability — not dismissed — see v2.0+ below for where it actually lives.
- **All of Phase Polish, not optional** — a 1.0 that still looks and feels like Phase 1's functional-only grid contradicts what shipping a "1.0" signals (stable, ready, finished) — animations and the general UI pass aren't cosmetic extras at that point, they're part of whether the product reads as done.

See ARCHITECTURE.md → ADR-17 for the full decision record.

### Reference roadmap, v0.4.0 → v0.9.0 — non-binding, re-confirm scope at each cut

**Not fixed.** This is a plausible default chunking, not a committed plan — re-confirm what actually goes into each version when work on it is about to start, same discipline already applied to `v0.3.0` above and to every feature built today. Don't treat these version numbers or groupings as more precise than they are.

- `v0.4.0` — Game detail panel
- `v0.5.0` — Favorites/recently played + refined "Library" view (related — both touch the main browsing experience)
- `v0.6.0` — "Big Picture" view (large enough to warrant its own cut)
- `v0.7.0` — Per-game emulator configuration (closes the rest of Phase 2)
- `v0.8.0` — Phase Polish batch 1: transition animations + general UI pass (highest visual impact)
- `v0.9.0` — Phase Polish batch 2: welcome sentinel + auto-updater + theming + sponsor/credits
- `v1.0.0` — stabilization + QA pass against the criterion above

### v2.0+ — open bucket, one named focus

**Disc-based system support (PS1/PS2/Saturn/etc.) is the explicit first focus of v2.0** — not buried in an undifferentiated backlog. It's a real capability gap (a large share of retro-gaming interest is disc-based), deliberately deferred out of the v1.0 path above specifically because the checksum/DAT detection design it needs doesn't exist yet — designing that mechanism is the actual first step, before any version number gets attached to it for real.

Everything else stays an undifferentiated bucket, not broken down by version yet: any Phase 3 items not explicitly pulled into the v1.0 path, and the Section 13-style speculative ideas tracked above (Speculative / Future Ideas). The "nearer-term, simple" speculative ideas specifically don't have to wait for v2.0 — they can slot into any earlier version once actually designed, the same way `.bin` detection and library removal landed alongside `v0.2.0`'s work today without being formally scoped into it ahead of time.

### How future paths get defined — not now, not by proximity

**Rule 1 — the detailed path to v2.0 gets defined when v1.0 ships, not before.** The same process used today to define the Path to v1.0 (explicit criterion, reasoned per-component, presented for approval, recorded as an ADR — see ADR-17) repeats at that point for v2.0's own detailed path. Nothing in this document pre-defines it now; the `v0.4.0`→`v0.9.0` reference chunking and the `v2.0+` bucket above are deliberately as far as this plan goes today.

**Rule 2 — reaching v2.0 doesn't auto-promote anything out of the Speculative / Future Ideas pool.** Once in v2.0, what's still left undefined gets evaluated, and scope for pulling something from that pool (above) becomes a fair question to ask for the first time — but no individual idea moves from the pool into actual construction just because a version cut happens to be nearby in time. Each one still has to go through its own real definition process first (design, scope, Open Decisions if it needs any) — the same bar every feature already shipped in this document had to clear. Skipping that step for a pool idea because it's "close" to v2.0 would be exactly the scope-creep pattern ADR-17 was written to catch, applied here preventively to the whole pool instead of case-by-case.

---

## Project Structure

See `DEVELOPMENT.md` → Project Structure for the current, authoritative file tree. Deliberately kept in one place, not duplicated here — this section used to carry its own copy, which silently drifted out of sync with the real codebase through Phase 2 (still showing pre-ADR-11 `EmulatorConfig.cs`, missing `DownloadVerificationService`/`EmulatorInstallerService`/`KnownEmulators.json` and their tests) until the 2026-08-06 documentation audit caught it. One canonical copy removes the drift risk instead of just re-syncing it once.

---

## Development Phases

### Phase 1: MVP
> **Objective:** Detect → show → play, using manually-configured emulators.

**Milestones:**

| Milestone | Description | Status |
|-----------|-------------|--------|
| FR-01 | User can add one or more root ROM folders | **Interactively confirmed** — user added a ROM folder via "Add Folder" on their real machine |
| FR-02 | System recursively scans and detects valid ROM files | **Interactively confirmed** — rescan performed, ROM picked up |
| FR-03 | Each detected ROM is automatically associated with a known system/console | **Interactively confirmed** — Pokémon Emerald correctly detected as GBA |
| FR-04 | Each detected ROM looks up its box art on SteamGridDB | **Interactively confirmed** — box art was seen actually rendered in the cover grid after a rescan (not just present as a cached file on disk) |
| FR-05 | Box art is cached locally, resized to the exact size it's displayed at | **Interactively confirmed** — same pass as FR-04; the rendered cover appeared correctly sized in the grid, not just as a cached file on disk |
| FR-06 | User configures, per system, which emulator (.exe) to use | **Interactively confirmed** — mGBA configured for GBA in `SettingsWindow` with `ArgumentTemplate = "{RomPath}"` |
| FR-07 | Selecting and confirming a ROM launches the emulator with correct arguments | **Interactively confirmed** — Pokémon Emerald launched successfully via mGBA |
| FR-08 | The library persists between sessions (no full re-scan on every launch) | **Interactively confirmed** — closed the app fully and reopened it; library and emulator configuration persisted correctly, per the user's own direct report |
| FR-09 | User can trigger a manual re-scan | **Interactively confirmed** — rescan triggered before launch |

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
| WPF-UI (lepo.co) | **Not yet added** | Mica/Fluent theming | Decided in the original foundation document, validated in a prior project (SteamManager) — but never actually integrated (confirmed via the 2026-08-06 documentation audit: no `Wpf.Ui` package reference, no WPF-UI resources in `App.xaml`). Bridge ships on stock WPF today. Tracked as an explicit Phase Polish item, not assumed done — see `## Roadmap` |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM (ObservableObject, RelayCommand) | Standard for this template |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | DI container | Standard for this template |
| Microsoft.Extensions.Logging | 10.0.10 | `ILogger<T>` logging | Standard for this template |
| LiteDB | 5.0.21 | Library persistence | Resolved — see Open Decision #1 / ARCHITECTURE.md → ADR-2 |
| SharpCompress | 0.50.2 | Archive extraction (`.7z`/`.zip`) for automatic emulator install | See ARCHITECTURE.md → ADR-14 |
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
10. ~~Cut `v0.1.0` as a tagged GitHub Release (Phase 1 MVP restore point)~~ — done; release job's version-change gate didn't fire (genesis version, no real bump) so the tag/release were created manually — see DEVELOPMENT.md → Release Process
11. ~~Phase 2 inventory: automatic emulator detection/download — what it implies, RetroArch as the first candidate (covers all 15 seed platforms via cores), what the Playnite catalog research now applies to~~ — done, discussed and approved before any code
12. ~~`Emulator`/`EmulatorProfile` schema split + `DownloadVerificationService` (pinned-hash + exact-size verified downloads) + `KnownEmulators.json` catalog~~ — done, ADR-11; RetroArch 1.22.2 entry independently verified (downloaded + hashed by hand from the official buildbot), core catalog left as an explicit, Release-build-gated placeholder
13. ~~First real `KnownEmulatorCore` entry: `nes` → FCEUmm~~ — done, ADR-11; confirmed cores have no "stable" channel at all (`buildbot.libretro.com/nightly/.../latest/` is the real one, per an official RetroArch repo issue), navigated the real index to get the exact filename, hashed by hand (`sha256sum` + `certutil`, matching), and confirmed the internal DLL filename by actually extracting the `.zip`. 97 tests pass in both Debug and Release now — the Release guard no longer fails, because the one entry present is fully real, not because verification is complete
14. ~~Manually run the app and actually look at it~~ — done, and not a clean pass: the `v0.1.0` published `.exe` didn't open at all. Root-caused with the Bug Investigation Process (one hypothesis tested and ruled out with real evidence before the actual cause was found), fixed (`IncludeNativeLibrariesForSelfExtract=true`), confirmed with the same isolated-folder reproduction that found the bug, and the already-published `v0.1.0` release asset was replaced in place. See ARCHITECTURE.md → ADR-12, DEVELOPMENT.md → Current Status/Release Checklist. This is a stronger outcome than "confirmed the UI renders" would have been — it caught a bug that would have hit every single person who downloaded `v0.1.0`.

15. ~~Interactive click-through/visual pass — actually use Add Folder/Rescan/Settings and watch the grid populate~~ — done, per the user's own direct report (performed on their real machine, independent of the ADR-12 investigation, not reproduced by Claude): added a ROM folder, Pokémon Emerald was correctly detected, configured mGBA in Settings with `"{RomPath}"`, ran a rescan, launched the game successfully. Covers FR-01 (add folder), FR-02/FR-03 (scan + detection), FR-06 (emulator config), FR-07 (launch), FR-09 (rescan) with real interactive use, not just code review.

16. ~~Build the archive-extraction/install orchestration that turns a verified `DownloadResult` into a registered `Emulator` + generated `EmulatorProfile` rows~~ — done, ADR-14. `{CorePath}` wired into `ArgumentTemplate` as a real resolver token (not baked literal text), `LaunchService` re-validates it at launch time the same way it already does the executable. `SharpCompress` chosen for extraction — pure managed, confirmed via reflection against the real installed assembly rather than assumed API, deliberately avoiding a repeat of ADR-12's native-dependency mistake. Proven end-to-end against the one verified catalog pair (`nes` → FCEUmm) with real `.zip` fixtures extracted through the real code path, not mocked — including both levels of failure cleanup (frontend extraction failure wipes the partial install; a core failure after a successful frontend does not roll the frontend back), the already-installed-reuse path, cancellation, and staged progress reporting. Exposed via a new "Auto-Install" button in `SettingsWindow`, gated on `HasKnownInstallOptionAsync` so it only appears where the catalog actually has a verified entry.

**Phase 1's 9 FRs are wired end-to-end in code, covered by unit tests, launch-verified, and now interactively confirmed working end-to-end (add folder → detect → configure emulator → rescan → launch); `v0.1.0` shipped as a tagged release, and the release asset itself has been launch-verified and fixed.** Phase 2's emulator auto-detect/download mechanism — schema, verified-download, and the install orchestration itself — is implemented, tested, and (per Timeline items 21–24 below) proven end-to-end for all 15 of 15 seed platforms, both data-verified and interactively confirmed.

**Next, in order:**

17. ~~Source and hash-verify the second seed platform's core~~ — done, see item 21 below. 13 seed platforms remain, same process proven twice now (navigate the real nightly index, download, double-hash, extract to confirm the internal filename) — each one becomes usable through the existing orchestration immediately, no further mechanism work needed per core.
18. ~~FR-08 (persistence across sessions)~~ — done, confirmed via a full app close-and-reopen, per the user's own direct report. ~~FR-04/FR-05 (SteamGridDB box art lookup + caching)~~ — done too, per the user's own direct report during the Auto-Install interactive session: Mario 3's box art was seen actually rendered in the cover grid after a rescan, not just present as a cached file on disk — closing the exact gap this item had flagged as open.
19. ~~An interactive pass on the new "Auto-Install" button itself~~ — done, and it found a real bug on the first click: `ExecutableNotFoundAfterExtraction`. `KnownEmulator.ExecutableRelativePath` (`"retroarch.exe"`) was wrong — the real RetroArch 1.22.2 archive nests everything under `RetroArch-Win64/`, not flat as third-party documentation had claimed. Root-caused by extracting the actual downloaded-and-verified `.7z` (recovered from `%LocalAppData%\Bridge\Downloads\`, left behind since ADR-14's failure handling only cleans up the *extraction target*, not the verified *source* archive) with Bridge's own `SharpCompress` code path and listing its real entries — `RetroArch-Win64/retroarch.exe`. Corrected in `KnownEmulators.json`; `EmulatorInstallerServiceTests`' shared fixture rebuilt to match the real nested structure instead of the flat one that let this pass every automated test. See ARCHITECTURE.md → ADR-11 (2026-08-03 update) and ADR-14. This is exactly the gap ADR-14 flagged as open ("no visual/interactive confirmation") — closed by actually doing it, and it paid for itself immediately.
20. ~~Second real bug from the same interactive session: after Auto-Install (or a manual save) succeeded, Settings kept showing stale Executable/Argument Template values until closed and reopened~~ — done. `SettingsViewModel.LoadPlatformsAsync` rebuilds `Platforms` with fresh `PlatformConfigItem` instances every call, but `SelectedPlatform` was never re-pointed at the new matching one, so `OnSelectedPlatformChanged` — the only thing that refreshes those text-box-bound fields — never re-fired. Both `AutoInstallCommand` and `SaveEmulatorProfileCommand` now explicitly reselect after reload. The regression test caught a second bug in the fix itself before it shipped: reselecting *before* setting the final "Installed."/"Saved." message let `OnSelectedPlatformChanged`'s own status-clearing side effect wipe it right back out — reordered. Separately, documented (not fixed, out of scope) that there's no way to remove a `Game` from the library once confirmed gone for good — only mark-missing exists (ADR-6) — added to the Phase 2 backlog above.
21. ~~Second `KnownEmulatorCore` entry: `snes` → Snes9x~~ — done, ARCHITECTURE.md → ADR-11's 2026-08-04 update. Platform chosen as the next in `SeedSystems.json` order after `nes`; core chosen by the same editorial-language standard used for FCEUmm (libretro docs describe Snes9x as "most up-to-date", "highly accurate", "recommended for netplay"; the main alternative, bsnes-mercury, forks into 3 performance-tiered variants Bridge's single-entry catalog shape would have to pick one of arbitrarily). Downloaded from the same confirmed-real nightly buildbot channel, hashed independently with `sha256sum`+`certutil` (matching), `CoreFileName` confirmed by directly listing the `.zip`'s contents (`snes9x_libretro.dll`, flat at root). 137 Release / 136 Debug tests still pass — the Release guard test doesn't count per-platform coverage, only rejects placeholder values on entries present, so this addition didn't change how many tests exist, only how much real data the manifest holds. 13 of 15 seed platforms remain without a catalog entry. `snes` hasn't had its own live Auto-Install click yet — lower-risk than `nes`'s first click since the orchestration is already proven, but not claimed as interactively confirmed until it is.
22. ~~Remaining 13 `KnownEmulatorCore` entries, in one batch~~ — done, ARCHITECTURE.md → ADR-11's 2026-08-04 batch update. Researched and tabled first (all 13 platform/core choices justified against `docs.libretro.com`'s editorial language, presented for review before any download), corrected twice against real buildbot evidence rather than the docs site during that review (PC Engine: confirmed `mednafen_pce_libretro.dll.zip`, the accurate non-Fast variant, actually exists on the buildbot despite its docs page 404ing — switched from the Fast variant originally picked only because it was the one page that resolved; Lynx: confirmed Holani's real nightly file is `holani_libretro.dll.zip` by checking `Last-Modified` against two stale decoy filenames from Nov 2024 sitting at similar paths). All 13 downloaded, hashed independently with `sha256sum`+`certutil` (all matching), archive contents listed directly to confirm each `CoreFileName` — 10 distinct files back the 13 entries (Genesis Plus GX reused for `genesis`/`sms`/`gamegear`, SameBoy for `gb`/`gbc`), all flat single-file zips, no nested-folder surprise. **All 15 seed platforms now have a data-verified catalog entry — the catalog-sourcing gap this Timeline tracked since item 17 is closed.** 137 Release / 136 Debug tests still pass unchanged (no test asserts per-platform coverage). What remains: only `nes` has actually been proven via a real interactive Auto-Install click; the other 14 (including `snes`) are data-verified but not click-tested — see ARCHITECTURE.md → ADR-11/14 and DEVELOPMENT.md → Known Limitations for the same distinction, kept explicit on purpose.
23. ~~Interactive Auto-Install + launch confirmation for 11 more platforms~~ — done, per the user's own direct report on their real machine (not reproduced by Claude). 8 Auto-Install clicks, each installing successfully and launching a real ROM: `lynx` (Holani), `wonderswan` (Beetle Cygne), `gb`/`gbc` (SameBoy), `genesis`/`sms`/`gamegear` (Genesis Plus GX), `gba` (mGBA), `pcengine` (Beetle PCE), `n64` (Mupen64Plus-Next), `nds` (melonDS DS) — 11 platforms because 3 of those clicks each covered multiple platforms sharing one core. **Combined with `nes`, 12 of 15 seed platforms are now interactively proven end-to-end.** Only `snes`, `atari2600`, and `atari7800` remain data-verified but not click-tested. Two things investigated in the same session and confirmed, not assumed: (1) the Genesis/SMS/Game Gear "same core" observation is exactly the documented multi-platform-reuse design (ADR-11), confirmed by inspecting `KnownEmulators.json` directly, not a detection bug; (2) two real Atari 2600 ROMs (`.bin` extension) in the user's actual Downloads folder went undetected — root-caused via the Bug Investigation Process to `SeedSystems.json`'s `atari2600` entry only listing `"a26"`, not `"bin"` (case-sensitivity ruled out by direct code inspection first). Not fixed — `.bin`'s ambiguity across other systems needs a real scoping decision — added to the Phase 2 backlog above and DEVELOPMENT.md → Known Limitations. The box-art-language question raised in the same session was confirmed as the already-documented, approved "first result, no scoring" simplification (Decision #4 / `MetadataService`), not a bug — nothing changed there.
24. ~~Interactive Auto-Install + launch confirmation for the final 3 platforms~~ — done, per the user's own direct report on their real machine. `atari7800` (ProSystem), `snes` (Snes9x), and `atari2600` (Stella) all installed and launched a real ROM successfully. **All 15 of 15 seed platforms are now interactively confirmed end-to-end — the data-verified-but-not-click-tested gap tracked since item 16 no longer exists for any seed platform.** See ARCHITECTURE.md → ADR-11/ADR-14 (2026-08-05 update). The `.bin`/Atari-2600 extension gap from item 23 remains open and unrelated — a scanning-side data gap, not an install-mechanism gap. This is the point Phase 2's install work was aimed at: cut `v0.2.0` next (see Timeline below).

**Phase 2's emulator auto-install mechanism is complete and fully proven: all 15 seed platforms have both a data-verified catalog entry and a real, interactive Auto-Install confirmation that launched a game.** `v0.2.0` is the natural release point for this milestone — see `README.md`/`CHANGELOG.md` for the version bump.

25. ~~Manually remove a `Game` from the library~~ — done, ARCHITECTURE.md → ADR-15. Designed first (New Feature Process): scoped to `IsMissing == true` only (not "hide any game" — a different, unrequested feature), right-click context menu on the tile (the whole tile was already a single Button fully consumed by launch, no selection model existed), confirmation dialog matching the existing Yes/No pattern. Deletes the `Game` row, its `BoxArt` row, and the cached box-art file — with a real check (not an assumption) that no *other* `BoxArt` row still references the same cached file before deleting it, since `ImageCacheService` dedupes by image URL, not `GameId`. New `ILibraryRepository.DeleteGameAsync`/`DeleteBoxArtAsync` (policy-free CRUD, matching every other repository method) and `IImageCacheService.DeleteCachedImageAsync` (best-effort, never blocks the primary deletion). 12 new tests, including one that documents (not fixes — not reachable through the UI given the `IsMissing` restriction) what `RomScannerService` actually does if a `Game` row disappears while its file remains on disk: silently re-adds it with a fresh `Guid`, confirmed by reading the scanner's dedup logic directly, not assumed. 149 Release / 148 Debug tests pass. Closes the exact gap first found during the Pokémon Emerald `.sav`-as-Game interactive session (Timeline item 20).
26. ~~`.bin`/Atari 2600 extension gap~~ — done, ARCHITECTURE.md → ADR-16. Investigated before applying anything: confirmed `"bin"` doesn't collide with any of the 15 seed platforms' real extensions, and confirmed by reading `LibraryRepository.SeedPlatformsIfEmpty` directly that seeding is one-shot at the whole-collection level — editing `SeedSystems.json` alone would never reach an already-seeded `bridge.db` (including the one used throughout this project's own testing). Fixed with a real migration, not just the JSON edit: a new `ReconcileSeedPlatformExtensions()` runs on every `LibraryRepository` open, unions each seed platform's extensions into the existing row (additive only, never removes a custom extension), and inserts any seed platform whose row is missing entirely — generalizing to the same one-shot-seeding gap for "a whole new platform," not just this one extension. 3 new tests manipulate a raw `LiteDatabase` to simulate real pre-existing data (old `["a26"]`-only row, a row with an extra custom extension, a row deleted entirely) and confirm reconciliation actually reaches it on reopen — not just a fresh database. 152 Release / 151 Debug tests pass.

---

*This document is a living plan. Update as the project evolves.*
