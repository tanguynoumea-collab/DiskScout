---
status: partial
phase: 09-programs-tab-real-uninstaller-assistant
source: [09-VERIFICATION.md]
started: "2026-04-25T16:50:00Z"
updated: "2026-04-25T16:50:00Z"
---

## Current Test

[awaiting human testing — user will validate on the running prototype]

## Tests

### 1. End-to-end wizard navigation on a real installed program
expected: Right-click a program in the Programs tab → context-menu "Désinstaller…" → modal wizard opens. Navigate Selection → Preview (publisher rules previewed) → Run (live stdout/stderr from native uninstaller) → ResidueScan (7 categories enumerated) → ConfirmDelete (default-unchecked tree, every leaf opt-in) → Report (JSON+HTML export buttons). Recommended target: a JetBrains IDE (rich rule + likely InstallTrace) or Mozilla Firefox (rich rule). Also test a small generic app (no rule match) for the no-match path.
result: [pending]

### 2. IRRÉVERSIBLE modal safety UX
expected: At ConfirmDelete step, click "Confirmer la suppression" → final modal appears with: explicit "DÉFINITIVEMENT" + "IRRÉVERSIBLE" + "pas de quarantaine, pas de corbeille" wording, `MessageBoxImage.Stop` red-circle icon, default button = "Non". Clicking "Non" or pressing Esc cancels without deletion. Clicking "Oui" performs `IFileDeletionService.DeleteAsync(paths, DeleteMode.Permanent)`.
result: [pending]

### 3. JSON + HTML report export
expected: After ConfirmDelete completes, wizard advances to Report step. "Exporter JSON" and "Exporter HTML" buttons each open a SaveFileDialog. JSON output is valid (open in any JSON viewer, structure matches `UninstallReport` schema). HTML opens in a browser with dark theme, all sections render (Identité du programme / Désinstalleur natif / Résidus détectés / Résultat de la suppression / Règles éditeur / Trace d'installation), no broken styling, no `<script>` tag. Try a program whose name contains `<` or `&` (e.g. "AT&T Connect") to validate XSS-encoding.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
