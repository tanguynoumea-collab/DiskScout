---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
last_updated: "2026-04-25T16:25:30.216Z"
progress:
  total_phases: 9
  completed_phases: 0
  total_plans: 6
  completed_plans: 6
  percent: 100
---

# STATE: DiskScout

**Last updated:** 2026-04-25 (after Plan 09-06 — Integration + Report ; Phase 9 plans 6/6 complete, awaiting verifier)

## Project Reference

**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer. L'utilisateur supprime lui-même ailleurs (Explorateur, PowerShell) après analyse.

**Current Focus:** Phase 09 — programs-tab-real-uninstaller-assistant

## Current Position

Phase: 09 (programs-tab-real-uninstaller-assistant) — PLANS COMPLETE, awaiting goal verification
Plan: 6 of 6 (all plans executed)

- **Milestone:** v1.1 (post-v1, Programs-tab uninstaller assistant)
- **Phase:** 9 of 9 — Programs Tab Real Uninstaller Assistant
- **Plans:** 09-01 (Install Tracker) + 09-02 (Native Uninstaller Driver) + 09-03 (Residue Scanner) + 09-04 (Publisher Rule Engine) + 09-05 (Uninstall Wizard UI) + 09-06 (Integration + Report) — ALL COMPLETE
- **Status:** All 6 plans executed ; build clean, full suite 140/140, single-file Release publish 81.4 MB ; ready for gsd-verifier goal-backward verification
- **Progress:** [██████████] 100%

```
[ ] [ ] [ ] [ ] [ ] [ ] [ ] [ ]
 1   2   3   4   5   6   7   8
```

## Performance Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Phases complete | 0 / 8 | 8 |
| Requirements delivered | 0 / 37 | 37 |
| Scan time 500 GB SSD | — | < 3 min |
| TreeView expand 10k children | — | < 500 ms |
| Cancel latency | — | < 2 s |
| Single-file binary size | — | 70-90 MB |
| Phase 09-programs-tab-real-uninstaller-assistant P05 | 13m 19s | 4 tasks | 26 files |

## Accumulated Context

### Key Decisions

| Decision | Phase | Rationale |
|----------|-------|-----------|
| WPF + .NET 8 LTS over WinUI 3 / MAUI | Pre-roadmap | Mature tooling, single-file publish stable, no MSIX required |
| CommunityToolkit.Mvvm over Prism/ReactiveUI | Pre-roadmap | Source-gen, zero runtime dep, minimal binary weight |
| `[LibraryImport]` over `[DllImport]` | Pre-roadmap | Trim-safe, required for single-file publish |
| Manual DI in App.xaml.cs over MS.Extensions.DI | Pre-roadmap | ≤20 services, keeps binary lean |
| Flat List + ParentId JSON persistence | Pre-roadmap | Avoids `StackOverflowException` at 1M+ nodes |
| Single FS scan feeds all three panes | Pre-roadmap | Never scan disk 3×; consistent sizes across panes |
| Two-stage fuzzy matching (exact/prefix → token Jaccard) | Pre-roadmap | Avoids false positives on "Microsoft"/"Adobe"-like prefixes |
| Lecture seule absolue — no `Delete*` anywhere | Pre-roadmap | Product safety; enforced by CI grep |
| Admin manifest `requireAdministrator` | Pre-roadmap | Needed for Program Files / Windows / ProgramData access |
| 8 phases (consolidated from research's 9) | Roadmap | Standard granularity; merged publish into export phase |

### Active Todos (Cross-Phase)

- [ ] CI grep check for `File.Delete` / `Directory.Delete` / `Microsoft.VisualBasic.FileIO.*` — must be in place before Phase 2 merges code
- [ ] `.NET 10 LTS` migration tracked as v2 (PLAT-V2-01); scheduled before 2026-11-10
- [ ] Regression fixture of 100 real AppData↔registry pairs — build before Phase 3 is declared done
- [ ] Clean-VM publish smoke test — first attempt in Phase 8, may uncover packaging bugs not visible in VS

### Roadmap Evolution

- Phase 9 added: Programs Tab Real Uninstaller Assistant (Revo-Pro-style — install tracker temps réel + native uninstaller driver + residue scanner + publisher rule engine + wizard UI). User decisions locked: extend registry scan, Revo-Pro level, direct delete (no quarantine).
- 2026-04-25: Plan 09-01 (Install Tracker) completed — IInstallTracker + IInstallTraceStore contracts + JsonInstallTraceStore + InstallTracker (FileSystemWatcher + RegNotifyChangeKeyValue P/Invoke) + 12 passing tests. See `.planning/phases/09-programs-tab-real-uninstaller-assistant/09-01-SUMMARY.md`.
- 2026-04-25: Plan 09-02 (Native Uninstaller Driver) completed — INativeUninstallerDriver with parser (MSI/Inno/NSIS/Generic) + RunAsync with Win32 Job Object KILL_ON_JOB_CLOSE tree-kill + 30-min timeout + IProgress<string> output streaming + 20 passing tests (14 parser + 6 RunAsync). See `.planning/phases/09-programs-tab-real-uninstaller-assistant/09-02-SUMMARY.md`.
- 2026-04-25: Plan 09-03 (Residue Scanner) completed — IResidueScanner + ResidueScanner (7 categories: Registry, Filesystem, Shortcut, MsiPatch, Service, ScheduledTask, ShellExtension) + ResiduePathSafety non-bypassable whitelist (19 fs substrings + 17 registry prefixes + 15 critical service names) + IServiceEnumerator/IScheduledTaskEnumerator test seams + 46 passing tests (36 path-safety + 10 scanner). See `.planning/phases/09-programs-tab-real-uninstaller-assistant/09-03-SUMMARY.md`.
- 2026-04-25: Plan 09-04 (Publisher Rule Engine) completed — IPublisherRuleEngine + PublisherRuleEngine (LoadAsync embedded+user merge with last-write-wins by Id, Match with specificity scoring, ExpandTokens for env vars + {Publisher}/{DisplayName}) + 7 embedded rule JSONs as `<EmbeddedResource>` (adobe, autodesk, jetbrains, mozilla, microsoft-office, steam, epic-games) + AppPaths.PublisherRulesFolder helper + 16 passing tests (10 facts + 1 theory × 7 env vars). All 111 / 111 tests in suite pass; no regressions. See `.planning/phases/09-programs-tab-real-uninstaller-assistant/09-04-SUMMARY.md`.
- 2026-04-25: Plan 09-05 (Uninstall Wizard UI) completed (auto-approved checkpoint per --auto chain) — UninstallWizardViewModel + 5 step VMs (Selection / Preview / RunUninstall / ResidueScan / ConfirmDelete) + ResidueTreeNode (tri-state checkable, default UNCHECKED, child propagation) + UninstallWizardWindow + 5 step views with DataTemplate-by-type pattern; ProgramsTabView gained right-click "Désinstaller…" context menu + 2 diagnostic columns ("Tracé ?", "Règles éditeur"); App.xaml.cs cached 4 Phase-9 services + static OpenUninstallWizard launcher; defense-in-depth ResiduePathSafety re-assertion at 6 wizard call-sites (BuildTree, SelectedPaths getter, ConfirmAsync + ResidueScan scanner-output and rule-merge); D-03 honored (ZERO IQuarantineService refs in Phase-9 wizard files; deletion via DeleteMode.Permanent); 19 unit tests (10 wizard VM + 9 step). All 130 / 130 tests pass; 0 warnings, 0 errors. See `.planning/phases/09-programs-tab-real-uninstaller-assistant/09-05-SUMMARY.md`.

### Blockers

None.

### Risks Being Tracked

- **Phase 2** is the largest technical risk (reparse points, long paths, FindClose leaks under cancellation). Mitigation: isolated as its own phase with unit-test harness before any UI integration.
- **Phase 5** WPF virtualization at 100k+ nodes has undocumented edge cases. Mitigation: Live Visual Tree audit + synthetic dataset benchmark.
- **Phase 8** first single-file publish on clean VM typically reveals packaging issues (manifest embedding, LibraryImport trim behavior). Mitigation: budget a validation loop.

## Session Continuity

### Last Session

- **Date:** 2026-04-25
- **Action:** Executed Plan 09-05 (Uninstall Wizard UI, auto-approved checkpoint per --auto chain). Added 8 view-models under `ViewModels/UninstallWizard/` (WizardStep enum, UninstallWizardViewModel state machine, ResidueTreeNode tri-state hierarchy, 5 step VMs Selection/Preview/RunUninstall/ResidueScan/ConfirmDelete) and 6 views under `Views/UninstallWizard/` (UninstallWizardWindow modal + 5 step views with DataTemplate-by-type pattern). Modified ProgramsViewModel (SelectedRow + UninstallSelectedCommand + UninstallRequested event + Annotate API + InstalledProgramRow.HasInstallTrace/MatchedPublisherRuleIds), ProgramsTabView.xaml (2 new columns "Tracé ?" + "Règles éditeur" + ContextMenu with "Désinstaller…" MenuItem), ProgramsTabView.xaml.cs (DataContextChanged subscribes UninstallRequested -> App.OpenUninstallWizard), App.xaml.cs (cached 4 Phase-9 services + static OpenUninstallWizard launcher, fired-and-forgotten PublisherRuleEngine.LoadAsync at startup). Defense-in-depth ResiduePathSafety re-assertion at 6 call-sites; ConfirmDelete uses DeleteMode.Permanent + dedicated MessageBox.Show with IRRÉVERSIBLE wording + MessageBoxImage.Stop + default=No (D-03 honored, ZERO IQuarantineService refs). 19 unit tests (10 wizard VM covering state machine + tree node + ConfirmDelete safety, 9 step business-logic with hand-written fakes — no Moq dep, project policy precedent).
- **Outcome:** 3 task commits (6b92ae4 wizard scaffold + step VMs, 31ea66a step tests, bf5bba0 XAML window + Programs DataGrid integration). Auto-approval evidence: build clean (0 warnings, 0 errors), all 130 / 130 tests passing (baseline 111 + 19 = 130), grep cross-checks all green (DeleteMode.Permanent: 4 hits, IRRÉVERSIBLE: 2, ResiduePathSafety.IsSafeToPropose: 6 ≥2 required for defense-in-depth, IQuarantineService: 0, MessageBoxImage.Stop: 1, ProgramsTabView.xaml has all 3 expected strings). Manual UAT (right-click -> wizard navigation -> tree manipulation -> IRRÉVERSIBLE modal) deferred to Plan 09-06's end-to-end smoke; build+test+grep evidence is the auto-approval substitute. Note: ProgramsViewModel.Annotate is wired but not yet called from MainViewModel.OnScanCompleted (intentionally deferred per plan instructions to Plan 09-06).

### Next Session

- **Next action:** Execute Plan 09-06 (Integration + Report) — wire ProgramsViewModel.Annotate in MainViewModel.OnScanCompleted (build dictionaries from IInstallTraceStore.ListAsync + IPublisherRuleEngine.Match), add Report step (Step 6 / Done state) consuming UninstallOutcome + DeletionOutcome, wire UninstallWizardViewModel.Trace via IInstallTraceStore.LoadAsync, end-to-end UAT with a real installed program (recommended: a JetBrains IDE for rich rule + likely InstallTrace, plus a small generic app for no-match path).
- **Expected deliverable:** End-to-end Phase 9 closure: HTML/JSON report exportable from the Done state, both diagnostic columns populated post-scan, full wizard flow tested with a real uninstall.

### Files to Watch

- `.planning/ROADMAP.md` — phase goals and success criteria
- `.planning/REQUIREMENTS.md` — traceability table (37 items mapped)
- `.planning/research/SUMMARY.md` — research synthesis
- `.planning/research/STACK.md` — locked stack + exact `.csproj` + manifest
- `.planning/research/ARCHITECTURE.md` — component boundaries, data flow
- `.planning/research/PITFALLS.md` — 24 domain pitfalls + phase mapping

---
*STATE initialized: 2026-04-24 at roadmap creation*
