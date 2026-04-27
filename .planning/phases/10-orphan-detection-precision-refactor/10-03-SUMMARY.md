---
phase: 10-orphan-detection-precision-refactor
plan: 03
subsystem: services
tags: [publisher-alias, fuzzy-matcher, embedded-resources, json-catalog, three-stage-resolver, semaphore-slim, idempotent-load, orphan-detection]

# Dependency graph
requires:
  - phase: 10-orphan-detection-precision-refactor
    provides: PathRuleEngine pattern, AppPaths.PathRulesFolder, Resources/PathRules/*.json EmbeddedResource glob (10-01); MachineSnapshot indexed cache (10-02)
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: FuzzyMatcher.ComputeMatch (folder/publisher/displayName triple) + IsMatch threshold helper
provides:
  - PublisherAlias record (Canonical, Aliases[], Type) — type discriminates "publisher" vs "product" canonicals
  - IPublisherAliasResolver interface (LoadAsync + ResolveAsync) returning (double Score, string? MatchedCanonical)?
  - PublisherAliasResolver sealed class — idempotent SemaphoreSlim-gated load, three-stage resolve, FuzzyMatcher fallback, 0.7 threshold (default), Cancellation honored
  - aliases.json embedded resource at DiskScout.Resources.PathRules.aliases.json — 15 canonical entries (3 publishers + 12 products) curated for the Phase-10 365-item ProgramData corpus
affects:
  - 10-04-MultiSourceMatcher (consumes IPublisherAliasResolver inside the Registry/Publisher matcher to bridge folder<->registry naming entropy)
  - 10-05-ConfidenceScorer-and-RiskClassifier (no direct consumption — resolver output is upstream of scoring)
  - 10-06-Integration-and-AuditMode (resolver wired in App.xaml.cs manual DI alongside PathRuleEngine + MachineSnapshotProvider)

# Tech tracking
tech-stack:
  added: []  # zero new NuGet packages — System.Text.Json (BCL), System.Reflection (BCL), SemaphoreSlim (BCL)
  patterns:
    - "Three-stage max-score resolver: exact alias (whole-string, case-insensitive) -> token alias expansion (rewrite folder tokens via reverse alias-token map, subset-cover the canonical -> 0.85 floor; otherwise Jaccard) -> FuzzyMatcher.ComputeMatch fallback on the original triple. Threshold 0.7. Below threshold returns null."
    - "Idempotent eager + lazy load: SemaphoreSlim(1,1) gate + bool _loaded flag + double-check after gate acquisition. ResolveAsync auto-loads on first call; LoadAsync is optional and idempotent."
    - "Embedded JSON catalog under Resources/PathRules/aliases.json — shares the .csproj EmbeddedResource glob from Plan 10-01. The PathRuleEngine's existing JsonException catch + empty-Id skip absorbs this structurally-distinct sibling without code change."
    - "Canonical-as-alias-of-self contract: every canonical is registered as its own alias so that 'Revit' resolves to canonical 'Revit' at score 1.0. Eliminates a special case in downstream consumers."
    - "Tokenize() shares semantics with FuzzyMatcher.Tokenize (alphanumeric runs, length >= 2, lower-cased) so token-set logic stays consistent across both layers."

key-files:
  created:
    - src/DiskScout.App/Models/PublisherAlias.cs
    - src/DiskScout.App/Services/IPublisherAliasResolver.cs
    - src/DiskScout.App/Services/PublisherAliasResolver.cs
    - src/DiskScout.App/Resources/PathRules/aliases.json
    - tests/DiskScout.Tests/PublisherAliasResolverTests.cs
  modified:
    - tests/DiskScout.Tests/PathRuleEngineTests.cs  # Test 1 filtered to exclude aliases.json from the 5-PathRule manifest assertion

key-decisions:
  - "PublisherAlias shape: (string Canonical, string[] Aliases, string? Type) per the plan's critical_invariants override. Type discriminates 'publisher' vs 'product' canonicals so downstream consumers can weight matches differently. Plan 10-03-PLAN.md originally proposed a flat (FolderName, CanonicalPublisher) record — the prompt's critical_invariants block took precedence."
  - "ResolveAsync signature: (folderName, publisher?, displayName?, ct) -> (Score, MatchedCanonical)? per the prompt's critical_invariants override (plan originally proposed candidatePublishers list). The 3-stage strategy still returns publisher OR product canonicals — matching the Phase-10 corpus's mixed bag (BcfManager folder maps to product 'BCF Manager', not a publisher)."
  - "Token-expansion stage scores at 0.85 when the canonical's tokens are a SUBSET of the rewritten folder tokens. Rationale: presence of a recognized alias token is itself strong evidence; raw Jaccard on a 1-token canonical like 'Revit' vs a 2-token folder like 'RVT 2025' would give 0.5 (below threshold) and miss the very signal the alias table was designed to surface."
  - "aliases.json sits beside the 5 PathRule JSONs from Plan 10-01 (covered by the existing EmbeddedResource glob). PathRuleEngine.LoadAsync absorbs the structurally-distinct sibling via its JsonException catch + empty-Id skip path (verified by the still-green PathRuleEngineTests suite). PathRuleEngineTests Test 1 needed a 1-line filter to exclude aliases.json from the 5-PathRule manifest count assertion (Rule 3 auto-fix — blocking issue caused by the current task's changes)."
  - "FuzzyMatcher fallback (stage 3) is the no-regression guarantee: anything the legacy fuzzy detector caught is still caught. The alias and token-expansion stages are pure ADDITIONS to recall; they cannot lower a fuzzy score, only raise it via max(stage1, stage2, stage3)."
  - "Bad / missing / wrong-shape aliases.json is fully absorbed: JsonException + Exception + missing-stream + null-array all log Warning then proceed with an empty alias dictionary. The resolver remains operative via the FuzzyMatcher fallback path. Verified by Test 11 (xUnit assembly with no aliases.json) and Test 12 (BadJsonStubAssembly with garbage bytes)."

patterns-established:
  - "Embedded alias catalog: JSON ARRAY of {canonical, aliases[], type} entries under Resources/PathRules/. New canonicals can be added by editing aliases.json — no code change. Future plans (10-04..10-06) and v1.x updates can extend the catalog without resolver changes."
  - "3-stage max-score resolver: pattern reusable for any future entropy-bridging service (e.g., publisher <-> SKU code, file-extension <-> mime-type). Exact-table lookup -> token-expansion via reverse map -> fallback to a more lenient algorithm; max wins; below-threshold returns null."
  - "Test-stub Assembly subclass: BadJsonStubAssembly demonstrates an in-test stub Assembly that returns deterministic bytes for a known manifest resource name. Reusable for any future test that needs to exercise embedded-resource error paths without polluting the real assembly."

requirements-completed: []  # Plan 10-03 has no requirements: [] in the frontmatter.

# Metrics
duration: 18m
completed: 2026-04-27
---

# Phase 10 Plan 03: PublisherAliasResolver Summary

**Three-stage publisher / product alias resolver bridging folder<->registry naming entropy via an embedded 15-canonical JSON catalog (aliases.json) + token-expansion + FuzzyMatcher fallback at 0.7 threshold — stage [5] of the AppData orphan-detection pipeline now consumable by Plan 10-04 with a no-regression guarantee.**

## Performance

- **Duration:** ~18 min
- **Started:** 2026-04-27 (orchestrator dispatch, Wave 2)
- **Completed:** 2026-04-27
- **Tasks:** 2
- **Commits:** 2 atomic task commits + 1 doc commit (this SUMMARY)
- **Files created:** 5
- **Files modified:** 1

## Accomplishments

- 1 new contract pair (PublisherAlias model + IPublisherAliasResolver interface) under DiskScout.Models / DiskScout.Services namespaces.
- 1 sealed implementation (PublisherAliasResolver) — idempotent SemaphoreSlim-gated load, three-stage Resolve with max-score wins, 0.7 threshold, cancellation honored at every stage, full graceful degradation on bad / missing / wrong-shape JSON.
- 1 embedded resource (aliases.json) — 15 canonical entries: 3 publishers (Autodesk, Microsoft, NVIDIA) + 12 products (Revit, BCF Manager, Visual Studio, Navisworks, Geospatial Coordinate Systems, DiRoots, DWG TrueView, Dynamo, PlaceMaker, Steel Connections, iCloud, iTunes). Curated from the Plan-10 365-item ProgramData corpus.
- 16 new unit tests (9 facts + 4 theories with multiple inline data + 3 in-test infrastructure tests). Final test suite: 224 / 224 passing (197 baseline + 27 from this plan).
- Build clean: 0 warnings, 0 errors.
- ZERO occurrences of `File.Delete` / `Directory.Delete` / `FileSystem.Delete` introduced in any of the 4 new source files (verified by grep).
- All 197 baseline tests still pass — including all 14 PathRuleEngineTests, which absorb the new aliases.json sibling resource via the engine's existing JsonException catch + empty-Id skip path.

## Task Commits

Each task was committed atomically:

1. **Task 1: PublisherAlias model + IPublisherAliasResolver + aliases.json catalog** — `19a9eee` (feat). Includes Rule 3 auto-fix for PathRuleEngineTests Test 1 (filter aliases.json from the 5-PathRule manifest count).
2. **Task 2: PublisherAliasResolver impl with 3-stage match + 16 tests** — `d32b55c` (feat).

## Files Created/Modified

### Created
- `src/DiskScout.App/Models/PublisherAlias.cs` — `public sealed record PublisherAlias(string Canonical, string[] Aliases, string? Type)`. XML doc clarifies the publisher/product discriminator.
- `src/DiskScout.App/Services/IPublisherAliasResolver.cs` — interface with `LoadAsync(ct)` + `ResolveAsync(folderName, publisher?, displayName?, ct)` returning `(double Score, string? MatchedCanonical)?`. XML doc spells out the 3-stage strategy + threshold semantics.
- `src/DiskScout.App/Services/PublisherAliasResolver.cs` — sealed class implementing the interface. Constructor pair: production (uses own assembly + 0.7 threshold) and internal test seam (arbitrary assembly + arbitrary threshold). Exposes `CanonicalCount` for diagnostics + tests.
- `src/DiskScout.App/Resources/PathRules/aliases.json` — JSON ARRAY of 15 canonical entries; covered by the existing `<EmbeddedResource Include="Resources\PathRules\*.json">` glob from Plan 10-01.
- `tests/DiskScout.Tests/PublisherAliasResolverTests.cs` — 16 tests (9 facts + 4 theories) including the BCF Manager real-world acceptance case, BadJsonStubAssembly stress test, missing-resource fallback test, idempotent-load test, cancellation gate test, case-insensitivity theory.

### Modified
- `tests/DiskScout.Tests/PathRuleEngineTests.cs` — Test 1 (`ManifestResources_ContainExactly5PathRules`) filtered to exclude `aliases.json` from the manifest-count assertion. The 5 *PathRule* JSONs are still asserted by name. Comment in the test documents why.

## Decisions Made

- **PublisherAlias shape (Canonical, Aliases[], Type) over the plan's flat (FolderName, CanonicalPublisher).** The prompt's critical_invariants block specified the richer shape — the Type discriminator lets downstream code weight publisher matches vs product matches differently. Each canonical entry contains many alias variations; this folds the Plan's 30-entry flat list into 15 fatter entries.
- **ResolveAsync signature (folderName, publisher?, displayName?, ct) -> (Score, MatchedCanonical)? over the plan's (folderName, IEnumerable<string> candidatePublishers, ct) -> string?.** The prompt's critical_invariants overrode the plan. The richer return tuple gives downstream confidence-scoring access to the score itself, not just the matched name.
- **Stage-2 token-expansion floors at 0.85 when canonical-tokens-subset-of-rewritten-folder.** Pure Jaccard would give 0.5 for "RVT 2025" -> "Revit" (1 / 2-or-3) — below threshold — making the alias table useless for SKU-suffixed folders. The 0.85 floor reflects the strong evidence carried by an alias-token hit + canonical-coverage. Stays under 1.0 (reserved for exact whole-string alias) so exact > expansion in the max() race.
- **Canonical-as-alias-of-self.** Every canonical entry is registered as its own alias. So `Resolve("Revit")` returns `(1.0, "Revit")` rather than falling through to fuzzy. Eliminates an off-by-one special case in consumers.
- **FuzzyMatcher fallback as no-regression guarantee.** Stage 3 always runs (when publisher OR displayName non-null) — alias stages add to recall; they cannot subtract. Anything FuzzyMatcher would have caught at the Plan-09 baseline is still caught at the same score.
- **Embedded resource colocated with PathRules/.** Plan 10-01's csproj glob already covers `Resources\PathRules\*.json` — placing aliases.json there avoided a third `<EmbeddedResource>` block. The resource's logical name is `DiskScout.Resources.PathRules.aliases.json`. PathRuleEngine.LoadAsync absorbs the structurally-distinct sibling via its existing JsonException catch + empty-Id skip path.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] PathRuleEngineTests Test 1 needed to exclude aliases.json from the manifest-count assertion**
- **Found during:** Task 1 (after creating aliases.json).
- **Issue:** Test 1 (`ManifestResources_ContainExactly5PathRules`) asserts that the `DiskScout.Resources.PathRules.*.json` glob matches EXACTLY 5 resources. Adding `aliases.json` brings the count to 6, breaking the test even though the 5 PathRule JSONs themselves are still present.
- **Fix:** Added `&& !n.EndsWith("aliases.json", StringComparison.OrdinalIgnoreCase)` to the LINQ filter and updated the test's leading XML comment to document why. The test still asserts all 5 PathRule JSONs by name (no recall lost).
- **Files modified:** `tests/DiskScout.Tests/PathRuleEngineTests.cs`
- **Verification:** All 14 PathRuleEngineTests still green; the larger test contract — engine loads PathRules and gracefully skips aliases.json — was already covered by `LoadAsync_NoUserFolder_LoadsEmbeddedRulesOnly` and `Match_System32Path_ReturnsOsCriticalRule` (both still pass).
- **Committed in:** `19a9eee` (folded into Task 1 commit since the fix was directly caused by Task 1's introduction of aliases.json).

### Plan signature departures (per prompt critical_invariants — not auto-fixes)

The prompt's critical_invariants block (constraints #5 and #6) specified a richer PublisherAlias shape and ResolveAsync signature than the plan in 10-03-PLAN.md. These are intentional overrides per the prompt, NOT discovered deviations:

- **PublisherAlias shape:** Plan said `(FolderName, CanonicalPublisher)`; prompt said `(Canonical, Aliases[], Type)`. Implemented per prompt.
- **ResolveAsync signature:** Plan said `(folderName, IEnumerable<string> candidatePublishers, ct) -> string?`; prompt said `(folderName, publisher?, displayName?, ct) -> (double Score, string? MatchedCanonical)?`. Implemented per prompt.
- **aliases.json contents:** Plan said ~30 entries focused on Adobe/MS/JB/NV; prompt said 15 entries (3 publishers + 12 products) focused on the Plan-10 365-item Autodesk/AEC corpus (Revit, BCF Manager, Navisworks, etc.). Implemented per prompt.
- **Test count:** Plan said 9 tests; prompt said 12-18. Implemented 16.

These divergences are documented for traceability but do not constitute auto-fixes (they were specified up-front by the prompt's critical_invariants).

---

**Total deviations:** 1 auto-fixed (Rule 3 - Blocking)
**Impact on plan:** The auto-fix was forced by the simple fact that adding a new EmbeddedResource changed the manifest count baseline assumed by Test 1. No scope creep. The plan signature departures listed above were specified up-front by the prompt and do not constitute deviations.

## Issues Encountered

- **First test run revealed the token-expansion stage's Jaccard math was too strict for the "RVT 2025" -> "Revit" case** (Test 3 `Resolve_AliasExpandsAndMatches_RegistryDisplayName`). Pure Jaccard on a 1-token canonical vs a 2-token folder gives 0.5 — below the 0.7 threshold. Refined the stage-2 algorithm to floor the score at 0.85 when the canonical's tokens are a subset of the rewritten folder tokens. This reflects the strong evidence of an alias-token hit + canonical-coverage. Stays below 1.0 (reserved for exact whole-string match), so exact > expansion in max(). Re-running the suite: 224 / 224 green.
- **No CLAUDE.md violations.** ZERO File.Delete / Directory.Delete / FileSystem.Delete in any of the 4 new source files. Manual DI pattern preserved (App.xaml.cs wiring is reserved for Plan 10-04 / 10-06 per dependency-graph). No new NuGet dependencies. All async methods take CancellationToken.

## User Setup Required

None — purely internal infrastructure. The aliases.json catalog ships embedded; no environment variables, no external services, no dashboard configuration. End-users can override the catalog by dropping a JSON file in `%LocalAppData%\DiskScout\path-rules\aliases.json` once Plan 10-06 wires user-folder merge (currently embedded-only).

## Next Phase Readiness

- `IPublisherAliasResolver` is a stable contract ready for Plan 10-04 to inject into the Registry/Publisher matcher of stage [5] of the orphan pipeline. The pipeline can call `await resolver.ResolveAsync(folder.Name, regEntry.Publisher, regEntry.DisplayName, ct)` and use `MatchedCanonical` to drive the registry-Match O(1) hash lookup against `MachineSnapshot` indexes.
- The 0.7 threshold is the project's canonical fuzzy threshold (matches `FuzzyMatcher.IsMatch` default + the existing `OrphanDetectorService.FuzzyThreshold` constant). No new tunables introduced.
- Test suite green at 224/224 with 0 warnings and 0 errors.
- No regressions: all 197 baseline tests still pass, including all 14 PathRuleEngineTests.
- Plan 10-04 (MultiSourceMatcher) can now wire: `PathRuleEngine` (stages 1+3) + `MachineSnapshotProvider` (stage 4) + `PublisherAliasResolver` (stage 5) into a single per-candidate evaluation pass — the three core consumable contracts of the 7-step pipeline are now all in place.

## Self-Check: PASSED

- FOUND: src/DiskScout.App/Models/PublisherAlias.cs
- FOUND: src/DiskScout.App/Services/IPublisherAliasResolver.cs
- FOUND: src/DiskScout.App/Services/PublisherAliasResolver.cs
- FOUND: src/DiskScout.App/Resources/PathRules/aliases.json
- FOUND: tests/DiskScout.Tests/PublisherAliasResolverTests.cs
- FOUND: commit 19a9eee (Task 1: PublisherAlias model + IPublisherAliasResolver + aliases.json catalog)
- FOUND: commit d32b55c (Task 2: PublisherAliasResolver impl with 3-stage match + 16 tests)

---
*Phase: 10-orphan-detection-precision-refactor*
*Completed: 2026-04-27*
