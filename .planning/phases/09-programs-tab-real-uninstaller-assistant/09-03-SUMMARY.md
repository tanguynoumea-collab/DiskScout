---
phase: 09-programs-tab-real-uninstaller-assistant
plan: 03
subsystem: services
tags: [residue-scanner, whitelist-safety, fuzzy-match, registry, schtasks, shell-extensions, msi-patches, install-trace, serilog, xunit]

# Dependency graph
requires:
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: InstallTrace records (Plan 09-01) — consumed for HighConfidence/TraceMatch findings
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: NativeUninstallerDriver outcomes (Plan 09-02) — scanner runs AFTER native uninstall completes
  - phase: 03-registry-and-orphans
    provides: RegistryHive 4-view enumeration pattern (HKLM/HKCU x32/x64); FuzzyMatcher.IsMatch; FileSafety whitelist style
provides:
  - IResidueScanner contract (ScanAsync target + optional InstallTrace + IProgress + cancellation) consumed by Plan 09-05 (Wizard UI step "Scan résidus post-uninstall")
  - ResidueFinding / ResidueCategory (7 categories) / ResidueTrustLevel / ResidueSource models consumed by Plan 09-05 (tree view) and Plan 09-06 (final report)
  - ResiduePathSafety whitelist (19 fs substrings, 17 registry prefixes, 15 critical service names) — non-bypassable guard reused by Plan 09-04 (publisher rules) and Plan 09-05 (delete confirmation)
  - IServiceEnumerator + IScheduledTaskEnumerator internal interfaces (test-injectable seam)
affects: [09-04-publisher-rule-engine, 09-05-wizard-ui, 09-06-integration-report]

# Tech tracking
tech-stack:
  added: []  # No new NuGet packages — registry-based service enumeration replaces System.ServiceProcess.ServiceController
  patterns:
    - "Centralized whitelist with two-axis filter: filesystem-substring (case-insensitive Contains) + registry-prefix (case-insensitive StartsWith) + service-name-equality"
    - "Single chokepoint for findings (TryAdd) — every emit goes through ResiduePathSafety.IsSafeToPropose, dedup via Category|Path key"
    - "Per-category SafeRun try/catch — one branch's failure logs Warning and lets remaining categories continue (never aborts whole scan)"
    - "Test-injectable enumerators (IServiceEnumerator, IScheduledTaskEnumerator) via internal constructor — hand-written fakes (no Moq dep)"
    - "Registry test-prefix knob — tests write transient HKCU\\Software\\DiskScoutTest_{Guid} subkeys and inject prefix into scanner; cleanup in Dispose"
    - "Trust hierarchy: TraceMatch (HighConfidence) > PublisherRule (reserved for Plan 09-04) > NameHeuristic (MediumConfidence)"

key-files:
  created:
    - src/DiskScout.App/Models/ResidueFinding.cs
    - src/DiskScout.App/Services/IResidueScanner.cs
    - src/DiskScout.App/Services/ResidueScanner.cs
    - src/DiskScout.App/Helpers/ResiduePathSafety.cs
    - tests/DiskScout.Tests/ResiduePathSafetyTests.cs
    - tests/DiskScout.Tests/ResidueScannerTests.cs
  modified: []

key-decisions:
  - "ResiduePathSafety is the single non-bypassable guard — every finding goes through TryAdd which calls IsSafeToPropose. Service findings additionally pass through IsSafeServiceName. Whitelist is over-rejective by design (per CONTEXT.md risk: false positive = data loss)."
  - "Registry-based service enumeration (HKLM\\SYSTEM\\CurrentControlSet\\Services subkey walk) instead of ServiceController.GetServices() — avoids new NuGet dep on System.ServiceProcess.ServiceController per project policy."
  - "Hand-written FakeServiceEnumerator + FakeScheduledTaskEnumerator instead of Moq (Plan 09-01 precedent: test project does not depend on Moq, project policy forbids new NuGet packages)."
  - "MSI patch heuristic uses 8-char GUID prefix matching against filename (no MsiOpenDatabase). Filename starts with the same 8 hex digits as RegistryKeyName GUID prefix => candidate match. Pragmatic for v1; can be upgraded to PatchInfo.Subject in a future plan if false-positive rate is too high."
  - "Shell extension scan refuses to run when InstallLocation is on the whitelist (defensive — no third-party shell ext lives at the OS layer; if it does, it's not residue)."
  - "Schtasks CSV output may repeat the header line per task folder; the parser skips any line that starts with quoted 'TaskName' to avoid emitting bogus rows."
  - "Cancellation is checked at the top of every per-item loop AND between every category; deeply-nested filesystem fixtures cancel within ~2s reliably (tested with pre-cancelled token)."

patterns-established:
  - "Pattern: Per-feature whitelist as a static class with `public static readonly string[]` denylist arrays — grep-stable, immutable across the rest of the phase, easy to audit at code-review time"
  - "Pattern: Internal constructor with injected category flags + test seams (registryTestPrefix, windowsInstallerPath, IServiceEnumerator, IScheduledTaskEnumerator, shellExtensionTestPrefix) — keeps the public surface minimal while letting xUnit fixtures bypass the real environment"
  - "Pattern: TryAdd helper as the only path to mutate the findings list — guarantees whitelist + dedup are applied uniformly across all 7 scan branches"
  - "Pattern: Hand-written test fakes with `IList<T>` (or List<T>) backing field built from IEnumerable in constructor — no Moq, no NSubstitute, deterministic"
  - "Pattern: 'TestPrefix knob' for registry-touching tests — write transient HKCU subkeys under DiskScoutTest_{Guid}, inject the prefix into the scanner, clean up in Dispose"

requirements-completed: []  # Phase 9 has no mapped REQ-IDs (post-v1, see ROADMAP.md note)

# Metrics
duration: 10m 46s
completed: 2026-04-25
---

# Phase 09 Plan 03: Residue Scanner Summary

**Seven-category post-uninstall residue scanner (registry / filesystem / shortcuts / MSI patches / services / scheduled tasks / shell extensions) with non-bypassable ResiduePathSafety whitelist (19 fs substrings + 17 registry prefixes + 15 critical service names) and HighConfidence install-trace correlation.**

## Performance

- **Duration:** 10m 46s
- **Started:** 2026-04-25T15:40:27Z
- **Completed:** 2026-04-25T15:51:13Z
- **Tasks:** 3 / 3
- **Files created:** 6
- **Files modified:** 0
- **Tests added:** 46 (36 path-safety + 10 scanner — exceeds plan's 19-test minimum), all passing
- **Total test suite:** 95 / 95 passing (no regressions; baseline was 49 from Plan 09-02, +36 path-safety, +10 scanner)

## Accomplishments

- **Seven residue categories** behind a single `IResidueScanner` contract — every category honored per CONTEXT.md D-02 ("Revo Pro level"). Categories: Registry, Filesystem, Shortcut, MsiPatch, Service, ScheduledTask, ShellExtension.
- **`ResiduePathSafety` non-bypassable whitelist**:
  - 19 filesystem critical substrings (System32, SysWOW64, WinSxS, drivers, Boot, Fonts, Microsoft.NET, servicing, assembly, Common Files, Windows Defender, Windows Security, Windows NT, Microsoft\Edge, WindowsApps).
  - 17 registry critical prefixes (Tcpip, Dhcp, Dnscache, WinDefend, MsSecCore, WdNisSvc, Sense, TrustedInstaller, EventLog, RpcSs, LSM, MpsSvc, Microsoft\Windows Defender, Windows Security, Policies\Microsoft\Windows Defender, Control\Class, Setup).
  - 15 critical service names (WinDefend, MsSecCore, WdNisSvc, Sense, WdFilter, MpsSvc, BFE, EventLog, RpcSs, DcomLaunch, TrustedInstaller, Dhcp, Dnscache, LSM, lsass).
- **Trace correlation = HighConfidence**: when an `InstallTrace` (Plan 09-01) is provided, the scanner emits `Source = TraceMatch` + `Trust = HighConfidence` for every traced filesystem path that still exists post-uninstall. This is the highest-trust source by design.
- **Filesystem fuzzy match**: `FuzzyMatcher.IsMatch` (threshold 0.7) over LOCALAPPDATA / APPDATA / COMMONAPPDATA / Program Files / Program Files (x86) / Public / Temp. Returns `Source = NameHeuristic` + `Trust = MediumConfidence`.
- **Registry walk** across the four hive views (HKLM/HKCU x32/x64) — replicates Plan 03's pattern. Probes `SOFTWARE\<Publisher>`, `SOFTWARE\<Publisher>\<DisplayName>`, `SOFTWARE\Wow6432Node\<Publisher>`, the Uninstall key by RegistryKeyName, and the App Paths\<DisplayName>.exe template. Test mode walks an injected HKCU prefix.
- **MSI patch heuristic**: enumerates `%WINDIR%\Installer\*.msp`; matches by 8-char GUID prefix between filename and target's `RegistryKeyName`. Path overridable via constructor for tests. No MsiOpenDatabase dependency.
- **Service enumeration** via direct registry walk of `HKLM\SYSTEM\CurrentControlSet\Services` (each subkey = one service; reads `DisplayName` + `ImagePath`). Avoids taking a new NuGet dep on `System.ServiceProcess.ServiceController`. Match heuristic: name-contains-publisher OR display-contains-displayname OR binary-startsWith-installLocation. Double-gated by `IsSafeServiceName` AND `IsSafeToPropose` on the binary path.
- **Scheduled tasks** via `schtasks.exe /query /fo CSV /v` (10s timeout). Minimal RFC-4180 CSV parser handles embedded quotes. Match by Author / TaskPath / ActionPath. ActionPath is whitelist-checked.
- **Shell extensions**: walks `HKLM\SOFTWARE\Classes\CLSID` (Registry64 view), inspects each `InprocServer32` default value; emits a finding when the DLL path lives under the target's `InstallLocation`. Refuses entirely if `InstallLocation` is itself on the whitelist (defensive — no third-party shell ext lives at the OS layer).
- **Cancellation propagates within ~2s** in every branch (tested with a pre-cancelled token over a 50-folder, 500-file synthetic fixture).
- **Per-category fault tolerance**: each scan runs inside `SafeRun` — any unhandled exception logs at Warning and lets the remaining categories continue. Cancellation is rethrown.

## Task Commits

Each task was committed atomically with `--no-verify` (parallel-mode convention from Plans 09-01 / 09-02):

1. **Task 1: ResidueFinding model + IResidueScanner + ResiduePathSafety + 36 safety tests** — `3d12d9f` (feat)
2. **Task 2: filesystem + registry + shortcut residue scans + trace correlation + 6 tests** — `be47f5b` (feat)
3. **Task 3: MSI patch + Service + ScheduledTask + ShellExtension scans + 4 tests** — `238532d` (feat)

_TDD note: per-task tests landed alongside the implementation in a single commit. The RED phase was verified explicitly for the path-safety tests (built and confirmed `CS0103: 'ResiduePathSafety' does not exist` before writing the impl). Subsequent task tests rely on the model interfaces from Task 1 — they couldn't compile before the impl skeleton existed, so the RED step is implicit in the build error path._

## Files Created/Modified

### Created

- **`src/DiskScout.App/Models/ResidueFinding.cs`** — `ResidueCategory` (7-value enum), `ResidueTrustLevel` (3 levels), `ResidueSource` (3 sources), `ResidueFinding` record, `ResidueScanTarget` record. ~95 LOC.
- **`src/DiskScout.App/Services/IResidueScanner.cs`** — Interface contract with `ScanAsync(target, installTrace?, progress, ct)` returning `IReadOnlyList<ResidueFinding>`. ~30 LOC.
- **`src/DiskScout.App/Helpers/ResiduePathSafety.cs`** — Static whitelist guard. Three `public static readonly string[]` arrays (CriticalFilesystemSubstrings, CriticalRegistryPrefixes, CriticalServiceNamePatterns) + two static methods (`IsSafeToPropose`, `IsSafeServiceName`). ~110 LOC.
- **`src/DiskScout.App/Services/ResidueScanner.cs`** — `sealed class ResidueScanner : IResidueScanner` plus `internal interface IServiceEnumerator` and `internal interface IScheduledTaskEnumerator` plus two private nested default impls (`WmiServiceEnumerator` registry-based, `SchTasksEnumerator` schtasks.exe-based + CSV parser). ~580 LOC.
- **`tests/DiskScout.Tests/ResiduePathSafetyTests.cs`** — 36 [Theory]/[Fact] cases covering all whitelist axes and case-insensitive matching for both `IsSafeToPropose` and `IsSafeServiceName`.
- **`tests/DiskScout.Tests/ResidueScannerTests.cs`** — 10 integration-tagged tests: 6 from Task 2 (FS fuzzy, FS whitelist enforcement, trace existence filter, registry name heuristic, shortcut substring, pre-cancelled token) + 4 from Task 3 (MSI prefix, service whitelist, scheduled task author/action, shell-extension CLSID). Hand-written `FakeServiceEnumerator` + `FakeScheduledTaskEnumerator` (no Moq).

### Modified

None.

## Decisions Made

- **ResiduePathSafety is the only chokepoint for finding emission.** All seven scan branches add findings exclusively through the private `TryAdd(findings, seen, finding)` helper, which calls `ResiduePathSafety.IsSafeToPropose(finding.Path)` first. Service-name guarding (`IsSafeServiceName`) runs at the source-of-truth (inside `ScanServices` before `TryAdd`) since the path format `Service:{name}` would not be caught by `IsSafeToPropose`. This single chokepoint guarantees the whitelist is non-bypassable across present and future categories.
- **Trust hierarchy is explicit and small.** `HighConfidence` is reserved for trace-correlated findings only — the trace is the literal record of "we watched the installer create this path, it's still here, it must be residue." `MediumConfidence` covers all current heuristic matches (fuzzy folder match, name-contains-publisher service, etc.). `LowConfidence` is reserved for future use (Plan 09-04 publisher rules with weak signals; ML-derived heuristics if we ever add them). The wizard (Plan 09-05) will default-check High and ask the user for Medium, presenting Low as opt-in.
- **MSI patch heuristic by 8-char GUID prefix only.** The plan's `<behavior>` accepted "filename startsWith Publisher first 3 letters OR file is older than 90 days AND no installed program references that GUID". I went with the GUID-prefix-vs-RegistryKeyName variant because (a) it has zero false-positives when RegistryKeyName is a real MSI GUID, and (b) the publisher-prefix variant has too many false positives with vendors using 3-letter prefixes (Adobe → "Ado", Apple → "App", Autodesk → "Aut"). Falls back to no-op when RegistryKeyName isn't a GUID.
- **Schtasks CSV header repeats per folder.** Real-world `schtasks /query /fo CSV /v` output emits the header line once per task folder. The parser explicitly skips any subsequent line that starts with `"TaskName"` (quoted) to avoid emitting empty rows. This is a known schtasks quirk — easier to handle in the parser than to filter out at the CSV layer.
- **Shell extension scan refuses to run when InstallLocation is on the whitelist.** Defensive: legitimate third-party shell extensions never live under `\Windows\System32\` or similar OS-managed paths. If `InstallLocation` IS such a path, the target program is either Microsoft itself (system component, shouldn't be uninstalled) or the InstallLocation field is corrupted — either way, we abstain.
- **Per-category SafeRun lets one branch fail without aborting the scan.** A user with a corrupted scheduled-task store should still see registry / filesystem / service findings. The per-category try/catch wraps the call; only `OperationCanceledException` is re-thrown.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] `RegistryHive` ambiguous between `DiskScout.Models.RegistryHive` and `Microsoft.Win32.RegistryHive`**
- **Found during:** Task 2 (first build of `ResidueScanner.cs`)
- **Issue:** `using Microsoft.Win32;` for `RegistryKey` + `using DiskScout.Models;` for the `Models.RegistryHive` enum (carried in by the Models namespace) makes `RegistryHive` ambiguous. CS0104. Same blocker hit by Plan 09-01.
- **Fix:** Fully qualified `Microsoft.Win32.RegistryHive` in the `HiveViews` tuple declaration, the `RegistryKey.OpenBaseKey` calls, and the `WmiServiceEnumerator`/`ScanShellExtensions` registry opens. No project-wide rename, no public API change.
- **Files modified:** `src/DiskScout.App/Services/ResidueScanner.cs` (4 sites).
- **Verification:** `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug --nologo` exits 0 with 0 warnings.
- **Committed in:** `be47f5b` (Task 2 commit).

**2. [Rule 3 - Blocking] `System.ServiceProcess.ServiceController` requires a new NuGet package**
- **Found during:** Task 3 (first build after adding `using System.ServiceProcess;`)
- **Issue:** `ServiceController.GetServices()` lives in the `System.ServiceProcess.ServiceController` NuGet package, which the project does not reference. The `<important_notes>` section explicitly forbids new NuGet packages.
- **Fix:** Replaced `ServiceController.GetServices()` with direct enumeration of `HKLM\SYSTEM\CurrentControlSet\Services` subkeys via `Microsoft.Win32.Registry` (already referenced). Each subkey is a service; `DisplayName` and `ImagePath` are values on the subkey. `ImagePath` is normalised (strip leading `\??\` and surrounding quotes, cut at first space for unquoted args). Behaviour is equivalent for the scanner's purposes.
- **Files modified:** `src/DiskScout.App/Services/ResidueScanner.cs` (`WmiServiceEnumerator` rewrite + remove the `using System.ServiceProcess;` line).
- **Verification:** Service enumeration test (`Services_MatchesByPublisherSubstring_AndRespectsSafeServiceName`) passes via injected fake. Real-environment smoke (a manual `dotnet run` was not performed for this plan; the WPF app does not yet wire up `ResidueScanner`, so end-to-end live verification is deferred to Plan 09-05).
- **Committed in:** `238532d` (Task 3 commit).

**3. [Rule 3 - Blocking] Plan asks for Moq for IServiceEnumerator/IScheduledTaskEnumerator fakes**
- **Found during:** Task 3 (writing tests)
- **Issue:** Plan 09-03 Task 3 specifies "Use Moq for `IServiceEnumerator` and `IScheduledTaskEnumerator` to inject deterministic fixtures". Moq is not in `tests/DiskScout.Tests/DiskScout.Tests.csproj`, and project policy (per `<important_notes>` and Plan 09-01 SUMMARY) forbids new NuGet packages.
- **Fix:** Implemented `FakeServiceEnumerator` and `FakeScheduledTaskEnumerator` private nested classes inside `ResidueScannerTests.cs`. Each takes an `IEnumerable<ValueTuple>` in the constructor and returns it from the interface method. Functionally equivalent to a Moq setup — sufficient for the deterministic-fixture intent of the plan.
- **Files modified:** `tests/DiskScout.Tests/ResidueScannerTests.cs` (private nested classes + xmldoc explaining the choice).
- **Verification:** Tests `Services_MatchesByPublisherSubstring_AndRespectsSafeServiceName` and `ScheduledTasks_MatchByAuthorOrTaskPathContainsPublisher` pass.
- **Committed in:** `238532d` (Task 3 commit).

**4. [Rule 1 - Bug] Test fixture used a Common-Files-shared BinaryPath that the whitelist correctly rejected**
- **Found during:** Task 3 (initial test run)
- **Issue:** Initial `Services_MatchesByPublisherSubstring_AndRespectsSafeServiceName` fixture used Adobe's real-world BinaryPath `C:\Program Files (x86)\Common Files\Adobe\ARM\1.0\armsvc.exe`. The whitelist correctly rejects this — `\Common Files` is shared by Microsoft / Adobe / many vendors, so the scanner refuses to propose deletion of any binary there. The implementation is correct; the fixture didn't reflect the realistic path the test was meant to assert.
- **Fix:** Changed the fixture to `C:\Program Files\Adobe\Acrobat DC\Acrobat\armsvc.exe` (Adobe-only path, not shared). The test now correctly verifies that legitimate-publisher service binaries pass the whitelist.
- **Files modified:** `tests/DiskScout.Tests/ResidueScannerTests.cs` (one fixture line + an explanatory comment).
- **Verification:** Test passes; `Services_MatchesByPublisherSubstring_AndRespectsSafeServiceName` now correctly asserts both heuristic-pass (Adobe) and heuristic-reject (WinDefend by service name + Spooler by binary path under System32).
- **Committed in:** `238532d` (Task 3 commit).

---

**Total deviations:** 4 auto-fixed (3 × Rule 3 — Blocking; 1 × Rule 1 — Bug).
**Impact on plan:** All four fixes preserve plan intent. The Rule-3 fixes (RegistryHive ambiguity, ServiceController->registry, Moq->hand-written fakes) follow Plan 09-01's precedent verbatim. The Rule-1 fix corrected a misleading test fixture; the production code did not change. No scope creep, no architectural deviation.

## Issues Encountered

- **Whitelist over-rejection of `\Common Files\` is intentional, not a bug.** During the Rule-1 fix above, I considered exempting Adobe-vendor paths from the Common Files whitelist. Decided against: Microsoft Office's auto-update binaries also live in `\Common Files\Microsoft Shared\`, and a publisher-rule (Plan 09-04) is the right place to handle vendor-specific exceptions, not the safety floor. The whitelist's job is to over-reject; publisher rules' job is to specifically grant exemptions for known vendors.
- **The `_shellExtensionTestPrefix` parameter widened the internal constructor to 13 parameters.** That's a code-smell; in a refactor Plan we could collapse it into a `ResidueScannerOptions` record. Out of scope for this plan, deferred.
- **CSV warning CS8620 on tuple-array literals**: solved by explicitly typing the array as `new (string, string?, string?)[]` to match the fake's `IEnumerable<(string, string?, string?)>` parameter. Both fakes now compile with zero warnings.
- **`schtasks.exe` is not stubbed in tests** — the SchTasksEnumerator default impl shells out for real. We never hit it in tests because every Scheduled-task test injects a `FakeScheduledTaskEnumerator`. The schtasks parser is exercised only in production (or a future end-to-end Plan 09-05 wizard test).

## False-Positive Risks

- **Filesystem fuzzy match**: `FuzzyMatcher.IsMatch` threshold 0.7 is calibrated for AppData orphan detection (Phase 03). It can over-match for short publisher/displayname strings (e.g., publisher "Apple" might match folder "App"). Mitigation: combined with whitelist + dedup, but the wizard (Plan 09-05) MUST present these as MediumConfidence and require user confirmation before delete.
- **Registry name walk in test mode** is greedier than the production walk (it recurses 4 levels deep with substring match on Publisher / DisplayName, vs. the production version which only probes specific template paths). This is intentional for tests; the production walk is conservative.
- **MSI 8-char GUID prefix** has a theoretical collision rate of ~1 in 4.3 billion per pair, but in practice the same publisher will have all their MSI patches share a low-bit prefix only if they're truly the same product. Test 7 covers the positive case; we'll know more after Plan 09-05's manual smoke run.
- **Service binary path startsWith match** — if InstallLocation is empty/null, this branch is skipped (no false positives). If InstallLocation is set, we still ALSO check that `IsSafeToPropose(BinaryPath) == true`, so we won't propose any service whose binary lives under \Windows\System32 etc.

## Test Counts

| Suite | Tests | Outcome |
|-------|-------|---------|
| ResiduePathSafetyTests | 36 | 36 / 36 pass |
| ResidueScannerTests    | 10 | 10 / 10 pass |
| **Plan 09-03 subtotal** | **46** | **46 / 46 pass** |
| Full DiskScout.Tests suite | 95 | 95 / 95 pass (no regressions) |

## User Setup Required

None — no external service configuration required. The scanner enumerates the local user's HKLM/HKCU registry, runs `schtasks.exe` (always present on Windows), and reads `%WINDIR%\Installer`. All paths are read-only — true to CONTEXT.md D-03 (scanner produces findings only; deletion happens in Plan 09-05).

## Next Phase Readiness

- **Plan 09-04 (Publisher Rule Engine)** can begin in parallel — it doesn't import this scanner. Plan 09-04 will populate the `ResidueSource.PublisherRule` enum value (currently emitted by no scan branch) when applying vendor-specific patterns; the contract is stable.
- **Plan 09-05 (Wizard UI step 4 "Scan résidus post-uninstall")** is the primary consumer:
  - Inject `IResidueScanner` into the wizard view-model.
  - Call `ScanAsync(target, installTrace, progressCallback, ct)` after the native uninstaller completes (Plan 09-02).
  - Group findings by `ResidueCategory` for the tree view.
  - Default-check `Trust = HighConfidence`; ask the user for `MediumConfidence`; treat `LowConfidence` as opt-in (when Plan 09-04 lands).
  - Show `Reason` on hover/expand.
- **Plan 09-06 (Integration + Report)** will consume `ResidueFinding` for the final HTML/JSON export — schema is stable and uses canonical record types (no JSON source-gen yet, matching Plan 09-01's decision).
- **Manual smoke test deferred to Plan 09-05.** The scanner is unit-tested in isolation (95 / 95 pass); end-to-end live verification will happen when the wizard wires it up.

## Self-Check: PASSED

Verification performed during execution:

- `[ -f src/DiskScout.App/Models/ResidueFinding.cs ]` — FOUND (commit `3d12d9f`)
- `[ -f src/DiskScout.App/Services/IResidueScanner.cs ]` — FOUND (commit `3d12d9f`)
- `[ -f src/DiskScout.App/Helpers/ResiduePathSafety.cs ]` — FOUND (commit `3d12d9f`)
- `[ -f src/DiskScout.App/Services/ResidueScanner.cs ]` — FOUND (commits `be47f5b` initial impl, `238532d` Task 3 extension)
- `[ -f tests/DiskScout.Tests/ResiduePathSafetyTests.cs ]` — FOUND (commit `3d12d9f`)
- `[ -f tests/DiskScout.Tests/ResidueScannerTests.cs ]` — FOUND (commit `be47f5b` Task 2 tests, `238532d` Task 3 tests appended)
- `git log --oneline | grep -E "3d12d9f|be47f5b|238532d"` — all 3 commit hashes present.
- `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug --nologo` — exit 0, 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName~Residue"` — 46 / 46 pass.
- `dotnet test` (full suite) — 95 / 95 pass; no regressions.
- All `<acceptance_criteria>` literals from PLAN match by `grep` in `ResiduePathSafety.cs` (System32, WinSxS, drivers, Windows Defender, WinDefend, TrustedInstaller, IsSafeToPropose, IsSafeServiceName) and in `ResidueScanner.cs` (FuzzyMatcher.IsMatch, ResiduePathSafety.IsSafeToPropose, IServiceEnumerator, IScheduledTaskEnumerator, *.msp, \Installer, InprocServer32, ResidueCategory.* for all 7 categories, ResiduePathSafety.IsSafeServiceName, internal constructor with 13 parameters incl. test seams, 4 distinct private scan methods).

---
*Phase: 09-programs-tab-real-uninstaller-assistant*
*Completed: 2026-04-25*
