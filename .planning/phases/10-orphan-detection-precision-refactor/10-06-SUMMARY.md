---
plan: 10-06
phase: 10-orphan-detection-precision-refactor
type: execute
status: complete
completed: 2026-04-27
subsystem: ui
tags: [ui, viewmodel, xaml, mvvm, diagnostics, docs]
requires:
  - 10-04 (Diagnostics property on OrphanCandidate)
provides:
  - Score column + tooltip + "Pourquoi ?" button in OrphansTabView
  - OrphanDiagnosticsWindow modal showing 7-step trace per AppData orphan
  - User-facing docs/heuristics.md reference
affects:
  - OrphansViewModel (extended with ShowDiagnosticsCommand)
  - OrphanRow (5 new computed properties)
  - App.xaml resources (RiskLevelToBrushConverter registered)
tech-stack:
  added: []
  patterns:
    - one-way IValueConverter with frozen brushes
    - DataTemplate row with conditional Visibility on HasDiagnostics
    - RelayCommand opening a modal Window with DataContext-injected VM (MVVM strict)
    - ItemsControl + DataTemplate inside Border.ToolTip for rich hover content
key-files:
  created:
    - src/DiskScout.App/ViewModels/OrphanDiagnosticsViewModel.cs
    - src/DiskScout.App/Helpers/RiskLevelToBrushConverter.cs
    - src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml
    - src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml.cs
    - tests/DiskScout.Tests/OrphanDiagnosticsViewModelTests.cs
    - docs/heuristics.md
  modified:
    - src/DiskScout.App/ViewModels/OrphansViewModel.cs (OrphanRow + ShowDiagnosticsCommand)
    - src/DiskScout.App/Views/Tabs/OrphansTabView.xaml (Score column + tooltip + Pourquoi button)
    - src/DiskScout.App/App.xaml (RiskLevelToBrushConverter resource)
decisions:
  - "Frozen SolidColorBrushes in RiskLevelToBrushConverter — single instance per band, no per-row allocation"
  - "MVVM strict — code-behind on OrphanDiagnosticsWindow limited to InitializeComponent + OnCloseClicked"
  - "ShowDiagnosticsCommand on OrphansViewModel (not on OrphanRow) — opens modal with new VM each time"
  - "Score column visibility driven by HasDiagnostics — non-AppData rows render unchanged"
  - "Auto-approved checkpoint per --auto chain config (build clean + all 10-06 tests green)"
metrics:
  duration: ~25 min
  completed: 2026-04-27
  tests_added: 16
  tests_total: 300 (was 284 — excluding in-flight 10-05 corpus test)
  files_created: 6
  files_modified: 3
---

# Plan 10-06 — UI Diagnostics Surface + Heuristics Documentation Summary

Surface every Phase-10 diagnostic produced by Plan 10-04 in the Rémanents tab UI: a color-coded Score badge column with a rich tooltip listing triggered rules, a "Pourquoi ?" button on AppData rows that opens a modal showing the 7-step score breakdown, and a user-facing `docs/heuristics.md` reference explaining the pipeline + scoring rules + custom rule authoring.

## Tasks executed

| # | Commit | Summary |
|---|--------|---------|
| 1 | `c1dbcad` | OrphanRow extended with Diagnostics + 4 computed properties; new OrphanDiagnosticsViewModel; new RiskLevelToBrushConverter; 16 unit tests |
| 2 | `b5c23ae` | OrphansTabView 5-column row with Score badge + Pourquoi button; OrphanDiagnosticsWindow modal (700×540); App.xaml registers converter; OrphansViewModel.ShowDiagnosticsCommand |
| 3 | `970e7c8` | docs/heuristics.md (384 lines) covering 7-step pipeline + scoring + path-rule authoring + FAQ |
| 4 | (no commit) | checkpoint:human-verify auto-approved per --auto chain — see UAT outcome below |

## Files created (6)

### ViewModels
- `src/DiskScout.App/ViewModels/OrphanDiagnosticsViewModel.cs` — sealed partial ObservableObject (no [ObservableProperty] needed — read-only properties set in ctor). Pre-formats `TriggeredRulesLines` (`"{RuleId} — {Reason}"`), `MatchedSourcesLines` (`"{Source}: {Evidence} ({+/-N} pts)"`), `SizeDisplay` (via existing `ByteFormat.Fmt`), `LastWriteDisplay` (`yyyy-MM-dd HH:mm` local time, invariant culture).

### Helpers
- `src/DiskScout.App/Helpers/RiskLevelToBrushConverter.cs` — one-way `IValueConverter` mapping the 5 RiskLevel bands to frozen `SolidColorBrush` instances. Aucun=`#27AE60`, Faible=`#2ECC71`, Moyen=`#F39C12`, Eleve=`#E67E22`, Critique=`#E74C3C`. Null/unknown → `Brushes.Transparent`. `ConvertBack` throws `NotSupportedException`.

### Views
- `src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml` — modal Window (700×540, `WindowStartupLocation=CenterOwner`, `ShowInTaskbar=False`). DockPanel layout: header (full path / parent significant / size / last write), bottom action bar (Fermer button), body ScrollViewer with score banner (color-coded by Risk), Triggered rules section (ItemsControl), Matchers section (ItemsControl).
- `src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml.cs` — minimal code-behind: `InitializeComponent()` + `OnCloseClicked` calling `Close()`. CLAUDE.md MVVM strict honored.

### Tests
- `tests/DiskScout.Tests/OrphanDiagnosticsViewModelTests.cs` — 16 tests (8 VM + 5 converter + 2 OrphanRow-flow + 1 EmptyRulesAndMatchers).

### Documentation
- `docs/heuristics.md` — 384 lines, structured per `<behavior>` of Task 3:
  1. Why the refactor (FP measurement)
  2. Pipeline ASCII diagram with the 7 steps annotated
  3. Step 1 HardBlacklist with deployed pattern table (~70 entries cited)
  4. Step 2 ParentContextAnalyzer with leaf-list and concrete example
  5. Step 3 KnownPathRules — table of 5 categories with MinRiskFloor + example patterns per category
  6. Step 4 MultiSourceMatcher (4 sources)
  7. Step 5 PublisherAliasResolver (3-stage cascade) with embedded alias snippets
  8. Step 6 ConfidenceScorer — full delta tables (matchers, categories, residue bonuses)
  9. Step 7 RiskLevelClassifier — bands → action → UI badge
  10. "Comment ajouter une règle utilisateur" with copy-pastable JSON example pointing to `%LocalAppData%\DiskScout\path-rules\`
  11. v1.3 deferral for user-aliases
  12. FAQ (5 questions)
  13. Corpus reference (link to 10-05-SUMMARY.md)
  14. `--audit` mode usage
  15. Out of scope + further reading

## Files modified (3)

- `src/DiskScout.App/ViewModels/OrphansViewModel.cs` — `OrphanRow` ctor gains `Diagnostics = c.Diagnostics;`. New computed properties `Diagnostics`, `HasDiagnostics`, `ConfidenceScore`, `Risk`, `ScoreBadgeText`. New `[RelayCommand] ShowDiagnostics(OrphanRow?)` opens `OrphanDiagnosticsWindow` with a fresh `OrphanDiagnosticsViewModel`. Defensive null check (no-op if `row?.Diagnostics is null`).

- `src/DiskScout.App/Views/Tabs/OrphansTabView.xaml` — row template DataTemplate updated from 3-column to 5-column Grid. New columns 2 and 4 (Score badge + Pourquoi button) bound on `HasDiagnostics → Visibility`. Score badge is a `Border` with `Background={Binding Risk, Converter=...}`, `CornerRadius=8`, containing a `TextBlock` text-bound to `ScoreBadgeText`. ToolTip uses an inline `<Border.ToolTip><ToolTip MaxWidth="520">` with a StackPanel of bound Run elements + an `ItemsControl` over `Diagnostics.TriggeredRules`. Pourquoi `Button` uses `RelativeSource AncestorType=UserControl` to reach `OrphansViewModel.ShowDiagnosticsCommand`.

- `src/DiskScout.App/App.xaml` — added `<helpers:RiskLevelToBrushConverter x:Key="RiskLevelToBrushConverter"/>` to the global `<Application.Resources>`.

## Critical invariants — verified

| Invariant | Verification |
|-----------|--------------|
| ZERO regression on 284 prior tests | `dotnet test --filter "FullyQualifiedName!~CorpusAcceptance"` → **300/300 pass** (16 new) |
| ZERO File.Delete/Directory.Delete added | `git diff master --` grep negative on all 9 files touched |
| MVVM strict on OrphanDiagnosticsWindow | Code-behind = `InitializeComponent()` + `OnCloseClicked()` (Close-only) |
| OrphansViewModel.Load signature unchanged | Confirmed — only NEW members added |
| OrphanRow ctor signature unchanged | Confirmed — only `Diagnostics = c.Diagnostics;` line added |
| Score column hidden for non-AppData rows | `Visibility="{Binding HasDiagnostics, Converter={StaticResource BooleanToVisibilityConverter}}"` on both Border (badge) AND Button (Pourquoi) |
| Tooltip lists TriggeredRules | `<ItemsControl ItemsSource="{Binding Diagnostics.TriggeredRules}">` inside `<Border.ToolTip>` |
| MinRiskFloor invariant respected upstream | Pipeline 10-04 already clamps; UI just renders Risk |

## Tests added: 16

Cumulative: **300** (was 284). Note: an additional in-flight `CorpusAcceptanceTests` test from parallel agent 10-05 brings the grand total to 303 once 10-05 commits, but that test is owned by 10-05 and outside this plan's scope.

Highlights:
- `Ctor_Populates_*` — every read-only field copied from `AppDataOrphanCandidate`
- `TriggeredRulesLines_FormattedAs_RuleId_And_Reason` — locks the `"{Id} — {Reason}"` format
- `MatchedSourcesLines_FormattedWithSignedDelta` — locks the `"{Source}: {Evidence} ({+/-N} pts)"` format with signed integers (theory case: +20 → "+20 pts")
- `SizeDisplay_*` — `0 → "0 o"`, non-zero → human-readable units
- `LastWriteDisplay_FormattedYYYYMMDD_HHmm` — regex match + date prefix (timezone-agnostic)
- 5 `RiskLevelToBrush_*` cases — explicit ARGB per band, null → transparent, ConvertBack throws
- 2 `OrphanRow_*` flow-through cases — verified `HasDiagnostics` toggles correctly between AppData (true) and StaleTemp (false)

## Acceptance gates (Plan 10-06 critical)

- ✅ `grep -c "Diagnostics = c.Diagnostics" OrphansViewModel.cs` → 1
- ✅ `grep -c "public AppDataOrphanCandidate? Diagnostics" OrphansViewModel.cs` → 1
- ✅ `grep -c "public sealed partial class OrphanDiagnosticsViewModel"` → 1
- ✅ `grep -c "public sealed class RiskLevelToBrushConverter"` → 1
- ✅ `grep -c "RiskLevelToBrushConverter" App.xaml` → 1
- ✅ `grep -c "Pourquoi" OrphansTabView.xaml` → 2
- ✅ `grep -c "HasDiagnostics" OrphansTabView.xaml` → 2 (badge + button)
- ✅ `grep -c "ScoreBadgeText\|Diagnostics" OrphansTabView.xaml` → 8
- ✅ `ls OrphanDiagnosticsWindow.xaml` exists
- ✅ `grep -c "OrphanDiagnosticsWindow" OrphanDiagnosticsWindow.xaml.cs` → 2
- ✅ `grep -c "ShowDiagnosticsCommand\|ShowDiagnostics" OrphansViewModel.cs` → 1
- ✅ `wc -l docs/heuristics.md` → 384 (≥ 100 required)
- ✅ All 8 docs/heuristics.md grep gates met
- ✅ `dotnet build` clean (0 warnings, 0 errors)

## UAT outcome — auto-approved per --auto chain

**Type:** `checkpoint:human-verify` (visual UAT)
**Resolution:** Auto-approved (`{user_response}=approved`) per orchestrator's `--auto` chain policy.

**Auto-approval evidence:**
- Build clean: `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug` → 0 warnings, 0 errors
- 10-06 tests: 16/16 pass (filter `FullyQualifiedName~OrphanDiagnostics`)
- Full suite (excl. in-flight 10-05 corpus test): 300/300 pass
- All grep cross-checks for Tasks 1-3 acceptance criteria green
- No `File.Delete`/`Directory.Delete` introduced

**Why no .exe launch:** the user's `feedback_auto_launch_build.md` memory says "always run the .exe after a successful build of a GUI project" for human inspection. This executor runs in **autonomous CI mode** per the orchestrator's `<commit_protocol>` directive: "DO NOT launch the .exe (no GUI tests in CI; user requested auto-launch in their `feedback_auto_launch_build.md` memory but in autonomous CI mode we skip — document in SUMMARY)." The visual UAT can be performed by the user manually after this plan completes by running:

```powershell
dotnet run --project src/DiskScout.App/DiskScout.App.csproj
```

…then scanning C:\, opening the Rémanents tab, expanding the AppData orphelins group, and verifying the 8 visual checkpoints listed in the plan's `<how-to-verify>` section.

## Deviations from Plan

**None — plan executed exactly as written, with three small adjustments documented as auto-fixes:**

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed misuse of ZeroToBoolInverseConverter for Visibility binding**
- **Found during:** Task 2 (initial OrphanDiagnosticsWindow.xaml draft)
- **Issue:** First draft wrote `Visibility="{Binding TriggeredRulesLines.Count, Converter={StaticResource ZeroToBoolInverseConverter}, ConverterParameter=invert}"` but the converter returns `bool`, not `Visibility`, and ignores `ConverterParameter`.
- **Fix:** Removed the empty-state placeholder TextBlocks. The two ItemsControl bound to TriggeredRulesLines / MatchedSourcesLines simply render zero items when the list is empty; UX is acceptable (the modal still has the section headers + score banner). Adding a proper visibility converter would have been gold-plating outside scope.
- **Files modified:** `src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml`
- **Commit:** `b5c23ae` (folded into Task 2 commit)

**2. [Rule 3 - Blocking] Pourquoi button RelativeSource binding hop**
- **Found during:** Task 2 (XAML compile)
- **Issue:** First draft used `AncestorType=ItemsControl, AncestorLevel=3` to reach the ViewModel's command, which is fragile (depends on the exact nesting depth of the Expander/ItemsControl hierarchy).
- **Fix:** Changed to `AncestorType=UserControl` — the `OrphansTabView` UserControl has `DataContext = OrphansViewModel`, so this is direct and stable.
- **Files modified:** `src/DiskScout.App/Views/Tabs/OrphansTabView.xaml`
- **Commit:** `b5c23ae` (folded into Task 2 commit)

**3. [Out of scope] Did not auto-launch the .exe**
- **Reason:** Orchestrator's `<commit_protocol>` instruction explicitly overrides the user's auto-launch memory in CI mode.
- **Documented:** This SUMMARY (UAT outcome section).

### Parallel-execution note

The parallel 10-05 agent left two transitive changes in the working tree at the time of this plan's execution:
- `tests/DiskScout.Tests/DiskScout.Tests.csproj` modified (added `<EmbeddedResource>` for `Fixtures/programdata_corpus_365.json`)
- `tests/DiskScout.Tests/Fixtures/` directory created with `programdata_corpus_365.json` + `generate_corpus.cjs`

These were **NOT touched** by 10-06 — they are owned by 10-05 and will be committed by that agent. The csproj diff was visible only because both agents share the test project, which is expected per parallel-execution rules.

A `CorpusAcceptanceTests.RemanentDetector_Should_Match_Manual_Audit_With_95_Percent_Concordance` test failure was observed during Task 4's "all-tests" verification, but this test belongs to 10-05 (still in-flight) and is outside 10-06 scope. All 10-06 tests (16/16) and all baseline tests (284/284) pass.

## Phase 10 closure note

This plan ships the user-facing UI surface for the Phase-10 detection engine. With Plans 10-01..10-05 building the engine itself and 10-05's `CorpusAcceptanceTests` providing the concordance metric, Plan 10-06 makes the precision visible to the user via:

1. A score badge that turns red on Critique items (no risk of accidental deletion)
2. A "Pourquoi ?" modal that gives full transparency on the 7-step trace
3. `docs/heuristics.md` so users can author their own path rules

Cite the concordance metric — once finalized by 10-05 — in any user-facing release notes referencing Phase 10. See `[10-05-SUMMARY.md](10-05-SUMMARY.md)` for the measured concordance figure.

## Self-Check

- [x] All 3 implementation tasks committed atomically (`c1dbcad`, `b5c23ae`, `970e7c8`)
- [x] Checkpoint Task 4 auto-approved per --auto chain — documented above
- [x] Build clean (0 errors, 0 warnings)
- [x] 10-06 tests 16/16 pass; baseline + 10-06 = 300/300 pass
- [x] No File.Delete/Directory.Delete added (verified via diff)
- [x] OrphanCandidate / OrphanRow positional ctors unchanged (backward-compatible additions only)
- [x] App.xaml.cs NOT touched (per parallel-execution rules — exclusive to 10-05)
- [x] All 9 files I touched are mine; the csproj edit + Fixtures/ are from 10-05 (untouched by me)
- [x] CLAUDE.md MVVM-strict honored on OrphanDiagnosticsWindow.xaml.cs

**Status:** PASSED.

## Self-Check: PASSED

Files verified to exist:
- FOUND: src/DiskScout.App/ViewModels/OrphanDiagnosticsViewModel.cs
- FOUND: src/DiskScout.App/Helpers/RiskLevelToBrushConverter.cs
- FOUND: src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml
- FOUND: src/DiskScout.App/Views/OrphanDiagnosticsWindow.xaml.cs
- FOUND: tests/DiskScout.Tests/OrphanDiagnosticsViewModelTests.cs
- FOUND: docs/heuristics.md

Commits verified to exist:
- FOUND: c1dbcad
- FOUND: b5c23ae
- FOUND: 970e7c8
