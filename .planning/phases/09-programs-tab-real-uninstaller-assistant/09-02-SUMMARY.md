---
phase: 09-programs-tab-real-uninstaller-assistant
plan: 02
subsystem: services
tags: [native-uninstaller-driver, jobobject, kill-on-job-close, pinvoke-kernel32, msiexec, innosetup, nsis, iprogress, cancellation, timeout, serilog, xunit]

# Dependency graph
requires:
  - phase: 01-foundations
    provides: Serilog logger (Serilog.ILogger), DI conventions in App.xaml.cs
  - phase: 03-registry-and-orphans
    provides: InstalledProgram record (registry-derived UninstallString)
provides:
  - INativeUninstallerDriver contract (ParseCommand + RunAsync) consumed by Plan 09-05 (Wizard UI step "Run native uninstaller")
  - UninstallCommand / UninstallOutcome / UninstallStatus / InstallerKind models consumed by Plan 09-05 + Plan 09-06 (final report)
  - Win32 Job Object tree-kill pattern (CreateJobObject + KILL_ON_JOB_CLOSE + AssignProcessToJobObject) reusable by future "run-foreign-process" features
affects: [09-05-wizard-ui, 09-06-integration-report]

# Tech tracking
tech-stack:
  added: []  # No new NuGet packages — kernel32 P/Invoke (native), System.Diagnostics.Process (BCL), Serilog already in place
  patterns:
    - "Win32 Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE for safe child-process tree teardown on cancellation"
    - "AssignProcessToJobObject called immediately after Process.Start (before any descendant can spawn)"
    - "Process.OutputDataReceived + ErrorDataReceived + BeginOutputReadLine streams stdout/stderr line-by-line via IProgress<string> (no buffering to end)"
    - "Linked CancellationTokenSource discriminates user-cancel (Cancelled) from hard timeout (Timeout); 30-minute hard ceiling"
    - "InternalsVisibleTo + internal test-only constructor accepting custom hardTimeout for fast Timeout-path coverage (no 30-minute test waits)"
    - "Family-based silent-flag policy: MSI (/I->/X + /qn /norestart), Inno (/SILENT /SUPPRESSMSGBOXES /NORESTART), NSIS (/S); never guess flags for Generic/Unknown"

key-files:
  created:
    - src/DiskScout.App/Models/UninstallExecution.cs
    - src/DiskScout.App/Services/INativeUninstallerDriver.cs
    - src/DiskScout.App/Services/NativeUninstallerDriver.cs
    - tests/DiskScout.Tests/NativeUninstallerDriverTests.cs
  modified:
    - src/DiskScout.App/AssemblyInfo.cs (added [InternalsVisibleTo("DiskScout.Tests")])

key-decisions:
  - "Refuse to fabricate silent flags for unknown installers — ParseCommand returns null when preferSilent=true and family is Generic/Unknown without QuietUninstallString. Wizard layer maps null+preferSilent to UninstallStatus.SilentNotSupported and offers the user an interactive run."
  - "DetectKind ordering: MsiExec by filename, then NSIS by canonical 'uninstall.exe' filename, then InnoSetup by 'unins*.exe' pattern, then NSIS-by-/S-token weak signal, then Generic. This avoids 'uninstall.exe' (NSIS) being misclassified as InnoSetup."
  - "ContainsTokenIgnoreCase used for /S detection so '/SILENT' (Inno) is NOT mistaken for a bare NSIS /S token."
  - "AssignProcessToJobObject failure does NOT abort the run — log a warning and rely on Process.Kill(entireProcessTree:true) as a belt-and-braces fallback. Job Object remains the primary mechanism."
  - "30-minute hard timeout is a constant (TimeSpan.FromMinutes(30)) but the internal constructor accepts an override. Tests use 2 s to verify Timeout outcome without 30-minute waits."
  - "On a timeout/cancellation we wait up to 2 s for the process to exit (process.WaitForExit(2000)) so output streams flush cleanly before we read CapturedOutputLineCount."

patterns-established:
  - "Pattern: Win32 Job Object lifetime owned by the RunAsync scope — created before Process.Start, closed in finally. KILL_ON_JOB_CLOSE makes 'CloseHandle' the cancellation primitive."
  - "Pattern: Test-only internal constructor for time-sensitive policies (hard timeout, retry windows) gated by [InternalsVisibleTo]. Avoids exposing knobs publicly while keeping tests fast."
  - "Pattern: cmd.exe as a deterministic 'fake uninstaller' in xUnit (echo for output streaming, exit /b N for exit codes, ping -n N for long-running processes). No new test infrastructure dependencies."

requirements-completed: []  # Phase 9 has no mapped REQ-IDs (post-v1, see ROADMAP.md note)

# Metrics
duration: 5m 53s
completed: 2026-04-25
---

# Phase 09 Plan 02: Native Uninstaller Driver Summary

**Win32 Job Object–backed uninstaller executor: parses UninstallString into MSI / Inno / NSIS / Generic commands, runs them with stdout/stderr streamed line-by-line, and tears the entire process tree down within 2 s on cancel via JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.**

## Performance

- **Duration:** 5m 53s
- **Started:** 2026-04-25T15:29:49Z
- **Completed:** 2026-04-25T15:35:42Z
- **Tasks:** 3 / 3
- **Files created:** 4
- **Files modified:** 1
- **Tests added:** 20 (14 parser + 6 RunAsync), all passing
- **Total test suite:** 49 / 49 passing (no regressions; baseline was 29, +14 parser, +6 RunAsync)

## Accomplishments

- **`INativeUninstallerDriver` contract** with two methods: `ParseCommand(InstalledProgram, string? quietUninstallString, bool preferSilent)` and `RunAsync(UninstallCommand, IProgress<string>?, CancellationToken)`.
- **Parser** handling four installer families:
  - **MsiExec** — detected by filename match `MsiExec.exe`. When `preferSilent=true`, rewrites `/I{guid}` -> `/X{guid}` (case-insensitive, preserving GUID casing) and appends `/qn /norestart` token-aware (no duplicates).
  - **InnoSetup** — detected by `unins*.exe` filename. Appends `/SILENT /SUPPRESSMSGBOXES /NORESTART`. Preserves Inno's mandatory `_?=...` working-directory sentinel argument.
  - **NSIS** — detected by canonical `uninstall.exe` filename or by a bare `/S` token in args. Appends `/S` if not already present.
  - **Generic / Unknown** — when `preferSilent=true` and no QuietUninstallString is given, returns `null` so the wizard maps it to `SilentNotSupported` (refuses to fabricate silent flags for unknown installers).
- **`QuietUninstallString` used directly** when provided AND `preferSilent=true`, with kind detected from its own executable.
- **`RunAsync`** built on:
  - **Win32 Job Object**: `CreateJobObject` -> `SetInformationJobObject(JobObjectExtendedLimitInformation, KILL_ON_JOB_CLOSE)` -> `AssignProcessToJobObject` immediately after `Process.Start`. `CloseHandle` in `finally` is the cancellation primitive.
  - **Output streaming** via `Process.OutputDataReceived` + `ErrorDataReceived` + `BeginOutputReadLine`/`BeginErrorReadLine`. Each non-null line increments `CapturedOutputLineCount` and is reported through the caller-supplied `IProgress<string>`.
  - **30-minute hard timeout** enforced by a linked `CancellationTokenSource.CancelAfter(_hardTimeout)`. User-cancel (`cancellationToken.IsCancellationRequested == true`) yields `UninstallStatus.Cancelled`; timeout-only yields `UninstallStatus.Timeout`.
  - **Belt-and-braces** `Process.Kill(entireProcessTree: true)` fallback if `AssignProcessToJobObject` ever fails (logged as a warning, run continues).
  - **`ExecutionFailure`** outcome when the executable cannot be located on disk (rooted path that doesn't exist), with a populated `ErrorMessage`.
- **Honors CONTEXT.md D-02** ("Revo Pro level"): driver is a dedicated, named service — not optional, not a wrapper around `ShellExecute`.

## Task Commits

Each task was committed atomically with `--no-verify` (parallel-mode convention from Plan 09-01):

1. **Task 1: Define UninstallExecution models + INativeUninstallerDriver interface** — `ac27c65` (feat)
2. **Task 2: Implement parser (ParseCommand) for MSI/Inno/NSIS/Generic + 14 parser tests** — `4610b8d` (feat)
3. **Task 3: Implement RunAsync with Job Object tree-kill, output streaming, timeout, cancellation + 6 RunAsync tests** — `10efd7b` (feat)

_TDD note: parser tests landed alongside the parser implementation (combined commit) since the data shape was defined in Task 1 and the parser is pure logic — no incremental RED/GREEN split would have added value. RunAsync tests landed alongside the RunAsync implementation for the same reason. Each task remains atomic and revertible._

## Files Created/Modified

### Created

- **`src/DiskScout.App/Models/UninstallExecution.cs`** — `InstallerKind` (Unknown / MsiExec / InnoSetup / NsisExec / WixBootstrapper / Generic), `UninstallStatus` (Success / NonZeroExit / SilentNotSupported / Timeout / Cancelled / ParseFailure / ExecutionFailure), `UninstallCommand` and `UninstallOutcome` records.
- **`src/DiskScout.App/Services/INativeUninstallerDriver.cs`** — driver contract with full XML docs covering null-return contract for unsupported silent + the 30-minute timeout / 2-second tree-kill guarantee.
- **`src/DiskScout.App/Services/NativeUninstallerDriver.cs`** — `~340 LOC` `sealed partial class` containing the parser, the RunAsync executor, and all `kernel32` Job-Object P/Invoke (`CreateJobObject`, `SetInformationJobObject`, `AssignProcessToJobObject`, `CloseHandle`) plus the `JOBOBJECT_BASIC_LIMIT_INFORMATION` / `IO_COUNTERS` / `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` structs marshalled via `Marshal.StructureToPtr`.
- **`tests/DiskScout.Tests/NativeUninstallerDriverTests.cs`** — 20 tests: 14 parser cases (Inno-quoted, Inno-with-`_?=`-sentinel, MSI install->uninstall conversion, MSI no-silent, NSIS uninstall.exe, Generic preferSilent->null, Generic no-silent->ok, QuietUninstallString-direct, empty/whitespace via Theory, working-directory rooted, MSI-non-rooted-null-WD, unquoted-split) + 6 RunAsync cases (echo Success, exit /b 7 NonZeroExit, ping cancellation tree-kill, missing-exe ExecutionFailure, custom-timeout Timeout, multi-line output streaming).

### Modified

- **`src/DiskScout.App/AssemblyInfo.cs`** — added `using System.Runtime.CompilerServices` and `[assembly: InternalsVisibleTo("DiskScout.Tests")]` so tests can reach the `internal NativeUninstallerDriver(ILogger, TimeSpan)` constructor used to inject a 2-second hard timeout for the `Timeout`-outcome test.

## Decisions Made

- **Parser refuses to fabricate silent flags for unknown installers.** When `preferSilent=true` and family is Generic / Unknown without a `QuietUninstallString`, `ParseCommand` returns `null` and logs at Information level. Rationale: running an interactive installer with guessed silent flags risks driving an unattended UI off-screen or skipping confirmation prompts. The wizard layer (Plan 09-05) maps `null + preferSilent` to `UninstallStatus.SilentNotSupported` and offers the user an interactive fallback.
- **DetectKind precedence: MSI > NSIS-canonical > Inno > NSIS-weak.** `uninstall.exe` (NSIS) starts with `unins`, so naive Inno-first detection misclassifies it. Reordering puts the more specific NSIS canonical name ahead of the Inno glob, then a final NSIS weak check on a bare `/S` token catches edge cases without false-positives on `/SILENT` (Inno).
- **`ContainsTokenIgnoreCase` token-boundary check.** Naive `args.Contains("/S")` would treat `/SILENT` as NSIS. `ContainsTokenIgnoreCase` requires whitespace boundaries on both sides of the token — `/S` matches only when standalone. This also keeps `/qn` and `/norestart` deduplication safe.
- **Job Object first; Process.Kill as fallback.** `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` is the primary tree-kill mechanism — closing the handle is atomic across all assigned descendants. `Process.Kill(entireProcessTree:true)` runs as belt-and-braces in case `AssignProcessToJobObject` fails (rare; logged warning, run continues). This is more conservative than relying on either alone.
- **Hard timeout is configurable for tests, not for callers.** The 30-minute ceiling is policy, not configuration — exposing it on the public constructor would invite misuse. The `internal` constructor + `[InternalsVisibleTo]` lets tests inject 2 s without polluting the public API.
- **`ExecutablePath` non-rooted accepted at runtime.** `IsLikelyResolvableExecutable` returns `true` for non-rooted commands (e.g., `MsiExec.exe`, `cmd.exe`) on the assumption that Windows will resolve them via `PATH`. Rooted paths must exist on disk. This matches the canonical MSI registry convention where `UninstallString` is just `MsiExec.exe /X{guid}` with no directory.

## Deviations from Plan

None of substance. The plan was executed exactly as written, with two micro-adjustments inside the parser to make the tests pass cleanly:

### Inline Adjustment 1: DetectKind precedence reordering

- **Found during:** Task 2 (test `Parse_Nsis_AppendsCapitalSFlag` failed)
- **Issue:** `uninstall.exe` (NSIS canonical) matched the Inno-Setup `unins*.exe` glob first, returning `InstallerKind.InnoSetup`. The plan's spec listed both rules but did not pin the precedence.
- **Fix:** Reordered `DetectKind` so the explicit NSIS filename match (`uninstall.exe`) runs before the Inno glob. The NSIS-by-`/S`-token weak signal stays at the end so it doesn't shadow stronger filename signals.
- **Files modified:** `src/DiskScout.App/Services/NativeUninstallerDriver.cs` (DetectKind only).
- **Committed in:** `4610b8d` (Task 2 commit).

### Inline Adjustment 2: ContainsTokenIgnoreCase for /S detection

- **Found during:** Task 2 (during the same DetectKind fix)
- **Issue:** A naive `args.Contains("/S", Ordinal)` would treat Inno's `/SILENT` as a NSIS `/S` token, misrouting Inno installers to the NSIS branch. The plan's spec used `args.Contains("/S")` directly.
- **Fix:** Replaced with `ContainsTokenIgnoreCase(args, "/S")`, which requires whitespace boundaries on both sides. This is also reused for `/qn` and `/norestart` dedup to avoid duplicate-flag bugs.
- **Files modified:** `src/DiskScout.App/Services/NativeUninstallerDriver.cs`.
- **Committed in:** `4610b8d` (Task 2 commit).

Both adjustments preserve the plan's intent (correct family detection + safe silent-flag append). They're sub-method tweaks, not architectural deviations.

## Issues Encountered

- **`Progress<T>` async dispatch race in tests.** `Progress<T>` posts callbacks asynchronously to the captured `SynchronizationContext` (or thread-pool in test runners), so an immediately-following `lines.Should().Contain(...)` could fire before the handler runs. Mitigated with a 100 ms `Task.Delay` after `RunAsync` completes in the streaming-output tests. The driver itself does NOT have this race — `CapturedOutputLineCount` is incremented synchronously by `OutputDataReceived` via `Interlocked.Increment` and is settled by `WaitForExit(2000)`.
- **PowerShell zombie-process check garbled `$_`.** The post-test `Get-Process | Where-Object { $_.MainWindowTitle -eq '' }` invocation got the `$_` mangled by the bash escape layer; the count of `PING.EXE` processes (the relevant signal) returned `0`, confirming tree-kill works. This is a test-harness ergonomics note, not a product issue.
- **No P/Invoke ambiguity this time.** Unlike Plan 09-01 (RegistryHive ambiguous between `Microsoft.Win32` and `DiskScout.Models`), the kernel32 imports are unique to this file and cause no naming clashes.

## User Setup Required

None. The driver consumes only kernel32 (always present on Windows), `System.Diagnostics.Process`, and `Serilog.ILogger` (already configured by the existing `App.xaml.cs` bootstrap). No NuGet additions, no manifest changes, no folder creation.

## Next Phase Readiness

- **Plan 09-03 (Residue Scanner)** can begin in parallel — it doesn't import this driver. Its `IInstallTraceStore` consumer was unblocked by 09-01 already.
- **Plan 09-04 (Publisher Rule Engine)** can begin in parallel — orthogonal to this work.
- **Plan 09-05 (Wizard UI)** is the primary consumer:
  - Step 3 of the wizard ("Run native uninstaller") will inject `INativeUninstallerDriver`, call `ParseCommand` once for preview (display the resolved exe + args + Kind to the user), then call `RunAsync` with a `Progress<string>` bound to a live log control.
  - Map `UninstallStatus.SilentNotSupported` (i.e., `ParseCommand` returned `null` AND `preferSilent==true`) to a UX prompt "Cet installeur ne supporte pas le silent — voulez-vous lancer en interactif ?".
  - Map `Cancelled` to "Cancelled by user — process tree was terminated cleanly" (no need to surface the Job Object detail).
  - Map `Timeout` to "Le désinstalleur n'a pas terminé en 30 min — il a été tué".
- **Plan 09-06 (Integration + Report)** will consume `UninstallOutcome` for the final report (status, exit code, elapsed, captured line count, error message) — schema is stable.

## Self-Check: PASSED

Verification commands (run inside the working copy):

```
[ -f src/DiskScout.App/Models/UninstallExecution.cs ] && echo FOUND
[ -f src/DiskScout.App/Services/INativeUninstallerDriver.cs ] && echo FOUND
[ -f src/DiskScout.App/Services/NativeUninstallerDriver.cs ] && echo FOUND
[ -f tests/DiskScout.Tests/NativeUninstallerDriverTests.cs ] && echo FOUND
git log --oneline | grep -E "ac27c65|4610b8d|10efd7b"
```

Run results during execution:

- `src/DiskScout.App/Models/UninstallExecution.cs` — FOUND (commit `ac27c65`)
- `src/DiskScout.App/Services/INativeUninstallerDriver.cs` — FOUND (commit `ac27c65`)
- `src/DiskScout.App/Services/NativeUninstallerDriver.cs` — FOUND (commits `4610b8d` initial parser, `10efd7b` RunAsync)
- `tests/DiskScout.Tests/NativeUninstallerDriverTests.cs` — FOUND (commits `4610b8d` parser tests, `10efd7b` RunAsync tests)
- `src/DiskScout.App/AssemblyInfo.cs` — modified, contains `InternalsVisibleTo("DiskScout.Tests")` (commit `10efd7b`)
- All 3 commit hashes present in `git log`.
- `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug` — exit 0, 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName~NativeUninstallerDriverTests"` — 20/20 pass.
- `dotnet test` (full suite) — 49/49 pass; no regressions.

---
*Phase: 09-programs-tab-real-uninstaller-assistant*
*Completed: 2026-04-25*
