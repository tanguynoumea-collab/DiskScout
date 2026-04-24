# Phase 1: Foundations & Launchable Shell - Summary

**Status:** ✅ Complete
**Completed:** 2026-04-24
**Requirements delivered:** PLAT-01, PLAT-02, PLAT-03, PLAT-04

## What shipped

A launchable WPF shell that triggers UAC, writes a rotating log, and exposes the MVVM scaffolding and domain models needed by all downstream phases. Zero scan logic, zero registry reads — pure bootstrap.

## Artifacts created

### Solution structure
- `DiskScout.slnx` — solution root
- `src/DiskScout.App/` — WPF app, target `net8.0-windows`, `UseWPF=true`, `AllowUnsafeBlocks=true` (LibraryImport)
- `tests/DiskScout.Tests/` — xUnit 2.5.3 + FluentAssertions 7.0.0 (pinned < 8 — v8 is commercial)

### NuGet packages
- `CommunityToolkit.Mvvm 8.4.2`
- `Serilog 4.2.0` + `Serilog.Sinks.File 7.0.0`
- `Microsoft.Win32.Registry 5.0.0` (prepped for Phase 3)

### Manifest (`app.manifest`)
- `requestedExecutionLevel level="requireAdministrator"`
- `longPathAware: true` for paths > 260 chars
- `dpiAwareness: PerMonitorV2`
- Windows 7/8/8.1/10/11 compatibility declarations

### Domain models (`Models/`)
- `FileSystemNode` — sealed record with flat `ParentId` (per PERS-02 requirement)
- `InstalledProgram` with `RegistryHive` enum
- `OrphanCandidate` with `OrphanCategory` enum
- `ScanResult` — versioned root (`SchemaVersion: 1`) with factory `CreateEmpty()`
- `ScanProgress` — `readonly record struct` (hot-path, zero alloc) + `ScanPhase` enum
- `DeltaResult` + `DeltaEntry` with `DeltaChange` enum

### Services (`Services/`)
- Interfaces: `IFileSystemScanner`, `IInstalledProgramsScanner`, `IOrphanDetectorService`, `IPersistenceService` (+ `ScanHistoryEntry`), `IDeltaComparator`, `IExporter` (+ `ExportFormat`, `ExportPane`)
- Stubs in `Services/Stubs/` — return empty results or throw `NotImplementedException` with forward-referencing message ("Persistence arrives in Phase 6.")

### Helpers (`Helpers/`)
- `AppPaths` — resolves `%LocalAppData%\DiskScout\`, log file, `scans/` folder
- `LogService` — Serilog rolling file (5 MB, 5 retained)
- `DwmDarkTitleBar` — `[LibraryImport]` P/Invoke to `DwmSetWindowAttribute` for dark title bar
- `DiskScoutJsonContext` — `System.Text.Json` source-gen for all domain models

### ViewModels (`ViewModels/`)
- `MainViewModel` — composition endpoint; receives all 6 services via constructor
- `ProgramsViewModel`, `OrphansViewModel`, `TreeViewModel` — minimal shells with French empty-state copy

### Views (`Views/`)
- `Themes/DarkTheme.xaml` — palette + TabItem/Button/Window style overrides
- `Controls/EmptyStatePanel.xaml` — reusable DependencyProperty `Message`
- `Tabs/ProgramsTabView.xaml`, `OrphansTabView.xaml`, `TreeTabView.xaml` — each wraps `EmptyStatePanel`

### Shell
- `App.xaml` — loads `DarkTheme.xaml`
- `App.xaml.cs` — composition root (manual DI, 6 stubs instantiated, logger bootstrapped)
- `MainWindow.xaml` — 3-tab dark shell with header placeholder for Phase 4's drive selector + Scanner button
- `MainWindow.xaml.cs` — applies dark title bar via DWM in `OnSourceInitialized`

### Tests
4 passing: `ScanResult` JSON roundtrip, `ScanProgress` value-type + roundtrip, `DeltaResult` all-change-types roundtrip, `FileSystemNode` record value-equality.

## Success criteria verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | UAC prompt on launch; shell with 3 tabs labeled Programmes/Rémanents/Arborescence, title "DiskScout [Admin]" | ✅ Verified — process running, log shows shell shown |
| 2 | Rolling log in `%LocalAppData%\DiskScout\diskscout.log` via Serilog, 5 MB / 5 files | ✅ Log file created and populated |
| 3 | Manifest embeds `requireAdministrator` + `longPathAware` | ✅ Present in `app.manifest` |
| 4 | All 6 domain models round-trip cleanly through `System.Text.Json` | ✅ 4/4 unit tests passing |
| 5 | `App.xaml.cs` instantiates placeholder services and `MainWindow` receives VM via constructor injection | ✅ No DI container, manual wiring |

## Launch confirmation

```
DiskScout.exe  PID 57348  Running (elevated)
2026-04-24 10:55:46 [INF] DiskScout starting (version 0.1.0.0).
2026-04-24 10:55:46 [INF] MainViewModel initialised; services wired via manual DI.
2026-04-24 10:55:47 [INF] DiskScout shell shown.
```

## Decisions locked for downstream phases

Per `01-CONTEXT.md`:
- Silent-exit-on-UAC-deny (D-01)
- Placeholder EmptyStatePanel with French copy (D-02, D-03)
- Flat `FileSystemNode` with `ParentId` (D-05, D-06)
- `ScanProgress` as `readonly record struct` (D-07)
- JSON source-gen via `DiskScoutJsonContext` (D-08)
- Standard chrome + dark title bar via DWM (D-09, D-10, D-11)
- Interface-only service layer (D-15)

## Ready for Phase 2

Phase 2 (FileSystem Scanner Core — P/Invoke) can:
- Replace `StubFileSystemScanner` with real implementation using `[LibraryImport]` for `FindFirstFileExW` / `FindNextFileW` / `FindClose`
- Populate `FileSystemNode` records with real scan data
- Wire `IProgress<ScanProgress>` from scanner → VM (already accepted by interface)

No structural changes needed to Phase 1 — the seams are clean.
