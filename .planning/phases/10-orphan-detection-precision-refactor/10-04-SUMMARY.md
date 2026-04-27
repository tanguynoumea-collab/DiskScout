---
plan: 10-04
phase: 10-orphan-detection-precision-refactor
type: execute
status: complete
completed: 2026-04-27
---

# Plan 10-04 — AppData Orphan Pipeline Integration

## Objective achieved

Integrate the Phase 10 foundations (PathRule engine + ParentContextAnalyzer + MachineSnapshot + PublisherAliasResolver) into a single 7-step pipeline that replaces the legacy `MatchesAnyProgram` heuristic in `OrphanDetectorService`'s AppData branch — producing rich `AppDataOrphanCandidate` diagnostics for every emitted candidate, suppressing HardBlacklisted paths entirely, and clamping risk via `MinRiskFloor`.

## Tasks executed

| # | Commit | Summary |
|---|--------|---------|
| 1 | `6979727` | 4 matchers (Service / Driver / Appx / Registry) + ConfidenceScorer + RiskLevelClassifier with declarative score deltas and risk bands |
| 2 | `b349af0` | AppDataOrphanCandidate record + AppDataOrphanPipeline 7-step orchestrator (HardBlacklist → ParentContext → KnownPathRules → MultiSourceMatcher → PublisherAlias → Score → Risk) |
| 3 | `5c38821` | OrphanDetectorService AppData-branch integration + manual DI wiring of 12 services in App.xaml.cs |

## Files created (15)

### Models
- `src/DiskScout.App/Models/AppDataOrphanCandidate.cs` — public sealed record with NodeId, FullPath, SizeBytes, LastWriteUtc, ParentSignificantPath, PathCategory, MatchedSources, TriggeredRules, ConfidenceScore (0-100), Risk, Action, Reason

### Services (interfaces + impls)
- `Services/IServiceMatcher.cs` + `ServiceMatcher.cs`
- `Services/IDriverMatcher.cs` + `DriverMatcher.cs`
- `Services/IAppxMatcher.cs` + `AppxMatcher.cs`
- `Services/IRegistryMatcher.cs` + `RegistryMatcher.cs` (consumes PublisherAliasResolver)
- `Services/IConfidenceScorer.cs` + `ConfidenceScorer.cs`
- `Services/IRiskLevelClassifier.cs` + `RiskLevelClassifier.cs`
- `Services/IAppDataOrphanPipeline.cs` + `AppDataOrphanPipeline.cs` — orchestrates the 7 steps, calls matchers in parallel via `Task.WhenAll`

## Files modified (3)
- `src/DiskScout.App/Models/OrphanCandidate.cs` — added `AppDataOrphanCandidate? Diagnostics { get; init; }` (init-only, optional, backward-compatible — positional ctor unchanged)
- `src/DiskScout.App/Services/OrphanDetectorService.cs` — AppData branch (lines 128-139 ish) replaced with pipeline call; DetectAsync becomes async; legacy `MatchesAnyProgram` and `FuzzyThreshold` removed; the 8 other branches (system artifacts, browser/dev caches, empty folders, broken shortcuts, Program Files vides, Temp anciens, MSI orphelins) UNTOUCHED
- `src/DiskScout.App/App.xaml.cs` — manual DI section extended with 12 new service instantiations (PathRuleEngine + ParentContextAnalyzer + 4 enumerators + MachineSnapshotProvider + PublisherAliasResolver + 4 matchers + scorer + classifier + pipeline). Eager `pathRuleEngine.LoadAsync().GetAwaiter().GetResult()` per Phase 9 precedent

## Critical invariants — verified

| Invariant | Verification |
|-----------|--------------|
| ZERO regression on 224 prior tests | `dotnet test` → **284/284 pass** (60 new tests) |
| ZERO File.Delete/Directory.Delete added | `git diff master --` grep negative on all 18 files touched |
| OrphanCandidate positional ctor unchanged | Diagnostics is init-only optional; OrphansViewModel/OrphanRow load unchanged |
| HardBlacklist returns null (suppress entirely) | Pipeline.EvaluateAsync returns null when any TriggeredRule has Category=OsCriticalDoNotPropose |
| MinRiskFloor clamps UP | RiskLevelClassifier.Classify(score, minFloor) honors PackageCache→Eleve, CorporateAgent→Eleve |
| Manual DI only | App.xaml.cs uses `new X(...)` — no MS.Extensions.DI |
| MultiSourceMatcher parallel | AppDataOrphanPipeline uses `Task.WhenAll` to run the 4 matchers concurrently |
| 8 non-AppData categories untouched | OrphanDetectorService diff shows only the AppData branch modified |

## Tests added: 60

Cumulative: 224 → **284**.

Highlights:
- `ConfidenceScorerTests` — each score delta verified (Registry -50, Driver -45, Appx -50, etc.), bonus accumulation (size=0/old/no-exe), clamp [0, 100]
- `RiskLevelClassifierTests` — each band edge (score=80→Aucun, 79→Faible, 60→Faible, 59→Moyen, 40→Moyen, 39→Eleve, 20→Eleve, 19→Critique), MinRiskFloor clamp UP behavior
- `AppDataOrphanPipelineTests` — HardBlacklist returns null, MinRiskFloor honored, 7-step flow integration with fakes, ParentContext routing (logs subdir → uses parent for matchers), PublisherAlias resolution (RVT 2025 matches Revit)
- 4 matchers — isolated unit tests with fakes for InstalledProgram lists / MachineSnapshot fixtures

## Acceptance gates (Phase 10 critical)

- ✅ Pipeline returns null for `C:\ProgramData\Microsoft\Crypto` (HardBlacklist via os-critical.json)
- ✅ Pipeline returns RiskLevel=Eleve minimum for any `Package Cache\*` path (MinRiskFloor)
- ✅ `RVT 2025` resolves against `Autodesk Revit 2025` registry entry (alias resolver)
- ✅ `BcfManager` resolves against `BCF Managers 6.5 - Revit 2021 - 2024 6.5.5`
- ✅ Empty folder of long-uninstalled program scores ≥ 80 → Aucun → Supprimer

## Deviations

- ServiceMatcher uses fixed `-45` average score delta (between Running -60 and Stopped -30) because the current `IServiceEnumerator` doesn't expose service state. Documented with `// TODO Phase 10.x: distinguish Running vs Stopped` per CONTEXT.md note. Does not affect concordance test thresholds.
- ProcessMatcher NOT implemented — explicitly deferred per CONTEXT.md `<deferred>`. The other 4 matchers cover ~95% of the in-use signal.

## What unblocks for Wave 4

- **10-05** (acceptance corpus + `--audit` CLI) can now consume the pipeline via `EvaluateAsync` directly to score the 365 fixture items and assert ≥95% concordance + 0% Critique→Supprimer.
- **10-06** (UI Score column + Pourquoi panel) can now bind to `OrphanCandidate.Diagnostics` to render the score badge and triggered-rules tooltip.

## Self-Check

- [x] All 3 tasks committed atomically
- [x] Build clean (0 errors, 0 warnings)
- [x] Tests 284/284 pass
- [x] No File.Delete/Directory.Delete added
- [x] OrphanCandidate backward-compatible
- [x] HardBlacklist + MinRiskFloor + 8-other-categories invariants verified
- [x] App.xaml.cs DI uses manual constructor injection only

**Status:** PASSED.
