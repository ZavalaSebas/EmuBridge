# Bridge — Project Guide

This document serves as a guide to this specific project AND as a reference for the architecture, workflow, and decisions made during planning.

---

## Current Status (as of 2026-08-07)

Phase 1 (v0.1.0) shipped as a tagged GitHub Release. Phase 2 work has started: `EmulatorConfig` was replaced by an `Emulator`/`EmulatorProfile` split (one physical install can now back many per-platform launch configs — see ARCHITECTURE.md → ADR-11) plus a `DownloadVerificationService` (pinned-hash + exact-size verified downloads, staging-then-verify, never-fail-silently on mismatch) and a `KnownEmulators.json` catalog. Existing legacy `EmulatorConfig` data migrates automatically on first open. **The install orchestration is built, proven end-to-end, and has now been interactively tested for real across all 15 of 15 seed platforms** — `EmulatorInstallerService` downloads, extracts (`SharpCompress`, pure managed), and registers a working `Emulator`/`EmulatorProfile` for a platform, exposed via a new "Auto-Install" button in `SettingsWindow` (see ARCHITECTURE.md → ADR-14). 187 unit tests pass in Release, 186 in Debug — the Release-only manifest guard test passes because all 15 `KnownEmulatorCore` entries are fully verified data-wise, and now every one of them is also proven by a real Auto-Install click that launched a game. No seed platform remains data-verified-only.

**Phase 1's original "never actually run" gap is now fully resolved, on two separate tracks.** First, launch itself: the published `.exe` was launched and observed multiple times from this environment during the 2026-08-01 investigation (diagnostic logging confirming full startup completion, `MainWindow` construction, `mainWindow.Show()` returning, the process staying alive) — that investigation is precisely what surfaced ADR-12's native-DLL bundling bug, which F5-only testing never would have caught. Second, and separately: per the user's own direct report on their real machine (not reproduced by Claude, not required to be) — added a ROM folder, Pokémon Emerald was correctly detected, configured mGBA in `SettingsWindow` with `"{RomPath}"`, ran a rescan, launched the game successfully. Together these cover FR-01/02/03/06/07/09 with genuine interactive use, not just a passing test suite or a debugger-attached run. FR-08 (persistence across a real app restart) is now confirmed too — a full close-and-reopen, per the user's own direct report, showed the library and emulator configuration intact. FR-04/FR-05 (SteamGridDB box art lookup + caching) are confirmed as well, from the same Auto-Install interactive session: box art was seen actually rendered in the cover grid after a rescan — a stricter bar than just confirming a cached file exists on disk, since it also exercises the resize-for-display path. See `PLAN.md` → FR milestone table.

**The new "Auto-Install" button's first real interactive click found a real bug, immediately.** `KnownEmulator.ExecutableRelativePath` (`"retroarch.exe"`) was wrong — sourced from third-party documentation (ADR-11) claiming the portable `.7z` extracts flat, which it doesn't (real structure: `RetroArch-Win64/retroarch.exe`). Every automated test had passed because the hand-built test fixture matched the same wrong assumption, not reality. Root-caused by extracting the actual downloaded-and-verified `.7z` (left behind in `%LocalAppData%\Bridge\Downloads\` — ADR-14's failure handling only cleans up the extraction *target*, not the verified *source* archive) with Bridge's own `SharpCompress` code path and listing its real entries. Fixed in the manifest; the test fixture rebuilt to match the real nested structure. See ARCHITECTURE.md → ADR-11's 2026-08-03 update.

**Phase 2's catalog is complete for all 15 seed platforms, and the install mechanism is now interactively proven for all 15 of them.** `EmulatorInstallerService`, the download-verification chain, and the catalog itself are all built, tested, and — for every seed platform — confirmed working through a real interactive install that launched an actual game, per the user's own direct report, across three sessions: `nes` first, then 11 more (`lynx` → Holani, `wonderswan` → Beetle Cygne, `gb`/`gbc` → SameBoy, `genesis`/`sms`/`gamegear` → Genesis Plus GX, `gba` → mGBA, `pcengine` → Beetle PCE, `n64` → Mupen64Plus-Next, `nds` → melonDS DS), then the final 3 (`snes` → Snes9x, `atari2600` → Stella, `atari7800` → ProSystem). **The "data-verified but not click-tested" gap this section tracked throughout Phase 2's build-out no longer exists for any seed platform.** See ARCHITECTURE.md → ADR-11/ADR-14 → Consequences (2026-08-05 update).

**Two things were investigated during that same interactive session and confirmed with real evidence, not assumed.** First: several Sega ROMs (Genesis/SMS/Game Gear) appeared to use "the same core" during Auto-Install — confirmed as the documented design (ARCHITECTURE.md → ADR-11's 2026-08-04 update), not a system-detection bug: `KnownEmulators.json` has three separate `KnownEmulatorCore` entries, one per platform, that all happen to point at the identical Genesis Plus GX binary/hash, because that core genuinely covers all three systems. Second: two real Atari 2600 ROMs (`.bin` extension) in the user's actual `%UserProfile%\Downloads\ROMS` folder went undetected. Following the Bug Investigation Process: the "weird casing" hypothesis was ruled out by direct code inspection (`RomScannerService`'s extension matching is fully case-insensitive), and the real cause was confirmed by inspecting `SeedSystems.json` directly — `atari2600` only lists `"a26"`, never `"bin"`, so the files fall through to the existing `unknown`-platform fallback (ADR-6), not a scanner bug. Both were fixed the same day — see ARCHITECTURE.md → ADR-15/ADR-16.

**A full Documentation Audit Checklist pass (2026-08-06)**, triggered by a stale "Phase 1 — Not Started" header left over from Phase 0 scaffolding, found and fixed 9 real drift issues across `PLAN.md`/`ARCHITECTURE.md`/`DEVELOPMENT.md` — most notably, WPF-UI (lepo.co) was documented in 3 places as Bridge's theming library despite never being integrated; Bridge ships on stock WPF today, and WPF-UI is now an explicit Phase Polish item instead of assumed done. See `PLAN.md` → Timeline item 27 for the full list.

**`v0.3.0`'s inline Auto-Install offer is built, unit-tested, and interactively confirmed** (per the user's own direct report: main flow, decline, "unknown" never offering, and no `IsBusy` conflict between launch and scan, all confirmed on their real machine). Launching a game with no emulator configured for a real (non-`"unknown"`) platform that has a verified catalog entry now offers to install automatically, and relaunches the game on success instead of requiring a second click — see ARCHITECTURE.md → ADR-18. The core picker, `v0.3.0`'s other originally-scoped item, was investigated and deliberately deferred: no platform in `KnownEmulators.json` has more than one core today, so there's no real case to build the picker against yet. The Roadmap itself was also revised (`PLAN.md` → Roadmap): the fixed `v0.4.0`→`v0.9.0` ladder is now unnumbered product-story groups, each earning a version only once it ships.

**The "Full library" group's first item, the game detail panel, is built, unit-tested, and interactively confirmed.** Investigated first, against the real `node-steamgriddb` wrapper source, not assumed: SteamGridDB has no description/blurb or screenshot data at all — only `release_date` is available beyond what `MetadataService` already fetched. Scoped down accordingly: release year, name, platform, and the existing box art, with "Description: not available" shown explicitly rather than hidden. `BoxArt` gained a nullable `ReleaseYear`; right-click any tile → "View Details" opens the new `GameDetailWindow`. Confirmed on the user's real machine: cover, name, year, platform render correctly, "Description: not available" shows as expected, context menu works. See ARCHITECTURE.md → ADR-19.

**The group's second item, Favorites, is built, unit-tested, and interactively confirmed.** Split from "recently played" up front — different mechanics (manual toggle vs. automatic) and different verification stories, tracked and committed separately. `Game.IsFavorite`, embedded like `IsMissing`; toggled via `GameTileContextMenu`'s new "Add to Favorites"/"Remove from Favorites" item, with a star (★) indicator on the tile's cover when favorited.

**Recently played (data capture only, no UI yet) is also built, unit-tested, and confirmed in real use.** `Game.LastPlayedUtc` is set on `LaunchOutcome.Started` (not session end) from both real launch sites in `MainViewModel`, ahead of any consuming view — deliberately judged differently from the core picker's deferral (ADR-18), since launching a game is a real recurring event happening today, not a hypothetical case with zero real instances. Verification took real investigation, not a quick check: the first inspection passes showed `LastPlayedUtc` still `null` even after a fresh rebuild, which turned out to be a verification-methodology gap, not a code defect — LiteDB only checkpoints writes to the physical `bridge.db` file on a clean `Dispose()` (app close), not per-operation, and Claude's own file-inspection tooling couldn't reliably see the live file. Root-caused with temporary diagnostic instrumentation (same method as ADR-12), confirmed independently by the user checking the file's own "Date modified" in Windows Explorer, then fully removed per the Bug Investigation Process. See ARCHITECTURE.md → ADR-20 for the full record.

**The "Full library" group's fourth and final item, the refined "Library" view, is built, unit-tested, and interactively confirmed** — sorting in all 3 modes, recently-played prioritizing a same-day launch, the favorites filter in both directions, the tile's release year, and every previously-built feature (context menu, star, missing badge, detail panel) still working under the new ordering. Scoped explicitly against Phase Polish before designing — no animation, functional refinement only: 3 sort modes (Name, Recently Played, Favorites First), a Favorites-only filter, and `BoxArt.ReleaseYear` shown directly on each tile, finally putting the group's earlier data to use in the grid itself. A "hide missing" filter was investigated and deliberately not built — it would block access to the existing "Remove from Library" action for exactly the entries a user would most want hidden; the existing dimmed-opacity + "(missing)" treatment already covers the declutter need without that risk. Sort/filter changes rebuild the grid purely from an in-memory cache — no repository round-trip. **With this, all 4 items in the "Full library" group are complete and verified in real use** — per the Roadmap's own rule, that's the moment it earns a version number: `v0.5.0`. See ARCHITECTURE.md → ADR-21.

**Three versions are being cut in sequence (2026-08-07), not one.** Preparing to release found that `v0.3.0` — as originally scoped, offering Auto-Install inline — had never actually been version-bumped, and neither had two smaller, unrelated items (Remove from Library, `.bin` fix) that landed right after `v0.2.0` and had been sitting committed but unreleased since. Rather than bundle unrelated stories into one release, all three are being cut separately, in chronological order: `v0.3.0` = Remove from Library + `.bin` fix (retroactive), `v0.4.0` = inline Auto-Install, `v0.5.0` = the "Full library" group. See `PLAN.md` → Roadmap for the full renumbering note and `CHANGELOG.md` for each version's real contents.

---

## Documentation Philosophy

> **IMPORTANT: This document is a living memory of the project. Treat it as such.**

### Why This Matters

This document is designed to serve two purposes simultaneously:

1. **Technical documentation** for developers contributing to the project
2. **Context preservation** for future sessions — whether by the original developer or an AI agent continuing the work

When making changes, consider:
- Will someone reading this in 2 years understand why this decision was made?
- Would an AI agent reading this have enough context to continue the work without asking clarifying questions?
- Is the historical context preserved for future reference?

### Documentation Principles

| Principle | Application |
|-----------|-------------|
| **Precisión sobre velocidad** | Un solo error factual hace que todo el documento pierda credibilidad |
| **Mínimo cambio** | Corrige únicamente inconsistencias objetivas; no reorganices por preferencia |
| **Preserva contexto histórico** | Si algo cambió, marca el estado anterior como "was/used to" |
| **Código = Verdad** | Si la docs dice una cosa y el código otra, la docs está wrong — arréglala |
| **Testeado** | Si no está documentado, no existe para un nuevo contributor |

---

## Why Bridge?

Bridge exists to remove the manual setup friction of retro emulation — sorting ROMs by system, finding the right emulator/core per console, learning each emulator's launch syntax, and separately hunting for box art. It combines Playnite's centralized-library architecture with EmulationStation's presentation style, scoped specifically to emulation (no storefront integration). Bridge manages ROMs and emulators the user already owns — it never includes, facilitates, or links to ROM acquisition.

---

## Architecture

Bridge's service design, technology stack rationale, and key design decisions (including the WPF vs. WinUI 3 evaluation and the image-caching/animation performance notes) live in [ARCHITECTURE.md](ARCHITECTURE.md), not here — this document was split from ARCHITECTURE.md from the start given the scope of this project. Consult it before making a change that affects how a service is designed or why a dependency was chosen.

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
│   │   ├── Emulator.cs / EmulatorProfile.cs / InstallSource.cs / ResolvedEmulatorProfile.cs
│   │   ├── KnownEmulator.cs / KnownEmulatorCore.cs
│   │   ├── DownloadOutcome.cs / DownloadResult.cs
│   │   ├── InstallOutcome.cs / InstallResult.cs
│   │   ├── ScanFolder.cs
│   │   ├── ScanResult.cs
│   │   ├── BoxArt.cs
│   │   ├── MetadataFetchResult.cs
│   │   └── LaunchResult.cs
│   ├── Resources/
│   │   ├── SeedSystems.json      # EmbeddedResource — 15 built-in platforms
│   │   └── KnownEmulators.json   # EmbeddedResource — curated emulator/core catalog (see ADR-11)
│   ├── Services/
│   │   ├── ILibraryRepository.cs / LibraryRepository.cs
│   │   ├── IRomScannerService.cs / RomScannerService.cs
│   │   ├── ISettingsService.cs / SettingsService.cs
│   │   ├── IImageCacheService.cs / ImageCacheService.cs
│   │   ├── IMetadataService.cs / MetadataService.cs
│   │   ├── IEmulatorService.cs / EmulatorService.cs
│   │   ├── ArgumentTemplate.cs     # shared {Token} resolver, used by EmulatorService + LaunchService
│   │   ├── ILaunchService.cs / LaunchService.cs
│   │   ├── IDownloadVerificationService.cs / DownloadVerificationService.cs
│   │   ├── IEmulatorInstallerService.cs / EmulatorInstallerService.cs   # SharpCompress extraction, see ADR-14
│   │   ├── MessageBoxService.cs      # IMessageBoxService/MessageBoxService
│   │   ├── FolderPickerService.cs    # IFolderPickerService/FolderPickerService
│   │   └── FilePickerService.cs      # IFilePickerService/FilePickerService
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   ├── GameTile.cs
│   │   ├── SettingsViewModel.cs
│   │   ├── PlatformConfigItem.cs
│   │   └── GameDetailViewModel.cs
│   ├── App.xaml / App.xaml.cs        # composition root — DI wiring, no StartupUri
│   ├── MainWindow.xaml / .xaml.cs
│   ├── SettingsWindow.xaml / .xaml.cs
│   ├── GameDetailWindow.xaml / .xaml.cs   # "View Details" context menu item, see ADR-19
│   └── Config.cs
├── Bridge.Tests/
│   ├── ViewModels/
│   │   ├── MainViewModelTests.cs
│   │   ├── SettingsViewModelTests.cs
│   │   └── GameDetailViewModelTests.cs
│   └── Services/
│       ├── LibraryRepositoryTests.cs
│       ├── RomScannerServiceTests.cs
│       ├── SettingsServiceTests.cs
│       ├── ImageCacheServiceTests.cs
│       ├── MetadataServiceTests.cs
│       ├── ArgumentTemplateTests.cs
│       ├── EmulatorServiceTests.cs
│       ├── LaunchServiceTests.cs
│       ├── DownloadVerificationServiceTests.cs
│       ├── EmulatorInstallerServiceTests.cs   # real .zip fixtures through real SharpCompress — see ADR-14
│       ├── KnownEmulatorsManifestTests.cs   # #if RELEASE guard — see ADR-11
│       ├── FakeLibraryRepository.cs
│       ├── FakeSettingsService.cs
│       ├── FakeImageCacheService.cs
│       ├── FakeEmulatorService.cs
│       ├── FakeEmulatorInstallerService.cs
│       ├── FakeDownloadVerificationService.cs
│       ├── FakeRomScannerService.cs
│       ├── FakeMetadataService.cs
│       ├── FakeLaunchService.cs
│       ├── FakeMessageBoxService.cs
│       ├── FakeFolderPickerService.cs
│       ├── FakeFilePickerService.cs
│       ├── FakeHttpMessageHandler.cs
│       └── SynchronousProgress.cs   # shared IProgress<T> test double — avoids Progress<T>'s async-dispatch flakiness
├── docs/
└── Bridge.slnx
```

---

## Version Management

**Single source of truth**: `<Version>` in `Bridge/Bridge.csproj`

```xml
<Version>0.1.0</Version>
<AssemblyVersion>$(Version).0</AssemblyVersion>
```

- `AssemblyVersion` derives from `$(Version)` so assembly version is correct (e.g., `0.1.0.0`)
- **Updater pattern** (optional): fetch `https://api.github.com/repos/{owner}/{repo}/releases/latest`, compare `Version.TryParse(tag.TrimStart('v'))` against `Config.AssemblyVersion`. If remote is newer, download the `.exe` asset.

  The most critical part is the **safe executable swap** — never overwrite the running `.exe` directly:

  ```csharp
  var currentExe = Environment.ProcessPath;
  var tempExe = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid()}.exe");
  var oldExe = currentExe + ".old";

  await NetworkHelper.DownloadFileAsync(downloadUrl, tempExe);
  File.Delete(oldExe);           // discard any stale .old
  File.Move(currentExe, oldExe); // rename running exe → .old
  File.Move(tempExe, currentExe); // rename downloaded → current location

  Process.Start(new ProcessStartInfo { FileName = currentExe, UseShellExecute = true });
  Environment.Exit(0);           // terminate running instance so new exe takes over
  ```

  On next launch, `CleanupOldExe()` deletes the `.old`. If the new process fails to start, a rollback moves `.old` back.

**To bump the version**: edit `<Version>` in the csproj, commit with a descriptive message, push to `main`.

### Welcome Sentinel

Show a welcome dialog on first launch or after a version change:

```csharp
// In Config.cs
public const string WelcomeSentinelFile = "welcome_sentinel.txt";
public static readonly string AppDataPath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bridge");

// Sentinel check
public static bool ShouldShowWelcome()
{
    var flagPath = Path.Combine(Config.AppDataPath, Config.WelcomeSentinelFile);
    if (!File.Exists(flagPath)) return true;
    return File.ReadAllText(flagPath) != Config.AssemblyVersion;
}

// After showing welcome (e.g. in MainWindow startup):
if (ShouldShowWelcome())
{
    var welcome = new WelcomeWindow { Owner = this };
    welcome.ShowDialog();
}
```

When the user dismisses the dialog with "Don't show again", write the current version to the sentinel file so it won't show again until the version changes.

### Constants Pattern (`Config.cs`)

**Prefer keeping constants centralized** in a dedicated `Config.cs` file rather than scattered across classes.

```csharp
public static class Config
{
    public const string AppName = "Bridge";
    public const string GitHubApiUrl = "https://api.github.com/repos/ZavalaSebas/Bridge/releases/latest";
    public const int RequestTimeoutSeconds = 10;
    // ... etc
}
```

---

## Semantic Versioning (SemVer)

> **Always follow SemVer for version numbers.**

Format: `MAJOR.MINOR.PATCH`

```
MAJOR.MINOR.PATCH
  │     │     │
  │     │     └── Fixes, bugs, security patches
  │     └──────── New features (backwards compatible)
  └────────────── Breaking changes (incompatible with previous)
```

### When to bump

| Change Type | Bump | Example |
|-------------|------|---------|
| Fix bug | PATCH | `1.0.0` → `1.0.1` |
| New feature | MINOR | `1.0.1` → `1.1.0` |
| Breaking change | MAJOR | `1.0.0` → `2.0.0` |
| Pre-release | Suffix | `1.0.0-beta.1` |

### Rules

1. **Start at 0.x.y** — while in development, MAJOR is 0. Bridge starts at `0.1.0`.
2. **Once 1.0.0** — public API is stable
3. **Never reuse versions** — if you delete a release, don't reuse that version number
4. **Update CHANGELOG.md** — document what changed in each version

---

## Release Process (CI/CD)

On push to `main`, `.github/workflows/release.yml` runs:

1. **Check version change** — compares `<Version>` in HEAD vs HEAD~1
2. **Build** — `dotnet build Bridge.slnx -c Release`
3. **Test** — `dotnet test Bridge.slnx -c Release` (no `--no-build` — the `test` job runs on its own fresh runner with no shared build output from `build`; see the `v0.2.0` section below for why this line drifted from the real workflow once already)
4. **CodeQL** — security scanning
5. **NuGet Audit** — vulnerability check
6. **Release** (only if version changed):
   - `dotnet publish` as self-contained single-file
   - Generate body from commit message
   - Create tag + release with `.exe`

### Critical workflow details

- `fetch-depth: 0` — required so `git show HEAD~1:path` can access the parent commit
- `permissions: contents: write` — required for release creation
- Csproj path: `Bridge/Bridge.csproj`
- Release body comes from the **commit body** — write it with `### Added/Fixed/Changed` sections

### Additional Quality Gates (optional)

Beyond the standard CI pipeline, these checks can be added as the project grows:

| Gate | Tool | When to Add |
|------|------|-------------|
| Formatting enforcement | `dotnet format` | Team with shared style |
| Linting | Roslyn analyzers (StyleCop) | Team or strict consistency |
| Coverage threshold | `coverlet` + gate | Before 1.0.0 |
| API compatibility check | `ApiChange` | Pre-1.0.0 stabilizes |

These are **not enabled by default** — add them only when the team size or project scope justifies the overhead.

### v0.1.0: no associated GitHub Actions run

`v0.1.0`'s tag and GitHub Release were created **manually** (`git tag` + `gh release create`), not by `release.yml`. This is expected, not a bug: the workflow's version-change check (`git show HEAD~1:Bridge/Bridge.csproj` vs `HEAD`) compares against the *previous* commit's `<Version>`, and `0.1.0` was the scaffolded starting value — the bump commit didn't change it, so `changed` evaluated `false` and the `release` job never ran. This is a one-time gap specific to a "genesis" version with no real prior version to diff against.

**Correction:** the original version of this note predicted every release from `0.2.0` onward would trigger the automated `release` job normally. That held for the version-change detection itself, but `v0.2.0` still ended up manual too — for a real, different reason. See the next section.

### v0.2.0: also no associated GitHub Actions run, for a different reason

`v0.2.0`'s tag and GitHub Release were **also** created manually — not the same "genesis version" non-issue as `v0.1.0`, but a real, structural limitation of the version-change check discovered in the process.

The bump commit (`61ae5f5`) had a genuine version change (`0.1.0` → `0.2.0`) and pushed successfully, but its CI run failed: the `test` job's `dotnet test Bridge.slnx -c Release --no-build` had never actually worked (confirmed by checking `v0.1.0`'s bump-commit CI run too — identical failure, just never surfaced because `v0.1.0`'s release was already being created manually for the genesis reason above, so nobody was depending on that job passing). `--no-build` requires build output from an earlier step in the *same job*, but the `test` job runs on its own fresh GitHub Actions runner with no shared filesystem with the `build` job — there was never anything to reuse. Fixed by dropping `--no-build` (`test` now builds itself; evaluated and rejected sharing build artifacts via `upload-artifact`/`download-artifact` instead, since this repo is public — CI minutes are free — and `codeql` already dominates the pipeline's wall-clock time, so the duplicate build costs nothing in practice).

The fix landed in a **second, separate commit** (`ddaf02a`), after the version bump. This is where the structural limitation bites: the version-change check is a one-shot comparison of `HEAD~1` vs `HEAD` for whichever commit triggered the run. By the time the CI fix was verified (in its own, non-version-changing commit), `HEAD~1` was the bump commit itself — same version as `HEAD`, so `changed` evaluates `false` on every subsequent push, permanently, for that release. Re-running the original failed workflow run doesn't help either: GitHub Actions re-runs a job using the workflow YAML as it existed at the commit that triggered it, not the current `main`, so re-running the bump commit's run would hit the exact same `--no-build` bug again.

**The general lesson, not just this one instance:** the version-change check can only ever fire once, on the exact commit that changes `<Version>`. If anything blocks that specific run from reaching the `release` job — a flaky CI job, a bug in the workflow itself, an infrastructure outage — there is no automated way to retroactively claim that release later; falling back to the same manual `git tag` + `gh publish` + `dotnet publish` + `gh release create` process used for `v0.1.0` and `v0.2.0` is the correct recovery, not a workaround to be embarrassed about. The manual process's own verification discipline (isolate the `.exe` in an empty folder before trusting it, hash the uploaded asset against the local build) doesn't go away just because the automated pipeline usually handles it — see the Release Checklist above.

### v0.3.0: the automated release job worked, for the first time

Unlike `v0.1.0`/`v0.2.0`, `v0.3.0`'s tag and GitHub Release were created **by `release.yml` itself** — the `--no-build` fix verified after `v0.2.0` held under real conditions this time, not just in the one-off CI run that confirmed it. Verified anyway, not assumed: downloaded the actual uploaded asset (not just checked it existed), confirmed its hash independently against what GitHub itself reports, and isolate-tested it in an empty folder per the checklist above. One real thing learned in the process: the downloaded asset's hash did **not** match the local build made before pushing — expected and harmless (see the Release Checklist note above on why local-build-hash comparisons don't apply to automated releases), not a repeat of the `v0.2.0`-style gap.

### v0.4.0: automated release, same verification, same result

Same as `v0.3.0` — `release.yml` fired correctly, the downloaded asset's hash matched GitHub's own reported digest exactly (confirming download integrity), and that exact file, isolated in an empty folder, ran cleanly (stable memory, empty `stderr`, closed on request rather than crashing). No new findings this time; the pattern established at `v0.3.0` held. https://github.com/ZavalaSebas/Bridge/releases/tag/v0.4.0

### v0.5.0: automated release, same verification, same result

Same as `v0.3.0`/`v0.4.0` — `release.yml` fired correctly, the downloaded asset's hash matched GitHub's own reported digest exactly, and that exact file, isolated in an empty folder, ran cleanly. This closes the retroactive 3-version sequence (`v0.3.0`, `v0.4.0`, `v0.5.0`) covering everything built but not yet released as of the Documentation Audit that started this stretch of work. https://github.com/ZavalaSebas/Bridge/releases/tag/v0.5.0

---

## Release Checklist

### Pre-release

- [ ] All features for this version are complete
- [ ] All tests pass locally: `dotnet test Bridge.slnx -c Release`
- [ ] No compiler warnings (or warnings documented)
- [ ] Code reviewed (if working with others)

### Version Bump

- [ ] Update `<Version>` in `Bridge/Bridge.csproj`
- [ ] Update `CHANGELOG.md` with new version and changes
- [ ] Commit with subject `bump vX.Y.Z — <short summary>` and body with `### Added / Fixed / Changed` sections (the commit body becomes the GitHub Release body)

### Commit & Push

- [ ] `git status` — no unexpected changes
- [ ] `git diff` — review all changes
- [ ] `git log --oneline -3` — verify commit history
- [ ] `git push origin main`

### Post-release

- [ ] Verify GitHub Actions workflow completed
- [ ] Check release page on GitHub
- [ ] **Test the downloaded `.exe` in isolation** — copy *only* the downloaded `.exe` into an empty folder and run it from there, nothing else present. Checking the asset's byte size, or running it from a folder that still has other publish output sitting next to it, is not sufficient — see ARCHITECTURE.md → ADR-12: the `v0.1.0` release shipped completely broken (WPF's native interop DLLs missing from the bundle) and the only verification done at the time was confirming `Bridge.exe`'s size matched, which caught nothing.
- [ ] Update documentation if needed

**Hash verification means something different for an automated release than a manual one — found for real during `v0.3.0`.** `PublishSingleFile` isn't byte-reproducible across separate `dotnet publish` invocations (embedded timestamps/GUIDs differ), so a local build's hash will **not** match what CI's own independent `dotnet publish` (in the `release` job, on a fresh runner) uploads — even when both are byte-size-identical and functionally correct. Comparing a local build's hash against an automated release's asset will always "fail" and is not a meaningful check. What actually verifies an automated release: (1) confirm the *downloaded* asset's hash matches what GitHub itself reports for that asset (`gh release view --json assets` → `digest` field, or recompute independently — this catches a corrupted/tampered upload, not a rebuild difference), and (2) isolate-test that same downloaded asset per the step above. The local-build-hash-matches-uploaded-asset check only applies to the **manual** release path (`v0.1.0`/`v0.2.0`'s process below), where the exact file already tested locally is the one `gh release create` uploads — there, a mismatch would mean something real went wrong.

### Hotfix

When a critical bug is found in a released version and cannot wait for the next regular release:

1. **Identify the tag** of the broken release — `git tag --list 'v*' | sort -V`
2. **Create branch** from that tag: `git switch -c hotfix/v1.1.1 v1.1.0` — branch name `hotfix/v1.1.1` on the left, existing tag `v1.1.0` (the broken release) on the right
3. **Fix the bug** — apply only the minimal changes needed; no unrelated refactors
4. **Bump `<Version>`** in `Bridge/Bridge.csproj` — increment PATCH only (e.g. `1.0.0` → `1.0.1`)
5. **Update `CHANGELOG.md`** — add entry under new version with `### Fixed`
6. **Commit** with subject `bump vX.Y.Z — <short summary>` and body describing the fix
7. **Push branch**: `git push origin hotfix/v1.1.1`
8. **Open PR** to `main` with a clear title referencing the hotfix version
9. **Merge PR** — CI runs on merge to `main` and creates the release automatically
10. **Verify** — release created on GitHub and the `.exe` works

---

## Documentation Sync Map

When you make a change, consult this table to know which documents to update:

| If you changed… | Update these document(s) | What to update specifically |
|---|---|---|
| **A user-facing feature** (added, modified, or removed) | `README.md` (Features section), `CHANGELOG.md` | README: add/update the feature name with a one-line description; CHANGELOG: new entry under `[Unreleased]` with `### Added` / `### Changed` / `### Removed` |
| **A breaking API change** | `CHANGELOG.md`, `DEVELOPMENT.md` (Version Management → SemVer) | CHANGELOG: `### Changed` with migration instructions; DEVELOPMENT.md: verify the change justifies a MAJOR bump per SemVer rules |
| **An architecture decision or the "why" behind a pattern** | `ARCHITECTURE.md` (Key Design Decisions) | Add a row to the table with the decision, rationale, and consequences |
| **A discovered limitation or unfixable bug** | `DEVELOPMENT.md` (Known Limitations table) | Add a row with the limitation, its root cause, and the recommended workaround |
| **The build, test, or release process** (CI workflow, scripts, tooling) | `DEVELOPMENT.md` (Release Process / CI-CD), `.github/workflows/release.yml` (the source of truth) | Update the prose description to match the actual workflow — if the YAML changed, the docs must reflect it |
| **A new NuGet dependency** | `ARCHITECTURE.md` (Key Design Decisions) — only if the choice is architecturally significant | Add a table row explaining why this library was chosen over alternatives (batteries-included vs lightweight, license compatibility, etc.) |
| **The project folder structure** (new project, new top-level folder) | `DEVELOPMENT.md` (Project Structure) | Update the ASCII tree to match the new layout |
| **The environment requirements** (SDK version, OS, IDE) | `DEVELOPMENT.md` (Development Environment Setup) | Update the Requirements table with the new version or tool |
| **An error-handling pattern** (custom exception, global handler change) | `DEVELOPMENT.md` (Error Handling) | Add the new exception class or update the requirements / examples |
| **The contribution workflow itself** (PR process, branch naming, review policy) | `CONTRIBUTING.md` (Workflow) | Update the numbered steps, commit format, or branch naming conventions |

> Note: the upstream `project-template/DEVELOPMENT.template.md` also has a "bootstrapping a new project from this template → `NEW_PROJECT_CHECKLIST.md`" row here. It's intentionally omitted in Bridge's copy — Bridge is a consumer of the template, not itself a template other projects bootstrap from, and `NEW_PROJECT_CHECKLIST.md` doesn't live in this repo (see `project-template/`).

**Rule:** Before marking a code change as complete, review this table and decide whether any document needs updating accordingly. Document updates are part of the change, not an afterthought — include them before closing the work.

---

## Documentation Audit Checklist

Over time, documentation drifts from reality. Run this audit periodically (or when something feels "off" in the docs) to bring it back in sync:

1. **Read all documents in full** — not skimming, every word. You cannot spot inconsistencies in a document you haven't fully read.
2. **Compare every claim against the actual code/state** — do not assume anything is still true. If it says "the config lives in Config.cs", verify that Config.cs still exists and has that constant.
3. **Classify each finding** as one of:
   - **Inconsistency** — the docs say X, the code does Y (contradiction)
   - **Outdated** — the docs describe something that no longer exists or has changed
   - **Redundant** — the same information appears in multiple places with risk of future drift
4. **Present findings in a table** with one row per issue: location, finding type, description, and proposed action
5. **Present findings for review** before touching any file — do not "just fix" inconsistencies without a shared understanding of what needs to change and why
6. **Stick to the agreed scope** — no scope creep during a documentation pass. If new inconsistencies appear during the fix, log them separately and address in a follow-up pass.
7. **Final cross-check**: after applying changes, verify that documents do not contradict each other. Strip any "planned"/"future"/"roadmap" language from active documents — track future ideas in GitHub Issues if needed, not in a living roadmap document that will drift again.

---

## Tests

Run locally with: `dotnet test Bridge.slnx -c Release`

Unit tests cover services (`RomScannerService`, `MetadataService`, `EmulatorService`, `LaunchService`) in isolation, mocking external dependencies (filesystem, SteamGridDB HTTP client, process launch). ViewModels are tested via their public commands and observable properties, not their bound Views.

> **Note**: Tests run in CI on every push. If a test fails, the build is blocked.

### Test conventions

- One `[Fact]` per test method (no `[Theory]` unless data-driven)
- No test dependencies — each test is independent
- Arrange → Act → Assert pattern
- Test class name = Service/Class name + "Tests" (e.g., `MyServiceTests`)
- Namespace mirrors source: `Bridge.Tests.Services.MyServiceTests`

---

## Logging

This project uses `Microsoft.Extensions.Logging` with ILogger injection.

### Requirements

- **ILogger must be injected** in all services and ViewModels via constructor
- **Log levels must be used appropriately**:
  - `LogInformation` — normal operations, user actions
  - `LogWarning` — recoverable issues, unexpected but handled states
  - `LogError` — failures that affect operation
- **No `Debug.WriteLine`** — use ILogger
- **No silent exception swallowing** — log errors with context

### Example

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }

    public async Task DoSomethingAsync()
    {
        _logger.LogInformation("Starting operation for {Item}", itemId);
        try
        {
            await _client.SendAsync(itemId);
            _logger.LogInformation("Operation completed for {Item}", itemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed for {Item}", itemId);
            throw;
        }
    }
}
```

---

## Error Handling

### Requirements

1. **Never swallow exceptions silently** — always log or notify the user
2. **Global exception handler** in `App.xaml.cs` for unhandled exceptions
3. **Use custom exceptions** when they add context (see `Exceptions/` folder)
4. **User-facing errors** should update StatusMessage or show a dialog

### Custom Exceptions

Define domain-specific exceptions in `Exceptions/` folder:

```csharp
public class BridgeException : Exception
{
    public BridgeException(string message) : base(message) { }
    public BridgeException(string message, Exception inner) : base(message, inner) { }
}
```

---

## Bug Investigation Process

When investigating a bug, the goal is to find the *actual* cause — not the first explanation that sounds convincing. Follow this process to avoid wasting time on plausible but wrong theories:

1. **Formulate a specific, testable hypothesis** — not "it might be X", but "if X is the cause, then when I do Y, Z should happen".
2. **Test that hypothesis with real evidence** (logs, temporary instrumentation, actual execution) — never accept a hypothesis because it "sounds logical" or because the code "looks similar" to a working reference.
3. **If the hypothesis is ruled out by evidence, say so explicitly and move on** — do not leave it as a "possible cause" without resolution.
4. **When an explanation is accepted, confirm it with a direct test before applying the fix** — do not fix based on theory alone.
5. **After the fix, add a regression test** that would have caught the original bug.
6. **Document the finding before closing** — do not let the real cause live only in the conversation history. Log it in the appropriate place depending on the type of finding:
   - **Actual bug that was fixed**: entry in `CHANGELOG.md` under `### Fixed` with a brief description of the *root cause*, not just the symptom.
   - **Investigation revealed a known limitation** that cannot be resolved now: `DEVELOPMENT.md` (Known Limitations table), with root cause and workaround.
   - **Investigation ruled out a hypothesis worth recording** (e.g., "not a threading issue — confirmed with test X so nobody has to rediscover that"): a short note in `ARCHITECTURE.md` or `DEVELOPMENT.md` as appropriate.
7. **Clean up any temporary instrumentation/logging** used during the investigation before closing.

---

## Dependency Injection

Use `Microsoft.Extensions.DependencyInjection` for service management.

### Registration

```csharp
// App.xaml.cs
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
services.AddTransient<MainViewModel>();
// ... etc
ServiceProvider = services.BuildServiceProvider();
```

### Lifetime Guidelines

| Lifetime | Use for |
|----------|---------|
| `Singleton` | Services that hold state, external connections |
| `Transient` | ViewModels, lightweight stateless services |
| `Scoped` | Rarely used in WPF |

---

## MVVM Pattern

This project follows the **Model-View-ViewModel** pattern for UI separation.

### Components

| Component | Responsibility |
|-----------|----------------|
| **Model** | Data and business logic (no UI) |
| **View** | XAML + code-behind (visible UI only) |
| **ViewModel** | UI state + commands, exposes data to the View |

### ViewModel Requirements

- Inherit from `ObservableObject` or implement `INotifyPropertyChanged`
- Expose data via properties — never fields
- Expose actions via `ICommand` properties
- No direct reference to Views

```csharp
public class MainViewModel : ObservableObject
{
    private string _status;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand SaveCommand => new RelayCommand(Save);

    private void Save()
    {
        Status = "Saved";
    }
}
```

### Commands

Use `RelayCommand` (from CommunityToolkit.Mvvm) for simple commands:

```csharp
public ICommand SaveCommand => new RelayCommand(Save);
public ICommand AsyncCommand => new AsyncRelayCommand(LoadDataAsync);
```

### View-ViewModel Wiring

In `App.xaml.cs` or a DI container, create the ViewModel and assign it as the View's `DataContext`:

```csharp
var mainVm = ServiceProvider.GetRequiredService<MainViewModel>();
var mainWindow = new MainWindow { DataContext = mainVm };
mainWindow.Show();
```

### WPF Bindings

Bind ViewModel properties to XAML using `{Binding}`:

```xml
<TextBlock Text="{Binding Status}" />
<Button Content="Save" Command="{Binding SaveCommand}" />
```

---

## Async/Await Patterns

All I/O operations should be async.

### Requirements

- Use `async Task` return types
- **Never use `.Result` or `.Wait()`** — blocks the UI thread
- Use `CancellationToken` for cancellable operations
- Use `IProgress<T>` for progress reporting

### Example

```csharp
public async Task<List<Item>> GetItemsAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Fetching items");

    var response = await _httpClient.GetAsync(_url, cancellationToken);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<List<Item>>(content);
}
```

---

## Configuration

All configuration goes in `Config.cs`:

```csharp
public static class Config
{
    // URLs
    public const string ApiUrl = "https://api.example.com";
    public const string UserAgent = "Bridge/0.1.0";

    // Timeouts
    public const int RequestTimeoutSeconds = 10;

    // Paths
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bridge");
}
```

### Rules

- **No magic numbers** — use named constants
- **No hardcoded secrets** — use environment variables or user input. The SteamGridDB API key specifically must never be hardcoded — see PLAN.md → Open Decisions #4 for how it will be supplied
- **Urls configurable** — makes testing easier

---

## Key Files Quick Reference

| File | Purpose |
|------|---------|
| `Bridge/Config.cs` | Centralized constants: `AppDataPath`, `LibraryDbPath`, `SettingsPath`, `ImageCachePath`, `UnknownPlatformId`, SteamGridDB base URL, seed resource name |
| `Bridge/App.xaml.cs` | DI composition root (all services + ViewModels registered), explicit `MainWindow` construction (no `StartupUri`), `DispatcherUnhandledException` global handler — implemented (see ADR-10) |
| `Bridge/Services/RomScannerService.cs` | Scans configured folders, detects ROM files, maps extension to platform, tracks missing ROMs; validates and adds scan folders (`Directory.Exists`, fails early) — implemented, tested |
| `Bridge/Services/LibraryRepository.cs` | LiteDB-backed persistence: platforms (seeded), games, scan folders, box art — implemented, tested |
| `Bridge/Resources/SeedSystems.json` | 15 built-in platforms (cartridge/handheld only — see ARCHITECTURE.md → ADR-7), embedded resource |
| `Bridge/Services/MetadataService.cs` | SteamGridDB search + grids lookup, batch box-art fetch, stop-early on rate-limit/auth failure — implemented, tested (see ADR-8) |
| `Bridge/Services/ImageCacheService.cs` | Downloads, resizes (WPF-native decode), and caches box art locally at display resolution — implemented, tested |
| `Bridge/Services/SettingsService.cs` | DPAPI-encrypted SteamGridDB API key storage in `settings.json` — implemented, tested (see ADR-5) |
| `Bridge/Services/EmulatorService.cs` | Validates and persists an `EmulatorProfile` against its `Emulator` (exe exists, `{RomPath}` present, `PlatformId` valid); resolves the join for callers — implemented, tested (see ADR-9, ADR-11) |
| `Bridge/Services/ArgumentTemplate.cs` | Shared `{Token}` resolver (`Validate`/`Expand`), used by both `EmulatorService` and `LaunchService` — implemented, tested |
| `Bridge/Services/LaunchService.cs` | Re-checks ROM/emulator existence, expands arguments, launches the process, exposes exit as a `Task` — implemented, tested (see ADR-9) |
| `Bridge/Services/DownloadVerificationService.cs` | Downloads to a staging path, verifies exact size (pre-check + streaming cutoff) and SHA256 before treating a file as installed; deletes and reports specifically on any mismatch — implemented, tested (see ADR-11) |
| `Bridge/Services/EmulatorInstallerService.cs` | Orchestrates auto-install: downloads+extracts (`SharpCompress`) a known emulator/core, registers the resulting `Emulator`/`EmulatorProfile` via `EmulatorService` — implemented, tested end-to-end with real archive fixtures (see ADR-14) |
| `Bridge/Resources/KnownEmulators.json` | Curated, hand-verified emulator/core catalog — RetroArch entry and all 15 of 15 platform cores verified, each with a real, interactive Auto-Install confirmation that launched a game — see ADR-11/ADR-14 |

---

## Known Limitations

| Limitation | Reason | Workaround |
|------------|--------|------------|
| `LaunchService`'s Phase 1 exit detection does not correctly detect when the emulator has closed if the launched process is a wrapper/launcher that spawns the real emulator process and exits itself (e.g. an updater shim, a single-instance relaunch, or a `.bat`/`.cmd` wrapper) — Bridge would return control to the launcher while the actual emulator is still running. | Phase 1 tracks the process handle returned directly by `Process.Start()` (see PLAN.md → Open Decisions #5, ARCHITECTURE.md → ADR-1), chosen deliberately over Windows Job Object process-tree tracking to avoid P/Invoke complexity before the wrapper/launcher problem is confirmed to occur frequently in practice. Directly related to the process-exit-detection bug class found in OrbSpoofer. | If this proves frequent for real emulators being configured, implement process-tree tracking via Windows Job Objects (`CreateJobObject`/`AssignProcessToJobObject`) — see ARCHITECTURE.md → ADR-1 for the noted improvement path. |
| `RomScannerService`'s per-file permission-denied handling (`UnauthorizedAccessException`/`IOException` caught on an individual file during scanning — see ARCHITECTURE.md → ADR-6) is implemented but not covered by an automated test. | Reliably simulating a permission-denied file in a portable, fast unit test requires manipulating Windows ACLs, which is fragile and slow — not worth the cost for Phase 1. The other error-handling cases (missing folder, empty file) are covered; this one specifically isn't. | Verify manually if this code path changes (create a file, deny read access via `icacls`, run a scan, confirm it's skipped and logged) rather than relying on the automated suite for this specific path. Revisit with a filesystem abstraction (e.g. `IFileSystem`) if untestable-I/O-error coverage becomes a recurring need beyond this one case. |
| `MetadataService` has no "safe to retry at" timestamp for a SteamGridDB rate-limit (429) stop — it can only say "stopped early, remaining games pending for the next batch run, whenever that is." | Confirmed by checking the actual `Retry-After`-style handling in three independent sources — the official Node.js wrapper's source code (`SteamGridDB/node-steamgriddb`), the community .NET wrapper (`craftersmine/SteamGridDB.NET`), and general web search — none document or read a rate-limit-retry header from SteamGridDB. Not assumed; actively looked for and not found. Fabricating an arbitrary wait time was explicitly rejected in favor of documenting this as a real gap. | If SteamGridDB ever adds a documented rate-limit header, or if inspecting real 429 responses at runtime turns up an undocumented one, wire it into `MetadataFetchResult` then. Until confirmed, don't guess a backoff duration. |
Populate further rows as additional limitations are discovered during development.

---

## Architecture Decision Records (ADR)

For significant architectural decisions, document the context, decision, and consequences.

### Creating New ADRs

Refer to [`ARCHITECTURE.md`](ARCHITECTURE.md) for the ADR format and instructions on adding new records.

---

## Workflow Rules

**These are strict rules that must always be followed:**

1. **Never commit without explicit confirmation** — a commit should represent a coherent, verified unit of work. Run `dotnet build -c Release` and `dotnet test -c Release`, review the diff, and confirm the change is ready before staging. Whether that confirmation comes from yourself, a teammate, or whoever is directing the work, do not commit without it.
2. **Never push without explicit confirmation** — pushing to main triggers CI and creates a GitHub release automatically. Unlike a local commit, a push is visible to everyone and harder to undo cleanly once the pipeline ran. Do not push without explicit confirmation that the change is ready, whether that confirmation comes from yourself, a teammate, or whoever is directing the work.
3. **Verify build and tests locally before pushing** — run `dotnet build -c Release` and `dotnet test -c Release`.
4. **Multiple commits are fine for progress**, but group them meaningfully when pushing.
5. **Commit messages matter** — subject line ≤72 chars, body describes exactly what was done and why. For version bumps, the body becomes the release notes.
6. **Force push only for cleanup** — when squashing test commits or fixing history. Never force push over someone else's work.

### Good commit structure:

```
feat: add new feature

### Added
- Feature description

### Fixed
- Bug fix description

### Changed
- Breaking change description
```

### Git Hooks

> **Los git hooks NO se versionan automáticamente.** La carpeta `.git/hooks/` es local y no se comparte. Si el proyecto define hooks para verificaciones pre-commit, deben instalarse manualmente:

```bash
# Opción A: hooks versionados en hooks/ (crear carpeta si no existe)
git config core.hooksPath hooks/

# Opción B: copiar manualmente
cp hooks/pre-commit .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
```

Ninguna validación pre-commit está activa por defecto — cada desarrollador debe decidir instalarla. La regla **3 (verificar build/test local)** aplica siempre, haya o no hooks instalados.

---

## New Feature Process

Before writing code for a new feature, follow this process:

1. **Inventory options and scope first** — what are the possible approaches? What is in scope vs explicitly out of scope? Do not commit to a solution before understanding the landscape.
2. **Explicit design first** — cover background states that are easy to skip:
   - UI interaction flow (happy path + edge cases)
   - Loading and error states
   - Cancellation behavior (if applicable)
   - What happens in boundary cases (empty data, network failure, concurrent access)
3. **Write the design down before writing code** — scope, approach, and edge cases should be explicit and reviewable before implementation starts.
4. **Review the actual code at each step** — read the diff, run the tests, verify edge cases were actually handled. Do not accept a summary of "it's done" as verification.
5. **Tests cover the background states from the design** — not just the happy path. Every state documented in step 2 should have a corresponding test.
6. **Cross-check**: does this change require documentation updates? Consult the [Documentation Sync Map](#documentation-sync-map) before closing.

---

## Development Environment Setup

### Requirements

| Requirement | Version | Notes |
|------------|---------|-------|
| OS | Windows 10/11 | Required |
| .NET SDK | 10 | `dotnet --version` to verify |
| IDE | VS 2022 17.10+ / VS Code / Rider | .NET 10 support |
| Git | Latest | Version control |

### First Steps

```bash
# 1. Clone the repo
git clone https://github.com/ZavalaSebas/Bridge.git
cd Bridge

# 2. Restore packages
dotnet restore

# 3. Build
dotnet build Bridge.slnx -c Release

# 4. Run tests
dotnet test Bridge.slnx -c Release

# 5. Run the app
dotnet run --project Bridge/Bridge.csproj
```

---

## Branding & Sponsorship

### Heart Icon in Status Bar

Add a sponsor link in the status bar with a heart icon:

```xml
<!-- MainWindow.xaml -->
<StatusBar Grid.Row="2">
    <StatusBarItem>
        <Button Command="{Binding OpenSponsorCommand}"
                Background="Transparent"
                BorderThickness="0"
                Cursor="Hand"
                ToolTip="Support the project">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="♥" Foreground="#E74C3C" FontSize="14" Margin="0,0,6,0" />
                <TextBlock Text="Support on Ko-fi" FontSize="12" />
            </StackPanel>
        </Button>
    </StatusBarItem>
    <StatusBarItem HorizontalAlignment="Right">
        <TextBlock Text="Made with care by ZavalaSebas" FontSize="11" Foreground="Gray" />
    </StatusBarItem>
</StatusBar>
```

```csharp
// ViewModel
public ICommand OpenSponsorCommand => new RelayCommand(OpenSponsor);

private void OpenSponsor()
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Config.SponsorUrl,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to open sponsor link");
    }
}
```

```csharp
// Config.cs
public const string SponsorUrl = "https://ko-fi.com/sebastianzavala82573";
// or: public const string SponsorUrl = "https://github.com/sponsors/ZavalaSebas";
```

---

### Credits / About Dialog

Show a credits window with the app name, version, author credit, and legal disclaimer:

```xml
<!-- Views/CreditsWindow.xaml -->
<Window x:Class="Bridge.Views.CreditsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="About Bridge"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner"
        Width="420" Height="320"
        ShowInTaskbar="False">

    <Grid Margin="24">
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="Bridge"
                       FontSize="22" FontWeight="SemiBold"
                       TextAlignment="Center" />
            <TextBlock Text="Version 0.1.0"
                       TextAlignment="Center"
                       Foreground="Gray"
                       Margin="0,4,0,20" />

            <Separator Margin="0,0,0,20" />

            <TextBlock Text="Made with care by ZavalaSebas"
                       TextAlignment="Center"
                       FontSize="14"
                       Margin="0,0,0,20" />

            <TextBlock TextWrapping="Wrap"
                       TextAlignment="Center"
                       FontSize="11"
                       Foreground="Gray">
This software is provided &quot;as is&quot;, without warranty of any kind, express or implied. Use at your own risk.

See the
<Hyperlink NavigateUri="https://github.com/ZavalaSebas/Bridge/blob/main/LICENSE"
          RequestNavigate="LicenseLink_Click">LICENSE</Hyperlink>
file for details.
            </TextBlock>

            <Button Content="Close"
                    Width="80"
                    Margin="0,24,0,0"
                    IsCancel="True"
                    IsDefault="True" />
        </StackPanel>
    </Grid>
</Window>
```

```csharp
// Views/CreditsWindow.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Bridge.Views;

public partial class CreditsWindow : Window
{
    public CreditsWindow()
    {
        InitializeComponent();
    }

    private void LicenseLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink link)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = link.NavigateUri.ToString(),
                UseShellExecute = true
            });
        }
    }
}
```

```csharp
// Trigger desde MainWindow
private void ShowCredits_Click(object sender, RoutedEventArgs e)
{
    var credits = new CreditsWindow { Owner = this };
    credits.ShowDialog();
}
```

---

## Keyboard Navigation

WPF supports keyboard navigation out of the box, but must be designed intentionally.

### Tab Order

WPF follows the XAML declaration order by default — if your layout matches the visual/logical order, no explicit `TabIndex` is needed. Only add explicit `TabIndex` when the logical order differs from the visual order:

```xml
<!-- Natural order — no TabIndex needed -->
<StackPanel>
    <TextBox />
    <Button Content="Next" />
    <ComboBox />
</StackPanel>

<!-- Explicit order needed when visual layout doesn't match logical -->
<Grid>
    <TextBox TabIndex="2" />
    <Button TabIndex="0" Content="First" />
    <ComboBox TabIndex="1" />
</Grid>
```

### Focus Indicators

Bridge doesn't have WPF-UI integrated yet — decided in the original foundation document (WPF + WPF-UI over WinUI 3), but never actually installed; confirmed via the 2026-08-06 documentation audit that Bridge ships on stock WPF today (see PLAN.md → Roadmap → Phase Polish). Stock WPF's own default focus visual is sufficient for keyboard users to see where they are. **Do not disable or remove it** without providing an accessible replacement, and re-verify this guidance once WPF-UI is actually integrated — its own focus-indicator behavior may differ.

### Keyboard Shortcuts

Define application-level shortcuts in `Window.InputBindings`:

```xml
<Window.InputBindings>
    <KeyBinding Key="S" Modifiers="Ctrl" Command="{Binding SaveCommand}" />
    <KeyBinding Key="F5" Command="{Binding RefreshCommand}" />
</Window.InputBindings>
```

For menu items, use `InputGestureText`:

```xml
<MenuItem Header="_Save" Command="{Binding SaveCommand}" InputGestureText="Ctrl+S" />
```

### Dialogs

Modal dialogs should return focus to the parent window when closed. Set `Owner` before showing:

```csharp
var dialog = new SettingsWindow { Owner = this };
dialog.ShowDialog();
```

### Alt+Key Navigation

For menu accesskeys (underlined letters), prefix the letter with underscore in the `Header`:

```xml
<MenuItem Header="_File">
<MenuItem Header="_Edit">
```

Press `Alt` to show accesskeys, then press the letter to activate.

---

Built with care by ZavalaSebas
