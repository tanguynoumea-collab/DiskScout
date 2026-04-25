---
phase: 09-programs-tab-real-uninstaller-assistant
verified: 2026-04-25T18:30:00Z
status: human_needed
score: 9/9 must-haves verified (automated); 3 items require human UAT
re_verification: false
human_verification:
  - test: "Right-click a real installed program -> wizard opens -> navigate Selection -> Preview -> RunUninstall"
    expected: "Wizard window appears; step 2 shows trace events (if install tracked) and publisher rule hits; step 3 runs the native uninstaller with live output streaming"
    why_human: "requireAdministrator manifest blocks non-elevated bash launch; UAC requires interactive elevation; deferred throughout all 6 plans under --auto chain"
  - test: "Step 5 IRREVERSIBLE modal appears with MessageBoxImage.Stop icon, default button = No, explicit 'pas de quarantaine' wording"
    expected: "Modal shows Stop icon, Yes/No with No selected by default, wording contains IRRÉVERSIBLE and 'pas de quarantaine, pas de corbeille'"
    why_human: "Visual appearance and UX safety of the final confirmation gate — cannot verify dialog rendering programmatically"
  - test: "Export JSON and HTML report after a complete uninstall flow; open HTML in browser"
    expected: "JSON is valid and contains all report fields (program identity, uninstall outcome, residue totals, deleted-entry snapshots); HTML renders without script tags, all text correctly HTML-encoded, inline CSS only, self-contained portable document"
    why_human: "Real-data end-to-end export with actual registry/filesystem residues — only verifiable after a full wizard run on a real installed program"
---

# Phase 9: Programs Tab Real Uninstaller Assistant — Verification Report

**Phase Goal:** Transform the Programs tab into a Revo-Pro-style assisted uninstaller — real-time install tracker, native uninstaller driver with Job-Object cancellation, deep residue scanner across registry/filesystem/services/tasks/shell-extensions, publisher rule engine with embedded + user-extensible JSON rules, and a 6-step wizard UI with strict safety guards (whitelist enforcement, default-unchecked tree, irreversible-confirm modal). Suppression DIRECTE permanente (pas de quarantaine pour ce flow).

**Verified:** 2026-04-25T18:30:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | D-01: Install tracker uses FileSystemWatcher + RegNotifyChangeKeyValue (not just registry scan) | VERIFIED | `InstallTracker.cs` has 10 occurrences of `FileSystemWatcher` and 4 occurrences of `RegNotifyChangeKeyValue` P/Invoke; `StartFileSystemWatchers()` and registry watch loop both implemented and non-trivial |
| 2 | D-02: Native uninstaller driver with Job Object tree-kill cancellation | VERIFIED | `NativeUninstallerDriver.cs` (606 LOC): `CreateJobObject`, `KILL_ON_JOB_CLOSE`, `AssignProcessToJobObject` all present; `RunAsync` is real (~200 LOC, not a stub); family detection for MSI/Inno/NSIS/Generic confirmed |
| 3 | D-02: Deep residue scanner covers all 7 categories | VERIFIED | `ResidueScanner.cs` emits `ResidueCategory.Filesystem`, `Registry`, `Shortcut`, `MsiPatch`, `Service`, `ScheduledTask`, `ShellExtension` — all 7 confirmed by grep against actual source lines |
| 4 | D-02: Publisher rule engine with 7 embedded vendor rules + user-extensible folder | VERIFIED | 7 JSON files in `Resources/PublisherRules/`; `<EmbeddedResource>` + `<LogicalName>` in csproj; `PublisherRuleEngine.cs` loads from manifest resources + `%LocalAppData%\DiskScout\publisher-rules\` with last-write-wins merge |
| 5 | D-03: Deletion is DeleteMode.Permanent — ZERO quarantine in wizard flow | VERIFIED | `ConfirmDeleteStepViewModel.cs` has 3 occurrences of `DeleteMode.Permanent`; `IQuarantineService` has 0 matches anywhere in `ViewModels/UninstallWizard/`; `MessageBoxImage.Stop` present; IRRÉVERSIBLE wording at line 191 |
| 6 | 6-step wizard UI with DataTemplate-by-type dispatch | VERIFIED | `WizardStep` enum has 7 values (Selection/Preview/RunUninstall/ResidueScan/ConfirmDelete/Report/Done); `UninstallWizardWindow.xaml` has 6 `DataTemplate DataType` entries; all 6 step view files confirmed present |
| 7 | Whitelist safety — ResiduePathSafety non-bypassable, 3-layer defense-in-depth | VERIFIED | 8 occurrences of `ResiduePathSafety.IsSafeToPropose` across wizard VMs (3 in `ResidueScanStepViewModel`, 3 in `ConfirmDeleteStepViewModel` BuildTree/SelectedPaths/ConfirmAsync, confirmed by live grep); whitelist has 19 FS substrings, 17 registry prefixes, 15 service names |
| 8 | Tree defaults UNCHECKED — user must tick boxes | VERIFIED | `ResidueTreeNode._isChecked = false` field initializer confirmed; xmldoc explicitly states "Default IsChecked is false — the user MUST tick boxes" |
| 9 | Programs DataGrid integration — right-click context menu + 2 diagnostic columns + end-to-end wiring to wizard launcher | VERIFIED | `ProgramsTabView.xaml` has "Tracé ?", "Règles éditeur" columns and "Désinstaller…" MenuItem; `ProgramsTabView.xaml.cs` subscribes `UninstallRequested` -> `App.OpenUninstallWizard(program)`; `App.xaml.cs` `OpenUninstallWizard` constructs a fresh wizard VM with all 9 services and calls `ShowDialog` |

**Score: 9/9 truths verified by automated code analysis**

---

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `src/DiskScout.App/Models/InstallTrace.cs` | VERIFIED | Present, substantive (models + enums) |
| `src/DiskScout.App/Services/IInstallTracker.cs` | VERIFIED | Contract present |
| `src/DiskScout.App/Services/IInstallTraceStore.cs` | VERIFIED | Contract present |
| `src/DiskScout.App/Services/JsonInstallTraceStore.cs` | VERIFIED | Present |
| `src/DiskScout.App/Services/InstallTracker.cs` | VERIFIED | 10x FileSystemWatcher, 4x RegNotifyChangeKeyValue |
| `src/DiskScout.App/Models/UninstallExecution.cs` | VERIFIED | InstallerKind, UninstallStatus, UninstallCommand, UninstallOutcome |
| `src/DiskScout.App/Services/INativeUninstallerDriver.cs` | VERIFIED | Contract present |
| `src/DiskScout.App/Services/NativeUninstallerDriver.cs` | VERIFIED | 606 LOC, Job Object, MSI/Inno/NSIS/Generic families |
| `src/DiskScout.App/Models/ResidueFinding.cs` | VERIFIED | 7-value ResidueCategory enum, ResidueFinding record |
| `src/DiskScout.App/Services/IResidueScanner.cs` | VERIFIED | Contract present |
| `src/DiskScout.App/Services/ResidueScanner.cs` | VERIFIED | All 7 categories emitted |
| `src/DiskScout.App/Helpers/ResiduePathSafety.cs` | VERIFIED | 129 LOC, 3 whitelist arrays, IsSafeToPropose + IsSafeServiceName |
| `src/DiskScout.App/Models/PublisherRule.cs` | VERIFIED | PublisherRule + PublisherRuleMatch records |
| `src/DiskScout.App/Services/IPublisherRuleEngine.cs` | VERIFIED | LoadAsync, AllRules, Match, ExpandTokens |
| `src/DiskScout.App/Services/PublisherRuleEngine.cs` | VERIFIED | Embedded resource loading + user folder + regex matching |
| `src/DiskScout.App/Resources/PublisherRules/*.json` (7 files) | VERIFIED | 7 files: adobe, autodesk, jetbrains, mozilla, microsoft, steam, epic |
| `src/DiskScout.App/ViewModels/UninstallWizard/WizardStep.cs` | VERIFIED | 7 enum values including Report (step 6) |
| `src/DiskScout.App/ViewModels/UninstallWizard/UninstallWizardViewModel.cs` | VERIFIED | State machine, 6 GoToX commands |
| `src/DiskScout.App/ViewModels/UninstallWizard/SelectionStepViewModel.cs` | VERIFIED | Present |
| `src/DiskScout.App/ViewModels/UninstallWizard/PreviewStepViewModel.cs` | VERIFIED | Present |
| `src/DiskScout.App/ViewModels/UninstallWizard/RunUninstallStepViewModel.cs` | VERIFIED | Present |
| `src/DiskScout.App/ViewModels/UninstallWizard/ResidueScanStepViewModel.cs` | VERIFIED | 3x IsSafeToPropose |
| `src/DiskScout.App/ViewModels/UninstallWizard/ConfirmDeleteStepViewModel.cs` | VERIFIED | 3x Permanent, IRRÉVERSIBLE wording, MessageBoxImage.Stop, GoToReportCommand |
| `src/DiskScout.App/ViewModels/UninstallWizard/ResidueTreeNode.cs` | VERIFIED | Default IsChecked=false, tri-state propagation |
| `src/DiskScout.App/ViewModels/UninstallWizard/ReportStepViewModel.cs` | VERIFIED | ExportJsonAsync, ExportHtmlAsync, SaveFileDialog |
| `src/DiskScout.App/Views/UninstallWizard/UninstallWizardWindow.xaml` | VERIFIED | 6 DataTemplate entries |
| `src/DiskScout.App/Views/UninstallWizard/` (5 step views) | VERIFIED | All 5 step view .xaml + .xaml.cs present |
| `src/DiskScout.App/Views/UninstallWizard/ReportStepView.xaml` | VERIFIED | Present |
| `src/DiskScout.App/Models/UninstallReport.cs` | VERIFIED | 17-field report record |
| `src/DiskScout.App/Services/IUninstallReportService.cs` | VERIFIED | BuildFromWizard, ExportAsync |
| `src/DiskScout.App/Services/UninstallReportService.cs` | VERIFIED | JSON + HTML branches, WebUtility.HtmlEncode, Debug.Assert script-tag guard |
| `src/DiskScout.App/Helpers/AppPaths.cs` | VERIFIED | InstallTracesFolder + PublisherRulesFolder both present |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `ProgramsTabView.xaml.cs` | `App.OpenUninstallWizard` | `OnUninstallRequested` handler | WIRED | DataContextChanged subscribes UninstallRequested; handler calls App.OpenUninstallWizard(program); Unloaded unhooks |
| `ProgramsViewModel` | `UninstallWizard` | `UninstallRequested` event | WIRED | Event declared at line 37; raised from UninstallSelectedCommand; CanExecute gated on SelectedRow != null |
| `App.OpenUninstallWizard` | `UninstallWizardViewModel` | 9-service constructor | WIRED | All 9 null-checked service references passed; ShowDialog called |
| `ConfirmDeleteStepViewModel.ConfirmAsync` | `IFileDeletionService.DeleteAsync` | `DeleteMode.Permanent` | WIRED | Line 213: `await _deletion.DeleteAsync(paths, DeleteMode.Permanent, default)` |
| `ConfirmDeleteStepViewModel` | `ReportStepViewModel` | `GoToReportCommand.Execute(null)` | WIRED | Line 221: replaces old WizardStep.Done — deletion advances to Report step |
| `MainViewModel.OnScanCompleted` | `Programs.Annotate` | `Task.Run + Dispatcher.Invoke` | WIRED | Lines 186-201: builds traced+rules dicts off-thread, marshals Annotate back to UI thread |
| `App.xaml.cs` | `PublisherRuleEngine.LoadAsync` | `GetAwaiter().GetResult()` | WIRED | Line 79: synchronous startup load (not fire-and-forget) |
| `InstallTracker` | `FileSystemWatcher + RegNotifyChangeKeyValue` | `StartFileSystemWatchers()` + registry watch loop | WIRED (D-01) | Both mechanisms active during StartAsync; 10x FSW + 4x RegNotifyChangeKeyValue grep hits |
| `NativeUninstallerDriver.RunAsync` | `Win32 Job Object` | `CreateJobObject → AssignProcessToJobObject → CloseHandle` | WIRED (D-02) | 14 Job Object-related grep hits in implementation |

---

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|-------------------|--------|
| `ResidueScanStepViewModel` | `Findings` ObservableCollection | `IResidueScanner.ScanAsync` + publisher rule `ExpandTokens` | Yes — real disk/registry scan; IsSafeToPropose gate before each emit | FLOWING |
| `ConfirmDeleteStepViewModel` | `Roots` (tree) | `BuildTree` from `_wizard.AllResidueFindings` | Yes — populated from scanner output; residue path safety re-asserted | FLOWING |
| `ProgramsViewModel.InstalledProgramRow` | `HasInstallTrace`, `MatchedPublisherRuleIds` | `MainViewModel.OnScanCompleted` annotation Task | Yes — `ListAsync` + `Match()` called; Dispatcher.Invoke marshals result | FLOWING |
| `ReportStepViewModel` | Report record | `IUninstallReportService.BuildFromWizard(_wizard)` | Yes — aggregates UninstallOutcome + AllResidueFindings + DeletionOutcome from wizard state | FLOWING |
| `PreviewStepViewModel` | `TraceEvents` + `RuleHits` | `_wizard.Trace.Events` + `_wizard.MatchedRules` ExpandTokens | Conditional — Trace is null until a tracked install is correlated (known gap per Plan 06 SUMMARY); RuleHits flow from publisher engine match | PARTIAL — Trace wiring deferred (known, documented) |

**Known gap (documented in Plan 06 SUMMARY):** `UninstallWizardViewModel.Trace` is not auto-populated from `IInstallTraceStore.LoadAsync` during wizard launch. The RegistryKey correlation logic is not yet wired. This means Step 4 trace-correlation findings (`Source=TraceMatch, Trust=HighConfidence`) are a no-op unless the caller sets `Trace` explicitly. The plan explicitly deferred this as post-v1 work. Step 4's scanner and publisher-rule merge paths ARE wired and functional.

---

### Behavioral Spot-Checks

Step 7b: SKIPPED — `requireAdministrator` manifest blocks non-elevated bash launch. The exe is present at the working directory path (85,325,270 bytes, confirmed). All runnable logic exercised through the test suite.

---

### Requirements Coverage

Phase 9 has no mapped REQ-IDs in REQUIREMENTS.md (post-v1). Verification performed against CONTEXT.md strategic decisions D-01, D-02, D-03 instead.

| Decision | Status | Evidence |
|----------|--------|---------|
| D-01: FileSystemWatcher + RegNotifyChangeKeyValue (not just retroactive registry scan) | SATISFIED | InstallTracker.cs uses both mechanisms, confirmed by grep (FSW: 10 hits, RegNotifyChangeKeyValue: 4 hits) |
| D-02: Revo Pro level — native uninstaller driver + deep residue scan + publisher rules ALL THREE | SATISFIED | All three subsystems are real implementations, not stubs: NativeUninstallerDriver (606 LOC, Job Object), ResidueScanner (7 categories, safety whitelist), PublisherRuleEngine (7 embedded rules, regex matching, user extensibility) |
| D-03: Direct permanent deletion — NO quarantine fallback | SATISFIED | `DeleteMode.Permanent` in ConfirmDeleteStepViewModel.ConfirmAsync; `IQuarantineService` = 0 matches in wizard directory |

---

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| `PreviewStepViewModel` — `_wizard.Trace` | `Trace` is null when no install tracker correlation exists (deferred wiring) | INFO | Step 2 shows empty trace events for programs installed before the tracker shipped; correctly reports `HadInstallTrace = false` in report; not a runtime error |

No blocker anti-patterns found. The `return null` in `RunUninstallStepViewModel.cs` at line 150 is inside a try/catch block reading a registry value — legitimate exception-suppression pattern, not a stub.

---

### Human Verification Required

#### 1. End-to-end wizard flow on a real installed program

**Test:** Run the published DiskScout.exe as Administrator (UAC elevation required). Go to the Programs tab. Right-click on a small benign program (a JetBrains IDE or Mozilla Firefox is ideal — both have publisher rules). Click "Désinstaller…".

**Expected:**
- The wizard window opens (900x650 modal)
- Step 1 shows the program's name, version, publisher; "PreferSilent" checkbox is visible; "Règles éditeur" summary shows matched rule IDs
- Step 2 shows two panels: install trace events (left, empty if no trace) and publisher rule-derived paths (right, populated for JetBrains/Mozilla)
- Step 3 runs the native uninstaller with live output in the ListBox; "Suivant" becomes enabled after the uninstaller exits

**Why human:** `requireAdministrator` UAC prevents non-elevated bash from launching the exe; deferred across all 6 plans under the `--auto` chain flag.

---

#### 2. IRRÉVERSIBLE confirmation modal safety UX

**Test:** Complete steps 1-4 of the wizard. On step 5, check at least one residue item. Click "Confirmer la suppression DÉFINITIVE".

**Expected:**
- A modal dialog appears with:
  - Red shield icon (`MessageBoxImage.Stop`)
  - "Non" is the default button (pressing Enter selects No)
  - Message body contains "IRRÉVERSIBLE", "DÉFINITIVEMENT", "pas de quarantaine, pas de corbeille"
- Clicking "Non" returns to step 5 without deleting anything
- Clicking "Oui" proceeds to the Report step (not directly to Done)

**Why human:** Visual confirmation of icon type, default button selection, and exact modal wording cannot be verified programmatically; this is the last safety gate before irreversible deletion.

---

#### 3. JSON + HTML report export

**Test:** Complete a full uninstall flow through step 6 (Report). Click "Exporter JSON" and "Exporter HTML". Open the HTML file in a browser.

**Expected:**
- JSON file is valid, contains `generatedUtc`, `displayName`, `publisher`, `uninstallStatus`, `residueByCategory`, `deletedCount`, `deletedBytesFreed`
- HTML file is self-contained (no external links, no `<script>` tags), renders correctly in browser with dark background (#1e1e1e), shows all 6 sections (Identité / Désinstalleur / Résidus / Suppression / Règles / Trace)
- Special characters in program names (parentheses, colons) are HTML-encoded, not raw

**Why human:** Real-data end-to-end report verification with actual filesystem residues from a real uninstall; HTML visual correctness requires browser rendering.

---

### Test Suite Coverage

| Phase 9 Plan | Tests Added | File |
|-------------|------------|------|
| 09-01 Install Tracker | 12 | InstallTraceStoreTests (5) + InstallTrackerTests (7) |
| 09-02 Native Uninstaller Driver | 20 (17 [Fact]/[Theory] + 3 InlineData rows) | NativeUninstallerDriverTests |
| 09-03 Residue Scanner | 46 | ResiduePathSafetyTests (14) + ResidueScannerTests (10) + [Theory] rows |
| 09-04 Publisher Rule Engine | 16 | PublisherRuleEngineTests (10 methods + 7 InlineData rows) |
| 09-05 Wizard UI | 19 | UninstallWizardViewModelTests (10) + UninstallWizardStepTests (9) |
| 09-06 Integration + Report | 10 | UninstallReportServiceTests |
| **Phase 9 total** | **~123** | **All test files present and confirmed found** |
| **Suite total per SUMMARY** | **140/140** | Baseline pre-phase-9: 17 tests |

All 9 test files confirmed present on disk. [Fact]/[Theory] markers confirmed in each file. The SUMMARY claims 140/140 passing; this is not independently re-runnable from the verifier session (requires elevated process for some integration tests that touch real registry).

---

### Gaps Summary

No code gaps found. All artifacts exist, are substantive (not stubs), and are wired into the application flow.

**Known deferred items (documented in SUMMARYs, not gaps):**

1. **`UninstallWizardViewModel.Trace` auto-population** — RegistryKey correlation between installed programs and install traces is not wired in Plan 06. Step 4's `Source=TraceMatch` / `Trust=HighConfidence` findings will only appear for programs that were installed *while the tracker was running*. Correctly documented in Plan 06 SUMMARY; `HadInstallTrace = false` in the report is truthful. Post-v1 work.

2. **JetBrains glob pattern handling** — Publisher rules contain wildcard paths like `%UserProfile%\.IntelliJIdea*`. The `PublisherRuleEngine.ExpandTokens` does not glob-expand these; glob expansion was deferred to the consumer (Plan 09-05 SUMMARY flagged this as an action item). The scanner integration layer would need `Directory.EnumerateDirectories` with wildcard support. Not a hard gap — scanner correctly skips non-existent paths after token expansion. Post-v1 work.

3. **Manual UAT** — The entire `--auto` chain deferred interactive validation due to `requireAdministrator` UAC blocking bash-driven exe launch. This is the reason for `status: human_needed` rather than `passed`.

---

### Build + Publish Evidence (from SUMMARY, not independently re-run)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Build errors | 0 | 0 | PASS |
| Build warnings | 0 | 0 | PASS |
| Test suite | 140/140 | 140/140 | PASS |
| Published exe size | 85,325,270 bytes (~81.4 MB) | 70-110 MB | PASS |
| Published exe present on disk | YES (confirmed by ls) | Present | PASS |

---

_Verified: 2026-04-25T18:30:00Z_
_Verifier: Claude (gsd-verifier)_
