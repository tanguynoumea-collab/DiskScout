# STATE: DiskScout

**Last updated:** 2026-04-24

## Project Reference

**Core Value:** Montrer clairement où va l'espace disque et identifier les rémanents — sans jamais rien supprimer. L'utilisateur supprime lui-même ailleurs (Explorateur, PowerShell) après analyse.

**Current Focus:** Initial v1 milestone — ship a portable read-only Windows disk analyzer combining registry-driven installed-program inventory, orphan/remnant detection, and size-sorted filesystem tree in a single `.exe`.

## Current Position

- **Milestone:** v1 (initial release)
- **Phase:** 1 of 8 — Foundations & Launchable Shell
- **Plan:** Not started (pending `/gsd:plan-phase 1`)
- **Status:** Roadmap approved, awaiting first plan
- **Progress:** 0/8 phases complete

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

### Blockers

None.

### Risks Being Tracked

- **Phase 2** is the largest technical risk (reparse points, long paths, FindClose leaks under cancellation). Mitigation: isolated as its own phase with unit-test harness before any UI integration.
- **Phase 5** WPF virtualization at 100k+ nodes has undocumented edge cases. Mitigation: Live Visual Tree audit + synthetic dataset benchmark.
- **Phase 8** first single-file publish on clean VM typically reveals packaging issues (manifest embedding, LibraryImport trim behavior). Mitigation: budget a validation loop.

## Session Continuity

### Last Session

- **Date:** 2026-04-24
- **Action:** Project initialization — PROJECT.md, REQUIREMENTS.md (37 items), research (SUMMARY/STACK/ARCHITECTURE/PITFALLS), ROADMAP.md (8 phases), STATE.md written
- **Outcome:** Roadmap created with 100% requirement coverage, ready for plan-phase

### Next Session

- **Next action:** Run `/gsd:plan-phase 1` to decompose Phase 1 (Foundations & Launchable Shell) into executable plans
- **Expected deliverable:** Launchable WPF shell with admin manifest, Serilog rotating log, models + helpers, empty three-tab UI

### Files to Watch

- `.planning/ROADMAP.md` — phase goals and success criteria
- `.planning/REQUIREMENTS.md` — traceability table (37 items mapped)
- `.planning/research/SUMMARY.md` — research synthesis
- `.planning/research/STACK.md` — locked stack + exact `.csproj` + manifest
- `.planning/research/ARCHITECTURE.md` — component boundaries, data flow
- `.planning/research/PITFALLS.md` — 24 domain pitfalls + phase mapping

---
*STATE initialized: 2026-04-24 at roadmap creation*
