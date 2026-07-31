# Bridge — Project Guide

This document serves as a guide to this specific project AND as a reference for the architecture, workflow, and decisions made during planning.

---

## Current Status (as of 2026-07-31)

All Phase 1 backend services are implemented and tested (`LibraryRepository`, `RomScannerService`, `MetadataService`, `ImageCacheService`, `SettingsService`, `EmulatorService`, `LaunchService`), and the Phase 1 minimal UI is now built on top of them: composition root (`App.xaml.cs`, no `StartupUri`), `MainWindow`/`MainViewModel` (cover grid, empty state, scan/box-art progress, toolbar), `SettingsWindow`/`SettingsViewModel` (emulator config, SteamGridDB API key). 83 unit tests, all green. All 5 Open Decisions plus the TrackingMode addition are resolved (ADR-1 through ADR-10 in `ARCHITECTURE.md`). Full detail and commit-by-commit history: `PLAN.md` → `## Timeline`.

**Known, honestly-tracked gap:** the UI has never actually been run/observed — `dotnet build`/`dotnet test` pass, but no WPF window has been launched and watched from this environment. First real verification is a manual "does it run" pass.

`App.xaml.cs` now has a `DispatcherUnhandledException` handler (logs, shows the user a Yes/No dialog, shuts down cleanly on No) — this was tracked as a gap earlier in this same session and closed before commit, not left as a Known Limitation, since the fix was cheap and the file was already open. Nothing else is pending at the service/logic layer.

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
3. **Test** — `dotnet test Bridge.slnx -c Release --no-build`
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

`v0.1.0`'s tag and GitHub Release were created **manually** (`git tag` + `gh release create`), not by `release.yml`. This is expected, not a bug: the workflow's version-change check (`git show HEAD~1:Bridge/Bridge.csproj` vs `HEAD`) compares against the *previous* commit's `<Version>`, and `0.1.0` was the scaffolded starting value — the bump commit didn't change it, so `changed` evaluated `false` and the `release` job never ran. This is a one-time gap specific to a "genesis" version with no real prior version to diff against. Every release from `0.2.0` onward involves an actual version change and will trigger the automated `release` job normally — if a future release is also missing a workflow run, that's a real regression worth investigating, not this same non-issue recurring.

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
- [ ] Test downloaded `.exe` works
- [ ] Update documentation if needed

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
| `Bridge/Services/EmulatorService.cs` | Validates and persists `EmulatorConfig` (exe exists, `{RomPath}` present, `PlatformId` valid) — implemented, tested (see ADR-9) |
| `Bridge/Services/ArgumentTemplate.cs` | Shared `{Token}` resolver (`Validate`/`Expand`), used by both `EmulatorService` and `LaunchService` — implemented, tested |
| `Bridge/Services/LaunchService.cs` | Re-checks ROM/emulator existence, expands arguments, launches the process, exposes exit as a `Task` — implemented, tested (see ADR-9) |

This table is aspirational until the corresponding files exist — update the "not yet created" note to the real purpose/status as each file is implemented.

---

## Known Limitations

| Limitation | Reason | Workaround |
|------------|--------|------------|
| `LaunchService`'s Phase 1 exit detection does not correctly detect when the emulator has closed if the launched process is a wrapper/launcher that spawns the real emulator process and exits itself (e.g. an updater shim, a single-instance relaunch, or a `.bat`/`.cmd` wrapper) — Bridge would return control to the launcher while the actual emulator is still running. | Phase 1 tracks the process handle returned directly by `Process.Start()` (see PLAN.md → Open Decisions #5, ARCHITECTURE.md → ADR-1), chosen deliberately over Windows Job Object process-tree tracking to avoid P/Invoke complexity before the wrapper/launcher problem is confirmed to occur frequently in practice. Directly related to the process-exit-detection bug class found in OrbSpoofer. | If this proves frequent for real emulators being configured, implement process-tree tracking via Windows Job Objects (`CreateJobObject`/`AssignProcessToJobObject`) — see ARCHITECTURE.md → ADR-1 for the noted improvement path. |
| `RomScannerService`'s per-file permission-denied handling (`UnauthorizedAccessException`/`IOException` caught on an individual file during scanning — see ARCHITECTURE.md → ADR-6) is implemented but not covered by an automated test. | Reliably simulating a permission-denied file in a portable, fast unit test requires manipulating Windows ACLs, which is fragile and slow — not worth the cost for Phase 1. The other error-handling cases (missing folder, empty file) are covered; this one specifically isn't. | Verify manually if this code path changes (create a file, deny read access via `icacls`, run a scan, confirm it's skipped and logged) rather than relying on the automated suite for this specific path. Revisit with a filesystem abstraction (e.g. `IFileSystem`) if untestable-I/O-error coverage becomes a recurring need beyond this one case. |
| `MetadataService` has no "safe to retry at" timestamp for a SteamGridDB rate-limit (429) stop — it can only say "stopped early, remaining games pending for the next batch run, whenever that is." | Confirmed by checking the actual `Retry-After`-style handling in three independent sources — the official Node.js wrapper's source code (`SteamGridDB/node-steamgriddb`), the community .NET wrapper (`craftersmine/SteamGridDB.NET`), and general web search — none document or read a rate-limit-retry header from SteamGridDB. Not assumed; actively looked for and not found. Fabricating an arbitrary wait time was explicitly rejected in favor of documenting this as a real gap. | If SteamGridDB ever adds a documented rate-limit header, or if inspecting real 429 responses at runtime turns up an undocumented one, wire it into `MetadataFetchResult` then. Until confirmed, don't guess a backoff duration. |
| The only manual run of Bridge so far was via Visual Studio (F5, `dotnet build` output under `Bridge/bin/Debug/...`), not the self-contained single-file `.exe` (`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`) attached to the `v0.1.0` GitHub Release. These are different build/packaging paths — the F5 run doesn't confirm the published `.exe` itself launches or behaves the same way. | The F5 run was the fastest way to eyeball the UI during development; publishing and running the packaged `.exe` end-to-end hasn't been done yet. | Before relying on a release `.exe` as "confirmed working," actually download and run that specific artifact once, separately from any F5/IDE run. |

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

> Note: `Bridge.slnx` and `Bridge/Bridge.csproj` are not part of this documentation scaffold — creating the actual .NET solution/project is a separate, later step (see PLAN.md → Timeline).

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

WPFUI (the project's UI framework) provides visible focus indicators by default on all controls. **Do not disable or remove them** without providing an accessible replacement. The default focus rectangle is sufficient for keyboard users to see where they are.

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
