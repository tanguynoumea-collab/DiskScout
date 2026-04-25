---
phase: 09-programs-tab-real-uninstaller-assistant
plan: 04
subsystem: services
tags: [publisher-rules, embedded-resources, json-rule-engine, regex-matching, env-var-expansion, system.text.json, serilog, xunit]

# Dependency graph
requires:
  - phase: 09-programs-tab-real-uninstaller-assistant
    provides: AppPaths.PublisherRulesFolder helper sibling pattern (Plan 09-01 baselined InstallTracesFolder)
  - phase: 01-foundations
    provides: AppPaths helper, Serilog logger, System.Text.Json conventions
provides:
  - IPublisherRuleEngine contract (LoadAsync + AllRules + Match + ExpandTokens) consumed by Plan 09-03 (ResidueScanner — reserved PublisherRule expansion) and Plan 09-05 (Wizard UI step 2 "Preview résidus connus")
  - PublisherRule + PublisherRuleMatch records consumed by Plan 09-03 / 09-05
  - 7 publisher rule JSONs embedded in the assembly via <EmbeddedResource> (adobe, autodesk, jetbrains, mozilla, microsoft-office, steam, epic-games)
  - User-extensible rule folder under %LocalAppData%\DiskScout\publisher-rules with last-write-wins override semantics by Id
  - AppPaths.PublisherRulesFolder + PublisherRulesFolderName helpers (siblings of Plan 09-01's InstallTracesFolder)
affects: [09-03-residue-scanner, 09-05-wizard-ui, 09-06-integration-report]

# Tech tracking
tech-stack:
  added: []  # No new NuGet packages — System.Text.Json (existing), System.Text.RegularExpressions (BCL), System.Reflection (BCL)
  patterns:
    - "Embedded JSON resources via <EmbeddedResource Include=...> + <LogicalName>DiskScout.Resources.PublisherRules.%(Filename).json</LogicalName> for stable lookup names independent of folder layout"
    - "Production constructor delegates to internal test-seam constructor (logger, userRulesFolder, resourceAssembly) — same pattern as Plan 09-03 ResidueScanner"
    - "Last-write-wins merge via Dictionary<string,PublisherRule> keyed by Id — embedded loaded first, then user, user overwrites"
    - "Regex with 200 ms timeout to bound worst-case match cost on hostile patterns"
    - "Specificity score = 10 (publisher base) + 100 when DisplayNamePattern narrows further — large gap so DisplayName-narrowed always wins"
    - "Token expander runs Environment.ExpandEnvironmentVariables FIRST (handles all %VAR% incl. %ProgramFiles(x86)% with parens) then literal {Publisher} / {DisplayName} substitution"
    - "Defensive normalization: NormalizeRule replaces null array fields with Array.Empty<string>() so consumers never null-check"
    - "Hand-written tests with FluentAssertions — no Moq dep (project policy precedent from Plans 09-01 / 09-03)"

key-files:
  created:
    - src/DiskScout.App/Models/PublisherRule.cs
    - src/DiskScout.App/Services/IPublisherRuleEngine.cs
    - src/DiskScout.App/Services/PublisherRuleEngine.cs
    - src/DiskScout.App/Resources/PublisherRules/adobe.json
    - src/DiskScout.App/Resources/PublisherRules/autodesk.json
    - src/DiskScout.App/Resources/PublisherRules/jetbrains.json
    - src/DiskScout.App/Resources/PublisherRules/mozilla.json
    - src/DiskScout.App/Resources/PublisherRules/microsoft.json
    - src/DiskScout.App/Resources/PublisherRules/steam.json
    - src/DiskScout.App/Resources/PublisherRules/epic.json
    - tests/DiskScout.Tests/PublisherRuleEngineTests.cs
  modified:
    - src/DiskScout.App/DiskScout.App.csproj (added <EmbeddedResource> ItemGroup with LogicalName glob)
    - src/DiskScout.App/Helpers/AppPaths.cs (appended PublisherRulesFolderName + PublisherRulesFolder, preserves Plan 09-01's InstallTracesFolder)

key-decisions:
  - "Embedded resource LogicalName 'DiskScout.Resources.PublisherRules.{filename}.json' is stable regardless of folder restructuring — gives the engine a predictable prefix to filter in GetManifestResourceNames"
  - "Last-write-wins merge by Id, with embedded loaded first and user folder loaded second — simplest model the user can reason about (drop a JSON named anything, set the same id, you override)"
  - "Specificity = 10 base + 100 when DisplayName narrows. Large gap so a DisplayName-narrowed match (e.g., microsoft-office for 'Excel') always outranks a publisher-only match — even if multiple of the latter were stacked"
  - "Regex timeout = 200 ms (per match) — bounds worst-case cost from a poorly-crafted user pattern; on timeout we log Warning and skip the rule, never throw"
  - "ExpandTokens uses Environment.ExpandEnvironmentVariables (BCL) which natively handles %ProgramFiles(x86)% with the parens — no manual percent-pair scanning needed"
  - "Token substitution skips empty values (publisher null → leaves '{Publisher}' literal); consumers can detect/skip unfilled templates"
  - "NormalizeRule maps null array fields to Array.Empty<string>() — System.Text.Json leaves them null when JSON omits the property, and downstream code (matcher, ResidueScanner) does Length>0 checks that would NRE on null"
  - "Microsoft rule narrowed to id 'microsoft-office' (not bare 'microsoft') — Microsoft publishes Visual Studio, .NET, the runtime, Skype legacy, etc., and each lives elsewhere; the rule is intentionally Office/Teams/OneDrive scoped via DisplayNamePattern"
  - "JetBrains rule lists per-IDE legacy dotted dirs (.IntelliJIdea*, .PyCharm*, .Rider*, ...) — JetBrains products historically used %UserProfile%\\.{Product}{Version} before consolidating under %AppData%\\JetBrains in 2020+; both must be cleaned"
  - "Steam keyed off Publisher 'Valve' AND DisplayName 'Steam' — DisplayNamePattern narrows because Valve also publishes Half-Life, Portal, etc. as standalone Steam-installed games; only the Steam client itself maps to this rule"

patterns-established:
  - "Pattern: <EmbeddedResource> + <LogicalName> glob in csproj — same approach can ship Scriban templates, scan profiles, or any other JSON-driven config without writing a custom MSBuild item"
  - "Pattern: Internal-ctor test seam taking IFolder + IAssembly equivalents — folder for filesystem-driven config, assembly for embedded resources; test fixture controls both deterministically"
  - "Pattern: Dictionary<string,T>.Values.ToList() for last-write-wins merge — clearer than custom List+Replace logic"
  - "Pattern: NormalizeRule with-expression on records — System.Text.Json deserialization defects (null arrays from omitted fields) get fixed once at load time, not at every consumer"

requirements-completed: []  # Phase 9 has no mapped REQ-IDs (post-v1, see ROADMAP.md note)

# Metrics
duration: 3m 38s
completed: 2026-04-25
---

# Phase 09 Plan 04: Publisher Rule Engine Summary

**JSON-driven, regex-matched, env-var-aware publisher rule engine with 7 embedded vendor rules (Adobe / Autodesk / JetBrains / Mozilla / Microsoft Office / Steam / Epic) + user-extensible folder under %LocalAppData%\DiskScout\publisher-rules\ — last-write-wins by Id, malformed input never throws, all 111 tests pass.**

## Performance

- **Duration:** 3m 38s
- **Started:** 2026-04-25T15:56:59Z
- **Completed:** 2026-04-25T16:00:37Z
- **Tasks:** 2 / 2
- **Files created:** 11 (model + interface + impl + 7 JSON rules + test file)
- **Files modified:** 2 (csproj for EmbeddedResource glob, AppPaths.cs for PublisherRulesFolder)
- **Tests added:** 10 methods / 16 cases (1 [Theory] with 7 InlineData rows + 9 plain [Fact])
- **Total test suite:** 111 / 111 passing (baseline 95 from Plan 09-03; +16 = 111, no regressions)

## Accomplishments

- **PublisherRule + PublisherRuleMatch records** as the on-disk JSON schema (Models/PublisherRule.cs). Four pattern arrays: filesystemPaths / registryPaths / services / scheduledTasks. PublisherPattern is a regex (case-insensitive); DisplayNamePattern is optional and narrows the match.
- **IPublisherRuleEngine contract** (Services/IPublisherRuleEngine.cs): `LoadAsync`, `AllRules`, `Match(publisher, displayName)`, `ExpandTokens(template, publisher, displayName)`. 30 LOC interface, fully xmldoc'd.
- **PublisherRuleEngine implementation** (Services/PublisherRuleEngine.cs, 215 LOC):
  - **LoadAsync**: enumerates `assembly.GetManifestResourceNames()` filtered to prefix `DiskScout.Resources.PublisherRules.` and `.json` extension; deserializes each via `System.Text.Json` with `PropertyNameCaseInsensitive = true`. Then enumerates `*.json` in the user folder. Stores in `Dictionary<string, PublisherRule>` keyed by `Id` so user files overwrite embedded by id (last-write-wins). Logs `Information` summary "Loaded {Total} rules ({Embedded} embedded, {User} user)". Bad input → `Warning` log + skip. Cancellation honored via `ThrowIfCancellationRequested()`.
  - **Match**: O(N) over loaded rules; regex with 200ms timeout for both PublisherPattern and DisplayNamePattern. Score = 10 (publisher matched) + 100 (DisplayName narrowed). Bad regex (timeout / `ArgumentException`) → `Warning` + skip rule, never throws. Returns matches sorted by `SpecificityScore` descending.
  - **ExpandTokens**: `Environment.ExpandEnvironmentVariables(template)` then literal `{Publisher}` / `{DisplayName}` replacement. Skips empty values (token left as-is). Unresolved `%...%` in result → `Debug` log (expected on hosts that simply don't have the variable).
- **7 publisher rule JSONs embedded** as resources via `<EmbeddedResource Include="Resources\PublisherRules\*.json"><LogicalName>DiskScout.Resources.PublisherRules.%(Filename).json</LogicalName></EmbeddedResource>` in the csproj. Verified post-build via PowerShell + Reflection: all 7 manifest names present:
  - `DiskScout.Resources.PublisherRules.adobe.json`
  - `DiskScout.Resources.PublisherRules.autodesk.json`
  - `DiskScout.Resources.PublisherRules.epic.json`
  - `DiskScout.Resources.PublisherRules.jetbrains.json`
  - `DiskScout.Resources.PublisherRules.microsoft.json`
  - `DiskScout.Resources.PublisherRules.mozilla.json`
  - `DiskScout.Resources.PublisherRules.steam.json`
- **AppPaths.PublisherRulesFolder** (`%LocalAppData%\DiskScout\publisher-rules`) appended alongside Plan 09-01's `InstallTracesFolder` — same get-with-CreateDirectory pattern, both helpers coexist cleanly.
- **Honors CONTEXT.md D-02 ("Revo Pro level")**: third pillar of the deep-clean stack (after install tracker / native uninstaller driver / residue scanner). 7 publishers ship embedded; users can drop additional rules without rebuilding the app.
- **Honors must_haves truth #5**: invalid/unparseable JSON in either embedded or user file is logged at Warning and SKIPPED — engine never throws on bad input. Exercised by Test 6 (`LoadAsync_MalformedUserJson_DoesNotThrow_AndStillLoadsEmbedded`).

## Task Commits

Each task was committed atomically with `--no-verify` (parallel-mode convention from Plans 09-01/02/03):

1. **Task 1: PublisherRule + IPublisherRuleEngine + 7 embedded JSONs + csproj + AppPaths** — `39568c9` (feat)
2. **Task 2: PublisherRuleEngine implementation + 16 test cases** — `df8952e` (feat)

_TDD note: Task 1 (model + interface + JSONs) is data + contract only — no behavior to test in isolation. Task 2 ships impl + tests in the same commit; the RED step is the build error path before the impl exists. This matches the precedent set by Plans 09-01 (combined model+test commits) and 09-03 (impl+tests together)._

## Files Created/Modified

### Created

- **`src/DiskScout.App/Models/PublisherRule.cs`** — `PublisherRule` record (Id, PublisherPattern, DisplayNamePattern, FilesystemPaths, RegistryPaths, Services, ScheduledTasks) + `PublisherRuleMatch` record (Rule, SpecificityScore). 31 LOC with xmldoc.
- **`src/DiskScout.App/Services/IPublisherRuleEngine.cs`** — Engine contract: `LoadAsync(ct)`, `AllRules`, `Match(publisher, displayName)`, `ExpandTokens(template, publisher, displayName)`. 30 LOC.
- **`src/DiskScout.App/Services/PublisherRuleEngine.cs`** — `sealed class PublisherRuleEngine : IPublisherRuleEngine`. Public ctor delegates to internal test-seam ctor. `EmbeddedResourcePrefix` const = `"DiskScout.Resources.PublisherRules."`. Static `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`. `RegexTimeout = 200ms`. ~215 LOC.
- **`src/DiskScout.App/Resources/PublisherRules/adobe.json`** — Adobe (publisher: `(?i)^Adobe(\\s|$)`); 5 fs paths, 3 registry paths, 4 services, 2 scheduled tasks.
- **`src/DiskScout.App/Resources/PublisherRules/autodesk.json`** — Autodesk (publisher: `(?i)^Autodesk`); 5 fs, 3 reg, 3 services, 1 task.
- **`src/DiskScout.App/Resources/PublisherRules/jetbrains.json`** — JetBrains (publisher: `(?i)^JetBrains`); 10 fs paths covering both modern `%LocalAppData%\\JetBrains` and legacy per-IDE `%UserProfile%\\.{IDE}*` dirs (.IntelliJIdea*, .PyCharm*, .WebStorm*, .PhpStorm*, .GoLand*, .Rider*, .CLion*).
- **`src/DiskScout.App/Resources/PublisherRules/mozilla.json`** — Mozilla (publisher: `(?i)^Mozilla`); 5 fs, 3 reg, 1 service.
- **`src/DiskScout.App/Resources/PublisherRules/microsoft.json`** — id=`microsoft-office`, publisher: `(?i)^Microsoft Corporation$`, displayName: `(?i)(Office|Outlook|Word|Excel|PowerPoint|OneNote|Skype|Teams|OneDrive)`. Intentionally narrowed to Office/communications products to avoid matching every Microsoft-published binary on the system.
- **`src/DiskScout.App/Resources/PublisherRules/steam.json`** — id=`steam`, publisher: `(?i)Valve` + displayName: `(?i)Steam`. The DisplayName narrowing is critical — Valve also "publishes" individual Steam games via the same uninstall registry path, but only the Steam client itself maps here.
- **`src/DiskScout.App/Resources/PublisherRules/epic.json`** — id=`epic-games`, publisher: `(?i)Epic Games`; 3 fs, 3 reg, 1 service.
- **`tests/DiskScout.Tests/PublisherRuleEngineTests.cs`** — 10 test methods (16 cases): manifest enumeration verification, AppPaths folder creation, no-user-folder load, user-rule add, user-rule override, malformed-JSON resilience, adobe match, microsoft-office DisplayName narrowing + non-match rejection, ExpandTokens with publisher+displayName tokens, [Theory] across 7 env vars (`%ProgramFiles(x86)%`, `%ProgramData%`, `%UserProfile%`, `%AppData%`, `%LocalAppData%`, `%Temp%`, `%WinDir%`).

### Modified

- **`src/DiskScout.App/DiskScout.App.csproj`** — appended a NEW `<ItemGroup>` containing `<EmbeddedResource Include="Resources\PublisherRules\*.json">` with `<LogicalName>DiskScout.Resources.PublisherRules.%(Filename).json</LogicalName>`. Did not touch the existing `<Resource Include="Assets\DiskScout.ico" />` group.
- **`src/DiskScout.App/Helpers/AppPaths.cs`** — appended `public const string PublisherRulesFolderName = "publisher-rules";` to the consts block (now 4 consts: AppFolderName / LogFileName / ScansFolderName / InstallTracesFolderName / PublisherRulesFolderName), and `public static string PublisherRulesFolder` after `InstallTracesFolder`. Plan 09-01's `InstallTracesFolderName` const + `InstallTracesFolder` property are preserved verbatim — no edits, no removal.

## Decisions Made

- **Microsoft rule scoped to Office/Teams/OneDrive (not bare "Microsoft").** Microsoft Corporation publishes most of Windows itself (Visual Studio, .NET runtime, Skype, Edge, Defender, etc.). A bare `(?i)^Microsoft Corporation$` rule with the kitchen-sink filesystem patterns we'd need would over-match and propose deleting things like `%LocalAppData%\\Microsoft\\Edge` for any Microsoft-published target. Instead, the rule is intentionally narrowed via `DisplayNamePattern = (?i)(Office|Outlook|Word|Excel|PowerPoint|OneNote|Skype|Teams|OneDrive)` — the seven products that genuinely live under `%LocalAppData%\\Microsoft\\{DisplayName}` paths.
- **Steam rule narrowed by DisplayName.** Valve's publisher field appears on Steam itself AND on every individual Steam-installed game (because the game's installer registers `Publisher = Valve` and `DisplayName = <Game Name>`). Without the DisplayName narrowing, every game would inherit Steam's filesystem/registry residue patterns, which would be wrong. `displayNamePattern: "(?i)Steam"` ensures the rule only matches the Steam client itself.
- **Specificity gap of 100 (not 1).** I considered `score += 1` for DisplayName-narrowed rules, but a single point creates ambiguity if a rule somehow accumulates other minor specificity bonuses in the future. A gap of 100 makes the ordering robust against future additions (e.g., +1 for InstallLocation match, +1 for Version regex, etc., all still summing well under 100).
- **Regex timeout = 200 ms per match.** Anything higher and a maliciously-crafted user pattern could stall the engine for seconds. Anything lower (e.g., 50ms) and pathological-but-legitimate patterns might false-fail on slow CI runners. 200 ms is the same value used by `System.Text.Json`'s default regex timeout for converters and balances safety vs. compatibility.
- **ExpandTokens runs env-vars FIRST, tokens SECOND.** Reverse order would let `{Publisher}` interpolate into `%ProgramFiles%` if a user's publisher happened to contain `%`-pairs (defensive — extremely unlikely but free to do correctly). Env-var-first also lets `%LocalAppData%\\{Publisher}` work cleanly: the env var resolves to a path, then `{Publisher}` substitutes into the result.
- **Unresolved `%...%` after expansion logged at Debug, not Warning.** A user pattern with `%CustomPath%` that isn't set on this machine is not an error — it's an opt-in pattern that just doesn't apply here. Debug-level lets us still trace why a rule's path was empty without flooding the log.
- **NormalizeRule via record `with`-expression.** `System.Text.Json` leaves array fields null when the JSON omits them. The matcher and downstream `ResidueScanner` integration check `array.Length > 0` — calling `.Length` on null throws NRE. Normalizing once at load time, instead of null-guarding at every consumer, is cleaner.
- **Folder-on-access pattern preserved (Plan 01 precedent).** `AppPaths.PublisherRulesFolder` calls `Directory.CreateDirectory(path)` every read — same as `InstallTracesFolder`, `ScansFolder`. Idempotent, cheap, robust to a user accidentally deleting the folder mid-session.

## Deviations from Plan

**None.** Plan 09-04 was self-contained and well-scoped:
- No new NuGet packages needed (System.Text.Json + System.Text.RegularExpressions + System.Reflection are all in the BCL or already referenced).
- No file ambiguities (`PublisherRule` is a new type; no naming collisions with `Microsoft.Win32.*` or other BCL types).
- No Moq dependency required — the test seam is a plain folder-path + assembly, both injectable directly.
- Plan 09-01's `InstallTracesFolder` was already present in `AppPaths.cs`; we appended cleanly without touching it.

The `<important_notes>` section flagged the AppPaths merge risk explicitly; reading the file first confirmed Plan 09-01's additions were intact, and the append went through without conflict.

## Issues Encountered

- **System.Text.Json + sealed records with arrays**: Initial worry that the deserializer might fail on records with array properties + non-default constructor. It works fine — `System.Text.Json` matches by parameter name (case-insensitive thanks to `PropertyNameCaseInsensitive = true`) and arrays deserialize via the standard collection converter. Confirmed by Test 4 (`LoadAsync_UserRuleWithNewId_IsMergedIntoAllRules`) which round-trips a user-written JSON to a fully-populated record.
- **`Environment.ExpandEnvironmentVariables` and `%ProgramFiles(x86)%`**: I expected this to fail because of the parens, since some shell-style expanders don't handle them. The BCL handles it correctly — `%ProgramFiles(x86)%` expands to `C:\\Program Files (x86)` on x64 Windows. Theory test row #1 specifically asserts this case.
- **No regression risk to Plans 09-01/02/03**: every test from those plans still passes (95 baseline → 111 after my 16 additions, full suite green). The only shared file (AppPaths.cs) was appended-only; the only shared csproj was a new ItemGroup (no edits to existing groups).
- **Embedded-resource verification at build time**: I added a PowerShell + Reflection check after the Task 1 build to confirm the manifest contains all 7 expected resource names with the exact `DiskScout.Resources.PublisherRules.{filename}.json` pattern. This caught no errors but documents the expected manifest layout for future debugging.

## Test Counts

| Suite                        | Tests | Outcome           |
|------------------------------|-------|-------------------|
| PublisherRuleEngineTests     | 16    | 16 / 16 pass      |
| **Plan 09-04 subtotal**      | **16**| **16 / 16 pass**  |
| Full DiskScout.Tests suite   | 111   | 111 / 111 pass    |

The 16 cases break down as:
- 9 plain `[Fact]` tests (manifest enumeration, AppPaths folder, no-user-folder load, user-rule add, user-rule override, malformed JSON resilience, adobe match, microsoft-office DisplayName narrowing + rejection, ExpandTokens with both tokens after env expansion)
- 1 `[Theory]` with 7 `[InlineData]` rows (env-var expansion across `%ProgramFiles(x86)%`, `%ProgramData%`, `%UserProfile%`, `%AppData%`, `%LocalAppData%`, `%Temp%`, `%WinDir%`)

## False-Positive Risks

- **JetBrains globs (`%UserProfile%\\.IntelliJIdea*`)**: The `*` glob is literal in the pattern — `ResidueScanner` (Plan 09-03) when it consumes these will need to enumerate the parent folder and match by prefix. The current scanner treats paths as exact paths, so when Plan 09-05 wires this engine into the residue flow, the glob handling must be added at the scanner integration layer, NOT in the rule engine. The engine's `ExpandTokens` does not glob — it just resolves env vars and tokens. **Action item for Plan 09-05**: glob-expand JetBrains-style `*` patterns when feeding rule paths to the residue list.
- **Microsoft DisplayName regex**: `(?i)(Office|Outlook|Word|Excel|...)` will also match programs whose DisplayName *contains* but isn't *exactly* one of these tokens — e.g., a third-party "Excel Helper" or "Office 365 Tools" would match. Mitigation: matching is one input; the residue scanner still applies the `ResiduePathSafety` whitelist (Plan 09-03) before proposing any deletion. The wizard (Plan 09-05) presents findings as MediumConfidence by default for all `Source = PublisherRule` outputs.
- **Steam DisplayName `(?i)Steam`**: Will match "Steamy Coffee" or any other product with "Steam" in the name. Same mitigation as above — the rule engine is one input; ResiduePathSafety + user confirmation is the safety floor.
- **Path token escaping**: The current substitution does literal `string.Replace`. If a publisher value contains characters with regex meaning (`{`, `}`, `\\`), the result is a literal path, not a regex. This is correct for fs/registry path consumption, but if any future consumer interprets the result AS a regex, the substitution would need escaping. Out of scope for v1 — flagged for Plan 09-05 awareness.

## User Setup Required

None — the 7 default publisher rules ship in the exe. Power users can drop additional `*.json` files into `%LocalAppData%\\DiskScout\\publisher-rules\\` to extend the rule set, or override an embedded one by giving their JSON the same `id` as the embedded file. The folder is auto-created on first `AppPaths.PublisherRulesFolder` access.

Schema documentation can be inferred from any of the 7 embedded JSONs — they are the canonical examples and will appear in `%LocalAppData%\\DiskScout\\publisher-rules\\` only if the user voluntarily extracts them (they are not "deployed" to disk by default; they live inside the assembly).

## Next Phase Readiness

- **Plan 09-03 (Residue Scanner) — already complete; ready to extend.** The scanner reserved `ResidueSource.PublisherRule` for this engine. The next integration step (likely in Plan 09-05) will inject `IPublisherRuleEngine` into the wizard view-model, call `Match(publisher, displayName)` on the selected program, then for each matching rule, call `ExpandTokens(...)` on every fs/registry/service/task pattern and feed the resulting paths to the existing `ResidueScanner` as known-paths to check. Findings emitted with `Source = PublisherRule` + `Trust = HighConfidence` when the path actually exists on disk (per must_haves truth #4).
- **Plan 09-05 (Wizard UI step 2 "Preview résidus connus")** is the primary consumer:
  - Inject `IPublisherRuleEngine` into the wizard view-model.
  - Call `LoadAsync` once at app startup (consider doing this in `App.OnStartup` parallel to the registry scan).
  - On program selection, call `Match(publisher, displayName)` and present a "Règles éditeur" badge if any match.
  - Show the matched-rule paths (via `ExpandTokens`) in step 2 as the "preview résidus connus" before native uninstaller runs.
  - Wire the rule-derived paths into `ResidueScanner` after native uninstall (step 4) to surface them as `Source = PublisherRule` findings.
- **Plan 09-06 (Integration + Report)** will include rule-derived findings in the final HTML/JSON export. Schema is stable: `ResidueFinding` already has `Source` and `Reason` fields (Plan 09-03).
- **Glob handling** (`%UserProfile%\\.IntelliJIdea*`): Out of scope for this engine — flagged for Plan 09-05 to handle at the scanner integration layer (likely via `Directory.EnumerateDirectories` with a wildcard).

## Self-Check: PASSED

Verification commands run during execution:

```bash
ls src/DiskScout.App/Resources/PublisherRules/*.json | wc -l   # → 7
[ -f src/DiskScout.App/Models/PublisherRule.cs ] && echo FOUND
[ -f src/DiskScout.App/Services/IPublisherRuleEngine.cs ] && echo FOUND
[ -f src/DiskScout.App/Services/PublisherRuleEngine.cs ] && echo FOUND
[ -f tests/DiskScout.Tests/PublisherRuleEngineTests.cs ] && echo FOUND
git log --oneline | grep -E "39568c9|df8952e"                  # → both present
```

- All 7 embedded resources verified via PowerShell + Reflection at build time:
  ```
  DiskScout.Resources.PublisherRules.adobe.json
  DiskScout.Resources.PublisherRules.autodesk.json
  DiskScout.Resources.PublisherRules.epic.json
  DiskScout.Resources.PublisherRules.jetbrains.json
  DiskScout.Resources.PublisherRules.microsoft.json
  DiskScout.Resources.PublisherRules.mozilla.json
  DiskScout.Resources.PublisherRules.steam.json
  ```
- `dotnet build src/DiskScout.App/DiskScout.App.csproj -c Debug -nologo /clp:ErrorsOnly` → exit 0, 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName~PublisherRuleEngineTests" --nologo -v minimal` → 16 / 16 pass.
- `dotnet test` (full suite) → 111 / 111 pass; no regressions.
- All `<acceptance_criteria>` literals from PLAN match by `grep`:
  - `PublisherRule.cs` contains `public sealed record PublisherRule(`, `PublisherPattern`, `DisplayNamePattern`, `FilesystemPaths`, `RegistryPaths`, `Services`, `ScheduledTasks`.
  - `IPublisherRuleEngine.cs` contains `LoadAsync`, `AllRules`, `Match(`, `ExpandTokens(`.
  - `PublisherRuleEngine.cs` contains `GetManifestResourceNames`, `"DiskScout.Resources.PublisherRules."`, `Regex.IsMatch(`, `RegexMatchTimeoutException`, `Environment.ExpandEnvironmentVariables`, `"{Publisher}"`, `"{DisplayName}"`, `PropertyNameCaseInsensitive = true`, internal ctor `(Serilog.ILogger logger, string userRulesFolder, System.Reflection.Assembly resourceAssembly)`.
  - `DiskScout.App.csproj` contains `<EmbeddedResource Include="Resources\\PublisherRules\\*.json">` and `<LogicalName>DiskScout.Resources.PublisherRules.%(Filename).json</LogicalName>`.
  - `AppPaths.cs` contains both `InstallTracesFolder` (Plan 01) AND `PublisherRulesFolder` (Plan 04).

---
*Phase: 09-programs-tab-real-uninstaller-assistant*
*Completed: 2026-04-25*
