---
status: partial
phase: 10-orphan-detection-precision-refactor
source: ["10-VERIFICATION.md"]
started: "2026-04-27T13:50:00Z"
updated: "2026-04-27T13:50:00Z"
---

## Current Test

[awaiting human testing — 4 items deferred from 10-VERIFICATION.md]

## Tests

### 1. Score badge colour rendering on AppData orphans

expected: Badge visible for AppData rows only; 5 risk colours match `RiskLevelToBrushConverter` (Aucun=#27AE60, Faible=#2ECC71, Moyen=#F39C12, Eleve=#E67E22, Critique=#E74C3C). Non-AppData rows (StaleTemp, MSI, etc.) show NO badge.

how_to_test:
1. Build: `dotnet publish src/DiskScout.App/DiskScout.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
2. Run elevated: `start-process -verb runAs ./DiskScout.exe`
3. Click **Scanner** on a drive that has `C:\ProgramData` content
4. Open the **Rémanents** tab
5. Expand the AppData orphans group
6. Verify Score column shows a colour-coded badge (green/orange/red) per row
7. Cross-check: empty folders should be green (~90, Aucun); `Microsoft\Crypto` should be filtered out entirely; `Package Cache\*` rows should be at least orange (Eleve)

result: [pending]

### 2. Tooltip shows triggered rules + matchers on hover

expected: Hovering the Score badge displays a tooltip with at least one triggered rule ID (e.g. `os-critical-pd-microsoft`) and/or matcher signal lines (e.g. `Service:NinjaRMMAgent`).

how_to_test:
1. From the Rémanents tab (test 1 done)
2. Mouse-over a Score badge for ~1 second
3. Verify tooltip text appears with rule/matcher breakdown

result: [pending]

### 3. "Pourquoi ?" modal opens, shows trace, closes

expected: Click on the "Pourquoi ?" button → `OrphanDiagnosticsWindow` modal opens centred over main window, displays:
  - Full path, SizeBytes
  - ConfidenceScore (integer 0-100)
  - List of triggered rules with their categories
  - List of matchers consulted with score deltas
  - "Calcul du score" breakdown (initial 100 → each delta)
  - Close button (Fermer) closes the modal cleanly

how_to_test:
1. From the Rémanents tab on an AppData row
2. Click "Pourquoi ?" button
3. Verify modal appears, content is readable, scrollable if needed
4. Click Fermer → modal closes, no exception in `diskscout.log`

result: [pending]

### 4. --audit CLI mode produces Excel-friendly CSV

expected: Running `DiskScout.exe --audit` from an elevated PowerShell produces a CSV at `%LocalAppData%\DiskScout\audits\audit_YYYYMMDD_HHmmss.csv` with:
  - Header: `Path,SizeBytes,ConfidenceScore,RiskLevel,TriggeredRules,ExonerationRules,RecommendedAction`
  - UTF-8 BOM (Excel opens without import wizard)
  - Exit code 0
  - One row per detected AppData orphan candidate

how_to_test:
1. From elevated PowerShell:
   ```powershell
   & "C:\path\to\DiskScout.exe" --audit
   ```
2. After it exits, check `%LocalAppData%\DiskScout\audits\` for the latest `audit_*.csv`
3. Open in Excel — verify columns line up, no garbage characters from BOM mismatch
4. `$LASTEXITCODE` should be 0

result: [pending]

## Summary

total: 4
passed: 0
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps

(none yet — gaps are added if/when human testing reveals issues)

---

**Notes:**
- All 4 items are non-blocking for the phase completion : the underlying logic is verified by the 315 automated tests + the corpus acceptance test (95.6% concordance, 0 critique misclass).
- These items will appear in `/gsd:progress` and `/gsd:audit-uat` until completed.
- If issues are found, run `/gsd:plan-phase 10 --gaps` to spawn closure plans.
