---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
last_updated: "2026-04-25T16:00:37Z"
progress:
  total_phases: 9
  completed_phases: 0
  total_plans: 6
  completed_plans: 4
  percent: 67
---

# STATE: DiskScout

**Last updated:** 2026-04-25 (after Plan 09-04 — Publisher Rule Engine)

## Project Reference

**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer. L'utilisateur supprime lui-même ailleurs (Explorateur, PowerShell) après analyse.

**Current Focus:** Phase 09 — programs-tab-real-uninstaller-assistant

## Current Position

Phase: 09 (programs-tab-real-uninstaller-assistant) — EXECUTING
Plan: 5 of 6 (next: 09-05 Wizard UI)

- **Milestone:** v1.1 (post-v1, Programs-tab uninstaller assistant)
- **Phase:** 9 of 9 — Programs Tab Real Uninstaller Assistant
- **Plans:** 09-01 (Install Tracker) + 09-02 (Native Uninstaller Driver) + 09-03 (Residue Scanner) + 09-04 (Publisher Rule Engine) completed
- **Status:** Executing Phase 09
- **Progress:** [███████░░░] 67%

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

### Blockers

None.

### Risks Being Tracked

- **Phase 2** is the largest technical risk (reparse points, long paths, FindClose leaks under cancellation). Mitigation: isolated as its own phase with unit-test harness before any UI integration.
- **Phase 5** WPF virtualization at 100k+ nodes has undocumented edge cases. Mitigation: Live Visual Tree audit + synthetic dataset benchmark.
- **Phase 8** first single-file publish on clean VM typically reveals packaging issues (manifest embedding, LibraryImport trim behavior). Mitigation: budget a validation loop.

## Session Continuity

### Last Session

- **Date:** 2026-04-25
- **Action:** Executed Plan 09-04 (Publisher Rule Engine) — added PublisherRule + PublisherRuleMatch records, IPublisherRuleEngine contract, PublisherRuleEngine sealed class (LoadAsync merges embedded GetManifestResourceNames-enumerated rules with user *.json files in %LocalAppData%\DiskScout\publisher-rules with last-write-wins by Id; Match scores publisher-only at 10 and DisplayName-narrowed at 110 with regex 200ms timeout + skip-on-bad-regex; ExpandTokens runs Environment.ExpandEnvironmentVariables then literal {Publisher}/{DisplayName} substitution), 7 publisher rule JSONs embedded via <EmbeddedResource Include="Resources\PublisherRules\*.json"><LogicalName>DiskScout.Resources.PublisherRules.%(Filename).json</LogicalName></EmbeddedResource> (adobe, autodesk, jetbrains, mozilla, microsoft-office narrowed to Office/Teams/OneDrive, steam narrowed to Valve+Steam, epic-games), AppPaths.PublisherRulesFolderName + PublisherRulesFolder helpers appended alongside Plan 01's InstallTracesFolder, 16 unit tests (10 facts + 1 theory × 7 env vars).
- **Outcome:** 2 task commits (39568c9 / df8952e), all 111 / 111 tests passing (baseline 95 + 16 = 111), build clean (0 warnings, 0 errors). Engine ready to be consumed by Plan 09-05 (Wizard UI step 2 "Preview résidus connus" + step 4 residue scan post-uninstall — rule-derived paths feed back to ResidueScanner with Source=PublisherRule + Trust=HighConfidence when path exists on disk).

### Next Session

- **Next action:** Execute Plan 09-05 (Wizard UI — refonte onglet Programs en wizard 5 étapes : Sélection programme + colonnes diagnostiques "Tracé ?" et "Règles éditeur ?" / Preview résidus connus / Run native uninstaller / Scan résidus post-uninstall / Confirmation suppression résidus + tree cochable + checkpoint UAT manuel pour validation visuelle).
- **Expected deliverable:** Refonte `ProgramsView.xaml` + `ProgramsViewModel` injectant les 4 services Wave-1 (`IInstallTracker`, `INativeUninstallerDriver`, `IResidueScanner`, `IPublisherRuleEngine`), avec wizard navigation, default-unchecked tree, modale de confirmation irreversible avant DELETE permanent (pas via quarantaine — CONTEXT.md D-03).

### Files to Watch

- `.planning/ROADMAP.md` — phase goals and success criteria
- `.planning/REQUIREMENTS.md` — traceability table (37 items mapped)
- `.planning/research/SUMMARY.md` — research synthesis
- `.planning/research/STACK.md` — locked stack + exact `.csproj` + manifest
- `.planning/research/ARCHITECTURE.md` — component boundaries, data flow
- `.planning/research/PITFALLS.md` — 24 domain pitfalls + phase mapping

---
*STATE initialized: 2026-04-24 at roadmap creation*
