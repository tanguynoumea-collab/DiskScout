# Project Research Summary

**Project:** DiskScout
**Domain:** Windows WPF desktop disk usage analyzer with registry-driven installed-program inventory and orphan/remnant detection
**Researched:** 2026-04-24
**Confidence:** HIGH

## Executive Summary

DiskScout is a read-only Windows disk analyzer occupying genuine competitive whitespace: the three-pane coupling of installed programs (from registry), orphan/remnant detection, and size-sorted filesystem tree in a single portable tool. No incumbent (WinDirStat, TreeSize Free, WizTree, SpaceSniffer, RidNacs) does this natively. The closest partial overlap is Revo Uninstaller, which only detects leftovers during an active uninstall flow, not as a standalone inventory view. The recommended build approach is WPF/.NET 8, CommunityToolkit.Mvvm, P/Invoke FindFirstFileExW with FIND_FIRST_EX_LARGE_FETCH, and manual DI wiring in App.xaml.cs. The stack is locked; the architecture pattern (one FS scan feeding all three panes through a shared FileSystemNode tree) is the defining structural decision that avoids scanning the disk 3x.

The highest-severity risks cluster around the P/Invoke scanner core: reparse point/junction traversal produces infinite loops or double-counted sizes; MAX_PATH truncation silently drops deep directories; FindClose leaks under cancellation exhaust kernel handles; and the ObservableCollection cross-thread mutation pattern crashes the UI if not addressed with the swap-after-scan pattern. The fuzzy orphan-detection logic is the second danger zone: a naive single-stage Levenshtein at threshold 0.7 creates silent false positives on common vendor prefixes (Microsoft, Adobe), and WOW6432Node registry redirection misses roughly 50% of installed programs in a 64-bit process if the dual-view enumeration is not explicit.

The single mandatory constraint that shapes every other decision: .NET 8 LTS end-of-life is 2026-11-10, approximately 6 months from today. DiskScout can ship its v1 milestone on .NET 8 safely, but a .NET 10 LTS migration must be a tracked roadmap item before EOL. FluentAssertions must be pinned below v8.0 (commercial licence from v8), and [LibraryImport] source-gen must be used instead of [DllImport] for single-file publish and trim compatibility. JSON persistence must use a flat List with ParentId from v1 -- the nested recursive tree serialization causes StackOverflowException at scale.

---

## Key Findings

### Recommended Stack

The stack is entirely locked and validated against NuGet feeds, MS Learn documentation, and Win32 API references as of 2026-04-24. WPF + .NET 8 is the correct choice: WinUI 3 requires MSIX packaging (kills the portable-exe requirement), MAUI is cross-platform overkill for a Windows-only tool, and .NET Framework 4.8 lacks single-file publish and source generators. The supporting library set is minimal -- no tree-map control library is needed (commercial ones are paid; OSS ones are stale), and a custom TreeMapPanel with the squarified algorithm is approximately 200 LoC.

The critical deviation from naive approaches: [LibraryImport] source-gen must replace [DllImport] for the P/Invoke surface. [DllImport] IL stubs can be trimmed in single-file publish, causing DllNotFoundException on clean machines -- invisible in VS debugger. PublishTrimmed=true must remain false for WPF. IncludeNativeLibrariesForSelfExtract=true is mandatory.

**Core technologies:**
- .NET 8 (net8.0-windows): Runtime, SDK, WPF -- LTS through 2026-11-10; plan .NET 10 LTS migration before EOL
- WPF: Desktop UI -- VirtualizingStackPanel, HierarchicalDataTemplate natively suited to this use case
- CommunityToolkit.Mvvm 8.4.2: MVVM source-generators -- [ObservableProperty], [RelayCommand], ObservableObject; zero runtime dep; Microsoft-maintained
- System.Text.Json (built-in): JSON persistence -- source-gen [JsonSerializable] for perf; [JsonIgnore] on Parent to avoid cycles; flat serialization required
- [LibraryImport] P/Invoke: Replaces [DllImport] -- source-gen, trim-safe, AOT-compatible for single-file publish
- Microsoft.Win32.Registry 5.0.0: Explicit dual-view (Registry32 + Registry64) enumeration of HKLM/HKCU uninstall keys
- Serilog 4.2.0 + Serilog.Sinks.File 7.0.0: Rolling log file, 5 MB rotation, 5 files retained
- CsvHelper 33.1.0: CSV export, zero deps, fluent ClassMap per tab
- Scriban 5.10.0: HTML report templating -- 300 KB vs. RazorLight 10 MB Roslyn footprint
- Fastenshtein 1.0.11: Levenshtein for orphan fuzzy matching -- instance API (not thread-safe; one instance per thread)
- xUnit 2.9.2 + FluentAssertions 7.x (pin below 8.0) + Moq 4.20.72: Test stack

**Explicit rejections:** Microsoft.Extensions.DependencyInjection (unnecessary for 20 or fewer services), Newtonsoft.Json (700 KB binary weight, no source-gen), Directory.EnumerateFiles (2-3x slower than FindFirstFileEx, GC-heavy), any commercial TreeMap control (paid per-seat), FluentAssertions 8.0 or later (commercial licence).

### Expected Features

The Windows disk-analyzer market is mature. Users have baseline expectations formed by WinDirStat, TreeSize, and WizTree. DiskScout occupies genuine whitespace: registry-linked installed-program inventory, fuzzy orphan detection, and size-sorted tree in one portable tool. The MVP scope (all P1 items) validates the core thesis; delta comparison and the Why flagged? explainer ship as v1.x.

**Must have (table stakes):**
- Scan engine: volume selection, live progress (files/sec, bytes, current path), clean cancellation, sub-3-minute scan on 500 GB SSD
- Size-sorted tree pane: hierarchical view, percentage bars, lazy/virtualized rendering, Open in Explorer, file count per folder
- Installed Programs pane: all three registry hives (HKLM 64-bit, HKLM WOW6432Node, HKCU), real InstallLocation size (not registry EstimatedSize), sort by size desc
- Orphans pane: four heuristic categories (unmatched AppData, empty Program Files, Temp older than 30d, orphan MSI patches) with reason column
- Cross-pane selection coupling (click in one pane propagates selection to others)
- JSON scan persistence (schema v1), export CSV + HTML for all three panes
- Portable single-file .exe with admin manifest

**Should have (competitive differentiators):**
- Registry-driven installed-program inventory with real InstallLocation size -- primary differentiator 1; no competitor provides this
- Two-stage fuzzy orphan matching (exact/prefix first, then token Jaccard) with explainable confidence -- primary differentiator 2
- Scan delta comparison (appeared / disappeared / grew / shrank) -- v1.x, requires persisted scans
- Read-only guarantee enforced via CI grep for File.Delete
- Zero network / zero telemetry -- documented prominently as a trust signal

**Defer (v2+):**
- Treemap visualization -- real engineering cost; defer until three-pane thesis is validated on real users
- File-extension breakdown pane, JSON schema v2 migration, dark mode, command-line mode

**Rejected anti-features (document to resist scope creep):** in-app file deletion, registry cleaning, duplicate detection, cloud sync, background monitoring/tray icon, MSI installer, auto-updater.

### Architecture Approach

DiskScout is a 3-layer MVVM desktop app with one domain-specific structural rule: FileSystemScanner runs once and produces the canonical FileSystemNode tree that all three panes consume. OrphanDetectorService is a pure consumer that correlates the registry result with subtrees extracted from the shared tree by path lookup -- avoids scanning the disk 3x and keeps sizes consistent across all panes at the same scan instant. The composition root is App.xaml.cs (Pure DI, approximately 50 lines of manual wiring in OnStartup). Services are never aware of ViewModels or Dispatcher; progress crosses the boundary exclusively via IProgress/Progress. ObservableCollection mutation happens only on the UI thread after scan completes (swap pattern). Live streaming is explicitly rejected: size sort requires all children to be known before a folder size is final.

**Major components:**
1. FileSystemScanner -- P/Invoke FindFirstFileExW + FIND_FIRST_EX_LARGE_FETCH, SafeFindHandle, bottom-up size aggregation, Parallel.ForEach at drive root, reparse-point detection, long-path prefix, cancellation every N entries
2. InstalledProgramsScanner -- dual RegistryView.Registry64 + Registry32 for HKLM and HKCU, dedup by (DisplayName, DisplayVersion, Publisher), orphan-registry-entry detection, pseudo-entry filtering
3. OrphanDetectorService -- consumes shared tree + programs; two-stage fuzzy matching; four heuristic classifiers; produces OrphanCandidate array with category enum and explainable reason
4. ScanOrchestratorViewModel -- awaits FS scan + registry scan in parallel, serializes orphan detection, populates all three pane VMs on UI thread via swap pattern
5. TreeNodeViewModel -- lazy wrapper over FileSystemNode; dummy child trick for expander chevron; children size-sorted on IsExpanded transition only
6. ScanPersistenceService -- flat List serialization (avoids recursion stack issues), atomic .tmp rename, schemaVersion pre-parse via JsonDocument before full deserialization
7. DeltaComparatorService -- path-keyed dictionary diff, O(n+m) complexity, PathNormalizer for case-fold consistency
8. ExportService -- IExporter interface with CsvExporter (CsvHelper) and HtmlExporter (Scriban embedded template)

**Key structural rules:** FileSystemNode.Parent is [JsonIgnore] (reconstructed post-deserialization); TreeView must use its own built-in ScrollViewer (wrapping in a parent ScrollViewer or StackPanel silently disables VirtualizingStackPanel); virtualization occurs after full scan -- live streaming rejected because size sort requires complete children.

### Critical Pitfalls

1. **Reparse points / junctions / OneDrive placeholders (highest severity)** -- Traversing FILE_ATTRIBUTE_REPARSE_POINT entries causes infinite loops or double-counted disk sizes. Test (dwFileAttributes and 0x400) != 0 on every WIN32_FIND_DATA entry; do not recurse; tag node as IsReparsePoint = true for UI display. Must be implemented in Phase 2, before parallelization.

2. **WOW6432Node registry redirection misses ~50% of programs** -- A 64-bit process sees only the 64-bit view of HKLM Software Uninstall. Explicitly enumerate both RegistryView.Registry64 and RegistryView.Registry32 for both HKLM and HKCU -- four queries total.

3. **Naive Levenshtein fuzzy matching creates silent false positives** -- A single score at 0.7 matches Microsoft folder against dozens of installed programs. Use two-stage approach: exact/prefix match on InstallLocation leaf folder first (confidence 1.0), then token Jaccard on Publisher+DisplayName only if step 1 fails, requiring minimum 2-token overlap AND score margin >= 0.15 above second-best.

4. **WPF virtualization silently disabled -- 30-second freeze on large trees** -- Multiple silent killers: wrapping TreeView in a ScrollViewer, omitting VirtualizationMode=Recycling, enabling grouping without IsVirtualizingWhenGrouping=True, or Auto column widths in DataGrid. Set VirtualizingStackPanel.IsVirtualizing=True + VirtualizationMode=Recycling explicitly; audit in Live Visual Tree.

5. **JSON persistence recursive tree leads to StackOverflow at scale** -- Nested Children JSON hits default stack limit at depth ~2000. Use flat serialization: List with Id and ParentId; reconstruct tree on load via dictionary. Raise MaxDepth = 128. Decide flat-vs-nested in Phase 6 -- retrofitting requires a schema migration.

6. **[LibraryImport] required over [DllImport] for single-file + trimming** -- [DllImport] IL stubs can be trimmed in single-file publish, causing DllNotFoundException on clean machines only. Use [LibraryImport] source-gen for all P/Invoke signatures. Keep PublishTrimmed=false.

7. **FindClose handle leak under cancellation** -- FindFirstFileEx returns INVALID_HANDLE_VALUE (= -1, not IntPtr.Zero) on failure. Use SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid with ReleaseHandle() calling FindClose. Check handle.IsInvalid.

---

## Implications for Roadmap

Research confirms a natural 9-phase build order where each phase produces a testable artifact and never requires revisiting correctness in a previous layer.

### Phase 1: Foundations
**Rationale:** Models, helpers, logging infrastructure, and the composition root must exist before any other component can be built or tested. No XAML yet.
**Delivers:** All model records (FileSystemNode, InstalledProgram, OrphanCandidate, ScanResult, ScanProgress, DeltaResult); helpers (FuzzyMatcher, ByteFormatter, PathNormalizer, JsonOptionsFactory); Serilog rolling-file setup; app.manifest with requireAdministrator + longPathAware; empty composition root.
**Avoids:** Pitfalls 11 (admin/UAC), 12 (manifest embedding), 20 (log file location)

### Phase 2: FileSystem Scanner (P/Invoke Core)
**Rationale:** The scan engine is the biggest technical unknown and highest-value component. Build before any UI -- unit-testable via a CLI harness. All correctness properties must be baked in now; retrofitting is expensive.
**Delivers:** FileSystemScanner with [LibraryImport] signatures, SafeFindHandle, long-path prefix, reparse-point detection, FIND_FIRST_EX_LARGE_FETCH, bottom-up size aggregation, Parallel.ForEach at drive root, cancellation every N entries, raw error-code handling, system-reserved-file tagging, correct nFileSizeHigh/Low combined as long.
**Avoids:** Pitfalls 1 (reparse/junction infinite loop -- highest severity), 2 (MAX_PATH), 3 (FindClose handle leak), 6 (cancellation), 7 (exception flood), 14 (pagefile skewing totals), 16 (cross-volume junction), 17 (4 GB file size), 22 (parallel aggregation races)
**Research flag:** Standard well-documented Win32 patterns -- no additional research needed; signatures verified in STACK.md

### Phase 3: Registry Scanner + Orphan Detection
**Rationale:** Depends on the FileSystemNode tree shape (Phase 2) but not on any UI. Unit-testable against fixture data. WOW6432Node dual-view and two-stage fuzzy matching must be correct before UI integration.
**Delivers:** InstalledProgramsScanner (4-view enumeration, orphan registry entry detection, pseudo-entry filtering); OrphanDetectorService (4 heuristic classifiers); two-stage FuzzyMatcher (exact/prefix first, then token Jaccard with margin check and ambiguity flag); RegistryReader helper.
**Avoids:** Pitfalls 8 (fuzzy false positives), 9 (WOW6432Node missed), 10 (orphan registry entries), 19 (per-user HKCU scope)
**Research flag:** Two-stage fuzzy matching threshold values are analytically derived -- build regression fixture of 100 real AppData/registry pairs before Phase 3 is declared done

### Phase 4: UI Shell + Scan Orchestration
**Rationale:** Once scan engines produce correct data, wire them to the MVVM shell with ThrottledProgress and swap-after-scan pattern.
**Delivers:** MainWindow.xaml, ScanOrchestratorViewModel, ThrottledProgress (100-200 ms interval), Cancel button via CancellationToken, drive-selection UI, three empty pane slots, scan-complete headline summary.
**Avoids:** Pitfalls 5 (ObservableCollection cross-thread -- swap pattern), 6 (cancellation UX), 13 (network drive default selection), 21 (progress flood dispatcher)
**Research flag:** Standard MVVM + IProgress patterns -- no additional research needed

### Phase 5: Three Panes (Programs, Orphans, Tree)
**Rationale:** Build each pane as a UserControl. VirtualizingStackPanel must be audited in Live Visual Tree. TreeNodeViewModel lazy-loading with dummy-child trick goes here.
**Delivers:** ProgramsPane + VM; OrphansPane + VM (grouped by category, Why flagged? tooltip); TreePane + VM + TreeNodeViewModel (lazy children, size-sorted on expand, percentage bar, Open in Explorer); cross-pane selection service.
**Avoids:** Pitfall 4 (WPF virtualization -- VirtualizationMode=Recycling, no ScrollViewer wrapper, fixed column widths)
**Research flag:** Benchmark with a synthetic 100k-entry dataset; verify VirtualizingStackPanel in Live Visual Tree; plan validation sprint before final XAML

### Phase 6: Persistence
**Rationale:** Enables scan history, scan reload, and is prerequisite for Phase 7 (delta). The flat-node serialization decision must be made here -- cannot be retrofitted without a schema migration.
**Delivers:** ScanPersistenceService (flat List + ParentId JSON, atomic .tmp rename, schemaVersion: 1 pre-parse via JsonDocument, MaxDepth=128, Gzip option); ScanHistoryWindow; auto-prune of oldest scans.
**Avoids:** Pitfall 15 (recursive tree JSON leads to StackOverflow and 500 MB files)
**Research flag:** Standard pattern -- no additional research needed

### Phase 7: Delta Comparison
**Rationale:** Depends on Phase 6 (needs two persisted scans). Path-keyed dictionary diff O(n+m) is correct -- not Myers/LCS which is for ordered sequences.
**Delivers:** DeltaComparatorService (Dictionary keyed by canonicalized path; Added/Removed/Grew/Shrank); DeltaPane + VM (4 sections, sorted by size delta desc by default); scan-history selection for comparison.
**Avoids:** Pitfall 24 (ObservableCollection.Clear() + N Add() -- build list off-thread, swap atomically)
**Research flag:** Algorithm fully specified in ARCHITECTURE.md -- no additional research needed

### Phase 8: Export
**Rationale:** Thin layer on top of already-complete models. Blocked on Phases 3-5. Last functional feature before publish hardening.
**Delivers:** IExporter interface; CsvExporter (CsvHelper ClassMap per pane); HtmlExporter (Scriban embedded .sbn template, Unicode-safe); ExportDialog; export for programs, orphans, tree, and delta.
**Research flag:** CsvHelper + Scriban patterns are straightforward -- no additional research needed

### Phase 9: Publish + Polish
**Rationale:** Single-file publish configuration, manifest embedding verification, clean-VM validation. Must validate on a clean Windows 11 VM with no .NET SDK. PublishTrimmed=false is mandatory.
**Delivers:** dotnet publish configuration (exact command validated in STACK.md), manifest embedding verified via mt.exe, clean-VM smoke test, binary size sanity check (70-90 MB expected), elevation display in title bar ([Admin]), scan-age timestamp in header, .NET 10 LTS migration tracked as explicit future roadmap item.
**Avoids:** Pitfall 12 (DllImport trimming in published exe, manifest not embedded)
**Research flag:** First clean-VM publish typically reveals integration issues -- budget a validation loop

### Phase Ordering Rationale

- Phases 1-3 are entirely backend-testable without any XAML. The scan engine and registry/fuzzy logic are the core value proposition and highest-risk components. Defects found in Phase 2 unit tests are roughly 10x cheaper to fix than defects found during Phase 5 UI integration.
- The shared FileSystemNode tree (Phase 2) is consumed by Phases 3, 5, 6, and 7 -- nothing downstream can start until its shape is stable and correct.
- Persistence (Phase 6) must precede Delta (Phase 7) because delta requires two stored scans.
- Export (Phase 8) is deliberately last among functional features -- reads from already-complete models, adds no architectural risk.
- One scan feeds all three panes: never scan the disk more than once per user-initiated scan action.
- Virtualization validated after full scan only. Live streaming explicitly rejected because size sort requires complete children.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 5 (Three Panes):** WPF HierarchicalDataTemplate + VirtualizingStackPanel interaction at 100k+ nodes has known undocumented edge cases. Plan a validation sprint with a synthetic dataset before committing to final XAML structure.
- **Phase 3 (Fuzzy Matching):** Two-stage token Jaccard threshold values are analytically derived, not empirically validated. Build a regression fixture of 100 real AppData/registry pairs before Phase 3 is declared done.
- **Phase 9 (Publish):** First clean-VM publish attempt typically reveals integration issues not visible in VS. Budget time for a validation loop.

Phases with standard, well-documented patterns (skip research-phase):
- **Phase 1 (Foundations):** Data model shapes, Serilog setup, manifest configuration -- fully specified in STACK.md
- **Phase 2 (Scanner):** P/Invoke signatures verified; SafeFindHandle pattern is canonical; reparse handling is documented
- **Phase 4 (UI Shell):** IProgress + Progress + ThrottledProgress pattern is canonical, well-specified in ARCHITECTURE.md
- **Phase 6 (Persistence):** Flat serialization + schemaVersion pre-parse fully specified in ARCHITECTURE.md
- **Phase 7 (Delta):** Path-keyed dictionary diff fully specified in ARCHITECTURE.md
- **Phase 8 (Export):** CsvHelper + Scriban patterns are straightforward

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All locked decisions validated against NuGet feeds, MS Learn docs, Win32 API docs as of 2026-04-24. Minor uncertainty on Scriban 5.10.x 2026 release cadence (functional use confirmed). |
| Features | HIGH | Competitor feature inventory cross-verified across multiple 2026 comparison articles and vendor sites. MVP scope consistent with PROJECT.md. Fuzzy threshold (0.7) and orphan heuristics are MEDIUM -- empirically unvalidated, expected to need v1.x tuning. |
| Architecture | HIGH | All patterns sourced from authoritative MS Learn documentation, Stephen Cleary async MVVM canon, and WinDirStat reference implementation. Shared-tree and flat JSON serialization rules are non-negotiable at scale. |
| Pitfalls | HIGH | Win32 filesystem, registry, and WPF virtualization are mature well-documented domains. Every listed pitfall has precedent in WinDirStat/TreeSize/WizTree lineage. MEDIUM on single-file + P/Invoke trimming interaction. |

**Overall confidence:** HIGH

### Gaps to Address

- **Fuzzy-match threshold empirical validation:** The 0.7 Levenshtein threshold and two-stage token Jaccard margin (0.15) are analytically reasonable but not validated against a labeled dataset. Build a regression fixture of 100 real AppData/registry pairings before Phase 3 is declared done. Expect threshold tuning in v1.x.
- **Scan performance on HDDs:** The 3-minute target for 500 GB is calibrated against SSD. On HDD, expect 5-8 minutes. Not a blocker for v1 but should be documented in the README.
- **Multi-user HKCU coverage:** DiskScout as admin reads only the admin user HKCU. Per-user installs for other accounts are invisible. Known documented limitation for personal-use mono-user scope; must be stated in the about dialog.
- **.NET 10 LTS migration timing:** .NET 8 EOL is 2026-11-10. The .NET 10 migration must be an explicit tracked roadmap item with scheduling before EOL.

---

## Sources

### Primary (HIGH confidence)
- [CommunityToolkit.Mvvm 8.4.2 -- NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm) -- version verified 2026-03-25
- [FindFirstFileExW -- MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw) -- P/Invoke signature + FIND_FIRST_EX_LARGE_FETCH = 0x2 verified
- [WIN32_FIND_DATAW -- MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-win32_find_dataa) -- struct layout verified
- [Single-file deployment -- MS Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) -- publish configuration
- [P/Invoke source generation (LibraryImport) -- MS Learn](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) -- [LibraryImport] vs [DllImport] trim behavior
- [.NET support policy -- dotnet.microsoft.com](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) -- .NET 8 EOL 2026-11-10 confirmed
- [Improve the Performance of a TreeView (WPF) -- MS Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview) -- virtualization requirements
- [WOW6432Node registry key -- MS Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/wow6432node-registry-key-present-32-bit-machine) -- dual-view enumeration requirement
- [Uninstall Registry Key -- MS Learn](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key) -- registry key structure
- [Async in 4.5: Progress and Cancellation -- .NET Blog](https://devblogs.microsoft.com/dotnet/async-in-4-5-enabling-progress-and-cancellation-in-async-apis/) -- canonical IProgress design rationale
- [WinDirStat -- GitHub](https://github.com/windirstat/windirstat) -- reference implementation for domain validation
- [AlternativeTo TreeSize alternatives 2026](https://alternativeto.net/software/treesize/) -- competitor feature inventory
- [WizTree -- Official site](https://diskanalyzer.com/) -- scan speed benchmarks
- [Fastenshtein 1.0.11 -- NuGet](https://www.nuget.org/packages/Fastenshtein) -- instance API thread-safety verified
- [Serilog.Sinks.File 7.0.0 -- NuGet](https://www.nuget.org/packages/serilog.sinks.file/) -- rolling file config
- [CsvHelper 33.1.0 -- NuGet](https://www.nuget.org/packages/csvhelper/) -- zero deps, .NET 8 target confirmed

### Secondary (MEDIUM confidence)
- [XAML Anti-Patterns: Virtualization -- CodeMag](https://codemag.com/Article/1407081/XAML-Anti-Patterns-Virtualization) -- ScrollViewer wrapper silent killer
- [Async MVVM Data Binding -- Stephen Cleary / MSDN Magazine](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/march/async-programming-patterns-for-asynchronous-mvvm-applications-data-binding) -- canonical async MVVM pattern
- [3 Steps to Lazy Load Data in WPF TreeView -- Syncfusion](https://www.syncfusion.com/blogs/post/3-steps-to-lazy-load-data-in-wpf-treeview-mvvm) -- dummy-child pattern
- [Squarified Treemaps in XAML -- CodeProject](https://www.codeproject.com/Articles/7039/Squarified-Treemaps-in-XAML-C-using-Microsoft-Long) -- custom TreeMapPanel algorithm reference
- [The definitive guide to diff algorithms -- Ably](https://ably.com/blog/practical-guide-to-diff-algorithms) -- confirms dictionary-keyed diff for unordered data
- [FolderSizes Trend Analyzer](https://www.foldersizes.com/screens/trend-analyzer) -- scan delta feature precedent in paid tool
- [XDA -- Stop using WinDirStat](https://www.xda-developers.com/stop-using-windirstat-and-switch-to-this-free-tool-instead/) -- competitor positioning and scan-speed benchmarks

### Tertiary (LOW confidence -- needs validation)
- [Scriban -- GitHub](https://github.com/scriban/scriban) -- version 5.10.x current; 2026 release cadence not verified via WebSearch
- FluentAssertions v8 commercial licence -- confirmed via community reports; pin below 8.0 as precaution

---
*Research completed: 2026-04-24*
*Ready for roadmap: yes*