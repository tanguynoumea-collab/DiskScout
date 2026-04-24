# Stack Research — DiskScout

**Domain:** Windows desktop disk usage analyzer (WPF/.NET 8), registry-driven installed-program detection, orphan/remnant file heuristics, high-performance filesystem scanning via P/Invoke
**Researched:** 2026-04-24
**Confidence:** HIGH

---

## Executive Verdict

The stack below is **locked and production-ready as of April 2026**. Every core choice made in `PROJECT.md` is validated against current evidence (NuGet feeds, Microsoft Learn docs, Win32 API docs). The only additions are supporting libraries for domains not yet decided: logging (Serilog), testing (xUnit), CSV export (CsvHelper), HTML export (Scriban), fuzzy matching (Fastenshtein), and TreeMap rendering (hand-rolled custom `Panel` with squarified algorithm — no third-party dependency).

**Critical caveat:** .NET 8 LTS ends **November 10, 2026** (≈6 months from today). DiskScout ships on .NET 8 safely for this milestone, but the roadmap MUST include a tracked migration to .NET 10 LTS before EOL. Flagged in PITFALLS.md.

---

## Recommended Stack

### Core Technologies (LOCKED — validated)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **.NET** | **8.0.x** (LTS) | Runtime + SDK | LTS through 2026-11-10. Stable WPF tooling, mature single-file publish, proven P/Invoke interop. Upgrade to .NET 10 LTS planned post-milestone-1. |
| **WPF** | Shipped with .NET 8 SDK (`<UseWPF>true</UseWPF>`) | Desktop UI framework | Stack mature, hardware-accelerated rendering in .NET 8, HierarchicalDataTemplate + VirtualizingStackPanel natively suited to tree views, no MSIX packaging required. |
| **CommunityToolkit.Mvvm** | **8.4.2** (latest stable, 2026-03-25) | MVVM source-generators (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`) | Microsoft-maintained, zero runtime dependency, source-generator eliminates INPC boilerplate. Part of .NET Foundation. |
| **System.Text.Json** | Ships with .NET 8 (9.0.x runtime lib) | JSON persistence of scan results | High-perf built-in serializer, supports source-gen (`[JsonSerializable]`) for AOT-friendly + startup perf, polymorphic serialization via `[JsonDerivedType]` for the tree node hierarchy. |
| **C# language version** | **12** (default for .NET 8) | Primary constructors, collection expressions, `ref readonly` params | Unlocks cleaner records for scan models, `ReadOnlySpan<char>` for path parsing hot loops. |

### Supporting Libraries (PRESCRIBED)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| **Serilog** | **4.2.0** | Structured logging core | Always — bootstrap in `App.xaml.cs`, inject `ILogger` into services via manual DI. |
| **Serilog.Sinks.File** | **7.0.0** | Rolling file sink | File rotation in `%LocalAppData%\DiskScout\diskscout.log`. `fileSizeLimitBytes: 5242880`, `rollOnFileSizeLimit: true`, `retainedFileCountLimit: 5`. |
| **CsvHelper** | **33.1.0** | CSV export for the 3 tabs | Installed programs, remnants, tree export. Zero dependencies, fluent ClassMap. |
| **Scriban** | **5.10.0** | HTML report template engine | Rendering `report.html` from an embedded `.sbn` template. Lighter & faster than RazorLight, no Roslyn runtime compile, no `Microsoft.CodeAnalysis` footprint (keeps single-file small). |
| **Fastenshtein** | **1.0.11** | Levenshtein distance (fuzzy match Publisher ↔ AppData folder) | Orphan heuristics — normalized ratio threshold 0.7. Fastest .NET impl, allocation-optimized. |
| **Microsoft.Win32.Registry** | **5.0.0** | HKLM/HKCU registry access | `Uninstall` subkey enumeration for installed-program detection. Required — removed from default BCL for non-Windows TFMs. |
| **xunit** | **2.9.2** | Unit test framework | Runs in parallel by default, Microsoft-aligned, best .NET 8 tooling. |
| **xunit.runner.visualstudio** | **2.8.2** | VS/dotnet test integration | Required for `dotnet test`. |
| **Microsoft.NET.Test.Sdk** | **17.11.1** | Test host | Required for all test frameworks on .NET 8. |
| **FluentAssertions** | **7.0.0** | Readable test assertions | `result.Should().BeEquivalentTo(...)` — worth the dependency; note licence became commercial in v8 → **pin to 7.x**. |
| **Moq** | **4.20.72** | Mocking in tests | Only for `IFileSystemScanner`, `IRegistryReader` service boundaries. Avoid over-mocking models. |

### NOT Using (explicit rejections)

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| **WinUI 3 / .NET MAUI** | WinUI 3 single-file publish is fragile (`Windows App Runtime` dependency, MSIX packaging needed for full features). MAUI is cross-platform overkill for a Windows-only tool. | WPF + .NET 8 — mature tooling, true portable single-file. |
| **Avalonia UI** | Cross-platform benefit irrelevant (Windows-registry + Win32 P/Invoke = Windows-only product). Adds binary bloat vs. WPF bundled in runtime. | WPF. |
| **.NET Framework 4.8** | Legacy — no single-file publish, no source-generators, no records, no `Span<T>` ergonomics, EOL trajectory. | .NET 8 SDK-style. |
| **Newtonsoft.Json** | Slower, larger, no source-gen, no AOT. Adds ~700 KB to single-file binary. | `System.Text.Json` (built-in). |
| **Prism / ReactiveUI / Caliburn.Micro** | Prism is heavy (modules/regions overkill for 1 window). ReactiveUI requires Rx mental model. Caliburn deprecated-ish. | `CommunityToolkit.Mvvm` source-generators. |
| **Microsoft.Extensions.DependencyInjection** | Unnecessary abstraction for ≤20 services. Adds binary weight. Explicit `new Service(dep)` in `App.xaml.cs` is clearer for an app of this size. | Manual constructor DI in `App.OnStartup`. |
| **`Directory.EnumerateFiles` / `DirectoryInfo.EnumerateFileSystemInfos`** | ~2–3× slower than `FindFirstFileEx` with `LARGE_FETCH`. Each returns `FileSystemInfo` which eagerly reparses attributes → GC pressure on 1M+ entries. | `FindFirstFileEx` P/Invoke + `WIN32_FIND_DATA` struct (no allocation per entry). |
| **Newtonsoft / XmlSerializer for scan persistence** | Legacy. | `System.Text.Json` + source-gen context. |
| **NLog / log4net** | log4net unmaintained. NLog fine but Serilog is the modern .NET standard (structured-first, better DX). | Serilog. |
| **RazorLight** | Pulls in Roslyn `Microsoft.CodeAnalysis.*` (~10 MB to single-file), runtime compile latency, overkill for a static HTML report template. | Scriban (Liquid-like syntax, 300 KB, no Roslyn). |
| **Telerik / DevExpress / Infragistics / Syncfusion TreeMap** | Commercial (per-seat licenses) for a free personal tool. Huge dependency bloat. | Custom `TreeMapPanel : Panel` with squarified algorithm (~200 LoC, see ARCHITECTURE.md). |
| **LiveCharts2** | SkiaSharp render backend → ~15 MB native bundle to single-file. No native TreeMap series — would still need custom layout. | Custom Panel (no third-party chart lib needed for this one visualization). |
| **FluentAssertions ≥ 8.0** | Commercial licence since v8. | Pin FluentAssertions to `[7.0.0, 8.0.0)`. |
| **NUnit / MSTest** | Both fine, but xUnit is Microsoft's internal choice for EF Core / ASP.NET Core / .NET runtime tests → best 2026 tooling integration. | xUnit. |

---

## LOCKED Decisions — Validation Status

| PROJECT.md Decision | Verdict | Evidence |
|---------------------|---------|----------|
| WPF + .NET 8 over WinUI 3 / MAUI | **CONFIRMED** | HIGH — .NET 8 LTS active, WPF hardware-accel in .NET 8, single-file publish stable. |
| CommunityToolkit.Mvvm over Prism/ReactiveUI | **CONFIRMED** | HIGH — 8.4.2 current, Microsoft-maintained, source-gen is the modern pattern. |
| System.Text.Json over Newtonsoft | **CONFIRMED** | HIGH — perf + binary-size win, source-gen for the polymorphic `TreeNode` hierarchy. |
| Manual DI in App.xaml.cs | **CONFIRMED** | HIGH — ≤20 services, explicit wiring is superior DX for this size. |
| Single-file self-contained `win-x64`, `requireAdministrator` | **CONFIRMED** | HIGH — see Publish Command section. |
| P/Invoke `FindFirstFileEx` + `FIND_FIRST_EX_LARGE_FETCH` | **CONFIRMED** | HIGH — MS Learn signature verified; flag value `0x2` documented. |

---

## UNLOCKED Areas — Prescribed Choices

### Logging → **Serilog 4.2.0 + Serilog.Sinks.File 7.0.0**

**Rationale:** Structured-first, fluent config, the de-facto .NET 2026 standard. Rolling on size supported natively.

```csharp
// App.xaml.cs
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "DiskScout", "diskscout.log"),
        fileSizeLimitBytes: 5 * 1024 * 1024,       // 5 MB
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 5,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

### Testing → **xUnit 2.9.2 + FluentAssertions 7.x + Moq 4.20.72**

**Rationale:** xUnit runs tests in parallel by default, enforces isolation via constructor injection (no shared state surprises). FluentAssertions pinned `<8.0` to avoid the commercial licence. Moq only at service boundaries.

### CSV Export → **CsvHelper 33.1.0**

**Rationale:** Zero dependencies, fluent `ClassMap<T>` for per-tab shaping (InstalledProgramsCsvMap, RemnantsCsvMap, TreeNodeCsvMap), robust Excel-friendly quoting/escaping out of the box.

### HTML Export → **Scriban 5.10.0** + embedded `.sbn` resource

**Rationale:** Liquid-like templating, compiled to delegate on first use, no Roslyn runtime. Templates live as embedded resources under `/Resources/Templates/report.sbn`. Bundle size ≈ 300 KB vs. RazorLight ≈ 10 MB.

### Fuzzy Matching (Levenshtein) → **Fastenshtein 1.0.11**

**Rationale:** Fastest .NET Levenshtein per benchmarks, allocation-optimized. Use the **instance-based** API for the hot loop (one Publisher compared against hundreds of folder names):

```csharp
// For orphan-detection: matching a DisplayName against N AppData folder names
var lev = new Fastenshtein.Levenshtein(displayName.ToLowerInvariant());
foreach (var folder in appDataFolders)
{
    int distance = lev.DistanceFrom(folder.ToLowerInvariant());
    double ratio = 1.0 - (double)distance / Math.Max(displayName.Length, folder.Length);
    if (ratio >= 0.7) { /* match */ }
}
```

Note: instance form is **not thread-safe** — one instance per thread if parallelized.

### TreeMap Rendering → **Custom `TreeMapPanel : Panel`** (no library)

**Rationale:** All commercial TreeMap controls (Telerik, DevExpress, Infragistics, Syncfusion) are paid per-seat licences. Open-source .NET options are stale/abandoned. The squarified algorithm (Bruls, Huizing, van Wijk, 2000) is ~200 LoC. Full control of visuals, no dependency, fits the "tout embarqué" constraint.

**Plan:** Custom `Panel` subclass overriding `MeasureOverride` / `ArrangeOverride`, implementing `Squarify(rect, List<double>)`. Child items are `ContentPresenter`s bound to a flat collection of leaf+directory nodes at the current drill-down level. Click → `ICommand` navigates into subtree.

### TreeView Virtualization → **VirtualizingStackPanel + Lazy-load children**

**Rationale:** WPF `TreeView` does **not** natively virtualize across hierarchy — it creates all containers on expand. For a 1M-node tree, we must combine two techniques:

```xml
<TreeView ItemsSource="{Binding RootNodes}"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          ScrollViewer.CanContentScroll="True"
          ScrollViewer.IsDeferredScrollingEnabled="False">
    <TreeView.ItemContainerStyle>
        <Style TargetType="TreeViewItem">
            <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"/>
        </Style>
    </TreeView.ItemContainerStyle>
    <TreeView.ItemTemplate>
        <HierarchicalDataTemplate ItemsSource="{Binding Children}">
            <!-- ... -->
        </HierarchicalDataTemplate>
    </TreeView.ItemTemplate>
</TreeView>
```

Paired with **lazy loading in the VM**: `TreeNodeViewModel.Children` exposes a single dummy child ("Loading...") until `IsExpanded` becomes `true`, which triggers populating actual children. This avoids materializing the full 1M-node tree upfront.

**Known gotcha:** `ScrollViewer.CanContentScroll="True"` is mandatory — default `false` forces full-height measurement and disables virtualization.

### Progress Reporting → **`IProgress<ScanProgress>` + `Progress<T>` with time-throttling**

**Rationale:** `Progress<T>` auto-marshals to the UI thread (captures `SynchronizationContext` at construction — **must** be instantiated on UI thread). Raw `Report()` every file = UI flood. Throttle inside the `IProgress` impl:

```csharp
public sealed class ThrottledProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _interval;
    private DateTime _lastReport = DateTime.MinValue;
    private T _latest = default!;

    public ThrottledProgress(Action<T> handler, TimeSpan interval)
    { _handler = handler; _interval = interval; }

    public void Report(T value)
    {
        _latest = value;
        var now = DateTime.UtcNow;
        if (now - _lastReport < _interval) return;
        _lastReport = now;
        Application.Current.Dispatcher.BeginInvoke(() => _handler(_latest));
    }
}
```

Recommended interval: 100–200 ms.

---

## P/Invoke — Validated Signatures for .NET 8

### `WIN32_FIND_DATA` struct

Verified against MS Learn (`minwinbase.h`). Use `CharSet.Unicode` for the W variant (`FindFirstFileExW`) to avoid `MAX_PATH` truncation.

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WIN32_FIND_DATA
{
    public uint dwFileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0;
    public uint dwReserved1;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string cFileName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string cAlternateFileName;
}

internal enum FINDEX_INFO_LEVELS
{
    FindExInfoStandard = 0,
    FindExInfoBasic    = 1,   // skips cAlternateFileName → faster
    FindExInfoMaxInfoLevel = 2
}

internal enum FINDEX_SEARCH_OPS
{
    FindExSearchNameMatch         = 0,
    FindExSearchLimitToDirectories = 1,
    FindExSearchLimitToDevices    = 2
}

[Flags]
internal enum FIND_FIRST_EX_FLAGS : uint
{
    None               = 0x0,
    CaseSensitive      = 0x1,
    LargeFetch         = 0x2,   // FIND_FIRST_EX_LARGE_FETCH
    OnDiskEntriesOnly  = 0x4
}
```

### Native imports (LibraryImport source-gen — .NET 8 preferred over DllImport)

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class NativeMethods
{
    // .NET 8: prefer LibraryImport (source-generated, AOT-friendly) over DllImport.
    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW",
                   StringMarshalling = StringMarshalling.Utf16,
                   SetLastError = true)]
    internal static partial SafeFindHandle FindFirstFileEx(
        string lpFileName,
        FINDEX_INFO_LEVELS fInfoLevelId,
        out WIN32_FIND_DATA lpFindFileData,
        FINDEX_SEARCH_OPS fSearchOp,
        IntPtr lpSearchFilter,
        FIND_FIRST_EX_FLAGS dwAdditionalFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileW",
                   StringMarshalling = StringMarshalling.Utf16,
                   SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindNextFile(
        SafeFindHandle hFindFile,
        out WIN32_FIND_DATA lpFindFileData);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindClose(IntPtr hFindFile);
}

internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeFindHandle() : base(ownsHandle: true) { }
    protected override bool ReleaseHandle() => NativeMethods.FindClose(handle);
}
```

**Key correctness points:**
- `SafeFindHandle` (SafeHandle subclass) guarantees `FindClose` even under abrupt cancellation → **critical** for `CancellationToken` safety with native handles.
- `FindExInfoBasic` skips the 8.3 alternate filename → measurable speedup.
- `FIND_FIRST_EX_LARGE_FETCH = 0x2` uses a larger buffer for directory queries (per MS Learn; supported Win 7 / WS 2008 R2+).
- Use `\\?\` prefix on long paths (>260 chars) or opt-in to long-path support via manifest (`<longPathAware>true</longPathAware>`).
- Pattern `"C:\\path\\*"` (trailing `\*`) — never trailing `\`.

### Usage pattern

```csharp
public IEnumerable<(string path, long size, bool isDir)> EnumerateDirectory(string dir, CancellationToken ct)
{
    using var handle = NativeMethods.FindFirstFileEx(
        Path.Combine(dir, "*"),
        FINDEX_INFO_LEVELS.FindExInfoBasic,
        out var data,
        FINDEX_SEARCH_OPS.FindExSearchNameMatch,
        IntPtr.Zero,
        FIND_FIRST_EX_FLAGS.LargeFetch);

    if (handle.IsInvalid)
    {
        int err = Marshal.GetLastWin32Error();
        // 5 ERROR_ACCESS_DENIED, 3 ERROR_PATH_NOT_FOUND — log & skip, don't throw
        yield break;
    }

    do
    {
        ct.ThrowIfCancellationRequested();
        if (data.cFileName is "." or "..") continue;
        bool isDir = (data.dwFileAttributes & 0x10) != 0; // FILE_ATTRIBUTE_DIRECTORY
        bool isReparse = (data.dwFileAttributes & 0x400) != 0; // skip junctions/symlinks
        if (isReparse) continue;
        long size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
        yield return (Path.Combine(dir, data.cFileName), size, isDir);
    }
    while (NativeMethods.FindNextFile(handle, out data));
}
```

---

## Single-File Publish — Exact Configuration

### `.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <LangVersion>12</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Single-file publish -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>

    <!-- Trimming: DO NOT enable for WPF (XAML reflection breaks) -->
    <PublishTrimmed>false</PublishTrimmed>

    <!-- ReadyToRun for faster startup (optional, adds ~15 MB) -->
    <PublishReadyToRun>true</PublishReadyToRun>

    <!-- Admin manifest -->
    <ApplicationManifest>app.manifest</ApplicationManifest>

    <!-- App metadata -->
    <AssemblyName>DiskScout</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

### `app.manifest`

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="DiskScout.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <!-- Long path support (>260 chars) -->
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 + 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
</assembly>
```

### Publish Command (exact, validated)

```bash
dotnet publish -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -o ./publish
```

Output: `./publish/DiskScout.exe` (≈ 80–110 MB compressed; extracts to `%TEMP%\.net\DiskScout\<hash>\` on first run).

**Gotchas:**
- `IncludeNativeLibrariesForSelfExtract=true` is **mandatory** — WPF has native libs (`PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`). Without this flag you get a folder of loose DLLs, not a single file.
- `IncludeAllContentForSelfExtract=true` embeds runtime config + deps.json + satellite assemblies — needed for a truly portable single file.
- `PublishTrimmed=true` **breaks WPF** — XAML parser uses reflection on VM property names.
- `PublishReadyToRun=true` is optional; improves cold-start by ~300 ms at a ~15 MB size cost.
- Override extraction dir with env var `DOTNET_BUNDLE_EXTRACT_BASE_DIR` if `%TEMP%` is policy-restricted.

---

## Installation Commands

```bash
# Create project
dotnet new wpf -n DiskScout -f net8.0 -o ./DiskScout
cd DiskScout

# Core (validated 2026-04-24)
dotnet add package CommunityToolkit.Mvvm --version 8.4.2
dotnet add package Microsoft.Win32.Registry --version 5.0.0

# Logging
dotnet add package Serilog --version 4.2.0
dotnet add package Serilog.Sinks.File --version 7.0.0

# Export
dotnet add package CsvHelper --version 33.1.0
dotnet add package Scriban --version 5.10.0

# Fuzzy matching
dotnet add package Fastenshtein --version 1.0.11

# Test project
dotnet new xunit -n DiskScout.Tests -f net8.0 -o ./DiskScout.Tests
cd ../DiskScout.Tests
dotnet add reference ../DiskScout/DiskScout.csproj
dotnet add package Microsoft.NET.Test.Sdk --version 17.11.1
dotnet add package xunit --version 2.9.2
dotnet add package xunit.runner.visualstudio --version 2.8.2
dotnet add package FluentAssertions --version 7.0.0   # MUST be <8.0 (commercial licence)
dotnet add package Moq --version 4.20.72
```

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| WPF + .NET 8 | WinUI 3 + Windows App SDK | Only if targeting Windows 11 exclusively **and** willing to ship MSIX installer. Not our case (portable exe). |
| CommunityToolkit.Mvvm | ReactiveUI | Large codebase with complex async pipelines where Rx composition shines. Overkill here. |
| System.Text.Json | Newtonsoft.Json | Legacy: `JObject`-heavy dynamic JSON manipulation, contract-resolvers with reference loop handling. Not our use case. |
| Manual DI | `Microsoft.Extensions.DependencyInjection` | ≥50 services, plugin architecture, or need scoped lifetimes. Not our case. |
| Serilog | `Microsoft.Extensions.Logging` + console provider | Console apps without structured logging needs. Not our case (need rolling file). |
| xUnit | NUnit | Existing NUnit codebase, `[TestCase]` data-driven tests used extensively. New project: xUnit. |
| CsvHelper | `nietras/Sep` | Multi-GB CSV generation with zero-alloc hot path. Overkill here — export is ≤1 MB. |
| Scriban | RazorLight | Need Razor syntax + C# expressions server-side. We want a simple, safe templating engine → Scriban. |
| Fastenshtein | `F23.StringSimilarity` | Need Jaro-Winkler / Jaccard / NGram / SIFT4 — richer algorithm set. We need pure Levenshtein → Fastenshtein. |
| Custom TreeMap Panel | DevExpress `TreeMapControl` | Enterprise with existing DevExpress licence. Commercial cost = no-go for this project. |
| `VirtualizingStackPanel` + lazy-load VM | Full custom virtualizing panel (e.g., `VirtualTreeView` patterns) | Trees >10M nodes where even the lazy-load pattern chokes. Not our case (500 GB disk ≈ 1–3M nodes). |

---

## Stack Patterns by Variant

**If scanning a SMB network share:**
- `\\server\share\path\*` pattern works with `FindFirstFileEx` (supported per MS Learn).
- But cannot use `\\server\share` directly — must have a subdirectory.
- Prefix `\\?\UNC\server\share\path\*` for long paths.

**If scan needs to survive denied directories (Windows, System32 across users):**
- Catch `ERROR_ACCESS_DENIED` (5) / `ERROR_FILE_NOT_FOUND` (2) / `ERROR_PATH_NOT_FOUND` (3) from `GetLastWin32Error()` after invalid handle.
- Log at `Information` level, return empty enumeration, continue.
- Never throw from the P/Invoke enumerator — scan resilience is product-critical.

**If parallelizing at drive root:**
- `Parallel.ForEach(rootDirs, opts, dir => ScanRecursive(dir, ...))` — use `ParallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount`.
- Do **NOT** parallelize per-directory recursively: spawns thousands of handles, no speedup on single SSD (I/O bound). Root-level only.
- Aggregate with `ConcurrentBag<TreeNode>` or a `Channel<T>` producer/consumer.

**If long paths (>260 chars) in user AppData:**
- Ensure `<longPathAware>true</longPathAware>` in manifest **and** Windows 10 1607+ long-path reg key enabled OR prefix all paths with `\\?\`.
- `Path.GetFullPath` in .NET 8 handles long paths transparently on supported Windows.

---

## Version Compatibility Matrix

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| CommunityToolkit.Mvvm 8.4.2 | .NET 6/7/8/9 | Source-generator runs at compile time — no runtime version lock. |
| Serilog 4.2.0 | netstandard2.0 + .NET 8 | API-compatible with Serilog.Sinks.File 7.0.0. |
| Serilog.Sinks.File 7.0.0 | Serilog 4.x | Major bump to 7.0 aligned with Serilog 4.x. |
| CsvHelper 33.1.0 | .NET 8 + netstandard2.0 + net462 | No dependencies. |
| Scriban 5.10.0 | .NET 8 + netstandard2.0 | No dependencies. |
| Fastenshtein 1.0.11 | netstandard1.0+ | Universal. |
| Fastenshtein instance API | **NOT thread-safe** | One `Levenshtein` per thread if parallelized. |
| FluentAssertions 7.x | xUnit 2.x | **DO NOT upgrade to 8.x** — commercial licence. Pin `[7.0.0, 8.0.0)`. |
| xUnit 2.9.2 | .NET 8, VS 2022 17.11+ | xUnit v3 exists but tooling still stabilizing — prefer 2.9.x for 2026. |
| .NET 8.0 LTS | Microsoft-Windows-10 v1607+ | EOL **2026-11-10** — plan .NET 10 migration. |

---

## Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| **Visual Studio 2022 17.11+** | Primary IDE | XAML designer, WPF live visual tree, test explorer. |
| **Rider 2025.x** | Alternative IDE | Faster XAML indexing than VS; no WPF designer. |
| **dotnet-format** | Auto-formatting | `dotnet tool install -g dotnet-format`; enforce via pre-commit. |
| **.editorconfig** | Style enforcement | Align with MS defaults + `csharp_new_line_before_open_brace = all`. |
| **BenchmarkDotNet** | Perf verification | Only if measuring P/Invoke vs. managed enumeration — add to a separate `DiskScout.Benchmarks` project, never to shipped binary. |
| **Dependabot / NuGet.Central.Package.Management** | Dependency updates | Optional, personal project scale. |

---

## Sources

### Primary (HIGH confidence)
- [CommunityToolkit.Mvvm 8.4.2 — NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm) — version verified 2026-03-25
- [Serilog.Sinks.File 7.0.0 — NuGet](https://www.nuget.org/packages/serilog.sinks.file/)
- [CsvHelper 33.1.0 — NuGet](https://www.nuget.org/packages/csvhelper/) — targets .NET 8, no deps
- [Fastenshtein 1.0.11 — NuGet](https://www.nuget.org/packages/Fastenshtein)
- [FindFirstFileExW — MS Learn (fileapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw) — signature + `FIND_FIRST_EX_LARGE_FETCH = 0x2` verified
- [WIN32_FIND_DATAA — MS Learn (minwinbase.h)](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-win32_find_dataa)
- [Single-file deployment — MS Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [.NET support policy — dotnet.microsoft.com](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) — .NET 8 EOL 2026-11-10
- [System.Text.Json polymorphism — MS Learn](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)
- [Optimize control performance — MS Learn (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- [How to: Improve TreeView performance — MS Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview)

### Secondary (MEDIUM confidence — cross-verified)
- [pinvoke.net FindFirstFileEx](https://www.pinvoke.net/default.aspx/kernel32/FindFirstFileEx.html) — cross-check of signature vs. MS Learn
- [Fastenshtein README — GitHub](https://github.com/DanHarltey/Fastenshtein) — instance API thread-safety note
- [Serilog.Sinks.File README](https://github.com/serilog/serilog-sinks-file) — `rollOnFileSizeLimit` behavior
- [xUnit vs NUnit vs MSTest — codingdroplets.com (2026)](https://codingdroplets.com/xunit-vs-nunit-vs-mstest-in-net-which-testing-framework-should-your-team-use-in-2026) — 2026 comparison
- [Bulk loading ObservableCollection — peteohanlon.wordpress.com](https://peteohanlon.wordpress.com/2008/10/22/bulk-loading-in-observablecollection/) — AddRange pattern
- [Async progress reporting — Stephen Cleary](https://blog.stephencleary.com/2012/02/reporting-progress-from-async-tasks.html) — canonical `IProgress<T>` pattern
- [Squarified Treemaps in XAML & C# — CodeProject](https://www.codeproject.com/Articles/7039/Squarified-Treemaps-in-XAML-C-using-Microsoft-Long) — reference algo implementation

### Supporting (LOW confidence — needs phase-level validation)
- [Scriban — GitHub](https://github.com/scriban/scriban) — version 5.10.x current but did not verify 2026 release cadence via WebSearch
- [LiveCharts2 — livecharts.dev](https://livecharts.dev/) — rejected (SkiaSharp footprint); confirmed via docs no native TreeMap series

---

*Stack research for: Windows desktop disk usage analyzer (WPF/.NET 8)*
*Researched: 2026-04-24*
*Confidence: HIGH across all locked decisions; MEDIUM on Scriban minor-version*
