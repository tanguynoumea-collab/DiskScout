# Phase 1: Foundations & Launchable Shell - Context

**Gathered:** 2026-04-24
**Status:** Ready for planning

<domain>
## Phase Boundary

Bootstrap the DiskScout solution to a state where `DiskScout.exe` launches, requests admin elevation, shows a dark-themed WPF window with three empty tabs (Programmes / Rémanents / Arborescence), writes to a rotating log file, and exposes domain models round-trippable through System.Text.Json. No scan logic, no registry reads, no UI data.

**In scope (requirements PLAT-01..04):**
- Solution structure (Models / Views / ViewModels / Services / Helpers)
- NuGet packages: CommunityToolkit.Mvvm 8.4.2, Serilog 4.2.0, Serilog.Sinks.File 7.0.0, System.Text.Json (built-in), Microsoft.Win32.Registry 5.0.0
- app.manifest with `requireAdministrator` + `longPathAware`
- Serilog rolling file sink (5 MB / 5 files) in `%LocalAppData%\DiskScout\diskscout.log`
- Domain model records: `FileSystemNode`, `InstalledProgram`, `OrphanCandidate`, `ScanResult`, `ScanProgress`, `DeltaResult`
- `App.xaml.cs` composition root with manual DI
- `MainWindow` + `MainViewModel` + three empty tab UserControls
- Dark title bar via `DwmSetWindowAttribute`

**Out of scope (later phases):**
- Any actual scanning, registry reading, orphan detection (Phase 2, 3)
- Drive selection UI, Start/Cancel buttons wired to real work (Phase 4)
- DataGrid / TreeView content (Phase 5)
- Persistence save/load (Phase 6)
- Delta (Phase 7), Export + Publish (Phase 8)

</domain>

<decisions>
## Implementation Decisions

User deferred all four discussed gray areas to Claude's discretion. Below are the recommended choices that downstream researcher and planner should treat as locked unless challenged.

### UAC Denied Behavior
- **D-01**: If the user denies the UAC prompt, the process exits silently with code 0 (Windows default behavior for a `requireAdministrator` manifest). No custom pre-elevation shell, no retry dialog. Rationale: simplest path, matches standard Windows tool behavior (e.g., regedit, services.msc). Logging is impossible pre-elevation since `%LocalAppData%\DiskScout\` may not yet exist in the right scope.

### Empty-State UI (each of the 3 tabs before first scan)
- **D-02**: Each empty tab shows a centered placeholder: an icon + one-line French copy + subtle reference to the "Scanner" button in the header.
  - Programmes: "Aucun scan effectué. Lance un scan pour voir les programmes installés."
  - Rémanents: "Aucun scan effectué. Lance un scan pour détecter les fichiers rémanents."
  - Arborescence: "Aucun scan effectué. Lance un scan pour explorer ton disque."
- **D-03**: Empty-state content is a dedicated `EmptyStatePanel` UserControl in `Views/Controls/`, reused across the three tabs with a bindable `Message` property. Keeps the three tab views thin.

### Domain Models Shape
- **D-04**: Use C# 12 `sealed record` types with primary constructors for all domain models. Immutability simplifies concurrent reads during UI binding and eliminates a class of race bugs.
- **D-05**: `FileSystemNode` uses **flat representation with `ParentId`** (not nested `Children` references), aligning with PERS-02 (JSON persistence must be flat to avoid stack overflow on 1M+ trees). The in-memory traversal wrapper (`FileSystemNodeView` or similar) that builds a parent→children lookup is a Phase 2 concern; Phase 1 only defines the flat record.
- **D-06**: Node identity is a `long Id` assigned at scan time (monotonic), not a string path (paths can be >260 chars, reparse points complicate uniqueness). The canonical full path is a separate field.
- **D-07**: `ScanProgress` is a `readonly record struct` (hot-path struct, no allocation per progress tick). All other models are `sealed record class`.
- **D-08**: JSON source-gen context (`[JsonSerializable]`) is declared in Phase 1 for all models to support AOT-friendly serialization and faster startup; polymorphism (`[JsonDerivedType]`) is not needed since the hierarchy is flat.

### Window Chrome & Branding
- **D-09**: Standard Windows chrome (default title bar, min/max/close buttons, system drag). No custom chrome — keeps the code simple and avoids DPI / multi-monitor / snap-layout headaches.
- **D-10**: Force dark title bar via `DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE = 20` applied in `MainWindow.OnSourceInitialized`. Works on Windows 10 20H1+ and Windows 11.
- **D-11**: Window title: `"DiskScout [Admin]"` — the `[Admin]` suffix is always present in v1 since the app cannot run non-elevated.
- **D-12**: Version string NOT in the title. Shown in a future `About` dialog (v2) or via right-click on the window icon. Phase 1 does not add an About dialog.
- **D-13**: Window icon: a simple hard-disk-with-magnifying-glass SVG embedded as `.ico`. Provided as placeholder; user can replace without touching code.
- **D-14**: Default window size: 1280×800, centered on primary monitor. Persistence of window state is v2 (UX-V2-02).

### Placeholder Services (not originally gray-listed but planner needs direction)
- **D-15**: Phase 1 defines **interfaces only** for the three scan engines (`IFileSystemScanner`, `IInstalledProgramsScanner`, `IOrphanDetectorService`) plus `IPersistenceService`, `IDeltaComparator`, `IExporter`. Each has a single stub implementation (`NotImplementedException` or returning empty results) so the composition root compiles and `MainViewModel` can receive them. Real implementations land in their respective phases.
- **D-16**: `ILogger` (Serilog's) is injected directly — no DiskScout-specific logging abstraction. Keeps the code idiomatic to the Serilog ecosystem.

### Claude's Discretion (planner may refine)
- Project filename conventions (e.g., `DiskScout.csproj` vs `DiskScout.App.csproj` if a test project is split out).
- Whether to create a separate `DiskScout.Tests` xUnit project in Phase 1 or defer to Phase 2. Recommended: create the empty test project in Phase 1 so later phases can add tests without scaffolding.
- Exact brush colors for the dark theme (use a small `Colors.xaml` resource dictionary; reuse Material-like palette values).
- Whether to add a `DiskScout.Assets` folder for the icon now or inline at MSBuild time.

### Folded Todos
None — no backlog todos exist.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project & Requirements
- `.planning/PROJECT.md` — Core value, constraints, read-only absolute rule, dev auto-launch workflow
- `.planning/REQUIREMENTS.md` §Platform (PLAT-01..04) — Admin manifest, exception handling, log rotation, long paths
- `.planning/ROADMAP.md` §"Phase 1: Foundations & Launchable Shell" — Success criteria (5 items)

### Research (read before planning)
- `.planning/research/STACK.md` — Full NuGet versions, manifest XML, csproj template, publish flags
- `.planning/research/ARCHITECTURE.md` — 9-phase build order, composition root pattern, service boundaries, `IProgress<T>` choice
- `.planning/research/PITFALLS.md` — `[LibraryImport]` over `[DllImport]`, long-path handling, manifest embedding gotchas
- `.planning/research/FEATURES.md` — MVP table stakes, anti-features (never add delete)
- `.planning/research/SUMMARY.md` — Cross-cutting findings digest

### External Specs & Docs
- [Microsoft Learn — Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) — `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`
- [Microsoft Learn — P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) — `[LibraryImport]` prep for Phase 2
- [Microsoft Learn — DwmSetWindowAttribute](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute) — dark title bar flag `DWMWA_USE_IMMERSIVE_DARK_MODE = 20`
- [Microsoft Learn — requireAdministrator manifest](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests#requestedexecutionlevel) — elevation declaration
- [CommunityToolkit.Mvvm 8.4.2 docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/observableobject) — `[ObservableProperty]`, `[RelayCommand]` source generators
- [Serilog.Sinks.File docs](https://github.com/serilog/serilog-sinks-file) — `fileSizeLimitBytes`, `rollOnFileSizeLimit`, `retainedFileCountLimit`

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
None — greenfield project. Nothing committed yet beyond `.planning/` docs and a `CLAUDE.md` guide.

### Established Patterns
The only project-level pattern so far is the documented MVVM layout in `CLAUDE.md` (Models / Views / ViewModels / Services / Helpers) and the read-only-absolute rule (zero `File.Delete` / `Directory.Delete`). Planner should enforce both from the first commit.

### Integration Points
- `App.xaml.cs` will be the sole composition root for the entire app — every subsequent phase adds services here, never introduces a DI container.
- `MainWindow.xaml` defines the shell (title bar, drives selector header region, TabControl, progress region). Phase 1 provides empty placeholders for each region so Phase 4 can fill them without restructuring XAML.

</code_context>

<specifics>
## Specific Ideas

- Tab order is **Programmes → Rémanents → Arborescence** (as stated in PROJECT.md). Keep French labels in the UI; code identifiers stay in English (`ProgramsTab`, `OrphansTab`, `TreeTab`).
- Dev workflow constraint from PROJECT.md: after a successful build in this phase, the agent will auto-launch `DiskScout.exe` so the user sees the empty shell. Phase 1 plan must end with a verifiable launchable state.
- No About dialog, no settings window, no splash screen in Phase 1. Bootstrap minimalism.

</specifics>

<deferred>
## Deferred Ideas

None surfaced during discussion — all four gray areas were resolved within Phase 1 scope.

### Reviewed Todos (not folded)
None.

</deferred>

---

*Phase: 01-foundations-launchable-shell*
*Context gathered: 2026-04-24*
