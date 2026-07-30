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
│  Local storage (TBD — see PLAN.md Open           │
│  Decisions #1) · Emulator processes              │
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
| `EmulatorService` design | Data-driven configuration (JSON/DB), not a hardcoded system→emulator mapping | Extensibility non-functional requirement: adding a system or emulator must not require touching core code; also sets up Phase 2's auto-detect/download without a redesign |
| External Metadata API | SteamGridDB | Provides box art by game name; requires an API key and rate-limit handling — key-handling approach (user-supplied vs. embedded) still pending, see PLAN.md → Open Decisions #4 |

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

No ADRs created yet. The 4 Open Decisions tracked in `PLAN.md` (storage engine, extension→system mapping, emulator argument template format, SteamGridDB API key handling) are expected to become the first ADRs once resolved in Phase 0.

---

## Creating a New ADR

1. Copy the ADR format block from the section above
2. Assign the next sequential number (e.g., `ADR-1`, `ADR-2`, …)
3. Paste it at the end of this document, before the "Creating a New ADR" section
4. Fill in the sections with concrete information
5. Add it as a new entry in the "Existing ADRs" section above
