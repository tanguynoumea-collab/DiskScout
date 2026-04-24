# Architecture Research

**Domain:** Windows desktop disk analyzer (WPF / .NET 8 / MVVM strict)
**Researched:** 2026-04-24
**Confidence:** HIGH

## Standard Architecture

### System Overview

DiskScout is a classic 3-layer MVVM desktop app, but with one domain-specific twist: three scan engines share a **single canonical `FileSystemNode` tree** produced by one filesystem crawl. The `InstalledProgramsScanner` runs independently (registry only), and the `OrphanDetectorService` is a pure *consumer* that correlates the registry result with AppData/ProgramFiles subtrees extracted from the shared tree. This avoids scanning the disk three times.

```
 ┌─────────────────────────────────────────────────────────────────┐
 │                           Views (XAML)                           │
 │   MainWindow      ProgramsPane     OrphansPane     TreePane      │
 │   ScanHistoryWnd  DeltaPane        ExportDialog                  │
 │                                                                  │
 │   (bindings only, no code-behind except drag/drop, resize)       │
 └──────────────────────────┬──────────────────────────────────────┘
                            │ DataContext / DataBinding
 ┌──────────────────────────┴──────────────────────────────────────┐
 │                         ViewModels                               │
 │                                                                  │
 │   MainWindowVM ── ScanOrchestratorVM                             │
 │         │              │                                         │
 │         ├── ProgramsPaneVM   (list + filter)                     │
 │         ├── OrphansPaneVM    (list + category filter)            │
 │         ├── TreePaneVM       (lazy TreeView root)                │
 │         ├── DeltaPaneVM      (diff viewer)                       │
 │         └── ScanHistoryVM    (past scans list)                   │
 │                                                                  │
 │   [RelayCommand] StartScan / CancelScan / Export / LoadScan      │
 │   IProgress<ScanProgress> pushed from services                   │
 └──────────────────────────┬──────────────────────────────────────┘
                            │ constructor-injected services
 ┌──────────────────────────┴──────────────────────────────────────┐
 │                           Services                               │
 │                                                                  │
 │   ┌───────────────────────┐   ┌───────────────────────┐          │
 │   │ FileSystemScanner     │   │ InstalledPrograms     │          │
 │   │ (P/Invoke            │   │ Scanner               │          │
 │   │  FindFirstFileEx,    │   │ (registry HKLM+HKCU)  │          │
 │   │  parallel roots,     │   │                       │          │
 │   │  bottom-up size agg) │   │                       │          │
 │   └──────────┬────────────┘   └────────────┬──────────┘          │
 │              │                             │                     │
 │              ▼                             ▼                     │
 │         FileSystemNode                InstalledProgram[]         │
 │         (canonical tree)                                         │
 │              │                             │                     │
 │              └─────────────┬───────────────┘                     │
 │                            ▼                                     │
 │              ┌─────────────────────────────┐                     │
 │              │  OrphanDetectorService      │                     │
 │              │  (consumes tree + programs, │                     │
 │              │   uses FuzzyMatcher helper) │                     │
 │              └────────────┬────────────────┘                     │
 │                           ▼                                      │
 │                    OrphanCandidate[]                             │
 │                                                                  │
 │   ScanPersistenceService  (JSON read/write, schemaVersion)       │
 │   DeltaComparatorService  (two ScanResults → DeltaResult)        │
 │   ExportService           (IExporter: CsvExporter, HtmlExporter) │
 │   LogService              (rolling file 5 MB)                    │
 └──────────────────────────┬──────────────────────────────────────┘
                            │
 ┌──────────────────────────┴──────────────────────────────────────┐
 │                        Models (POCOs)                            │
 │   FileSystemNode     InstalledProgram    OrphanCandidate         │
 │   ScanResult         DeltaResult         ScanProgress            │
 └──────────────────────────┬──────────────────────────────────────┘
                            │
 ┌──────────────────────────┴──────────────────────────────────────┐
 │                          Helpers                                 │
 │   FuzzyMatcher (Levenshtein)    PathNormalizer                   │
 │   PInvokeFileFinder             ByteFormatter                    │
 │   JsonOptionsFactory            RegistryReader                   │
 └──────────────────────────────────────────────────────────────────┘
                            │
                            ▼
                       Win32 API / Registry / Disk
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **Models** | Plain data containers, no logic, serializable | `record` or POCO classes, immutable where possible except `FileSystemNode` (mutable parent/children graph) |
| **Views** | Visual rendering, bindings, no business logic | XAML + minimal code-behind (drag/drop `Window.AllowDrop`, `SizeChanged`) |
| **ViewModels** | UI state, commands, progress exposure, binding-safe collections | `[ObservableObject]` partial classes + `[RelayCommand]` + `[ObservableProperty]` |
| **Services** | Domain work: scanning, matching, persistence, diff, export | Interfaces (`IFileSystemScanner`, etc.) + concrete class; async + `CancellationToken` + `IProgress<T>` |
| **Helpers** | Pure stateless utility functions | `static class` with pure methods, no allocations where possible in hot paths |

**Rule of thumb:** A file belongs in `Services/` if it has side effects (I/O, registry, disk) or orchestrates work. It belongs in `Helpers/` if it's a pure function (string matching, path normalization, byte formatting). Models are data only — never reach up into Services.

## Recommended Project Structure

```
DiskScout/
├── App.xaml                      # Application resources, styles
├── App.xaml.cs                   # Composition root — manual DI
├── app.manifest                  # requireAdministrator
├── DiskScout.csproj
│
├── Models/
│   ├── FileSystemNode.cs         # Mutable tree node (Parent/Children/Size/Path)
│   ├── InstalledProgram.cs       # Registry program record
│   ├── OrphanCandidate.cs        # Orphan detection result + category enum
│   ├── ScanResult.cs             # Top-level aggregate (programs + orphans + tree)
│   ├── ScanProgress.cs           # Progress DTO (phase, path, filesScanned, bytes)
│   ├── DeltaResult.cs            # Diff container (Added/Removed/Grew/Shrank)
│   └── SchemaVersion.cs          # const int CURRENT = 1
│
├── Services/
│   ├── Abstractions/
│   │   ├── IFileSystemScanner.cs
│   │   ├── IInstalledProgramsScanner.cs
│   │   ├── IOrphanDetectorService.cs
│   │   ├── IScanPersistenceService.cs
│   │   ├── IDeltaComparatorService.cs
│   │   ├── IExporter.cs
│   │   └── ILogService.cs
│   ├── FileSystemScanner.cs       # P/Invoke FindFirstFileEx + Parallel.ForEach
│   ├── InstalledProgramsScanner.cs
│   ├── OrphanDetectorService.cs   # Consumes tree + programs, uses FuzzyMatcher
│   ├── ScanPersistenceService.cs  # System.Text.Json + schemaVersion
│   ├── DeltaComparatorService.cs  # Path-keyed dictionary diff
│   ├── Exporters/
│   │   ├── CsvExporter.cs
│   │   └── HtmlExporter.cs
│   └── LogService.cs
│
├── Helpers/
│   ├── FuzzyMatcher.cs            # Levenshtein ratio + normalization
│   ├── PathNormalizer.cs          # Canonicalize paths (case, long-path prefix)
│   ├── PInvoke/
│   │   ├── Win32FileFinder.cs     # FindFirstFileExW signature + wrappers
│   │   └── Win32Native.cs         # DllImport + struct definitions
│   ├── RegistryReader.cs          # Safe HKLM/HKCU enumeration helper
│   ├── ByteFormatter.cs           # 1.23 GB pretty-print
│   └── JsonOptionsFactory.cs      # Shared JsonSerializerOptions
│
├── ViewModels/
│   ├── Base/
│   │   └── ViewModelBase.cs       # Optional : inherits ObservableObject
│   ├── MainWindowViewModel.cs
│   ├── ScanOrchestratorViewModel.cs   # Coordinates 3 scanners + progress
│   ├── ProgramsPaneViewModel.cs
│   ├── OrphansPaneViewModel.cs
│   ├── TreePaneViewModel.cs
│   ├── TreeNodeViewModel.cs           # Wraps FileSystemNode for lazy TreeView
│   ├── DeltaPaneViewModel.cs
│   └── ScanHistoryViewModel.cs
│
├── Views/
│   ├── MainWindow.xaml(.cs)
│   ├── Panes/
│   │   ├── ProgramsPane.xaml(.cs)
│   │   ├── OrphansPane.xaml(.cs)
│   │   ├── TreePane.xaml(.cs)
│   │   └── DeltaPane.xaml(.cs)
│   ├── Dialogs/
│   │   ├── ScanHistoryWindow.xaml(.cs)
│   │   └── ExportDialog.xaml(.cs)
│   └── Converters/
│       ├── BytesToHumanConverter.cs
│       └── BoolToVisibilityConverter.cs
│
└── Resources/
    ├── Styles.xaml
    └── Icons/
```

### Structure Rationale

- **`Models/`:** POCOs only. No references to Services or ViewModels. Keeps serialization trivial and prevents accidental UI coupling in persisted data.
- **`Services/Abstractions/`:** Interfaces split into their own folder. Enables testing with mocks and allows manual DI in `App.xaml.cs` to bind interface → implementation in a single place.
- **`Helpers/PInvoke/`:** P/Invoke kept in its own sub-namespace so the unsafe/native surface is localized and auditable. Reviewing security/safety of native calls is easier when they're grouped.
- **`ViewModels/`:** One VM per window/pane. `TreeNodeViewModel` exists separately because each tree node needs its own observable wrapper for lazy-loading (children only instantiated on expand).
- **`Views/Panes/`:** Each tab is a `UserControl`, not embedded directly in `MainWindow.xaml`. Keeps files under 300 lines, makes per-pane rework cheap, and mirrors ViewModel structure 1:1.
- **`App.xaml.cs` as composition root:** Mark Seemann's "Pure DI" pattern — entire object graph built in `OnStartup`. No DI container keeps the single-file binary smaller and there are ~12 services total, so manual wiring is under 50 lines.

## Architectural Patterns

### Pattern 1: Shared Tree, Single Scan

**What:** `FileSystemScanner` runs *once* and produces the canonical `FileSystemNode` tree. `OrphanDetectorService` consumes this tree by path lookup (e.g. "give me the `%LocalAppData%` subtree"). `TreePaneVM` also consumes the same tree root.

**When to use:** Always. The disk is the expensive resource — never scan twice.

**Trade-offs:**
- Pro: Scan time is bounded by one disk pass (~3 min for 500 GB on SSD as PROJECT.md targets)
- Pro: All views show consistent sizes (they all derived from the same scan instant)
- Con: Must hold the tree in memory — a 500k-file scan may use 200–400 MB RAM. Acceptable for desktop tool.
- Con: Orphan detection cannot start before FS scan reaches the relevant subtrees (AppData/Local, AppData/Roaming, Program Files, ProgramData, %TEMP%)

**Example:**
```csharp
// ScanOrchestratorViewModel.ExecuteScanAsync
var fsTask       = _fsScanner.ScanAsync(roots, progress, ct);
var programsTask = _programsScanner.ScanAsync(ct);

// Wait both to finish before orphan detection
var tree     = await fsTask.ConfigureAwait(false);
var programs = await programsTask.ConfigureAwait(false);

// Orphan detector consumes BOTH as read-only inputs
var orphans = await _orphanDetector
    .DetectAsync(tree, programs, progress, ct)
    .ConfigureAwait(false);

var result = new ScanResult(tree, programs, orphans, DateTimeOffset.Now);
```

### Pattern 2: Bottom-Up Size Aggregation

**What:** During the FS scan, leaf file sizes are known immediately from `WIN32_FIND_DATA`. Folder sizes are computed as `Sum(Children.Size)` *after* all children are enumerated. The scan is depth-first per root, but roots run in parallel (`Parallel.ForEach(drives, drive => ScanRecursive(drive))`).

**When to use:** Always for disk analyzers. Folder size cannot be known without enumerating descendants.

**Trade-offs:**
- Pro: No redundant walks — size is a side effect of the enumeration
- Pro: Parallelizes naturally at the first level (each drive letter is independent)
- Con: Need thread-safe node construction if children are added from multiple threads. Simplest solution: each parallel task owns its own subtree, roots are stitched onto a root-level `FileSystemNode` at the very end.

**Example:**
```csharp
private long ScanRecursive(string path, FileSystemNode parent, CancellationToken ct)
{
    long total = 0;
    foreach (var entry in Win32FileFinder.Enumerate(path)) // LARGE_FETCH P/Invoke
    {
        ct.ThrowIfCancellationRequested();
        var child = new FileSystemNode(entry.Name, parent);
        if (entry.IsDirectory)
            child.Size = ScanRecursive(entry.FullPath, child, ct);  // recurse
        else
            child.Size = entry.Size;
        parent.AddChild(child);  // thread-local — safe
        total += child.Size;
    }
    return total;
}
```

### Pattern 3: Lazy TreeView via `TreeNodeViewModel`

**What:** The `FileSystemNode` tree is fully materialized in memory by the scanner, but the *UI wrapper* (`TreeNodeViewModel`) only instantiates children when a node is expanded. Combined with `VirtualizingStackPanel.IsVirtualizing="True"` and `VirtualizationMode="Recycling"`, this keeps the TreeView responsive even at 500k+ nodes.

**When to use:** Always for TreeViews with > 5k total descendants. Confirmed pattern for disk analyzers (Syncfusion, ComponentOne guides).

**Trade-offs:**
- Pro: Initial tree render is instant — only root children VMs exist at first
- Pro: Memory for VM wrappers grows only as user explores
- Con: Requires the "dummy child" trick or `HasChildren` flag so the expander chevron appears before real children are loaded
- Con: Sort-by-size at expand time adds perceptible latency for huge folders — acceptable because sort happens once per expand

**Example:**
```csharp
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly FileSystemNode _node;
    private bool _childrenLoaded;

    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
        {
            _childrenLoaded = true;
            foreach (var child in _node.Children.OrderByDescending(c => c.Size))
                Children.Add(new TreeNodeViewModel(child));
        }
    }
}
```

**Decision on streaming vs. post-scan:** Virtualize *after* full scan — do **not** stream live during the scan. Rationale:
1. Size aggregation requires descendants to be done first, so a folder cannot be displayed until its scan completes
2. Sorting by size (the killer feature vs. Explorer) requires all children known
3. Live streaming creates janky UX: rows appearing and reordering constantly
4. Progress is communicated via `IProgress<ScanProgress>` (phase + current path + bytes) — that's sufficient feedback

### Pattern 4: `IProgress<T>` + `Progress<T>` for Cross-Thread Progress

**What:** Services accept `IProgress<ScanProgress>`. ViewModels create `new Progress<ScanProgress>(p => { /* UI update */ })`. The `Progress<T>` class captures the current `SynchronizationContext` (WPF UI thread) and marshals callbacks automatically.

**When to use:** Any long-running service that reports progress to the UI.

**Trade-offs:**
- Pro: Services stay UI-agnostic — they know nothing about `Dispatcher`
- Pro: Automatic thread marshalling without manual `Dispatcher.Invoke`
- Con: Must throttle — reporting every file (~500k calls) floods the dispatcher. Throttle to ~10 Hz in the service.

**Example:**
```csharp
// In service — UI-agnostic
var lastReport = Stopwatch.GetTimestamp();
foreach (var entry in enumeration)
{
    // ... scan ...
    if (Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds > 100)
    {
        progress?.Report(new ScanProgress(Phase.Scanning, entry.FullPath, filesSeen, bytesSeen));
        lastReport = Stopwatch.GetTimestamp();
    }
}

// In ViewModel — UI thread
[RelayCommand(CanExecute = nameof(CanStartScan))]
private async Task StartScanAsync(CancellationToken ct)
{
    var progress = new Progress<ScanProgress>(p =>
    {
        CurrentPhase = p.Phase;
        CurrentPath = p.Path;
        FilesSeen = p.FilesSeen;
    });
    _scanResult = await Task.Run(() =>
        _orchestrator.RunAsync(_selectedRoots, progress, ct), ct);
    PopulatePanesFromScan(_scanResult);
}
```

### Pattern 5: Scan Engines as Producers, ViewModels as Subscribers

**What:** Scanners never touch ViewModels or the `Dispatcher`. They produce immutable-ish result objects (`FileSystemNode` tree + `InstalledProgram[]` + `OrphanCandidate[]`). The `ScanOrchestratorVM` awaits all scans, then calls `PopulatePanes(scanResult)` on the UI thread, which hands each pane VM its data.

**When to use:** Always. Keeps services testable and prevents thread-affinity bugs.

**Trade-offs:**
- Pro: Services are pure, sync-free of UI concerns
- Pro: `ObservableCollection` population happens once, on UI thread — no need for `EnableCollectionSynchronization`
- Con: No progressive display of results — user sees panes populate all at once at end. Acceptable because per-pane populate is instant once data is in hand (< 100 ms for realistic counts).

### Pattern 6: `IExporter` Unified Interface

**What:** Each exporter (CSV, HTML) implements `IExporter` with overloads for each export type. A factory resolves the right exporter from user selection in the export dialog.

**When to use:** When you have ≥ 2 export formats and ≥ 2 export targets (programs, orphans, tree, delta).

**Trade-offs:**
- Pro: Adding a new format (e.g. Markdown, XLSX) is one new class
- Pro: Single dialog with "format" dropdown
- Con: Slight interface bloat if formats diverge (e.g. HTML wants to embed CSS while CSV doesn't)

**Example:**
```csharp
public interface IExporter
{
    string FormatName { get; }              // "CSV", "HTML"
    string FileExtension { get; }           // ".csv", ".html"

    Task ExportProgramsAsync(IReadOnlyList<InstalledProgram> items, string path, CancellationToken ct);
    Task ExportOrphansAsync(IReadOnlyList<OrphanCandidate> items, string path, CancellationToken ct);
    Task ExportTreeAsync(FileSystemNode root, int maxDepth, string path, CancellationToken ct);
    Task ExportDeltaAsync(DeltaResult delta, string path, CancellationToken ct);
}
```

### Pattern 7: Composition Root in `App.xaml.cs` (Pure DI)

**What:** `OnStartup` instantiates every service and ViewModel manually. No DI container. MainWindow gets its ViewModel passed to the constructor, bound to `DataContext`.

**When to use:** App with < 20 services and stable dependency graph.

**Example:**
```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Helpers (stateless, could be static)
    var fuzzy = new FuzzyMatcher();

    // Services
    var log       = new LogService(GetLogPath());
    var fs        = new FileSystemScanner(log);
    var programs  = new InstalledProgramsScanner(log);
    var orphans   = new OrphanDetectorService(fuzzy, log);
    var persist   = new ScanPersistenceService(GetScanDir(), log);
    var delta     = new DeltaComparatorService();
    var exporters = new IExporter[] { new CsvExporter(), new HtmlExporter() };

    // ViewModels
    var orchestrator = new ScanOrchestratorViewModel(fs, programs, orphans);
    var main = new MainWindowViewModel(orchestrator, persist, delta, exporters, log);

    MainWindow = new MainWindow { DataContext = main };
    MainWindow.Show();
}
```

## Data Flow

### Scan Flow (happy path)

```
User clicks "Scan" in MainWindow
    ↓
MainWindowVM.StartScanCommand (RelayCommand)
    ↓
ScanOrchestratorVM.RunAsync(roots, progress, ct)
    ↓
    ├── Task.Run → FileSystemScanner.ScanAsync()
    │                    ↓ P/Invoke FindFirstFileEx + Parallel.ForEach
    │              FileSystemNode tree (full)
    │
    └── Task.Run → InstalledProgramsScanner.ScanAsync()
                         ↓ RegistryReader HKLM+HKCU+WOW6432
                   InstalledProgram[]
    (both awaited in parallel)
    ↓
OrphanDetectorService.DetectAsync(tree, programs)
    ↓ FuzzyMatcher (Levenshtein ≥ 0.7) on Publisher/DisplayName
    ↓ Subtree lookups: %LocalAppData%, %AppData%, Program Files, %TEMP%
    OrphanCandidate[]
    ↓
ScanResult aggregate created on worker thread
    ↓ (await returns to UI thread via captured SynchronizationContext)
MainWindowVM.PopulatePanes(scanResult)
    ↓ (on UI thread — ObservableCollection mutations safe)
ProgramsPaneVM.Items ← programs
OrphansPaneVM.Items  ← orphans grouped by category
TreePaneVM.Root      ← new TreeNodeViewModel(scanResult.Tree)
    ↓
ScanPersistenceService.SaveAsync(scanResult)
    ↓
JSON file in %LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json
```

### Progress Flow

```
Service thread (Task.Run)                 UI thread (Dispatcher)
──────────────────────────                 ──────────────────────
Scanner loops through files
    │
    │ every ~100ms:
    ↓
IProgress<T>.Report(ScanProgress)
    │
    │ Progress<T> internally calls
    │ SynchronizationContext.Post(...)
    │
    └──────────────────────────────────→ Progress handler runs on UI thread
                                            │
                                            ↓
                                         VM.CurrentPath = p.Path
                                         VM.BytesSeen  = p.Bytes
                                            │
                                            ↓
                                         Binding updates UI
```

### Persistence Flow

```
ScanResult (in memory)
    ↓ JsonSerializer.SerializeAsync + JsonOptionsFactory
    │ includes: { schemaVersion: 1, scannedAt: ..., roots: [...], tree: {...}, programs: [...], orphans: [...] }
    ↓
.tmp file in %LocalAppData%\DiskScout\scans\
    ↓ atomic rename
scan_YYYYMMDD_HHmmss.json

--- load ---

File chosen from ScanHistoryWindow
    ↓ JsonSerializer.DeserializeAsync
    ↓
Read schemaVersion first (via JsonDocument pre-parse)
    ↓
if version < CURRENT: invoke migration pipeline (ScanMigrationV1toV2 etc.)
    ↓
Hydrate ScanResult
    ↓ (reconstruct Parent pointers in tree — they're not serialized to avoid cycles)
Populate panes as for fresh scan
```

### Delta Flow

```
User opens Delta pane, picks 2 scans from history
    ↓
DeltaPaneVM.CompareAsync(oldScan, newScan)
    ↓ Task.Run
DeltaComparatorService.Compare(old, new)
    ↓
    Flatten both trees into Dictionary<string, NodeSummary>   keyed by normalized FullPath
    Flatten both program lists into Dictionary<string, InstalledProgram>   keyed by DisplayName+Publisher
    │
    ↓
    Single pass over union of keys:
      if in new only            → Added
      if in old only            → Removed
      if in both, size grew     → Grew
      if in both, size shrank   → Shrank
      if in both, unchanged     → ignored (not surfaced)
    ↓
DeltaResult { AddedNodes, RemovedNodes, GrewNodes, ShrankNodes, + same for programs }
    ↓
DeltaPaneVM.LoadFromDelta(result) on UI thread
```

## Scaling Considerations

Desktop personal-use tool — "users" isn't the right axis. The real axis is **disk size / file count**.

| Scale | Architecture Adjustments |
|-------|--------------------------|
| < 100 GB / 100k files | Default architecture works. Scan < 30 s. |
| 100 GB – 1 TB / 100k – 1M files | Default architecture holds. PROJECT.md target : 500 GB in 3 min on SSD. FindFirstFileExW + LARGE_FETCH + Parallel.ForEach at drive level is sufficient. |
| 1 TB – 10 TB / > 5M files | RAM becomes the bottleneck (~200 B per `FileSystemNode` × 5M = 1 GB). Switch `FileSystemNode` to a struct-of-arrays layout or pool strings. Out-of-scope for v1. |
| > 10 TB | Requires streaming/on-disk index (MFT direct read). Explicit non-goal per PROJECT.md. |

### Scaling Priorities (what breaks first)

1. **First bottleneck: RAM from string allocations for paths.** Each `FileSystemNode` holds a `Name` string. On a 1M-file scan that's 1M strings averaging 20 chars = ~50 MB just for names, plus string overhead (~24 B/string) = ~75 MB. Fix: store only `Name` (not `FullPath`); compute full path by walking parents.
2. **Second bottleneck: GC pressure during parallel scan.** Per-file allocations (`WIN32_FIND_DATA`, `FileSystemNode`) churn Gen 0 heavily. Fix: reuse `WIN32_FIND_DATA` via `stackalloc` or `ArrayPool<byte>` for the native struct; pool `FileSystemNode` via `ObjectPool<T>` if profiling shows it matters.
3. **Third bottleneck: UI freezes if `IProgress<T>` reports every file.** Fix: 100 ms throttle in the service (see Pattern 4).
4. **Fourth bottleneck: JSON serialization of tree.** A 500k-node tree produces ~50 MB JSON. Fix: use `JsonSerializer.SerializeAsync` with streaming to `FileStream`, never `ToString()`. Already planned.

## Anti-Patterns

### Anti-Pattern 1: Scanning the disk three times

**What people do:** Have each of the three scanners (Programs / Orphans / FileSystem) do their own directory enumeration.

**Why it's wrong:** 3× I/O cost. For a 500 GB disk, that's 9 minutes instead of 3.

**Do this instead:** One FS scan produces one tree. `OrphanDetectorService` accepts the tree + programs as input and does pure in-memory analysis.

### Anti-Pattern 2: Updating `ObservableCollection` from a worker thread without `EnableCollectionSynchronization`

**What people do:** Call `_items.Add(x)` from a `Task.Run` scan loop.

**Why it's wrong:** WPF throws `NotSupportedException: This type of CollectionView does not support changes to its SourceCollection from a thread different from the Dispatcher thread` the moment the collection is bound.

**Do this instead:** Two valid strategies:
- **Preferred here:** Build the result on the worker thread (regular `List<T>`), await back to UI thread, then populate `ObservableCollection` once. Pattern 5.
- **Only if streaming required:** Call `BindingOperations.EnableCollectionSynchronization(_items, _lock)` on the UI thread *before* binding, then lock on `_lock` during worker-thread mutations. Not needed for DiskScout given Pattern 5.

### Anti-Pattern 3: `Dispatcher.Invoke` scattered in service code

**What people do:** `Application.Current.Dispatcher.Invoke(() => VM.Progress = 42)` inside a service method.

**Why it's wrong:** Couples services to WPF. Services become untestable outside a WPF context. Creates a subtle back-door dependency from Service → ViewModel.

**Do this instead:** Services take `IProgress<T>`. ViewModels create `Progress<T>` which captures the UI SynchronizationContext automatically. No manual `Dispatcher` calls anywhere in services.

### Anti-Pattern 4: `async void` outside event handlers

**What people do:** `public async void RunScan()` on a ViewModel method.

**Why it's wrong:** Exceptions crash the app; no awaitable return; can't test. `RelayCommand` already handles `async Task` properly.

**Do this instead:** `[RelayCommand] private async Task StartScanAsync(CancellationToken ct) { ... }`. The generator produces the `AsyncRelayCommand` wrapper. `async void` only for legacy WPF event handlers (drag/drop handlers in code-behind).

### Anti-Pattern 5: Mixing UI-thread and worker-thread access on `FileSystemNode`

**What people do:** Start binding the tree to the UI *while* the scanner is still populating it.

**Why it's wrong:** Race condition — the TreeView enumerates `Children` while a scan thread is adding to it.

**Do this instead:** The tree is built entirely on worker threads and becomes *effectively read-only* once the scan returns. UI only receives the final tree. If future versions want streaming, wrap children in a `ReadOnlyObservableCollection` populated via `BindingOperations.EnableCollectionSynchronization` — but not for v1.

### Anti-Pattern 6: Storing parent pointers in serialized JSON

**What people do:** Serialize `FileSystemNode.Parent` and get a cyclic-graph error from `System.Text.Json`.

**Why it's wrong:** Cycles break default JSON serialization. Using `ReferenceHandler.Preserve` works but produces ugly `$id`/`$ref` JSON that humans can't read.

**Do this instead:** Mark `Parent` as `[JsonIgnore]`. Reconstruct parent pointers after deserialization with a single recursive walk (`Hydrate(root, parent: null)`).

### Anti-Pattern 7: Fuzzy matching scattered across OrphanDetector logic

**What people do:** Inline Levenshtein checks inside `OrphanDetectorService` in several places.

**Why it's wrong:** Duplication; inconsistent threshold values across call sites; hard to tune.

**Do this instead:** Single `FuzzyMatcher` helper with `Match(string a, string b, double threshold = 0.7)`. `OrphanDetectorService` only *calls* it. Threshold is a constant in `FuzzyMatcher` or passed in once.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Windows Registry | `Microsoft.Win32.Registry` via `RegistryReader` helper | Must read both HKLM\SOFTWARE and HKLM\SOFTWARE\WOW6432Node (32-bit installs on 64-bit OS), plus HKCU. Null-check every value — registry entries are inconsistent. |
| Win32 file enumeration | P/Invoke `FindFirstFileExW` + `FindNextFileW` | Must pass `FindExInfoBasic` and `FIND_FIRST_EX_LARGE_FETCH`. Remember `FindClose` — use `SafeFindHandle` (`SafeHandle` subclass) to guarantee release even on cancellation. |
| Windows Shell folders | `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` etc. | Needed by OrphanDetector to know *which* subtrees to examine. |
| Filesystem write (persistence, logs) | `System.IO` + atomic rename (`.tmp` → final) | Never write directly to the final path — risk of half-written JSON if the process is killed mid-write. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| ViewModel ↔ Service | Constructor injection of interface | Services never reference ViewModels. One-way dependency. |
| Service ↔ Service | `OrphanDetector` takes tree + programs as method parameters | No service holds a reference to another service's *state*. Results flow via return values. |
| View ↔ ViewModel | DataBinding only | Zero code-behind beyond DragDrop/Resize events which delegate to VM commands. |
| Scanner thread ↔ UI thread | `IProgress<T>` for progress, `await` for completion | No direct `Dispatcher` use. |
| Model ↔ any layer | Models are referenced everywhere but reference nothing | Pure data. Never inject services into a Model. |

## Build Order Implications

This architecture suggests a natural phase sequence for the roadmap:

1. **Foundations (Phase 1):** Models, Helpers (FuzzyMatcher, ByteFormatter, PathNormalizer), LogService, App.xaml.cs skeleton with empty composition root. No UI. Prove the data shapes compile and round-trip through `System.Text.Json`.

2. **Scan Engine (Phase 2):** `FileSystemScanner` with P/Invoke + bottom-up aggregation. CLI test harness (temporary `Program.Main` or unit tests) to scan a test directory. No WPF yet. **Biggest unknown = biggest risk = first**.

3. **Registry + Orphans (Phase 3):** `InstalledProgramsScanner` + `OrphanDetectorService` using FuzzyMatcher. Still no UI — test via harness with a known registry dump and a stub tree.

4. **UI Shell + Scan Orchestration (Phase 4):** `MainWindow`, `ScanOrchestratorVM`, progress reporting via `IProgress<ScanProgress>`, Cancel button. Three empty panes.

5. **Panes (Phase 5):** `ProgramsPane`, `OrphansPane`, `TreePane` with lazy `TreeNodeViewModel`. This is where virtualization gets tuned.

6. **Persistence (Phase 6):** `ScanPersistenceService`, `ScanHistoryWindow`. Includes schemaVersion infrastructure (even if only v1 exists).

7. **Delta (Phase 7):** `DeltaComparatorService` + `DeltaPane`. Depends on persistence (to load two scans).

8. **Export (Phase 8):** `IExporter` + CSV/HTML exporters. Thin layer on top of existing models.

9. **Polish + Publish (Phase 9):** single-file publish profile, icon, about dialog, manifest tuning.

The architecture keeps phases 1–3 entirely backend-testable without any XAML — crucial because scan engines are the core value and the risky parts. UI (phases 4–5) is additive once data shapes are stable.

## Thread Safety Summary

| Object | Thread | Rule |
|--------|--------|------|
| `FileSystemNode` tree during scan | Worker threads | Each parallel task owns its own subtree. Stitch at the end. No cross-thread mutation. |
| `FileSystemNode` tree after scan | Any (read-only) | Treated as immutable once handed to ViewModels. |
| `ObservableCollection<T>` in VMs | UI thread only | Populated after `await` returns to UI context. Never mutate from worker. |
| `TreeNodeViewModel.Children` | UI thread only | Lazy-instantiated on `IsExpanded` change — always on UI thread. |
| `IProgress<T>.Report` | Any | `Progress<T>` marshals to captured context automatically. |
| Logger (`LogService`) | Any | Must be thread-safe internally (lock around file write or use `Channel<T>` + background writer). |
| `CancellationToken` | Any | Inherently thread-safe. Check at loop boundaries, not every statement. |

## Persistence Schema Shape

```json
{
  "schemaVersion": 1,
  "scanId": "c3e9b1a7-...",
  "scannedAt": "2026-04-24T14:32:17.123+02:00",
  "durationMs": 178432,
  "hostname": "DESKTOP-XYZ",
  "roots": ["C:\\", "D:\\"],
  "summary": {
    "totalBytes": 524288000000,
    "totalFiles": 487321,
    "totalFolders": 52104
  },
  "programs": [
    {
      "displayName": "Visual Studio Code",
      "publisher": "Microsoft Corporation",
      "installLocation": "C:\\Users\\x\\AppData\\Local\\Programs\\Microsoft VS Code",
      "uninstallString": "...",
      "registryHive": "HKCU",
      "sizeBytes": 412000000
    }
  ],
  "orphans": [
    {
      "category": "AppDataOrphan",
      "path": "C:\\Users\\x\\AppData\\Local\\SomeDeadApp",
      "sizeBytes": 148000000,
      "lastModified": "2024-02-10T...",
      "reason": "No installed program matches 'SomeDeadApp' (fuzzy threshold 0.7)"
    }
  ],
  "tree": {
    "name": "",
    "path": "",
    "isDirectory": true,
    "sizeBytes": 524288000000,
    "fileCount": 487321,
    "children": [
      {
        "name": "C:\\",
        "isDirectory": true,
        "sizeBytes": 412000000000,
        "fileCount": 301455,
        "children": [ ... ]
      }
    ]
  }
}
```

**Versioning strategy:**
- Read `schemaVersion` *before* full deserialization using `JsonDocument.Parse` on the first object
- If `schemaVersion == CURRENT`, deserialize directly
- If `schemaVersion < CURRENT`, apply migration chain (V1→V2, V2→V3, …) each producing the next-version POJO
- If `schemaVersion > CURRENT`, refuse to load (newer file format than this binary supports) with a clear user message
- Additive changes (new optional fields with defaults) are backward-compatible at the JSON level but should still bump `schemaVersion` for explicit tracking
- Breaking changes (renames, type changes, required new fields) must bump `schemaVersion` and ship a migration

**Gotcha:** Serializing the tree as nested JSON produces a deep document (`ReaderOptions.MaxDepth` default is 64 — may need to raise to 256 for deep folder hierarchies like `node_modules`). Configure this in `JsonOptionsFactory`.

## Delta Algorithm Design

**Chosen approach: path-keyed dictionary diff.**

Not sequence diff (Myers / LCS): those are for ordered sequences (lines of text). Filesystem trees aren't ordered — the natural identity of a node is its full path.

**Algorithm:**

```csharp
public DeltaResult Compare(ScanResult old, ScanResult @new)
{
    // Flatten trees to dictionaries keyed by canonical full path
    var oldMap = Flatten(old.Tree);       // Dictionary<string, NodeSummary>
    var newMap = Flatten(@new.Tree);

    var added   = new List<NodeSummary>();
    var removed = new List<NodeSummary>();
    var grew    = new List<NodeDelta>();
    var shrank  = new List<NodeDelta>();

    // One pass over union of keys
    foreach (var key in oldMap.Keys.Union(newMap.Keys))
    {
        var inOld = oldMap.TryGetValue(key, out var o);
        var inNew = newMap.TryGetValue(key, out var n);

        if (!inOld)          added.Add(n);
        else if (!inNew)     removed.Add(o);
        else if (n.Size > o.Size)  grew.Add(new NodeDelta(key, o.Size, n.Size));
        else if (n.Size < o.Size)  shrank.Add(new NodeDelta(key, o.Size, n.Size));
        // else unchanged, not surfaced
    }

    // Same pattern for programs: key by DisplayName + Publisher
    // ...

    return new DeltaResult(added, removed, grew, shrank, ...);
}
```

**Complexity:** O(n + m) where n, m are node counts in each scan. At 500k nodes, this is sub-second (even with string hashing overhead).

**Why not recursive tree walk?** A recursive paired walk would need to handle the case where a subtree exists in one scan but not the other. The dictionary approach handles this uniformly in one pass.

**Path normalization matters:** Both scans must use `PathNormalizer` (case-fold on Windows, strip trailing slashes, resolve `\\?\` long-path prefix consistently). A path mismatch like `C:\Users\X\appdata` vs `C:\Users\X\AppData` would falsely flag as "removed + added" pair.

**Display structure:** `DeltaPane` shows four sections (Added / Removed / Grew / Shrank), each sortable by size delta. Users typically scan for "what grew the most since last scan" — show `grew` sorted by `NewSize - OldSize` desc by default.

## Sources

- [Microsoft Learn — ObservableObject (CommunityToolkit.Mvvm)](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/observableobject) — HIGH confidence, authoritative
- [Microsoft Learn — Putting things together (CommunityToolkit.Mvvm)](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/puttingthingstogether) — HIGH confidence
- [Microsoft Learn — How to: Improve the Performance of a TreeView](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview) — HIGH confidence, official virtualization guidance
- [Microsoft Learn — BindingOperations.EnableCollectionSynchronization](https://learn.microsoft.com/en-us/dotnet/api/system.windows.data.bindingoperations.enablecollectionsynchronization) — HIGH confidence
- [MSDN Magazine — Patterns for Asynchronous MVVM Applications: Data Binding (Stephen Cleary)](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/march/async-programming-patterns-for-asynchronous-mvvm-applications-data-binding) — HIGH confidence, the canonical reference for async MVVM
- [.NET Blog — Async in 4.5: Enabling Progress and Cancellation in Async APIs](https://devblogs.microsoft.com/dotnet/async-in-4-5-enabling-progress-and-cancellation-in-async-apis/) — HIGH confidence, official `IProgress<T>`/`Progress<T>` design rationale
- [Rick Strahl — Async and Async Void Event Handling in WPF](https://weblog.west-wind.com/posts/2022/Apr/22/Async-and-Async-Void-Event-Handling-in-WPF) — MEDIUM confidence, widely-cited practitioner
- [Brian Lagunas — Does Reporting Progress with Task.Run Freeze Your WPF UI?](https://brianlagunas.com/does-reporting-progress-with-task-run-freeze-your-wpf-ui/) — MEDIUM confidence, throttling pattern
- [Syncfusion — 3 Steps to Lazy Load Data in WPF TreeView in MVVM Pattern](https://www.syncfusion.com/blogs/post/3-steps-to-lazy-load-data-in-wpf-treeview-mvvm) — MEDIUM confidence, concrete pattern
- [WPF Tutorial — Lazy loading TreeView items](https://wpf-tutorial.com/treeview-control/lazy-loading-treeview-items/) — MEDIUM confidence
- [Meziantou — Thread-safe observable collection in .NET](https://www.meziantou.net/thread-safe-observable-collection-in-dotnet.htm) — MEDIUM confidence, thread-safety discussion
- [Jeremy Bytes — Dependency Injection Composition Root](https://jeremybytes.blogspot.com/2013/03/dependency-injection-composition-root.html) — MEDIUM confidence, Pure DI explanation (Mark Seemann's pattern)
- [Medium (Shanto462) — Dependency Injection in WPF: A Complete Implementation Guide](https://medium.com/@shanto462/dependency-injection-in-wpf-a-complete-implementation-guide-468abcf95337) — LOW confidence, corroborating pattern
- [Confluent — Schema Evolution and Compatibility](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html) — HIGH confidence, schema versioning principles (domain-general)
- [Ably — The definitive guide to diff algorithms](https://ably.com/blog/practical-guide-to-diff-algorithms) — MEDIUM confidence, confirms dictionary-keyed diff is right tool (not Myers) for unordered data
- [WinDirStat on GitHub](https://github.com/windirstat/windirstat) — HIGH confidence for domain validation (reference implementation exists, MFC/C++)
- PROJECT.md and CLAUDE.md (local project constraints) — HIGH confidence

---
*Architecture research for: Windows WPF disk analyzer (DiskScout)*
*Researched: 2026-04-24*
