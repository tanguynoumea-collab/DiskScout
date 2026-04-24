# CLAUDE.md — DISKSCOUT

## Contexte
Outil Windows d'analyse d'occupation disque pour usage personnel.
Lecture seule — aucune suppression. Analyse le registre pour les programmes installés,
détecte les fichiers rémanents, construit l'arborescence triée par taille.

## Stack
- WPF / C# .NET 8
- CommunityToolkit.Mvvm (MVVM + RelayCommand + ObservableObject)
- System.Text.Json (persistance JSON)
- P/Invoke FindFirstFileEx pour perf scan
- Manifest : requireAdministrator

## Conventions
- Architecture MVVM stricte : Models / Views / ViewModels / Services / Helpers
- PascalCase partout
- async/await + CancellationToken systématique sur opérations longues
- IProgress<T> pour les rapports de progression UI
- Services instanciés par injection manuelle dans App.xaml.cs (pas de conteneur DI externe)
- Pas de code-behind dans les Views sauf événements UI purs (drag/drop, resize)
- Pas de File.Delete ni Directory.Delete dans le code — lecture seule absolue

## Règles métier
- Matching programme <-> dossier AppData : fuzzy match sur Publisher puis DisplayName (Levenshtein > 0.7)
- Dossier considéré orphelin si aucun programme installé ne matche
- Seuil fichiers Temp anciens : 30 jours
- Seuil Program Files vide : < 1 Mo ou uniquement logs

## Déploiement
Portable .exe self-contained single-file :
`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`

## Statut GSD
[Sera rempli après /gsd export en fin de session]

<!-- GSD:project-start source:PROJECT.md -->
## Project

**DiskScout**

Outil Windows desktop (WPF/.NET 8) d'analyse intelligente de l'occupation disque pour usage personnel mono-utilisateur. Scanne les disques fixes en lecture seule et présente trois vues complémentaires : programmes installés (via registre) avec taille réelle, fichiers rémanents de programmes désinstallés (AppData orphelins, Program Files vides, Temp anciens, patches MSI orphelins), et arborescence hiérarchique triée par taille décroissante. Se lance en portable single-file `.exe` sans installation.

**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer. L'utilisateur supprime lui-même ailleurs (Explorateur, PowerShell) après analyse.

### Constraints

- **Tech stack** : WPF + C# .NET 8 + CommunityToolkit.Mvvm + System.Text.Json — aucune dépendance externe runtime, tout embarqué dans le single-file
- **Architecture** : MVVM stricte (Models / Views / ViewModels / Services / Helpers), pas de code-behind sauf événements UI purs (drag/drop, resize)
- **Perf** : scan d'un disque de 500 Go utilisable sous 3 min sur SSD ; UI jamais bloquée (async/await + IProgress partout)
- **Safety** : aucun appel `File.Delete`, `Directory.Delete`, `FileSystem.Delete*` dans le code — lecture seule absolue
- **Robustesse** : `UnauthorizedAccessException`, `DirectoryNotFoundException`, `PathTooLongException`, `IOException` loggés et ignorés, scan continue
- **Privilèges** : manifest `requireAdministrator` obligatoire
- **Dépendances** : injection manuelle dans `App.xaml.cs`, pas de conteneur DI externe
- **Déploiement** : `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- **Logging** : fichier rotatif 5 Mo max dans `%LocalAppData%\DiskScout\diskscout.log`
- **Persistance** : JSON versionné (`schemaVersion: 1`) dans `%LocalAppData%\DiskScout\scans\scan_YYYYMMDD_HHmmss.json`
- **Workflow dev** : après chaque build réussi d'une phase produisant un état lançable, lancer automatiquement le prototype (`dotnet run` ou `./DiskScout.exe`) pour validation visuelle immédiate par l'utilisateur
<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->
## Technology Stack

## Executive Verdict
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
## LOCKED Decisions — Validation Status
| PROJECT.md Decision | Verdict | Evidence |
|---------------------|---------|----------|
| WPF + .NET 8 over WinUI 3 / MAUI | **CONFIRMED** | HIGH — .NET 8 LTS active, WPF hardware-accel in .NET 8, single-file publish stable. |
| CommunityToolkit.Mvvm over Prism/ReactiveUI | **CONFIRMED** | HIGH — 8.4.2 current, Microsoft-maintained, source-gen is the modern pattern. |
| System.Text.Json over Newtonsoft | **CONFIRMED** | HIGH — perf + binary-size win, source-gen for the polymorphic `TreeNode` hierarchy. |
| Manual DI in App.xaml.cs | **CONFIRMED** | HIGH — ≤20 services, explicit wiring is superior DX for this size. |
| Single-file self-contained `win-x64`, `requireAdministrator` | **CONFIRMED** | HIGH — see Publish Command section. |
| P/Invoke `FindFirstFileEx` + `FIND_FIRST_EX_LARGE_FETCH` | **CONFIRMED** | HIGH — MS Learn signature verified; flag value `0x2` documented. |
## UNLOCKED Areas — Prescribed Choices
### Logging → **Serilog 4.2.0 + Serilog.Sinks.File 7.0.0**
### Testing → **xUnit 2.9.2 + FluentAssertions 7.x + Moq 4.20.72**
### CSV Export → **CsvHelper 33.1.0**
### HTML Export → **Scriban 5.10.0** + embedded `.sbn` resource
### Fuzzy Matching (Levenshtein) → **Fastenshtein 1.0.11**
### TreeMap Rendering → **Custom `TreeMapPanel : Panel`** (no library)
### TreeView Virtualization → **VirtualizingStackPanel + Lazy-load children**
### Progress Reporting → **`IProgress<ScanProgress>` + `Progress<T>` with time-throttling**
## P/Invoke — Validated Signatures for .NET 8
### `WIN32_FIND_DATA` struct
### Native imports (LibraryImport source-gen — .NET 8 preferred over DllImport)
- `SafeFindHandle` (SafeHandle subclass) guarantees `FindClose` even under abrupt cancellation → **critical** for `CancellationToken` safety with native handles.
- `FindExInfoBasic` skips the 8.3 alternate filename → measurable speedup.
- `FIND_FIRST_EX_LARGE_FETCH = 0x2` uses a larger buffer for directory queries (per MS Learn; supported Win 7 / WS 2008 R2+).
- Use `\\?\` prefix on long paths (>260 chars) or opt-in to long-path support via manifest (`<longPathAware>true</longPathAware>`).
- Pattern `"C:\\path\\*"` (trailing `\*`) — never trailing `\`.
### Usage pattern
## Single-File Publish — Exact Configuration
### `.csproj`
### `app.manifest`
### Publish Command (exact, validated)
- `IncludeNativeLibrariesForSelfExtract=true` is **mandatory** — WPF has native libs (`PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`). Without this flag you get a folder of loose DLLs, not a single file.
- `IncludeAllContentForSelfExtract=true` embeds runtime config + deps.json + satellite assemblies — needed for a truly portable single file.
- `PublishTrimmed=true` **breaks WPF** — XAML parser uses reflection on VM property names.
- `PublishReadyToRun=true` is optional; improves cold-start by ~300 ms at a ~15 MB size cost.
- Override extraction dir with env var `DOTNET_BUNDLE_EXTRACT_BASE_DIR` if `%TEMP%` is policy-restricted.
## Installation Commands
# Create project
# Core (validated 2026-04-24)
# Logging
# Export
# Fuzzy matching
# Test project
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
## Stack Patterns by Variant
- `\\server\share\path\*` pattern works with `FindFirstFileEx` (supported per MS Learn).
- But cannot use `\\server\share` directly — must have a subdirectory.
- Prefix `\\?\UNC\server\share\path\*` for long paths.
- Catch `ERROR_ACCESS_DENIED` (5) / `ERROR_FILE_NOT_FOUND` (2) / `ERROR_PATH_NOT_FOUND` (3) from `GetLastWin32Error()` after invalid handle.
- Log at `Information` level, return empty enumeration, continue.
- Never throw from the P/Invoke enumerator — scan resilience is product-critical.
- `Parallel.ForEach(rootDirs, opts, dir => ScanRecursive(dir, ...))` — use `ParallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount`.
- Do **NOT** parallelize per-directory recursively: spawns thousands of handles, no speedup on single SSD (I/O bound). Root-level only.
- Aggregate with `ConcurrentBag<TreeNode>` or a `Channel<T>` producer/consumer.
- Ensure `<longPathAware>true</longPathAware>` in manifest **and** Windows 10 1607+ long-path reg key enabled OR prefix all paths with `\\?\`.
- `Path.GetFullPath` in .NET 8 handles long paths transparently on supported Windows.
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
## Development Tools
| Tool | Purpose | Notes |
|------|---------|-------|
| **Visual Studio 2022 17.11+** | Primary IDE | XAML designer, WPF live visual tree, test explorer. |
| **Rider 2025.x** | Alternative IDE | Faster XAML indexing than VS; no WPF designer. |
| **dotnet-format** | Auto-formatting | `dotnet tool install -g dotnet-format`; enforce via pre-commit. |
| **.editorconfig** | Style enforcement | Align with MS defaults + `csharp_new_line_before_open_brace = all`. |
| **BenchmarkDotNet** | Perf verification | Only if measuring P/Invoke vs. managed enumeration — add to a separate `DiskScout.Benchmarks` project, never to shipped binary. |
| **Dependabot / NuGet.Central.Package.Management** | Dependency updates | Optional, personal project scale. |
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
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
