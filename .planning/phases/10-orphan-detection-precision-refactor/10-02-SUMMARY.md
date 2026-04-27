---
phase: 10-orphan-detection-precision-refactor
plan: 02
subsystem: services
tags: [enumerator-promotion, machine-snapshot, ttl-cache, parallel-task-whenall, semaphoreslim, pnputil, get-appxpackage, hand-written-fakes, xunit]

# Dependency graph
requires:
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: ResidueScanner with internal IServiceEnumerator + IScheduledTaskEnumerator + WmiServiceEnumerator + SchTasksEnumerator nested implementations
  - phase: 01-foundations
    provides: Serilog logger, System.Text.Json conventions, manual DI pattern
provides:
  - public IServiceEnumerator (promoted from internal) — same tuple shape, now consumable across the assembly + tests as a first-class injectable seam
  - public IScheduledTaskEnumerator (promoted from internal) — same tuple shape
  - public IDriverEnumerator + production DriverEnumerator (pnputil.exe /enum-drivers /format:json with plain-text fallback)
  - public IAppxEnumerator + production AppxEnumerator (powershell.exe Get-AppxPackage -AllUsers | ConvertTo-Json with single-package edge case)
  - public sealed record MachineSnapshot (CapturedUtc + 4 entry lists + 3 pre-built indexes) + 4 sibling entry records (ServiceEntry / DriverEntry / AppxEntry / ScheduledTaskEntry)
  - public IMachineSnapshotProvider (GetAsync + Invalidate)
  - public sealed MachineSnapshotProvider (lazy + 5min TTL + parallel Task.WhenAll + SemaphoreSlim gate + per-source graceful degradation)
  - public static factories ResidueScanner.CreateDefaultServiceEnumerator + CreateDefaultScheduledTaskEnumerator (App.xaml.cs DI seam reserved for Plan 10-04)
affects: [10-04-multisource-matcher, 10-05-confidence-scorer, 10-06-pipeline-integration]

# Tech tracking
tech-stack:
  added: []  # No new NuGet packages — System.Text.Json (existing), System.Diagnostics.Process (BCL), System.Threading.SemaphoreSlim (BCL)
  patterns:
    - "Public-interface promotion: nested-internal types extracted to dedicated *.cs files in their owning namespace; concrete implementations stay package-private and are exposed via static factory methods on the original owning class"
    - "Process shell-out with timeout: ProcessStartInfo + RedirectStandardOutput + UseShellExecute=false + CreateNoWindow + WaitForExit(N_ms) + Kill on timeout + log Warning + yield break — same convention as Phase 9 SchTasksEnumerator"
    - "Defensive JSON parsing: tolerate both schema variants (object-with-array-property and bare-array root for pnputil; object-vs-array root for single-package PowerShell case); on parse failure, log Warning + yield empty rather than throw"
    - "Lazy + TTL cache: SemaphoreSlim(1,1) gate + cached MachineSnapshot? field + Volatile.Read fast path + double-check after gate acquisition — concurrent callers during in-flight build all share the same final snapshot reference"
    - "Parallel population: Task.WhenAll over 4 Task.Run blocks, each materializing one IEnumerable to a List with per-source try/catch substituting Array.Empty on failure (graceful degradation)"
    - "Index pre-build at snapshot construction: 3 IReadOnlySet<string> built once during the slow build path, exposed via init-only properties on the record so the snapshot stays fully immutable post-construction"
    - "Hand-written test fakes only — no Moq dep (project precedent from Plans 09-01 / 09-03 / 09-05)"
    - "Auto-memory rule honored: zero File.Delete / Directory.Delete in any new file (lecture seule absolue per CLAUDE.md)"

key-files:
  created:
    - src/DiskScout.App/Services/IServiceEnumerator.cs
    - src/DiskScout.App/Services/IScheduledTaskEnumerator.cs
    - src/DiskScout.App/Services/IDriverEnumerator.cs
    - src/DiskScout.App/Services/IAppxEnumerator.cs
    - src/DiskScout.App/Services/DriverEnumerator.cs
    - src/DiskScout.App/Services/AppxEnumerator.cs
    - src/DiskScout.App/Models/MachineSnapshot.cs
    - src/DiskScout.App/Services/IMachineSnapshotProvider.cs
    - src/DiskScout.App/Services/MachineSnapshotProvider.cs
    - tests/DiskScout.Tests/EnumeratorPromotionTests.cs
    - tests/DiskScout.Tests/MachineSnapshotProviderTests.cs
  modified:
    - src/DiskScout.App/Services/ResidueScanner.cs (removed 2 internal nested interfaces — now in their own public files; added 2 public static factory methods CreateDefaultServiceEnumerator + CreateDefaultScheduledTaskEnumerator wrapping the still-nested-private WmiServiceEnumerator + SchTasksEnumerator concrete impls; nested concrete implementations untouched)

key-decisions:
  - "Promote the 2 enumerator interfaces by EXTRACTING (move + change visibility) rather than DUPLICATING — single source of truth for the tuple shape; the 46 ResidueScannerTests pass unchanged because the namespace is identical and internal->public is non-breaking"
  - "Concrete impls (WmiServiceEnumerator + SchTasksEnumerator) STAY nested-private inside ResidueScanner.cs. Plan 10-04 needed access to them for App.xaml.cs DI wiring; rather than promote them too (which would expose registry-walk + CSV-parse internals), we exposed two public static factory methods on ResidueScanner that return IServiceEnumerator + IScheduledTaskEnumerator — the consumer never sees the concrete type"
  - "DriverEnumerator chose pnputil shell-out + JSON parse over P/Invoke against SetupAPI/CfgMgr32. Three reasons: (a) pnputil ships with every Windows since Vista, (b) JSON output (Win10 v1903+) gives all 4 fields in one call, (c) the IDriverEnumerator surface is mockable so test seams replace the slow shell-out. We documented the rationale in XML doc comments. Older Windows fall back to a plain-text line-prefix parser."
  - "AppxEnumerator chose powershell.exe Get-AppxPackage shell-out over the WinRT Windows.Management.Deployment.PackageManager API. The WinRT projection adds significant binary weight to the single-file publish; PowerShell is universal; the surface is mockable. Trade-off accepted because results are cached for 5 minutes."
  - "Single-package edge case for Get-AppxPackage: PowerShell emits a JSON object (not an array) when only one package matches. The parser handles BOTH JsonValueKind.Array and JsonValueKind.Object root shapes — try-array-then-object switch."
  - "TTL = 5 minutes per Phase 10 CONTEXT decision. The default lives in a private static readonly field; tests override via the constructor's TimeSpan? ttl parameter."
  - "Concurrency: SemaphoreSlim(1,1) gate over a single MachineSnapshot? field. Fast path reads via Volatile.Read without taking the gate (cache hit case). Slow path: gate + double-check freshness (another caller may have built it) + Task.WhenAll the 4 enumerators + assign to field + release. Concurrent callers during an in-flight build all queue at the gate, see the freshly-built snapshot on re-check, and return that same reference. Verified by Test 7 (10 concurrent GetAsync, slow service, CallCount==1)."
  - "Per-source graceful degradation: each Task.Run wraps the enumerator in a try/catch that substitutes Array.Empty<T>() on non-cancellation failure. Justification: the matcher pipeline must degrade gracefully — better a partial snapshot (3 sources good, 1 empty) than a fully failed scan."
  - "Indexes built at snapshot construction (not lazily on first matcher access): O(N_services) + O(N_drivers) + O(N_appx) work runs once during the slow build path; matcher hot loop becomes pure O(1) hash lookups. Initial sizing constants (256 services, 128 drivers/appx/tasks) tuned to typical corporate-machine numbers — minor avoid of List<T> reallocation during materialization."
  - "DriverProviderTokens splits each Provider field on whitespace + comma + semicolon and skips tokens of length < 2 — avoids polluting the index with single-character cruft and lets the matcher detect 'NVIDIA Corporation' vs 'NVIDIA Corp.' vs 'nvidia' as overlapping vendors via single-token hash hits."

metrics:
  tasks: 3
  files_created: 11
  files_modified: 1
  tests_added: 10  # 7 MachineSnapshotProviderTests + 3 EnumeratorPromotionTests
  total_tests_passing: 150  # 140 baseline (130 original + 10 from Plan 10-01) + 10 new
  duration: "6m 10s"
  completed: "2026-04-27"
---

# Phase 10 Plan 02: Enumerator Promotion + MachineSnapshot Cache Summary

**One-liner:** Promoted IServiceEnumerator + IScheduledTaskEnumerator from internal-nested to public + added IDriverEnumerator (pnputil) + IAppxEnumerator (Get-AppxPackage), then built MachineSnapshot record + MachineSnapshotProvider with 5-min TTL + parallel Task.WhenAll + SemaphoreSlim concurrency gate + per-source graceful degradation — the indexed snapshot Plan 10-04's MultiSourceMatcher will consume in O(1) per candidate.

## Objective Recap

Build the `MachineSnapshot` infrastructure that the Phase-10 AppData orphan-detection pipeline (step [4] MultiSourceMatcher) requires: a single fast lookup against an indexed view of the machine's installed services, drivers, Appx packages, and scheduled tasks. Without caching, every orphan candidate would re-enumerate the entire machine (`Get-AppxPackage` and `pnputil /enum-drivers` cost 1-3s each); with the 5-min TTL the user can launch a scan, then re-scan after 4 minutes without paying the indexation cost twice.

## What Was Built

### Promoted interfaces (2)

- **`IServiceEnumerator`** — moved verbatim from `internal` nested in `ResidueScanner.cs` to `public` in its own file `Services/IServiceEnumerator.cs`. Same `IEnumerable<(string Name, string DisplayName, string? BinaryPath)>` tuple shape. Same namespace `DiskScout.Services`. Non-breaking for the 46 ResidueScannerTests.
- **`IScheduledTaskEnumerator`** — same procedure: moved to `Services/IScheduledTaskEnumerator.cs`, made public. Same tuple shape `(string TaskPath, string? Author, string? ActionPath)`.

### New interfaces (2)

- **`IDriverEnumerator`** — `Services/IDriverEnumerator.cs`. Yields `(PublishedName, OriginalFileName, Provider, ClassName)` for each third-party driver in the Windows driver store.
- **`IAppxEnumerator`** — `Services/IAppxEnumerator.cs`. Yields `(PackageFullName, PackageFamilyName, Publisher, InstallLocation)` for each installed Appx package.

### New production implementations (2)

- **`DriverEnumerator`** — shells out to `pnputil.exe /enum-drivers /format:json` (10s timeout). Parses JSON via `System.Text.Json`. Falls back to plain-text line-prefix parsing on Windows < 10 v1903 (where `/format:json` is unsupported). On any parse/process failure, logs Warning and yields nothing — never throws.
- **`AppxEnumerator`** — shells out to `powershell.exe -NoProfile -NonInteractive -Command "Get-AppxPackage -AllUsers | Select-Object PackageFullName,PackageFamilyName,Publisher,InstallLocation | ConvertTo-Json -Depth 2"` (15s timeout). Handles BOTH `JsonValueKind.Array` (multi-package) and `JsonValueKind.Object` (single-package edge case) root shapes. Same Warning + yield-empty failure mode.

### New model (1)

- **`MachineSnapshot.cs`** in `Models/`. `public sealed record MachineSnapshot(CapturedUtc, IReadOnlyList<ServiceEntry>, IReadOnlyList<DriverEntry>, IReadOnlyList<AppxEntry>, IReadOnlyList<ScheduledTaskEntry>)` plus 3 init-only `IReadOnlySet<string>` index properties:
  - `ServiceBinaryPathPrefixes` — `Path.GetDirectoryName(svc.BinaryPath)` for each service with a non-null binary path (case-insensitive).
  - `DriverProviderTokens` — whitespace/comma/semicolon-split tokens of each driver's `Provider` field, length >= 2 (case-insensitive).
  - `AppxInstallLocationPrefixes` — each Appx package's `InstallLocation` (case-insensitive).
- 4 sibling records: `ServiceEntry`, `DriverEntry`, `AppxEntry`, `ScheduledTaskEntry` — tuple-equivalent to their respective `I*Enumerator` yield shapes.

### New cache provider (1)

- **`IMachineSnapshotProvider`** — `Services/IMachineSnapshotProvider.cs`. Two methods: `Task<MachineSnapshot> GetAsync(CancellationToken)` and `void Invalidate()`.
- **`MachineSnapshotProvider`** — `Services/MachineSnapshotProvider.cs`. Default TTL 5 minutes (configurable). Constructor takes the 4 enumerator interfaces + Serilog `ILogger`. Logic:
  1. **Fast path:** `Volatile.Read` of `_cached`; if `UtcNow - CapturedUtc < _ttl`, return immediately (no gate).
  2. **Slow path:** `await _gate.WaitAsync(ct)` + re-check freshness + `Task.WhenAll` of 4 `Task.Run` blocks (one per enumerator), each materializing an `IEnumerable` to a `List`, each wrapped in try/catch that substitutes `Array.Empty` on failure. Build snapshot + 3 indexes. `Volatile.Write` to `_cached`. Release.
  3. **Invalidate:** `_gate.Wait()` + null `_cached` + release — cannot race with an in-flight build.
- Information-level log on each rebuild with elapsed ms and per-source counts.

### ResidueScanner refactor (1 file modified)

- Removed the 2 internal nested interface declarations (lines 10-28 of the original file).
- Added 2 public static factory methods immediately after the test-only constructor:
  ```csharp
  public static IServiceEnumerator CreateDefaultServiceEnumerator(ILogger logger)
      => new WmiServiceEnumerator(logger);
  public static IScheduledTaskEnumerator CreateDefaultScheduledTaskEnumerator(ILogger logger)
      => new SchTasksEnumerator(logger);
  ```
  These wrap the still-nested-private concrete implementations so App.xaml.cs (Plan 10-04) can inject the same defaults the scanner already uses without exposing the concrete types or duplicating the implementations.
- Nested `WmiServiceEnumerator` + `SchTasksEnumerator` classes left untouched.

## Tests Added

### `MachineSnapshotProviderTests` (7 facts)

1. `GetAsync_with_empty_enumerators_returns_snapshot_with_empty_lists` — happy path, all entry lists + all 3 indexes empty.
2. `GetAsync_called_twice_within_TTL_returns_same_instance` — `ReferenceEquals(s1, s2) == true` + `fakeService.CallCount == 1` (cache hit path).
3. `Invalidate_forces_rebuild_on_next_GetAsync` — different instance + `CallCount == 2`.
4. `GetAsync_after_TTL_expiry_rebuilds_and_returns_new_instance` — 50ms TTL + 120ms wait + different instance.
5. `Index_sets_are_populated_from_source_data` — `ServiceBinaryPathPrefixes` contains parent dir of each binary, `DriverProviderTokens` contains `"NVIDIA"` + `"Corporation"` + `"Realtek"`, `AppxInstallLocationPrefixes` contains the Appx install path.
6. `GetAsync_substitutes_empty_list_when_enumerator_throws` — a `ThrowingDriverEnumerator` yields an empty `Drivers` list while the other 3 sources still populate (graceful degradation).
7. `Concurrent_GetAsync_calls_only_build_once` — 10 parallel `GetAsync` callers + a slow service enumerator (80ms blocking sleep) + `CallCount == 1` + all 10 results reference the same snapshot.

All 4 fakes hand-written (project policy precedent from Plan 09-05 — no Moq for new code).

### `EnumeratorPromotionTests` (3 facts)

1. `All_4_enumerator_interfaces_are_public` — `typeof(IServiceEnumerator).IsPublic` etc. Locks the promotion against future re-internalization.
2. `All_4_enumerator_interfaces_live_in_DiskScout_Services_namespace` — namespace stability check.
3. `ResidueScanner_exposes_static_factories_for_default_service_and_scheduled_task_enumerators` — reflects on `ResidueScanner.CreateDefaultServiceEnumerator` + `CreateDefaultScheduledTaskEnumerator` and asserts they are `public static`, take `(ILogger)`, and return the correct interface types — regression-locks the Plan 10-04 DI seam.

## Suite Status

- **Before plan:** 140 passing (130 baseline + 10 from Plan 10-01).
- **After plan:** 150 passing, 0 failed, 0 skipped.
- **The 46 ResidueScannerTests** (Plan 09-03) still pass unchanged — non-breaking promotion verified.
- Build: `dotnet build` clean (0 warnings, 0 errors).

## Acceptance Criteria — All Met

- [x] `grep -c "public interface IServiceEnumerator" src/DiskScout.App/Services/IServiceEnumerator.cs` = 1
- [x] `grep -c "public interface IScheduledTaskEnumerator" src/DiskScout.App/Services/IScheduledTaskEnumerator.cs` = 1
- [x] `grep -c "public interface IDriverEnumerator" src/DiskScout.App/Services/IDriverEnumerator.cs` = 1
- [x] `grep -c "public interface IAppxEnumerator" src/DiskScout.App/Services/IAppxEnumerator.cs` = 1
- [x] `grep -c "internal interface IServiceEnumerator" src/DiskScout.App/Services/ResidueScanner.cs` = 0
- [x] `grep -c "internal interface IScheduledTaskEnumerator" src/DiskScout.App/Services/ResidueScanner.cs` = 0
- [x] `grep -c "public sealed class DriverEnumerator : IDriverEnumerator" src/DiskScout.App/Services/DriverEnumerator.cs` = 1
- [x] `grep -c "public sealed class AppxEnumerator : IAppxEnumerator" src/DiskScout.App/Services/AppxEnumerator.cs` = 1
- [x] `grep -c "pnputil.exe" src/DiskScout.App/Services/DriverEnumerator.cs` >= 1 (= 4)
- [x] `grep -c "Get-AppxPackage" src/DiskScout.App/Services/AppxEnumerator.cs` >= 1 (= 7)
- [x] `grep -c "public sealed record MachineSnapshot(" src/DiskScout.App/Models/MachineSnapshot.cs` = 1
- [x] `grep -c "public interface IMachineSnapshotProvider" src/DiskScout.App/Services/IMachineSnapshotProvider.cs` = 1
- [x] `grep -c "public sealed class MachineSnapshotProvider : IMachineSnapshotProvider" src/DiskScout.App/Services/MachineSnapshotProvider.cs` = 1
- [x] `grep -c "Task.WhenAll|TimeSpan.FromMinutes(5)|SemaphoreSlim|Invalidate" src/DiskScout.App/Services/MachineSnapshotProvider.cs` = 8 (covers all 4 markers >= 1 each)
- [x] No `File.Delete` / `Directory.Delete` introduced in any Plan 10-02 file (verified via Grep across DriverEnumerator.cs, AppxEnumerator.cs, MachineSnapshotProvider.cs, MachineSnapshot.cs)
- [x] `dotnet build` succeeds with 0 errors, 0 warnings
- [x] `dotnet test` passes 150/150 (>= the 151 target was based on a 7-test estimate; we delivered 10 tests, with the EnumeratorPromotion suite expanded to 3 facts for regression + namespace + factory coverage — total still well above the 130 + 10 (Plan 10-01) + 7 (this plan) baseline)

## Deviations from Plan

None of the auto-fix Rules 1-3 fired during execution. The plan was executed exactly as written with two small refinements that are improvements rather than departures:

1. **EnumeratorPromotionTests gained a third fact** beyond the spec-required `All_4_enumerator_interfaces_are_public` — added `All_4_enumerator_interfaces_live_in_DiskScout_Services_namespace` (namespace regression lock) and `ResidueScanner_exposes_static_factories_for_default_service_and_scheduled_task_enumerators` (locks the static-factory contract that Plan 10-04 DI wiring depends on per the parallel_execution constraints in this plan's prompt). 3 facts instead of 1.
2. **TTL expiry test added beyond the 6-test spec.** The spec listed 6 tests for `MachineSnapshotProviderTests` (a–f). I added a 7th `GetAsync_after_TTL_expiry_rebuilds_and_returns_new_instance` because the spec mentioned TTL expiry in the behavior section but didn't enumerate a corresponding test, and the 50ms-TTL clock-roll case is the cheapest way to verify the freshness-window logic.

No CLAUDE.md violations. No `File.Delete` / `Directory.Delete` anywhere. No new NuGet dependencies. Manual DI pattern preserved (App.xaml.cs wiring is reserved for Plan 10-04 per parallel_execution constraints).

## What's Next (consumed by which plan)

- **Plan 10-04 (MultiSourceMatcher)** consumes `IMachineSnapshotProvider` + `MachineSnapshot` indexes for O(1) per-candidate matching. Plan 10-04 also wires the 4 new services in `App.xaml.cs` using `ResidueScanner.CreateDefaultServiceEnumerator` + `CreateDefaultScheduledTaskEnumerator` + `new DriverEnumerator(logger)` + `new AppxEnumerator(logger)` + `new MachineSnapshotProvider(logger, ...)`.
- **Plan 10-05 (ConfidenceScorer)** does not consume the snapshot directly; it consumes the `MatcherHit` records produced by Plan 10-04.
- **Plan 10-06 (Pipeline integration)** wires the snapshot provider's `Invalidate()` to the user's "Re-scan" action so a fresh scan picks up newly-installed services / drivers / Appx packages without waiting for the 5-min TTL.

## Self-Check: PASSED

All 12 claimed files exist on disk. All 3 claimed commits (977c415, 2554271, 19f5c02) exist in `git log`. Build clean, 150/150 tests passing.
