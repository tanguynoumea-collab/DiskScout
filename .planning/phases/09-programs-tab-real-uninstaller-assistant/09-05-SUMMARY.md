---
phase: 09-programs-tab-real-uninstaller-assistant
plan: 05
subsystem: ui
tags: [wpf-mvvm, wizard-state-machine, communitytoolkit-mvvm, treeview-tristate, hierarchicaldatatemplate, datatemplate-by-type, residue-path-safety-defense-in-depth, deletemode-permanent, irreversible-modal, programs-datagrid, contextmenu, manual-di]

# Dependency graph
requires:
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: IInstallTracker + IInstallTraceStore (Plan 09-01) — wired into wizard
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: INativeUninstallerDriver (Plan 09-02) — drives Step 3 (RunUninstall)
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: IResidueScanner + ResiduePathSafety + ResidueFinding (Plan 09-03) — drives Step 4
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: IPublisherRuleEngine + PublisherRule + ExpandTokens (Plan 09-04) — drives Step 2 + Step 4 merge
  - phase: 01-foundations
    provides: IFileDeletionService + DeleteMode.Permanent + DeletePrompt.ShowResult — drives Step 5 deletion
provides:
  - UninstallWizardViewModel + 5 step ViewModels (Selection / Preview / RunUninstall / ResidueScan / ConfirmDelete)
  - UninstallWizardWindow + 5 step Views with DataTemplate-by-type pattern
  - ResidueTreeNode (tri-state checkable hierarchy, default UNCHECKED, child propagation)
  - ProgramsTabView right-click "Désinstaller…" context menu + 2 diagnostic columns ("Tracé ?", "Règles éditeur")
  - App.OpenUninstallWizard(InstalledProgram) static helper (cached service references in App.xaml.cs)
  - ProgramsViewModel.Annotate(traced, rules) seam — Plan 06 will populate the dictionaries
affects: [09-06-integration-report]

# Tech tracking
tech-stack:
  added: []  # No new NuGet packages — all WPF + CommunityToolkit.Mvvm + BCL
  patterns:
    - "Wizard pattern: top-level VM holds CurrentStep enum + CurrentStepViewModel ObservableObject; window XAML <DataTemplate DataType=...> per step VM type swaps the active view via ContentControl"
    - "Defense-in-depth: ResiduePathSafety.IsSafeToPropose re-asserted in BuildTree, in SelectedPaths getter, AND at ConfirmAsync — even though scanner already filtered upstream"
    - "Tri-state TreeView checkbox via bool? IsChecked + IsThreeState=True; partial OnIsCheckedChanged propagates to children when not null"
    - "Per-leaf PropertyChanged subscription drives RecomputeTotals — ObservableCollection<ResidueTreeNode> Roots, no manual binding refresh needed"
    - "Hand-written test fakes (no Moq dep — project policy precedent from Plans 09-01 / 09-03 / 09-04)"
    - "Acceptable code-behind: PreviewStepView.xaml.cs handles Loaded -> vm.Load(); ProgramsTabView.xaml.cs subscribes UninstallRequested -> App.OpenUninstallWizard(p). Both are pure UI ceremony, no business logic."
    - "Cached services in App.xaml.cs (private fields + static Instance) — modal wizard re-entry from any program row without re-constructing services"
    - "Test pattern for Progress<string>: report with 30ms spacing because xUnit has no SyncContext (production WPF Dispatcher serializes callbacks; test thread pool would race on ObservableCollection.Add)"

key-files:
  created:
    - src/DiskScout.App/ViewModels/UninstallWizard/WizardStep.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/UninstallWizardViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/SelectionStepViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/PreviewStepViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/RunUninstallStepViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/ResidueScanStepViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/ConfirmDeleteStepViewModel.cs
    - src/DiskScout.App/ViewModels/UninstallWizard/ResidueTreeNode.cs
    - src/DiskScout.App/Views/UninstallWizard/UninstallWizardWindow.xaml
    - src/DiskScout.App/Views/UninstallWizard/UninstallWizardWindow.xaml.cs
    - src/DiskScout.App/Views/UninstallWizard/SelectionStepView.xaml
    - src/DiskScout.App/Views/UninstallWizard/SelectionStepView.xaml.cs
    - src/DiskScout.App/Views/UninstallWizard/PreviewStepView.xaml
    - src/DiskScout.App/Views/UninstallWizard/PreviewStepView.xaml.cs
    - src/DiskScout.App/Views/UninstallWizard/RunUninstallStepView.xaml
    - src/DiskScout.App/Views/UninstallWizard/RunUninstallStepView.xaml.cs
    - src/DiskScout.App/Views/UninstallWizard/ResidueScanStepView.xaml
    - src/DiskScout.App/Views/UninstallWizard/ResidueScanStepView.xaml.cs
    - src/DiskScout.App/Views/UninstallWizard/ConfirmDeleteStepView.xaml
    - src/DiskScout.App/Views/UninstallWizard/ConfirmDeleteStepView.xaml.cs
    - tests/DiskScout.Tests/UninstallWizardViewModelTests.cs
    - tests/DiskScout.Tests/UninstallWizardStepTests.cs
  modified:
    - src/DiskScout.App/ViewModels/ProgramsViewModel.cs (added SelectedRow + UninstallSelectedCommand + UninstallRequested event + Annotate + InstalledProgramRow.HasInstallTrace/MatchedPublisherRuleIds)
    - src/DiskScout.App/Views/Tabs/ProgramsTabView.xaml (added "Tracé ?" + "Règles éditeur" columns + DataGrid.ContextMenu with "Désinstaller…" MenuItem)
    - src/DiskScout.App/Views/Tabs/ProgramsTabView.xaml.cs (subscribes ProgramsViewModel.UninstallRequested -> App.OpenUninstallWizard)
    - src/DiskScout.App/App.xaml.cs (cached 4 Phase-9 services + static OpenUninstallWizard launcher)

key-decisions:
  - "Combined Task 1 + Task 2 step VMs in one commit (6b92ae4): the step-VM class shapes are required for compilation of the test file, and the business logic is short enough that splitting RED/GREEN per step adds no incremental insight. This matches the precedent from Plans 09-01 / 09-02 / 09-03 / 09-04 (impl + tests in same commit)."
  - "Defense-in-depth: ResiduePathSafety.IsSafeToPropose is called in 3 places inside the wizard (BuildTree leaf filter, SelectedPaths getter, and final ConfirmAsync). The scanner (Plan 09-03) already filters, but the wizard re-asserts because a corrupted AllResidueFindings list could otherwise reach deletion. This is non-negotiable per the user's safety requirement."
  - "ConfirmDeleteStepViewModel uses System.Windows.MessageBox directly with MessageBoxImage.Stop + default=No + YesNo buttons. Did NOT route through DeletePrompt.Ask because that prompt offers 3 modes (Quarantine/Recycle/Permanent) — D-03 forbids quarantine for this flow, so a dedicated 2-button modal with explicit IRRÉVERSIBLE wording is correct."
  - "DeletePrompt.ShowResult IS reused for the post-deletion summary modal — that one is mode-agnostic and presents counts/bytes consistently with the other tabs."
  - "App.xaml.cs uses cached service references + static Instance singleton instead of injecting the wizard VM into MainViewModel. Rationale: the wizard is a true modal sub-flow, not a tab — every Programs row launch needs the same services with no per-call DI rewiring. Static accessor is the smallest possible API."
  - "ProgramsViewModel.Annotate(traced, rules) takes nullable IDictionary parameters so Plan 06 can populate them when the integration is wired. For this plan, MainViewModel.OnScanCompleted does NOT call Annotate (commented-out hook deferred to Plan 06 per the plan's explicit instructions)."
  - "PreviewStepView.xaml.cs hooks the Loaded event to call vm.Load() — acceptable code-behind because it's a view-model lifecycle event (initial population), not business logic."
  - "DataGrid context-menu binding uses RelativeSource AncestorType=ContextMenu + PlacementTarget.DataContext — the standard WPF idiom because ContextMenu lives outside the visual tree and doesn't inherit DataContext."
  - "Test progress-streaming uses 30ms spacing between progress.Report calls in the fake: xUnit has no SyncContext, so Progress<T> dispatches each callback to the thread pool concurrently; without spacing, ObservableCollection.Add races. Production WPF (Dispatcher's SyncContext) serializes them naturally."

patterns-established:
  - "Pattern: Wizard state machine with WizardStep enum + ObservableObject CurrentStepViewModel + DataTemplate-by-type in the Window resources. Reusable for any future multi-step modal flow."
  - "Pattern: Tri-state TreeView checkbox via bool? IsChecked + IsThreeState=True + partial OnIsCheckedChanged propagating to children when value is not null. Standard MVVM tristate without a converter."
  - "Pattern: Defense-in-depth via ResiduePathSafety re-assertion at every layer that touches paths leading to deletion (scanner -> tree -> selection -> confirm). One whitelist, three checkpoints."
  - "Pattern: App-level static service cache + OpenXxxWizard helper for modal sub-flows that need DI but aren't part of the main shell's view-model graph."
  - "Pattern: Per-step CTS managed by the parent wizard (DisposeAndRecreateCts at GoToRunUninstall / GoToResidueScan transitions) — ensures a Cancel during ResidueScan doesn't poison a future RunUninstall re-entry."

requirements-completed: []  # Phase 9 has no mapped REQ-IDs (post-v1, see ROADMAP.md note)

# Metrics
duration: 13m 19s
completed: 2026-04-25
---

# Phase 09 Plan 05: Uninstall Wizard UI Summary

**Modal 5-step Uninstall Wizard wired to all 4 Wave-1 services (InstallTracker / NativeUninstallerDriver / ResidueScanner / PublisherRuleEngine), with non-bypassable ResiduePathSafety re-assertion at every path-handling layer, IRRÉVERSIBLE final modal, and DeleteMode.Permanent (NEVER quarantine per CONTEXT.md D-03).**

## Auto-Approval Note

This plan was executed under the orchestrator's `--auto` chain flag. Task 4 was a `checkpoint:human-verify` UAT for the modal wizard flow. **The checkpoint was auto-approved** per the auto-mode override in the executor prompt.

Acceptance evidence captured in lieu of manual UAT:

1. **Build verification:** `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug` — exits 0, 0 warnings, 0 errors.
2. **Test verification:** `dotnet test --filter "FullyQualifiedName~UninstallWizard"` — 19 / 19 pass (10 wizard + 9 step). Full suite 130 / 130 (no regressions; baseline 111).
3. **Grep cross-checks** (all from `<verification>` block of PLAN):
   - `DeleteMode.Permanent`: 4 occurrences in `ConfirmDeleteStepViewModel.cs` including the actual `_deletion.DeleteAsync(paths, DeleteMode.Permanent, default)` call ✓
   - `IRRÉVERSIBLE`: 2 occurrences in `ConfirmDeleteStepViewModel.cs` (xmldoc + modal body) ✓
   - `ResiduePathSafety.IsSafeToPropose`: 6 occurrences (3 in `ConfirmDeleteStepViewModel.cs` BuildTree+SelectedPaths+ConfirmAsync, 3 in `ResidueScanStepViewModel.cs`) — defense-in-depth requirement met (≥2 hits required, we have 6) ✓
   - `IQuarantineService`: 0 matches in any Phase 9 wizard file — D-03 honored, NO quarantine in wizard flow ✓
   - `MessageBoxImage.Stop`: 1 occurrence in `ConfirmDeleteStepViewModel.cs` (irreversible-confirmation modal) ✓
4. **Programs DataGrid columns:** `grep -n "Tracé ?\|Règles éditeur\|Désinstaller…" src/DiskScout.App/Views/Tabs/ProgramsTabView.xaml` — all 3 strings present (lines 53, 54, 46 respectively) ✓.
5. **Manual launch (out of band):** The exe `requireAdministrator` manifest prevents non-elevated bash sessions from spawning it; the `--auto` chain documents that interactive UAT (right-click -> wizard navigation -> tree checkbox manipulation -> IRRÉVERSIBLE modal acceptance) was deferred. Build + test + grep evidence is the substitute. Plan 06's UAT will exercise the wizard end-to-end with a real installed program; this plan ships the buildable, testable surface.

## Performance

- **Duration:** 13m 19s
- **Started:** 2026-04-25T16:07:34Z
- **Completed:** 2026-04-25T16:20:54Z (approximate; included grep checks + summary write)
- **Tasks:** 3 auto + 1 auto-approved checkpoint = 4 / 4
- **Files created:** 22 (8 view-models + 12 view files [.xaml + .xaml.cs] + 2 test files)
- **Files modified:** 4 (ProgramsViewModel.cs, ProgramsTabView.xaml, ProgramsTabView.xaml.cs, App.xaml.cs)
- **Tests added:** 19 (10 view-model + 9 step business-logic), all passing
- **Total test suite:** 130 / 130 passing (baseline 111 from Plan 09-04, +19 from this plan, no regressions)

## Accomplishments

- **5-step Uninstall Wizard** end-to-end:
  - **Step 1 (Selection):** confirm program + PreferSilent toggle; shows MatchedRulesSummary for early diagnostic.
  - **Step 2 (Preview):** parallel ListBoxes — Trace events (left, from `_wizard.Trace.Events`) and Rule hits (right, from `_wizard.MatchedRules` ExpandTokens-resolved). Loaded event invokes `vm.Load()`.
  - **Step 3 (RunUninstall):** ParseCommand with silent->interactive fallback, Process via `INativeUninstallerDriver.RunAsync`, IProgress<string> output streaming with 1000-line cap, status banner. Next button enabled only after Outcome.Status is Success or NonZeroExit.
  - **Step 4 (ResidueScan):** `_residueScanner.ScanAsync` + merge of publisher-rule expansions (filesystemPaths => HighConfidence/LowConfidence depending on disk existence; registryPaths => MediumConfidence). Each finding re-asserts `ResiduePathSafety.IsSafeToPropose`.
  - **Step 5 (ConfirmDelete):** TreeView grouped by ResidueCategory; tri-state IsThreeState checkboxes; default UNCHECKED everywhere; live SelectedCount + TotalSelectedDisplay; final modal has explicit "DÉFINITIVEMENT", "IRRÉVERSIBLE", "pas de quarantaine, pas de corbeille" wording, MessageBoxImage.Stop, default=No.
- **Programs DataGrid integration:**
  - 2 new columns: "Tracé ?" (✓ when InstallTrace exists) and "Règles éditeur" (rule ids, comma-separated).
  - Right-click ContextMenu with "Désinstaller…" MenuItem bound to `UninstallSelectedCommand` via the `RelativeSource AncestorType=ContextMenu / PlacementTarget.DataContext` idiom.
  - `ProgramsViewModel.SelectedRow` two-way bound to DataGrid.SelectedItem; `UninstallRequested` event raised when the user invokes the menu.
- **App.xaml.cs DI changes:**
  - Caches the 4 Phase-9 services as private fields (InstallTracker / InstallTraceStore / UninstallDriver / ResidueScanner / PublisherRuleEngine + alias to FileDeletionService).
  - PublisherRuleEngine.LoadAsync fired-and-forgotten at startup (Match() returns empty until the load completes — acceptable for a manual user-initiated flow).
  - Static `OpenUninstallWizard(InstalledProgram)` constructs a fresh `UninstallWizardViewModel` per launch, sets it as the window's DataContext, hooks CloseRequested -> window.Close, and calls ShowDialog (modal).
- **Defense-in-depth safety guards** at 3 wizard layers, 6 grep hits on `ResiduePathSafety.IsSafeToPropose`:
  1. `ResidueScanStepViewModel.ScanAsync`: re-asserts on scanner output AND on rule-derived expansions (3 hits).
  2. `ConfirmDeleteStepViewModel.BuildTree`: filters unsafe paths from leaf addition (1 hit).
  3. `ConfirmDeleteStepViewModel.SelectedPaths` getter: filters unsafe paths from the selection list (1 hit).
  4. `ConfirmDeleteStepViewModel.ConfirmAsync`: re-derives the safe selection one final time before invoking deletion (1 hit).
- **Honors CONTEXT.md D-03:** 0 matches for `IQuarantineService` anywhere in the wizard flow. Deletion is `IFileDeletionService.DeleteAsync(paths, DeleteMode.Permanent, default)` — direct, irreversible, no quarantine net.
- **Honors plan must_haves truth #4:** every leaf checkbox starts UNCHECKED (`ResidueTreeNode._isChecked = false` field initializer); the user MUST tick boxes; live tally updates via `RecomputeTotals` driven by per-leaf PropertyChanged subscriptions.
- **19 unit tests** cover state-machine init, Selection→Preview→Selection navigation, Cancel cancels CTS + raises CloseRequested, ResidueTreeNode tri-state propagation, EnumerateLeavesChecked, ConfirmDelete defense-in-depth filter, RunUninstall silent->interactive fallback, IProgress<string> streaming (with thread-pool spacing for xUnit), Outcome propagation to wizard, ResidueScan rule-merge with PublisherRule source attribution, ConfirmDelete tree grouping by Category, SelectedPaths filtering, DeleteMode.Permanent invocation.

## Task Commits

Each task committed atomically with `--no-verify` (parallel-mode convention from earlier Phase 9 plans):

1. **Tasks 1+2 combined: Wizard scaffold + step business logic + 10 wizard view-model tests** — `6b92ae4` (feat)
2. **Task 2 (test file): 9 step business-logic tests** — `31ea66a` (test)
3. **Task 3: wizard XAML window + 5 step views + Programs DataGrid integration + App.xaml.cs DI** — `bf5bba0` (feat)

_Tasks 1 and 2's production code shipped together because the step VM class shapes are needed by the test compilation; the business-logic methods are short enough that an artificial RED/GREEN split would not have improved insight. The test file was committed separately to keep the test additions reviewable in isolation._

## Files Created/Modified

### Created (22 files)

**View-models (8 files)** — `src/DiskScout.App/ViewModels/UninstallWizard/`:

- `WizardStep.cs` — 6-value enum (Selection / Preview / RunUninstall / ResidueScan / ConfirmDelete / Done).
- `ResidueTreeNode.cs` — tri-state checkable hierarchy node (~80 LOC) with default IsChecked=false, child propagation, EnumerateLeavesChecked, SizeDisplay + TrustDisplay computed properties.
- `UninstallWizardViewModel.cs` — top-level state machine (~155 LOC) holding Target + 6 service deps + wizard CTS + GoToX commands + Cancel/Close + CloseRequested event.
- `SelectionStepViewModel.cs` — Step 1 VM (~50 LOC) with PreferSilent two-way to wizard, MatchedRulesSummary diagnostic.
- `PreviewStepViewModel.cs` — Step 2 VM (~75 LOC) with TraceEvents + RuleHits ObservableCollections, Load() invoked from view's Loaded event.
- `RunUninstallStepViewModel.cs` — Step 3 VM (~155 LOC) with RunAsync (driver wiring + silent fallback + Progress streaming), CanProceed gate, ReadQuietUninstallString helper.
- `ResidueScanStepViewModel.cs` — Step 4 VM (~165 LOC) with ScanAsync (scanner + rule-merge + 3x ResiduePathSafety re-assertion), MeasureFolderBytes + TryGetFileSize helpers.
- `ConfirmDeleteStepViewModel.cs` — Step 5 VM (~245 LOC) with BuildTree (defense-in-depth #1), SelectedPaths getter (defense-in-depth #2), ConfirmAsync (defense-in-depth #3 + IRRÉVERSIBLE modal + DeleteMode.Permanent + DeletePrompt.ShowResult), CategoryLabel French translations.

**Views (12 files: 6 .xaml + 6 .xaml.cs)** — `src/DiskScout.App/Views/UninstallWizard/`:

- `UninstallWizardWindow.xaml` + `.xaml.cs` — 900x650 modal Window with 5 DataTemplate-by-type entries.
- `SelectionStepView.xaml` + `.xaml.cs` — program details card + diagnostic card + PreferSilent CheckBox + Annuler/Suivant buttons.
- `PreviewStepView.xaml` + `.xaml.cs` — 2-column ListBox layout, code-behind hooks Loaded -> vm.Load().
- `RunUninstallStepView.xaml` + `.xaml.cs` — Status banner + ListBox of OutputLines + 4 buttons (Précédent/Annuler/Lancer/Suivant).
- `ResidueScanStepView.xaml` + `.xaml.cs` — CurrentArea label + DataGrid bound to Findings (6 columns) + 3 buttons.
- `ConfirmDeleteStepView.xaml` + `.xaml.cs` — TreeView with HierarchicalDataTemplate (CheckBox IsThreeState=True + Label + SizeDisplay + TrustDisplay + Reason), bottom bar with SelectedCount + TotalSelectedDisplay + "Confirmer la suppression DÉFINITIVE" button.

**Tests (2 files)** — `tests/DiskScout.Tests/`:

- `UninstallWizardViewModelTests.cs` — 10 tests covering state-machine + ResidueTreeNode semantics + ConfirmDelete tree filtering.
- `UninstallWizardStepTests.cs` — 9 tests covering RunUninstall driver wiring + ResidueScan rule-merge + ConfirmDelete deletion-mode/defense-in-depth.

### Modified (4 files)

- `src/DiskScout.App/ViewModels/ProgramsViewModel.cs` — added `SelectedRow` ObservableProperty, `UninstallRequested` event, `UninstallSelectedCommand` (RelayCommand with CanExecute=SelectedRow!=null), `Annotate(traced, rules)` API; `InstalledProgramRow` gained `internal Source` (for re-annotation + wizard launch), `HasInstallTrace`, `MatchedPublisherRuleIds`, `TracedDisplay`, `RuleDisplay` properties + 2 new constructor params with default values for backwards compat.
- `src/DiskScout.App/Views/Tabs/ProgramsTabView.xaml` — added 2 `<DataGridTextColumn>` ("Tracé ?" + "Règles éditeur"), `SelectedItem="{Binding SelectedRow, Mode=TwoWay}"`, `<DataGrid.ContextMenu>` with "Désinstaller…" `<MenuItem>` using `RelativeSource AncestorType=ContextMenu / PlacementTarget.DataContext` to reach the VM.
- `src/DiskScout.App/Views/Tabs/ProgramsTabView.xaml.cs` — subscribes to `ProgramsViewModel.UninstallRequested` (in DataContextChanged), routes `OnUninstallRequested` to `App.OpenUninstallWizard(program)`. Unhooks on Unloaded.
- `src/DiskScout.App/App.xaml.cs` — added 6 cached service fields + `private static App? Instance`; OnStartup constructs and caches `JsonInstallTraceStore`, `InstallTracker`, `NativeUninstallerDriver`, `ResidueScanner`, `PublisherRuleEngine` (with fired-and-forgotten LoadAsync), aliased `_fileDeletion`. Added `public static void OpenUninstallWizard(InstalledProgram)` that null-checks all caches, builds a fresh `UninstallWizardViewModel`, hooks `CloseRequested -> window.Close`, calls `ShowDialog`.

## Decisions Made

- **Combined Task 1 + Task 2 production code in `6b92ae4`.** The step view-model class shapes are required for the test file's compilation (the tests reference `RunUninstallStepViewModel` etc.), and each step's business logic is ~20-150 LOC of straight-line code. A "scaffold first, fill in second" split would have meant compiling stubs and then committing-then-replacing them — strictly worse than committing once with the full implementation. Plan 09-01/02/03/04 SUMMARY all noted the same precedent for combined commits when no incremental RED/GREEN value exists.
- **Defense-in-depth ResiduePathSafety re-assertion in 3 wizard layers, 6 total call sites.** The scanner already filters via `ResiduePathSafety.IsSafeToPropose` (Plan 09-03, line 232 of `ResidueScanner.cs`). The wizard re-asserts because (a) `_wizard.AllResidueFindings` is a public mutable list — a corrupted addition could otherwise reach BuildTree, (b) `ConfirmAsync` is the last gate before deletion and must verify against tampering, and (c) the user's safety requirement explicitly demands "no false positive ever sent to delete". 6 grep hits are the documented contract.
- **MessageBox.Show direct, not DeletePrompt.Ask.** `DeletePrompt.Ask` is the existing 3-mode chooser (Quarantaine / Corbeille / Définitif). For this flow D-03 forbids quarantine — re-using `Ask` would either (a) show an offer that's never valid here, or (b) require a flag-extension. Using `System.Windows.MessageBox.Show` directly with explicit IRRÉVERSIBLE wording, MessageBoxImage.Stop, default=No keeps the safety wording front-and-center and the modal smaller. `DeletePrompt.ShowResult` IS reused unchanged for the post-deletion summary.
- **App.xaml.cs static singleton + cached fields, not full DI container.** CLAUDE.md mandates manual DI ("≤20 services, keeps binary lean"). Adding `Microsoft.Extensions.DependencyInjection` for one modal flow would violate that. The static `App.OpenUninstallWizard` helper is the smallest possible API: one method, one instance, no scope juggling.
- **Annotate API takes nullable IDictionary, not concrete dictionaries.** Plan 06 will populate the dictionaries from `InstallTraceStore.ListAsync` + `PublisherRuleEngine.Match`. For this plan, `MainViewModel.OnScanCompleted` does NOT call Annotate (per the plan's instruction to leave the call commented out). Future-proofing the signature with nullables means Plan 06 can land without re-touching the API.
- **Test progress-spacing 30ms because xUnit has no SyncContext.** In production WPF, `Progress<T>` captures the Dispatcher's `SynchronizationContext` at construction time, so all callbacks dispatch sequentially on the UI thread — `OutputLines.Add` is naturally serialized. In xUnit, no SyncContext is captured, so callbacks fan out to the thread pool concurrently. Without inter-report spacing, three `progress.Report("line N")` calls within microseconds caused `ObservableCollection.Add` to race (one test run produced `{"line 1", "line 2", <null>}`). The 30ms `Task.Delay` between reports is a test-only fix; production code is correct as-is. This is documented in the test fixture xmldoc.
- **Wizard CTS managed at parent, not at step VM.** Each Step3/Step4 transition disposes the previous CTS and creates a new one (`DisposeAndRecreateCts`). This means a Cancel during ResidueScan that the user then re-opens won't re-poison a fresh RunUninstall — the step VMs receive the live CTS from the wizard via constructor. Cleaner than per-step independent CTS management.

## Deviations from Plan

### Plan instruction adjustment

**1. [Process — combined Task 1 + Task 2 commits]** The plan describes Task 1 as "scaffold + minimal contracts" and Task 2 as "fill in business logic". I shipped the full step business logic in Task 1 commit `6b92ae4` because:
- The test file (`UninstallWizardViewModelTests.cs`) references `ConfirmDeleteStepViewModel.TotalSelectedBytes` and other live properties — Task 1's "stub" version would not have been buildable with the test suite.
- The plan precedent (Plans 09-01/02/03/04 SUMMARYs) consistently chose combined commits over artificial RED/GREEN splits when no incremental insight existed.
- The `<acceptance_criteria>` for Task 1 references behaviors that require working business logic (`TotalSelectedBytes`, `EnumerateLeavesChecked`, `OnIsCheckedChanged`).

This is a process choice, not a code change — the final repo state is identical to what the plan's "scaffold + fill" decomposition would produce. Captured here for transparency.

### Auto-fixed Issues

**2. [Rule 3 — Blocking] Test parallelism race in `RunUninstall_StreamsProgressLinesIntoOutputLines`**

- **Found during:** Task 2 first test run.
- **Issue:** xUnit runs tests in parallel; `Progress<string>` callbacks dispatched to the thread pool (no SyncContext in xUnit) raced on `OutputLines.Add` when 3 progress reports fired in rapid succession from the test fake.
- **Fix:** Added 30ms spacing between `progress.Report` calls in the test fake's `RunImpl`, plus a polling wait-for-3-lines loop in the test body. Production code unaffected — production WPF Dispatcher serializes callbacks naturally.
- **Files modified:** `tests/DiskScout.Tests/UninstallWizardStepTests.cs`.
- **Verification:** Test passes deterministically after fix; full UninstallWizard suite 19/19 green; full repo suite 130/130 green.
- **Committed in:** `31ea66a` (Task 2 test file commit).

**3. [Rule 1 — Bug] Initial test for silent->interactive fallback asserted on transient Status string**

- **Found during:** Task 2 first test run.
- **Issue:** I initially asserted `step.Status.Should().Contain("interactive")` after the run completed, but in the implementation the Status is overwritten with the success message after the fallback command runs. The intermediate "Aucune variante silencieuse" status didn't survive past `RunAsync`'s success path.
- **Fix:** Replaced the brittle Status-string assertion with a robust ParseCallCount + Outcome.Status assertion: the fake records 2 calls to `ParseCommand`, and `step.Outcome.Status == Success` proves the second (preferSilent=false) call returned a runnable command. This is the actual behavior the test was meant to verify.
- **Files modified:** `tests/DiskScout.Tests/UninstallWizardStepTests.cs`.
- **Committed in:** `31ea66a`.

---

**Total deviations:** 1 process choice (combined commits) + 2 auto-fixed test issues. **Impact on plan:** Zero — no production code change beyond the plan's intent; only test-infrastructure ergonomics fixes that follow established Phase 9 precedent.

## Issues Encountered

- **Manifest `requireAdministrator` blocks bash exec.** Per the project's user-memory rule "always run the .exe after a successful build", I attempted `cmd /c start "" DiskScout.exe`. The exe doesn't run from a non-elevated shell (UAC requires interactive elevation). Manual UAT (right-click -> wizard navigation -> tree checkbox manipulation) was therefore deferred per the orchestrator's `--auto` chain flag; build + test + grep evidence is the substitute. This is a test-environment limitation, not a product bug — the prior 4 plans hit the same constraint.
- **TreeView IsThreeState requires bool? not bool.** `ResidueTreeNode.IsChecked` had to be `bool?` (nullable) to support the indeterminate state when the user partially selects children. The CheckBox in XAML uses `IsThreeState="True"` + `IsChecked="{Binding IsChecked, Mode=TwoWay}"` (no converter needed). Documented in the field's xmldoc.
- **`ContextMenu` doesn't inherit DataContext.** ContextMenu lives outside the visual tree, so its DataContext isn't the parent control's. The standard WPF idiom — `Command="{Binding PlacementTarget.DataContext.UninstallSelectedCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"` — bridges this gap. Captured in the ProgramsTabView.xaml comments via the binding expression itself.
- **ProgramsTabView.xaml.cs needs `DataContextChanged` because the tab is constructed before its DataContext is assigned.** A constructor-time subscription would null-deref. The pattern (subscribe in DataContextChanged, unsubscribe on Unloaded) is the WPF-correct approach for VM-driven events on a UserControl whose DataContext is assigned via DataTemplate / parent Tab Item.
- **`_wizard.PreferSilent` two-way binding via custom property in SelectionStepViewModel.** I deliberately did NOT bind directly to `_wizard.PreferSilent` because that would tie the step VM's lifetime to the wizard's PropertyChanged plumbing. Instead, `SelectionStepViewModel.PreferSilent` is a manual passthrough setter that raises `OnPropertyChanged()`. This is verbose but explicit; the trade-off is acceptable for one property.

## Test Counts

| Suite | Tests | Outcome |
|-------|-------|---------|
| UninstallWizardViewModelTests | 10 | 10 / 10 pass |
| UninstallWizardStepTests | 9 | 9 / 9 pass |
| **Plan 09-05 subtotal** | **19** | **19 / 19 pass** |
| Full DiskScout.Tests suite | 130 | 130 / 130 pass (no regressions; baseline was 111) |

The 19 cases break down as:
- **State machine (4):** init at Selection, Selection->Preview, Preview->Selection back, Cancel propagates to CTS + raises CloseRequested.
- **ResidueTreeNode (3):** parent-checked propagates, parent-unchecked propagates, EnumerateLeavesChecked filters.
- **ConfirmDelete (5):** TotalSelectedBytes/SelectedCount update, BuildTree drops unsafe paths (defense-in-depth), Wizard.GoToConfirmDelete sets CurrentStep, BuildTree groups by Category, SelectedPaths returns only checked-and-safe.
- **RunUninstall (3):** silent->interactive fallback (ParseCallCount), Progress<string> streams (with thread-pool spacing), Outcome propagates to wizard + CanProceed=true.
- **ResidueScan (2):** scanner + rule merge with PublisherRule source attribution, AllResidueFindings populated + Next enabled.
- **ConfirmDelete deletion contract (2):** SelectedPaths only returns checked-and-safe, IFileDeletionService invoked with DeleteMode.Permanent.

## Known Stubs

None for this plan's deliverables. The wizard flow is end-to-end functional in code: a user can right-click a program -> open the wizard -> navigate all 5 steps -> confirm DEFINITIVELY. The only deferred wiring is **ProgramsViewModel.Annotate** — `MainViewModel.OnScanCompleted` does NOT call it (per the plan's explicit instruction to defer to Plan 06). This means **the "Tracé ?" and "Règles éditeur" columns will always show empty** until Plan 06 wires the dictionaries. This is documented in the plan + here, not a stub: the columns + binding paths exist correctly; the data sources are intentionally unconnected pending Plan 06's integration step.

The Annotate API has the right signature for Plan 06 (`IDictionary<string, bool> tracedByRegistryKey, IDictionary<string, string> rulesByDisplayName`) — Plan 06 will:
1. List trace headers via `IInstallTraceStore.ListAsync`, build `traced[regKey] = true` for any program whose RegistryKey matches a trace's `InstallerProductHint`.
2. For each Programs row, call `IPublisherRuleEngine.Match(p.Publisher, p.DisplayName)` and join the `Rule.Id`s into a comma-separated string.
3. Call `Programs.Annotate(traced, rules)` after `Programs.Load(result.Programs)` in `OnScanCompleted`.

## False-Positive Risks

- **Modal IRRÉVERSIBLE wording is the user's last gate.** If a user clicks Yes by reflex, files are gone. Mitigations already in place: default button = "Non" (`MessageBoxResult.No`); icon = `MessageBoxImage.Stop` (red shield); explicit "DÉFINITIVEMENT", "IRRÉVERSIBLE", "pas de quarantaine, pas de corbeille" in the message body.
- **Tree starts UNCHECKED, but a user could shift-click or tick a parent in haste.** Mitigation: every leaf's path goes through the whitelist 3 times before deletion. A user-visible bytes counter ("Sélection : N élément(s), X.X Mo") gives an immediate sanity check; selecting 50 GB by accident is hard to miss.
- **PublisherRuleEngine's LoadAsync is fired-and-forgotten in OnStartup.** If a user opens the Programs tab + right-clicks within ~50ms of app launch, `_ruleEngine.Match` may return empty (the load hasn't completed). Worst case: the "Règles éditeur" column shows blank for affected rows. Plan 06 can `await` the load before showing the main window if this proves to be a real issue. For v1, the race window is tiny and the fallback is graceful (empty rule list).
- **`ProgramsViewModel.Annotate` not wired in this plan.** Per plan instructions, this is deferred to Plan 06. Until Plan 06 lands, the 2 new diagnostic columns will show empty. This is a known gap — not a regression.

## User Setup Required

None — the wizard is launched via right-click on the Programs DataGrid, no additional configuration. The 4 Phase-9 services use existing storage paths (`%LocalAppData%\DiskScout\install-traces`, `%LocalAppData%\DiskScout\publisher-rules`) that are auto-created on first access.

## Next Phase Readiness

- **Plan 09-06 (Integration + Report) — primary consumer:**
  - **Wire `ProgramsViewModel.Annotate` in `MainViewModel.OnScanCompleted`** after `Programs.Load(result.Programs)`. Build the two dictionaries:
    - `tracedByRegistryKey`: for each header from `IInstallTraceStore.ListAsync`, mark the matching program by `RegistryKey` (correlation via `InstallerProductHint`).
    - `rulesByDisplayName`: for each program, call `_ruleEngine.Match(p.Publisher, p.DisplayName)` and join `Rule.Id`s.
  - **Add the Report step (Step 6 / Done state)** that consumes `_wizard.UninstallOutcome` (Plan 09-02) + `_wizard.DeletionOutcome` (this plan) for an HTML/JSON export.
  - **Wire UninstallWizardViewModel.Trace** by calling `IInstallTraceStore.LoadAsync(traceId)` once we know the trace id corresponding to the Target. Currently `Trace` stays null and the Step 4 trace-correlation branch is a no-op.
  - The wizard view-model's `CurrentStep = WizardStep.Done` is set after a successful confirmed deletion (line 217 of `ConfirmDeleteStepViewModel.cs`); Plan 06 can hang the report panel off this state.
- **Manual UAT deferred per --auto flag.** Plan 06's UAT will be the first end-to-end live test of the wizard with a real installed program. Recommended targets: a JetBrains IDE (rich rule + likely InstallTrace) and a small generic app (no rule, exercises the no-match path).
- **No regressions in any prior plan.** Full suite 130/130 green; all Phase 9 tests still pass.

## Self-Check: PASSED

Verification commands run during execution:

```bash
[ -f src/DiskScout.App/ViewModels/UninstallWizard/WizardStep.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/UninstallWizardViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/SelectionStepViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/PreviewStepViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/RunUninstallStepViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/ResidueScanStepViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/ConfirmDeleteStepViewModel.cs ] && echo FOUND
[ -f src/DiskScout.App/ViewModels/UninstallWizard/ResidueTreeNode.cs ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/UninstallWizardWindow.xaml ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/SelectionStepView.xaml ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/PreviewStepView.xaml ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/RunUninstallStepView.xaml ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/ResidueScanStepView.xaml ] && echo FOUND
[ -f src/DiskScout.App/Views/UninstallWizard/ConfirmDeleteStepView.xaml ] && echo FOUND
[ -f tests/DiskScout.Tests/UninstallWizardViewModelTests.cs ] && echo FOUND
[ -f tests/DiskScout.Tests/UninstallWizardStepTests.cs ] && echo FOUND
git log --oneline | grep -E "6b92ae4|31ea66a|bf5bba0"
```

All 16 wizard files + 2 test files + 4 modified files present (verified during execution).
3 task commit hashes present in `git log`:
- `6b92ae4` feat(09-05): add Uninstall Wizard view-models + state machine
- `31ea66a` test(09-05): add 9 step business-logic tests for Uninstall Wizard
- `bf5bba0` feat(09-05): wire Uninstall Wizard window + Programs DataGrid integration

Build clean: `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug` exits 0, 0 warnings, 0 errors.
Tests green: `dotnet test --filter "FullyQualifiedName~UninstallWizard"` 19/19; full suite 130/130.

Grep cross-checks (all from PLAN's `<verification>` block):

| Check | Required | Actual |
|-------|---------:|-------:|
| `DeleteMode.Permanent` in wizard files | ≥1 | 4 |
| `IRRÉVERSIBLE` in wizard files | ≥1 | 2 |
| `ResiduePathSafety.IsSafeToPropose` in wizard files | ≥2 (defense-in-depth) | 6 |
| `IQuarantineService` in wizard files | 0 (D-03) | 0 |
| `MessageBoxImage.Stop` in wizard files | ≥1 | 1 |
| `Header="Tracé ?"` in ProgramsTabView.xaml | 1 | 1 |
| `Header="Règles éditeur"` in ProgramsTabView.xaml | 1 | 1 |
| `Désinstaller…` in ProgramsTabView.xaml | 1 | 1 |
| `OpenUninstallWizard(` in App.xaml.cs | 1 | 1 |

---
*Phase: 09-programs-tab-real-uninstaller-assistant*
*Completed: 2026-04-25 (auto-approved per --auto flag)*
