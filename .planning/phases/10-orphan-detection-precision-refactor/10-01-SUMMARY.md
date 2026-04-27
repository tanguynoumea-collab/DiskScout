---
phase: 10-orphan-detection-precision-refactor
plan: 01
subsystem: services
tags: [path-rules, json-engine, embedded-resources, orphan-detection, hard-blacklist, parent-context]

# Dependency graph
requires:
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: PublisherRuleEngine pattern (embedded+user JSON merge by Id), AppPaths helper, ResiduePathSafety as coexisting safety layer
provides:
  - PathRule + RuleHit + MatcherHit records
  - PathCategory enum (6 members) + RiskLevel enum (Aucun=0..Critique=4) + RecommendedAction enum
  - IPathRuleEngine interface (AllRules + LoadAsync + Match)
  - IParentContextAnalyzer interface (GetSignificantParent)
  - PathRuleEngine sealed class (embedded+user JSON merge with last-write-wins by Id)
  - ParentContextAnalyzer sealed class (iterative walk-up of generic leaves)
  - 5 embedded JSON catalogs (DiskScout.Resources.PathRules.*) — 101 rules total
  - AppPaths.PathRulesFolder + AppPaths.AuditFolder (auto-creating properties under %LocalAppData%\DiskScout)
affects:
  - 10-02-MachineSnapshot (already complete — disjoint files, no impact)
  - 10-03-MultiSourceMatcher (consumes IPathRuleEngine + matchers)
  - 10-04-OrphanPipeline (consumes IPathRuleEngine + IParentContextAnalyzer for stages [1][2][3] of 7-step pipeline)
  - 10-05-ConfidenceScorer-and-RiskClassifier (uses MinRiskFloor from PathRule)
  - 10-06-Integration-and-AuditMode (writes CSV to AppPaths.AuditFolder)

# Tech tracking
tech-stack:
  added: []  # zero new NuGet packages — built on System.Text.Json + JsonStringEnumConverter (BCL)
  patterns:
    - "JSON ARRAY embedded resources: each PathRules JSON file is `[{...}, {...}]` (vs PublisherRules which is one object per file). Permits one category-themed file (os-critical, package-cache, ...) to ship many rules."
    - "JsonStringEnumConverter(allowIntegerValues:false) on the engine's JsonSerializerOptions — pins enum-as-string in/out and rejects accidental numeric values from user JSON."
    - "Iterative walk-up parent analyzer with explicit StringComparer.OrdinalIgnoreCase HashSet — no regex, ASCII-only locale codes."

key-files:
  created:
    - src/DiskScout.App/Models/PathRule.cs
    - src/DiskScout.App/Services/IPathRuleEngine.cs
    - src/DiskScout.App/Services/IParentContextAnalyzer.cs
    - src/DiskScout.App/Services/PathRuleEngine.cs
    - src/DiskScout.App/Services/ParentContextAnalyzer.cs
    - src/DiskScout.App/Resources/PathRules/os-critical.json
    - src/DiskScout.App/Resources/PathRules/package-cache.json
    - src/DiskScout.App/Resources/PathRules/driver-data.json
    - src/DiskScout.App/Resources/PathRules/corporate-agent.json
    - src/DiskScout.App/Resources/PathRules/vendor-shared.json
    - tests/DiskScout.Tests/PathRuleEngineTests.cs
    - tests/DiskScout.Tests/ParentContextAnalyzerTests.cs
  modified:
    - src/DiskScout.App/Helpers/AppPaths.cs
    - src/DiskScout.App/DiskScout.App.csproj

key-decisions:
  - "PathRule shape mirrors PublisherRule (record, public sealed) but uses a single PathPattern string + Category enum + nullable MinRiskFloor — covers both HardBlacklist (Category=OsCriticalDoNotPropose) and KnownPathRules in one model."
  - "Embedded JSONs are arrays — one file per category, many rules per file. Accepts both array and single-object shapes for user files for symmetry with PublisherRules."
  - "Match uses prefix match with case-insensitive OrdinalIgnoreCase + sort by expanded-pattern length descending. No regex/glob — keeps the engine fast and deterministic; nesting (general + specific) is expressed as two distinct rules."
  - "JsonStringEnumConverter is mandatory — discovered via test failure that default System.Text.Json deserialized PathCategory/RiskLevel as integers, silently dropping all 101 embedded rules."
  - "ParentContextAnalyzer uses an explicit HashSet of 30 generic leaves + 11 ASCII locale codes (en-us..zh-tw) — no regex on 'xx-xx' shape. Trades 11 InlineData strings for safety (no false positives on vendor folders that happen to be hyphenated like 'add-in')."

patterns-established:
  - "Path-rule JSON arrays under Resources/PathRules/*.json embedded via <EmbeddedResource> with LogicalName DiskScout.Resources.PathRules.%(Filename).json — repeat for any future category-themed catalogs."
  - "Engine ctor pattern: production ctor (uses AppPaths.X + typeof(Engine).Assembly) -> internal test-seam ctor (logger + folder + assembly). Mirrors PublisherRuleEngine."
  - "Match returns RuleHit list sorted by specificity DESC, tie-break by RuleId ASC — deterministic output for golden-file/snapshot tests."

requirements-completed: []

# Metrics
duration: 28m
completed: 2026-04-27
---

# Phase 10 Plan 01: PathRule Foundation Summary

**PathRule + PathRuleEngine + ParentContextAnalyzer foundations with 5 embedded JSON catalogs (101 rules covering OS-critical hard-blacklist, MSI package cache, driver data, corporate agents, vendor shared dirs) — stages [1][2][3] of the 7-step orphan pipeline are now consumable contracts.**

## Performance

- **Duration:** ~28 min
- **Started:** 2026-04-27T15:18:00Z (approx — orchestrator dispatch)
- **Completed:** 2026-04-27T15:46:00Z
- **Tasks:** 3
- **Commits:** 3 atomic task commits + 1 doc commit (this SUMMARY)
- **Files created:** 12
- **Files modified:** 2

## Accomplishments

- 4 new contract files (PathRule.cs + IPathRuleEngine.cs + IParentContextAnalyzer.cs + AppPaths.cs extended with PathRulesFolder + AuditFolder).
- `PathRuleEngine`: embedded+user JSON merge with last-write-wins by Id; never throws on bad JSON; Match returns RuleHits sorted by specificity DESC.
- `ParentContextAnalyzer`: 30 generic leaves + 11 locale codes recognized; iterative walk-up; trailing-separator normalized; drive-root edge case handled.
- 5 embedded JSON catalogs (101 PathRules total) covering the full ProgramData corpus categories observed in the 365-item audit:
  - `os-critical.json` — 70 rules (OsCriticalDoNotPropose) — Windows OS, Defender, GroupPolicy, vault/credentials, ProgramData\\Microsoft\\* tree, FLEXnet, Autodesk Adlm/AdSSO/Licensing, regid.* SWID tags.
  - `package-cache.json` — 5 rules (PackageCache + MinRiskFloor=Eleve) — Burn/WiX Package Cache, Visual Studio Packages/Setup, Windows\\Installer, OneDrive setup.
  - `driver-data.json` — 8 rules (DriverData) — Intel/NVIDIA/AMD/Realtek/HP/Dell/Lenovo OEM driver data.
  - `corporate-agent.json` — 11 rules (CorporateAgent + MinRiskFloor=Eleve) — NinjaRMM, Bitdefender, Zscaler, Matrix42, Empirum, Splashtop, CrowdStrike, SentinelOne, Symantec, Kaspersky, McAfee.
  - `vendor-shared.json` — 7 rules (VendorShared) — Adobe, Autodesk, JetBrains, OneDrive, Google, Mozilla, Oracle.
- 47 new unit tests added (14 PathRuleEngine + 33 ParentContextAnalyzer with theory expansions).
- Final test suite: 197 / 197 passing (140 baseline + 47 from 10-01 + 10 from 10-02 parallel work). 0 warnings, 0 errors. Build clean.
- ZERO occurrences of `File.Delete` / `Directory.Delete` / `FileSystem.Delete` introduced in any of the 12 new code files.

## Task Commits

Each task was committed atomically:

1. **Task 1: Define PathRule + RiskLevel + RecommendedAction contracts** — `7fa270e` (feat)
2. **Task 2: Implement PathRuleEngine + ParentContextAnalyzer + 5 embedded JSON catalogs** — `34c2ee2` (feat)
3. **Task 3: Cover PathRuleEngine + ParentContextAnalyzer with 47 unit tests** — `facad7e` (test, includes Rule-1 auto-fix for JsonStringEnumConverter)

## Files Created/Modified

### Created
- `src/DiskScout.App/Models/PathRule.cs` — PathRule + RuleHit + MatcherHit records, PathCategory + RiskLevel + RecommendedAction enums.
- `src/DiskScout.App/Services/IPathRuleEngine.cs` — engine contract (AllRules, LoadAsync, Match).
- `src/DiskScout.App/Services/IParentContextAnalyzer.cs` — analyzer contract.
- `src/DiskScout.App/Services/PathRuleEngine.cs` — sealed implementation; production+test-seam ctors; embedded+user JSON merge by Id; Match with case-insensitive prefix + specificity ordering.
- `src/DiskScout.App/Services/ParentContextAnalyzer.cs` — sealed implementation; iterative walk-up over OrdinalIgnoreCase HashSet of 30+11 generic leaves.
- `src/DiskScout.App/Resources/PathRules/os-critical.json` — 70 OsCriticalDoNotPropose rules.
- `src/DiskScout.App/Resources/PathRules/package-cache.json` — 5 PackageCache rules with MinRiskFloor=Eleve.
- `src/DiskScout.App/Resources/PathRules/driver-data.json` — 8 DriverData rules.
- `src/DiskScout.App/Resources/PathRules/corporate-agent.json` — 11 CorporateAgent rules with MinRiskFloor=Eleve.
- `src/DiskScout.App/Resources/PathRules/vendor-shared.json` — 7 VendorShared rules.
- `tests/DiskScout.Tests/PathRuleEngineTests.cs` — 14 tests (12 facts + 1 theory[3]).
- `tests/DiskScout.Tests/ParentContextAnalyzerTests.cs` — 33 tests after theory expansion.

### Modified
- `src/DiskScout.App/Helpers/AppPaths.cs` — added PathRulesFolderName + AuditsFolderName constants and PathRulesFolder + AuditFolder static properties (auto-create on access).
- `src/DiskScout.App/DiskScout.App.csproj` — added `<EmbeddedResource Include="Resources\PathRules\*.json"><LogicalName>DiskScout.Resources.PathRules.%(Filename).json</LogicalName>` block.

## Decisions Made

- **Single PathRule model** for both HardBlacklist (Category=OsCriticalDoNotPropose) and KnownPathRules — keeps the data model lean; downstream pipeline branches on Category.
- **Prefix matching only** — no regex/glob patterns. Nesting expressed as two rules (`C:\TestRoot` + `C:\TestRoot\Sub`); engine sorts by expanded-pattern length descending so the more specific rule wins.
- **JsonStringEnumConverter mandatory** — registered on JsonSerializerOptions so PathCategory/RiskLevel deserialize from `"OsCriticalDoNotPropose"`/`"Eleve"` strings rather than integer ordinals; rejects integer values to fail loudly on accidental int values in user JSON.
- **User JSON dual-shape** — accepts both `[{rule}, {rule}]` arrays and single `{rule}` objects for user-folder files; embedded files are always arrays.
- **OS-critical superset** — the 70 os-critical.json rules are a strict SUPERSET of the existing `ResiduePathSafety.CriticalFilesystemSubstrings` (Phase 9 safety floor); no path that was already blocked is now unblocked.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] JsonStringEnumConverter missing on PathRuleEngine JsonOptions**
- **Found during:** Task 3 (unit tests revealed PathRuleEngine.AllRules was empty after LoadAsync)
- **Issue:** Default `System.Text.Json` deserializer expects enums as JSON integers. Embedded JSON files use string names ("OsCriticalDoNotPropose", "Eleve", "VendorShared", ...). Without a `JsonStringEnumConverter`, every rule deserialization threw a `JsonException` which the engine logged at Warning and skipped — silently dropping all 101 embedded rules.
- **Fix:** Added `Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }` to `PathRuleEngine.JsonOptions`. The `allowIntegerValues:false` arg also rejects numeric ordinals in user JSON, forcing string-based enum values for clarity.
- **Files modified:** `src/DiskScout.App/Services/PathRuleEngine.cs`
- **Verification:** All 14 PathRuleEngine tests pass after fix; LoadAsync now loads all 101 rules from the 5 embedded JSONs.
- **Committed in:** `facad7e` (folded into Task 3 commit since it was discovered by Task 3's tests).

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** Necessary for correctness. The bug would have rendered the entire engine inert; Task 2's hand-written tests didn't catch it because they only checked manifest enumeration, not LoadAsync output. The lesson is captured in test coverage going forward (Test 3 `LoadAsync_NoUserFolder_LoadsEmbeddedRulesOnly` now pins this).

## Issues Encountered

- **Parallel-execution race during Task 1 build:** The wave-1 parallel agent (10-02) had created `IServiceEnumerator.cs` + `IScheduledTaskEnumerator.cs` external files but had not yet removed the duplicate `internal` declarations from `ResidueScanner.cs` when I tried to build at end of Task 1. The build failed with CS0101 (duplicate type definition). Resolution: 10-02 agent committed their cleanup (`977c415`) shortly after, after which the build went clean (`0 warnings, 0 errors`). No code change needed on my side — strictly a sequencing artifact of concurrent execution.

## Next Phase Readiness

- `IPathRuleEngine` and `IParentContextAnalyzer` are stable contracts ready to be consumed by:
  - **Plan 10-03 (MultiSourceMatcher):** parallel matchers + `IMachineSnapshotProvider` (already shipped by 10-02) feed into the pipeline alongside the rule engine.
  - **Plan 10-04 (OrphanPipeline):** wires stages [1] HardBlacklist (filter on `Category == OsCriticalDoNotPropose`), [2] ParentContextAnalyzer.GetSignificantParent, [3] PathRuleEngine.Match. Plan 10-04 must also wire the new services into `App.xaml.cs` manual-DI (deliberately deferred from this plan to avoid concurrent-merge conflict with 10-02).
  - **Plan 10-05 (ConfidenceScorer + RiskClassifier):** consumes `MinRiskFloor` to enforce the safety floor declarative semantics (PackageCache + CorporateAgent => never auto-Supprimer).
  - **Plan 10-06 (Integration + Audit Mode):** writes audit CSV to `AppPaths.AuditFolder`.
- 5 embedded JSON catalogs ship in the assembly under `DiskScout.Resources.PathRules.*` — verifiable via `typeof(PathRuleEngine).Assembly.GetManifestResourceNames()`.
- Test suite green at 197/197 with 0 warnings and 0 errors.
- No regressions: all 140 baseline tests still pass.

## Self-Check: PASSED

- FOUND: src/DiskScout.App/Models/PathRule.cs
- FOUND: src/DiskScout.App/Services/IPathRuleEngine.cs
- FOUND: src/DiskScout.App/Services/IParentContextAnalyzer.cs
- FOUND: src/DiskScout.App/Services/PathRuleEngine.cs
- FOUND: src/DiskScout.App/Services/ParentContextAnalyzer.cs
- FOUND: src/DiskScout.App/Resources/PathRules/os-critical.json
- FOUND: src/DiskScout.App/Resources/PathRules/package-cache.json
- FOUND: src/DiskScout.App/Resources/PathRules/driver-data.json
- FOUND: src/DiskScout.App/Resources/PathRules/corporate-agent.json
- FOUND: src/DiskScout.App/Resources/PathRules/vendor-shared.json
- FOUND: tests/DiskScout.Tests/PathRuleEngineTests.cs
- FOUND: tests/DiskScout.Tests/ParentContextAnalyzerTests.cs
- FOUND: commit 7fa270e (Task 1)
- FOUND: commit 34c2ee2 (Task 2)
- FOUND: commit facad7e (Task 3)

---
*Phase: 10-orphan-detection-precision-refactor*
*Completed: 2026-04-27*
