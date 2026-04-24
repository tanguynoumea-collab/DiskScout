# DiskScout

**Windows disk analyzer & cleaner with AI-assisted safety audit.**
Scan, classify, and clean up your disk — with a built-in button that generates an analysis prompt for ChatGPT / Claude / Gemini / Mistral to verify the safety of every deletion you plan.

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2B-0078d4)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Status: Alpha](https://img.shields.io/badge/Status-Alpha-orange)

---

## Why DiskScout

Most disk cleaners are either **fast but blind** (WizTree: great MFT scan, no cleanup) or **actionable but opaque** (CCleaner: broad cleanup, no visibility). DiskScout combines the two, **plus** it's cloud-aware (OneDrive/SharePoint placeholders handled correctly) and **plus** it ships the first AI-assisted safety layer: one click copies a structured prompt describing your exact selection, context-rich enough for any modern LLM to audit each item before you hit delete.

### What's unique

- **🤖 Log IA button** — on 4 cleanup tabs. Copies a tab-aware prompt for any LLM (ChatGPT, Claude, Gemini, Mistral) to score the deletion risk of your selection per item.
- **OneDrive / SharePoint aware** — physical bytes on disk vs logical cloud bytes are separated correctly using Windows reparse tags (`IO_REPARSE_TAG_CLOUD_*`). Placeholders don't inflate your totals.
- **Quarantine built-in** — deletions can land in a 30-day managed quarantine instead of the Windows Recycle Bin. One click restores them.
- **File safety score** — before batch purge, items under `.git`, `*.sln`, `package.json` or OS-critical paths are flagged with a ⚠ warning.
- **11 tabs, unified UX** — Santé dashboard, Programmes, Rémanents, Nettoyage, Arborescence, Carte (treemap), Gros fichiers, Extensions, Doublons (hash-verified), Vieux fichiers, Quarantaine.
- **Portable** — single-file self-contained `.exe`, no install.

---

## Quick start

1. Download `DiskScout.exe` from the [latest release](../../releases).
2. Double-click — accept the UAC prompt (admin required to read registry and `Program Files`).
3. Pick the drives you want, click **Scanner** (or **Scanner dossier…** for a focused scan).
4. When the scan finishes, the **Santé** tab opens with a grade (A+ → D) and prioritized actions.

Typical first scan: ~3 minutes / 500 GB on SSD.

---

## The 11 tabs

| Tab | What it's for |
|---|---|
| **📊 Santé** | Dashboard: grade, drive tiles, top actions, metric cards, top extensions & folders. |
| **💿 Programmes** | Installed programs from 4 registry views (HKLM × 2, HKCU × 2), with real size recomputed from disk. |
| **🔍 Rémanents** | Leftovers from uninstalled software: AppData orphans (fuzzy match), empty Program Files, stale Temp, orphan MSI patches. |
| **🧹 Nettoyage** | System artifacts (hiberfil, pagefile, Windows.old, Recycle, CrashDumps, Prefetch, WU cache, Delivery Optimization), dev caches (node_modules, .nuget, .gradle, .cargo, venv, target, etc.), browser caches (Chrome, Edge, Firefox, Opera, Brave, Vivaldi). Plus empty folders and broken shortcuts. |
| **🌳 Arborescence** | Virtualized lazy TreeView with GB size filter, 11-category document type analysis, age heatmap, and a 2/3-window-width proportional bar per node. |
| **🗺️ Carte** | Squarified treemap (Bruls/Huijsmans/van Wijk). Three color modes: depth, age, type. Click-to-drill-down with breadcrumbs. |
| **📁 Gros fichiers** | Top N files by size (default 200, configurable 10-2000), with search and sort. |
| **🏷️ Extensions** | Per-extension ranking by bytes: extension / file count / total size / % of disk. |
| **🔁 Doublons** | Grouped by (name, size), with an optional second pass using **xxHash3** (partial 64 KB, then full on collisions) to eliminate false positives. |
| **⏳ Vieux fichiers** | Files untouched for N days, filtered by size, grouped by extension. |
| **🛡️ Quarantaine** | Managed 30-day quarantine. Restore or purge expired entries. |

---

## Deletion safety — four layers

1. **Dialog** asks every time: Quarantaine DiskScout (30 days, restorable) / Corbeille Windows / Définitif (requires Shift + second confirmation).
2. **Batch purge** lists the first 6 items + total bytes before you commit. Groups with `.git`, `*.sln`, `package.json` in their ancestry are flagged with ⚠.
3. **Log IA button** generates a structured prompt for any LLM to audit your selection before you delete. The prompt carries the tab's detection method + safety guidance so the AI has full context.
4. **Audit trail**: every deletion is logged to `%LocalAppData%\DiskScout\diskscout.log` (rolling 5 MB × 5).

---

## Tech

- **WPF / .NET 8** (net8.0-windows), single-file self-contained publish
- **P/Invoke** `FindFirstFileExW` + `FIND_FIRST_EX_LARGE_FETCH` + `SafeFindHandle` for filesystem scan
- **CommunityToolkit.Mvvm** for MVVM
- **Serilog** for audit logging
- **xxHash3** (System.IO.Hashing) for duplicate verification
- **QuestPDF** for report export
- **Microsoft.VisualBasic.FileIO** for Recycle Bin operations
- Manual DI in `App.xaml.cs` — no DI container
- Manifest: `requireAdministrator` + `longPathAware` + `PerMonitorV2`

Build from source:

```powershell
git clone https://github.com/tanguynoumea-collab/DiskScout.git
cd DiskScout
dotnet build DiskScout.slnx
# or for a portable single-file release:
dotnet publish src/DiskScout.App/DiskScout.App.csproj -c Release -r win-x64
```

Output: `src/DiskScout.App/bin/Release/net8.0-windows/win-x64/publish/DiskScout.exe`

---

## Roadmap

See `.planning/MARKET-AUDIT.md` for the full strategic roadmap.

**Next up (Phase B):**
- MFT-direct scanner (target: 4 TB NTFS in < 60 s)
- Incremental rescan via USN journal
- Scheduled scans + email reports
- CLI (`DiskScout scan C:\ --output scan.json`)
- Excel (ClosedXML) export

**Phase C & D:**
- Near-duplicate image detection (pHash)
- Steam / Epic / GOG game scanner
- NTFS compression suggestions
- Multi-machine dashboard

---

## Status — Alpha 1.0

This is an **alpha release**. The software is functional, audit-logged, and safety-gated, but:
- Not yet code-signed → Windows SmartScreen will warn on first launch
- Scan speed is `FindFirstFileEx`-based (MFT scanner pending, Phase B)
- Test coverage is partial (domain models + FuzzyMatcher + scanner basics)

Issues, feedback and PRs welcome.

---

## License

MIT — see [LICENSE](LICENSE).
