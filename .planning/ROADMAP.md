# Roadmap: DiskScout

**Created:** 2026-04-24
**Granularity:** standard
**Coverage:** 37/37 v1 requirements mapped
**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer.

## Phases

- [x] **Phase 1: Foundations & Launchable Shell** - Bootstrap models, helpers, logging, admin manifest, and an empty WPF shell that launches and shows three empty tabs ✅
- [x] **Phase 2: FileSystem Scanner Core (P/Invoke)** - Trim-safe native scanner with reparse detection, long paths, bottom-up size aggregation, and cancellation — backend-only, unit-tested
- [x] **Phase 3: Registry & Orphan Detection** - Dual-view registry enumeration, two-stage fuzzy matching, and four orphan heuristic classifiers — backend-only, regression-tested
- [x] **Phase 4: UI Shell & Scan Orchestration** - Drive selection, Start/Cancel buttons, live throttled progress, scan orchestrator wiring backend scanners to the shell
- [x] **Phase 5: Three Result Panes** - Programs DataGrid (sort/filter), Orphans grouped list (category + reason), Tree view with virtualization, lazy-load, and percentage bars
- [x] **Phase 6: Scan Persistence & History** - Flat-node JSON persistence with schema versioning; scan history UI to list and reload past scans
- [x] **Phase 7: Delta Comparison** - Path-keyed dictionary diff between two persisted scans; delta pane displaying Added/Removed/Grew/Shrank with size deltas
- [x] **Phase 8: Export & Portable Publish** - CSV and HTML exporters respecting active filters/sorts; single-file self-contained win-x64 publish validated on a clean VM

## Phase Details

### Phase 1: Foundations & Launchable Shell
**Goal**: Produce a launchable WPF shell that requires admin elevation, writes a rotating log file, and has the MVVM scaffolding and data models needed for all downstream phases
**Depends on**: Nothing (first phase)
**Requirements**: PLAT-01, PLAT-02, PLAT-03, PLAT-04
**Success Criteria** (what must be TRUE):
  1. Launching `DiskScout.exe` triggers the UAC prompt; denying exits cleanly and accepting opens a WPF window with three empty tabs (Programmes / Rémanents / Arborescence) and title bar showing "[Admin]"
  2. The running app writes to `%LocalAppData%\DiskScout\diskscout.log` with Serilog rolling at 5 MB and 5 files retained
  3. The app manifest embeds `requireAdministrator` + `longPathAware` (verifiable via `mt.exe`) and the app can open a path longer than 260 characters without throwing
  4. All v1 domain model records (`FileSystemNode`, `InstalledProgram`, `OrphanCandidate`, `ScanResult`, `ScanProgress`, `DeltaResult`) exist and round-trip cleanly through `System.Text.Json` in unit tests
  5. `App.xaml.cs` composition root instantiates the placeholder services and the `MainWindow` receives its ViewModel via constructor injection (no DI container)
**Plans**: TBD
**UI hint**: yes

### Phase 2: FileSystem Scanner Core (P/Invoke)
**Goal**: Deliver a correct, fast, cancellable filesystem scanner that produces the canonical `FileSystemNode` tree consumed by every other feature
**Depends on**: Phase 1
**Requirements**: SCAN-05, SCAN-06
**Success Criteria** (what must be TRUE):
  1. A unit test harness scans a 500 GB SSD directory structure in under 3 minutes using `FindFirstFileExW` + `FIND_FIRST_EX_LARGE_FETCH` via `[LibraryImport]` source-gen and `SafeFindHandle`
  2. The scanner encounters a self-referential junction (`mklink /J loop parent`) and terminates normally without infinite recursion, tagging the node `IsReparsePoint = true`
  3. The scanner handles `UnauthorizedAccessException`, `DirectoryNotFoundException`, `PathTooLongException`, and `IOException` via raw Win32 error codes (no exception allocation in the hot path) and continues scanning
  4. Files larger than 4 GB are sized correctly by combining `nFileSizeHigh` and `nFileSizeLow` as a `long`; paths longer than 260 characters are reached via the `\\?\` prefix
  5. Invoking `CancellationToken.Cancel()` during a scan returns control within 2 seconds, all `SafeFindHandle`s dispose, and the process handle count returns to its pre-scan baseline
  6. Folder sizes are computed bottom-up via subtree-local accumulation (no cross-thread race); `Parallel.ForEach` operates at drive-root granularity only
**Plans**: TBD

### Phase 3: Registry & Orphan Detection
**Goal**: Produce the installed-programs inventory and orphan-candidate list by enumerating all registry views and correlating against the shared filesystem tree with explainable fuzzy matching
**Depends on**: Phase 2
**Requirements**: PROG-01, ORPH-02, ORPH-03, ORPH-04, ORPH-05
**Success Criteria** (what must be TRUE):
  1. `InstalledProgramsScanner` enumerates all four registry views (HKLM Registry64, HKLM Registry32/WOW6432Node, HKCU Registry64, HKCU Registry32) and returns a count consistent with "Programs and Features"
  2. `OrphanDetectorService` flags AppData folders that match no installed program via two-stage matching: exact/prefix on InstallLocation leaf first (confidence 1.0), then token Jaccard on Publisher+DisplayName with minimum 2-token overlap and margin ≥ 0.15; default threshold 0.7
  3. `OrphanDetectorService` flags Program Files / Program Files (x86) subfolders considered empty (< 1 MB or only `.log`/`.txt` content) as a distinct orphan category
  4. `OrphanDetectorService` flags files in `%TEMP%` and `%LocalAppData%\Temp` untouched for > 30 days
  5. `OrphanDetectorService` flags `.msp` patches in `Windows\Installer` not referenced by any installed program's product code
  6. A regression fixture of real AppData↔registry pairs runs with documented false-positive and false-negative rates; ambiguous matches are surfaced (not auto-resolved)
**Plans**: TBD

### Phase 4: UI Shell & Scan Orchestration
**Goal**: Wire the backend scanners into the shell via a scan orchestrator VM with drive selection, live throttled progress, and clean cancellation
**Depends on**: Phase 3
**Requirements**: SCAN-01, SCAN-02, SCAN-03, SCAN-04, SCAN-07
**Success Criteria** (what must be TRUE):
  1. The header lists all fixed local drives (filtered by `DriveType.Fixed` + `DriveFormat` sanity) with checkboxes; the user can tick one or more drives
  2. Clicking "Scanner" starts the orchestrated scan (filesystem + registry in parallel, then orphan detection); the button becomes disabled and a "Annuler" button becomes active
  3. A progress region updates at ~10 Hz (throttled `IProgress<ScanProgress>`) showing files processed, MB scanned, and current path without ever freezing the UI thread
  4. Clicking "Annuler" transitions to a "Cancelling…" state immediately and returns the app to idle within 2 seconds, releasing all native handles
  5. When the scan completes, a headline summary appears ("Scanned N files in Tm Ts — X GB, Y orphans, Z programs") and the three panes are populated atomically via the swap-after-scan pattern
**Plans**: TBD
**UI hint**: yes

### Phase 5: Three Result Panes
**Goal**: Present scan results in three rich, responsive, virtualized panes — Programs, Orphans, Tree — with sorting, filtering, and explainable heuristics
**Depends on**: Phase 4
**Requirements**: PROG-02, PROG-03, PROG-04, ORPH-01, ORPH-06, TREE-01, TREE-02, TREE-03, TREE-04, TREE-05
**Success Criteria** (what must be TRUE):
  1. The Programmes pane lists each installed program with DisplayName, Publisher, InstallDate, InstallLocation, and a real size recomputed from the scanned subtree; the user can click any column to sort and type in a search box to filter by name or publisher
  2. The DataGrid uses `EnableRowVirtualization=True` with fixed/star column widths; sorting a 10k-row grid stays responsive (< 1 s, no UI freeze, verified in Live Visual Tree)
  3. The Rémanents pane groups entries by category (AppData orphelins / Program Files vides / Temp anciens / Installer patches), and each row shows full path, size, and a reason string explaining which heuristic fired
  4. The Arborescence pane displays one `TreeView` per scanned drive with size agregée bottom-up, children sorted by size desc, and a percentage bar showing each node's share of its parent
  5. The `TreeView` uses `VirtualizingStackPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling` with lazy-load via the dummy-child pattern; expanding a 10k-children node stays below 500 ms and 100 MB RAM growth
  6. Clicking a tree node shows its full path in a detail strip and allows the user to continue navigating into descendants
**Plans**: TBD
**UI hint**: yes

### Phase 6: Scan Persistence & History
**Goal**: Persist each scan to disk in a forward-compatible flat JSON format and allow the user to browse and reload past scans
**Depends on**: Phase 5
**Requirements**: PERS-01, PERS-02, PERS-03
**Success Criteria** (what must be TRUE):
  1. Each completed scan is auto-saved to `%LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json` via atomic `.tmp` rename with `schemaVersion: 1` at the document root
  2. The JSON serializes the tree as a flat `List<FlatNode>` with `ParentId` (not nested `Children`); saving a 1M-node scan completes in under 5 seconds and produces a file under 50 MB with no `StackOverflowException`
  3. A Scan History window lists past scan files (timestamp, roots, file count, total size) and lets the user pick one to reload; loading hydrates the three panes exactly as at scan time
  4. Loading a scan reads `schemaVersion` first via `JsonDocument` pre-parse; a future-version file produces a clear user-facing error rather than a mid-parse crash
**Plans**: TBD
**UI hint**: yes

### Phase 7: Delta Comparison
**Goal**: Let the user compare any two persisted scans and see what changed via a path-keyed dictionary diff
**Depends on**: Phase 6
**Requirements**: DELT-01, DELT-02, DELT-03
**Success Criteria** (what must be TRUE):
  1. From the Scan History UI, the user can select two scans and open a Delta pane that runs the comparison off-thread and populates results via a single atomic swap (no 50k-event `ObservableCollection` flood)
  2. The Delta pane displays four sections — Apparu / Disparu / Grossi / Réduit — produced by a `Dictionary<string, NodeSummary>` diff keyed by canonicalized paths (via `PathNormalizer`) in O(n+m)
  3. Each delta row shows the absolute size delta (`+500 Mo`, `-2 Go`) and the percentage variation; the Grossi section is sorted by size delta desc by default
  4. Paths that differ only in case between the two scans (`C:\Users\X\appdata` vs `AppData`) are correctly identified as the same node, not as "removed + added"
**Plans**: TBD
**UI hint**: yes

### Phase 8: Export & Portable Publish
**Goal**: Let the user export any pane to CSV/HTML and ship a portable single-file self-contained `.exe` that runs on a clean Windows machine with no .NET runtime
**Depends on**: Phase 7
**Requirements**: EXPO-01, EXPO-02, EXPO-03, DEPL-01, DEPL-02
**Success Criteria** (what must be TRUE):
  1. From each of the three result panes (and the Delta pane), the user can invoke Export and choose CSV (via `CsvHelper` with a per-pane `ClassMap`) or HTML (via `Scriban` embedded template)
  2. Exports respect the filters and sort order currently applied to the view — the exported file mirrors what the user sees on screen, not the raw scan
  3. CSV files open cleanly in Excel and HTML files render cleanly in a modern browser; Unicode filenames are preserved correctly in both formats
  4. `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` produces a single `DiskScout.exe` (approximately 70-90 MB) that launches and scans on a clean Windows 11 VM with no .NET SDK installed
  5. The published `.exe` has `requireAdministrator` + `longPathAware` embedded in its manifest (verifiable via `mt.exe`), shows the UAC shield on its icon, and runs correctly when moved to any folder without installation
**Plans**: TBD

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundations & Launchable Shell | 0/? | Not started | - |
| 2. FileSystem Scanner Core (P/Invoke) | 0/? | Not started | - |
| 3. Registry & Orphan Detection | 0/? | Not started | - |
| 4. UI Shell & Scan Orchestration | 0/? | Not started | - |
| 5. Three Result Panes | 0/? | Not started | - |
| 6. Scan Persistence & History | 0/? | Not started | - |
| 7. Delta Comparison | 0/? | Not started | - |
| 8. Export & Portable Publish | 0/? | Not started | - |

## Notes

- **Dev workflow:** After each phase producing a launchable state, auto-run `dotnet run` (or the published `.exe`) for visual validation. Phase 1 delivers the minimal launchable shell so this feedback loop starts immediately.
- **Read-only invariant:** No `File.Delete`, `Directory.Delete`, or `Microsoft.VisualBasic.FileIO.FileSystem.Delete*` anywhere in the codebase. Enforced by a CI grep check running cross-phase.
- **Performance anchor:** Phase 2 is the largest technical risk (P/Invoke + cancellation + reparse points + long paths). It is deliberately isolated so correctness issues are caught in unit tests, not during UI integration.
- **.NET 10 LTS migration:** .NET 8 EOL is 2026-11-10. A post-v1 migration is tracked in REQUIREMENTS.md v2 (PLAT-V2-01) and must be scheduled before EOL.
- **Fuzzy-matching empirical validation:** Phase 3 ships with a regression fixture of real AppData↔registry pairs. Threshold tuning is expected in v1.x after user feedback.

### Phase 9: Programs Tab Real Uninstaller Assistant

**Goal:** Transform the Programs tab into a Revo-Pro-style assisted uninstaller — real-time install tracker, native uninstaller driver with Job-Object cancellation, deep residue scanner across registry/filesystem/services/tasks/shell-extensions, publisher rule engine with embedded + user-extensible JSON rules, and a 6-step wizard UI with strict safety guards (whitelist enforcement, default-unchecked tree, irreversible-confirm modal). Suppression DIRECTE permanente (pas de quarantaine pour ce flow, par décision utilisateur).
**Requirements**: Post-v1 — non mappés à REQUIREMENTS.md (phase ajoutée après création de la roadmap)
**Depends on:** Phase 8
**Plans:** 6 plans

Plans:
- [x] 09-01-PLAN.md — Install Tracker (FileSystemWatcher + RegNotifyChangeKeyValue + JSON trace store) ✅ 2026-04-25 (commits 342b7b7 / 94a1b89 / 4a39106 — 12 tests passing)
- [x] 09-02-PLAN.md — Native Uninstaller Driver (parser MSI/Inno/NSIS + Job-Object tree-kill + 30 min timeout + IProgress<string> output streaming) ✅ 2026-04-25 (commits ac27c65 / 4610b8d / 10efd7b — 20 tests passing)
- [x] 09-03-PLAN.md — Residue Scanner (7 catégories : registre, FS, raccourcis, MSI patches, services, tâches planifiées, shell extensions) + ResiduePathSafety whitelist ✅ 2026-04-25 (commits 3d12d9f / be47f5b / 238532d — 46 tests passing : 36 path-safety + 10 scanner)
- [x] 09-04-PLAN.md — Publisher Rule Engine (7 règles embarquées : Adobe, Autodesk, JetBrains, Mozilla, Microsoft, Steam, Epic + extensions utilisateur) ✅ 2026-04-25 (commits 39568c9 / df8952e — 16 tests passing)
- [x] 09-05-PLAN.md — Wizard UI (5 étapes + 2 colonnes diagnostiques sur la DataGrid Programs + checkpoint UAT auto-approuvé) ✅ 2026-04-25 (commits 6b92ae4 / 31ea66a / bf5bba0 — 19 tests passing : 10 wizard VM + 9 step business-logic)
- [x] 09-06-PLAN.md — Integration + Report (export JSON/HTML + Programs annotation post-scan + UAT end-to-end auto-approuvé) ✅ 2026-04-25 (commits 0043ddd / 0cd0d5e — 10 tests passing : 6 service + 4 integration ; full suite 140/140 ; single-file Release publish 81.4 MB)

### Phase 10: Orphan Detection Precision Refactor

**Goal:** Refondre le moteur de détection des rémanents AppData (>90 % de FP mesurés sur corpus de 365 items dans `C:\ProgramData`) pour atteindre <5 % de FP sans dégrader le rappel. Pipeline 7 étapes : HardBlacklist → ParentContextAnalyzer → KnownPathRules → MultiSourceMatcher (Service / Driver / Appx / Registry / ScheduledTask) → PublisherAliasResolver → ConfidenceScorer → RiskLevelClassifier. Nouveau modèle `AppDataOrphanCandidate` avec ConfidenceScore 0-100, RiskLevel + RecommendedAction, traçabilité des règles déclenchées (champ `Diagnostics` rétro-compatible sur `OrphanCandidate`). Mode `--audit` CLI exportant CSV. Acceptance gate ≥95 % concordance + 0 % CRITIQUE→Supprimer/CorbeilleOk sur le corpus 365.
**Requirements**: Post-v1 — non mappés à REQUIREMENTS.md (phase ajoutée après création de la roadmap)
**Depends on:** Phase 9
**Plans:** 5/6 plans executed

Plans:
- [x] 10-01-PLAN.md — PathRule + PathRuleEngine + ParentContextAnalyzer + 5 embedded JSON catalogs (os-critical / package-cache / driver-data / corporate-agent / vendor-shared) + AppPaths.PathRulesFolder/AuditFolder
- [x] 10-02-PLAN.md — Promote IServiceEnumerator + IScheduledTaskEnumerator to public; add IDriverEnumerator + IAppxEnumerator + impls; MachineSnapshot model + MachineSnapshotProvider (lazy TTL 5min, parallel population)
- [x] 10-03-PLAN.md — PublisherAliasResolver + ~30-entry aliases.json embedded resource + FuzzyMatcher fallback
- [x] 10-04-PLAN.md — AppDataOrphanCandidate model + 4 matchers (Service/Driver/Appx/Registry) + ConfidenceScorer + RiskLevelClassifier + AppDataOrphanPipeline orchestrator + OrphanDetectorService AppData-branch integration + App.xaml.cs DI wiring
- [ ] 10-05-PLAN.md — 365-item corpus fixture + acceptance test (>=95% concordance + 0% Critique misclass) + --audit CLI mode + AuditCsvWriter
- [x] 10-06-PLAN.md — UI Score column + tooltip + "Pourquoi ?" modal + RiskLevelToBrushConverter + docs/heuristics.md + visual UAT

---
*Roadmap created: 2026-04-24*
*Last updated: 2026-04-27 after `/gsd:plan-phase 10` — Phase 10 broken into 6 plans across 4 waves (10-01 + 10-02 wave 1 parallel, 10-03 wave 2, 10-04 wave 3, 10-05 + 10-06 wave 4 parallel).*
