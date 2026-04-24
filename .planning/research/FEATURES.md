# Feature Research

**Domain:** Windows desktop disk usage analyzer (WinDirStat / TreeSize / WizTree category) with registry-driven installed-program inventory and remnant/orphan detection
**Researched:** 2026-04-24
**Confidence:** HIGH (ecosystem well-documented; 2026 comparisons widely published)

## Executive Summary

The Windows disk-analyzer market in 2026 is mature and crowded. The incumbents (WinDirStat, TreeSize Free, WizTree, SpaceSniffer, RidNacs, JDiskReport) all converge on the same base: **scan a volume → show a size-sorted tree + treemap → let the user delete**. They compete on (1) raw scan speed — WizTree's MFT reader dominates at ~46x WinDirStat speed — and (2) UI polish / reporting depth (TreeSize Pro is the analytics leader).

**The gap DiskScout exploits:** none of the incumbents natively cross-reference the uninstall registry with the filesystem. TreeSize, WizTree, WinDirStat all show you "Folder X is 4 GB" but not "Folder X is an orphan from software you uninstalled last year." Revo Uninstaller does orphan detection but only in the context of an active uninstall, not as a standalone inventory view. That three-pane coupling — **installed programs ↔ remnants ↔ tree** — is DiskScout's unique value.

**Strategic implication for MVP:** do not try to out-scan WizTree (MFT-direct scanning is out of scope for a portable read-only .NET 8 tool) or out-analyze TreeSize Pro (enterprise reporting is not the target). Ship the differentiators (registry inventory + fuzzy orphan matching + scan delta) with table-stakes quality on the rest.

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist in any 2026 disk analyzer. Missing these = product feels amateur and users leave.

#### Pane: Scan

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Select volume(s) to scan from list of fixed drives | Every analyzer does this | S | Enumerate `DriveInfo.GetDrives()` where `DriveType.Fixed && IsReady`. Already in PROJECT.md. |
| Live progress during scan (files/sec, bytes scanned, current path) | WinDirStat, TreeSize, WizTree all show this. Users need feedback during multi-minute scans | M | `IProgress<ScanProgress>` with throttled UI updates (~10 Hz). Already in PROJECT.md. |
| Cancel scan cleanly mid-flight | Non-negotiable — a scan that can't be aborted is hostile | M | `CancellationToken` through P/Invoke enumerator + handle release. Already in PROJECT.md. |
| Graceful handling of access-denied / long-path / I/O errors (skip, log, continue) | WinDirStat notoriously reports "Unknown" for locked files; users tolerate it if the scan completes | M | Already in PROJECT.md constraints. Must never surface a modal dialog per error. |
| Portable single-file `.exe`, no installer | SpaceSniffer, RidNacs, JDiskReport all portable. USB-key use case is standard | S | Already in PROJECT.md. `PublishSingleFile=true`. |
| Request admin elevation on launch | Without admin, half of `Program Files` / other users' `AppData` is invisible, scan reports are meaningless | S | Already in PROJECT.md manifest. |
| Sub-3-minute scan of a 500 GB SSD | WizTree sets the bar at ~1 min, WinDirStat ~3–5 min post-2024 upgrade. Users have zero patience for 10+ min scans | L | P/Invoke `FindFirstFileEx` + `LARGE_FETCH` + `Parallel.ForEach` at root. Already in PROJECT.md perf constraints. |

#### Pane: Tree (size-sorted hierarchy)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Hierarchical tree view, sorted by size descending | Defining feature of the category since WinDirStat (2003) | M | WPF `TreeView` with `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`. |
| Percentage bar next to each node (RidNacs-style) | Users scan horizontally; bars are faster than reading numbers | S | Simple column with `Rectangle` width-bound to `SizeRatio`. |
| Human-readable size formatting (KB / MB / GB / TB) | Raw bytes are unreadable | S | Extension method on `long`. |
| Expand/collapse with lazy child loading | A single tree with 2M nodes expanded = frozen UI | M | Tree data already fully scanned; "lazy" here means virtualized UI rendering, not re-scanning. |
| File count per folder (recursive) | Helps distinguish "one huge video" from "1M small files" | S | Aggregate during bottom-up rollup. |
| Double-click to open folder in Explorer | Round-trip to action is mandatory for a read-only tool | S | `Process.Start("explorer.exe", path)`. |
| Right-click context menu (Open, Copy path, Open in Terminal) | Standard Windows idiom | S | Minimal menu; DO NOT add "Delete" (see anti-features). |

#### Pane: Installed Programs

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Read `Uninstall` keys from `HKLM\SOFTWARE`, `HKLM\SOFTWARE\WOW6432Node`, `HKCU\SOFTWARE` | Standard Windows inventory pattern (matches `appwiz.cpl`) | M | Enumerate all three hives, dedupe by `UninstallString` or `DisplayName`+`DisplayVersion`. |
| Show DisplayName, Publisher, Version, InstallDate | Same columns as "Apps & Features" | S | All are registry values. |
| Real on-disk size (recomputed, not registry `EstimatedSize`) | Registry `EstimatedSize` is often stale or missing. Users distrust it | M | Re-scan `InstallLocation` directory from persisted scan tree. Already in PROJECT.md. |
| Sort by size descending | The whole point of the pane | S | `ICollectionView` sort. |
| Click a program → jump to its folder in the Tree pane | Cross-pane navigation is a baseline UX expectation once multi-pane exists | S | Shared selection service. |

#### Pane: Orphans / Remnants

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| List suspicious orphan candidates with reason ("unmatched AppData", "empty Program Files", "old Temp") | Users of Revo/IObit expect this view | M | Classifier runs after scan + registry read. Already in PROJECT.md. |
| Group/filter by reason category | Without this, the list is undifferentiated noise | S | `CollectionView` with grouping. |
| Click orphan → jump to its folder in the Tree pane | See Installed Programs | S | Shared selection service. |
| Confidence or size threshold (don't flag 4 KB folders) | Avoid alarm fatigue; users ignore noisy tools | S | Skip folders under a minimum size (e.g., 1 MB) by default. |

#### Cross-Cutting

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Export scan results (CSV, HTML) | TreeSize, DiskSavvy, FolderSizes, Disk Analyzer Pro — universal feature | M | Already in PROJECT.md. Three exports: Programs / Orphans / Tree. |
| Persist scan to disk (save/load) | Users want to reopen without rescanning | M | Already in PROJECT.md: versioned JSON under `%LocalAppData%\DiskScout\scans\`. |
| Remember last window size / split positions | Every desktop app does this | S | `Settings` file or `ApplicationSettingsBase`. |
| Keyboard shortcuts (F5 rescan, Ctrl+E export, Esc cancel) | Power users expect them | S | WPF `InputBindings`. |

### Differentiators (Competitive Advantage)

Features that set DiskScout apart. These are the reasons a user would choose DiskScout over TreeSize Free or WizTree.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Registry-driven installed-program inventory with real InstallLocation size** | TreeSize / WizTree / WinDirStat show folders; they don't tell you *which installed program owns this folder*. Users have to alt-tab to "Apps & Features" and eyeball. DiskScout does the join. | M | Core differentiator. Requires registry read + path re-aggregation from scan tree. |
| **Fuzzy Levenshtein matching (threshold 0.7) between AppData folder names and installed-program DisplayName/Publisher** | AppData folder names diverge from registry DisplayName (casing, spaces, abbreviations, Publisher-vs-Product naming). Pure string-equal match misses 60%+. Fuzzy is the only way to get usable accuracy. | L | Defined in PROJECT.md & CLAUDE.md. Needs a tunable threshold and a "Why was this flagged?" explainer column so users trust the heuristic. |
| **Orphan classification by reason** (unmatched-AppData, empty-ProgramFiles, stale-Temp >30d, orphan-MSI-patch) | No mainstream competitor categorizes by *type* of remnant. Revo only does leftover detection post-uninstall. | M | Separate heuristics per category; already in PROJECT.md. |
| **Delta comparison between two scans** (appeared / disappeared / grew / shrank) | TreeSize Pro and FolderSizes have this (paid); no free tool offers it. Extremely useful for "what did that big update install?" or "what's been quietly growing?" | L | Requires persisted scans (already in PROJECT.md). Hash folder-tree nodes by path, diff by size. |
| **Read-only guarantee as product rule** (zero `File.Delete`/`Directory.Delete` in codebase) | Competitors (TreeSize, WizTree, SpaceSniffer) all offer deletion — which creates risk. Users who've once hit "delete the wrong folder" specifically seek read-only tools. Marketing angle: "DiskScout never touches your files." | S | Free from a code-volume perspective; the *cost* is resisting feature-creep pressure. Enforce via CI grep for `File.Delete`. |
| **Three-pane coupled navigation** (click in any pane → selection propagates to the other two) | WinDirStat's treemap↔list coupling is its most-loved feature. Extending this to 3 panes (Programs ↔ Orphans ↔ Tree) multiplies the insight. | M | Shared selection service / `ISelectionBroker` in MVVM. |
| **Zero network, zero telemetry, zero cloud** (and say so prominently) | 2026 users are sensitized to telemetry. WhatSize-style opt-out-buried-in-preferences is a trust-killer. A tool that literally cannot phone home is a differentiator in the privacy-aware segment. | S | Free; just don't add any HTTP client. Worth documenting in README. |
| **Admin-required with clear reason shown at startup** | Users distrust tools that silently ask for UAC. A "Why admin?" one-liner before elevation builds trust. | S | Manifest elevation is already in PROJECT.md; add an info dialog / about-box note. |

### Anti-Features (Commonly Requested, Often Problematic)

Features the ecosystem normalizes that DiskScout should deliberately NOT build. Document these now so they resist scope-creep during roadmap definition.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **File / folder deletion from inside the tool** | TreeSize, SpaceSniffer, WizTree all do it. Users will ask. | Breaks the core product rule (read-only absolute). A single destructive bug costs user data; trust never recovers. Eliminates an entire category of support burden. | Right-click → "Open in Explorer" or "Copy path" — user deletes elsewhere, consciously. Already in PROJECT.md out-of-scope. |
| **Registry cleaning / modification** | Ccleaner-style "fix your registry" users expect it from any cleanup-adjacent tool. | Destructive; requires admin-level trust; legally risky (Microsoft has deprecated the concept); identical trust problem as file deletion. | Report only: "These registry entries reference paths that no longer exist" — user decides. Even that is **out of scope for v1**. |
| **Duplicate file detection** | WizTree has it. Users conflate "find big stuff" with "find duplicates." | Requires hashing every file (orders of magnitude more I/O and CPU than size aggregation). Bloats scope from "show sizes" to "dedupe tool." Separate product category. | Don't build it. Point users to dupeGuru if they ask. |
| **Cloud backup / sync of scans** | Every SaaS-era user asks "can I sync this across machines?" | Adds server infra, auth, privacy surface, telemetry concerns. Kills the "100% local, zero network" differentiator instantly. | Scan JSON is portable; user can copy it to OneDrive themselves if they want. |
| **Real-time / background monitoring (filesystem watcher, tray icon)** | Some tools market "always-on disk monitoring." | Requires a background service, elevated token persistence, autostart entry — exactly the "bloat" users complain about in telemetry threads. Scope creep from tool → daemon. | Manual rescan. The whole point of "pose, lance, zappe" is no resident footprint. |
| **Multi-user / team / server scanning** | TreeSize Pro does it; users of shared NAS ask for it. | Requires credential management, network share enumeration, permission mapping. Completely different product. Explicitly out-of-scope in PROJECT.md. | "DiskScout is personal-use, single-machine. Use TreeSize Pro for server scanning." |
| **Antivirus / malware / PUP detection** | "While you're scanning my disk anyway, can you also..." | Entirely different threat model, different expertise, different legal exposure (liability of false negatives). Stay in lane. | Don't build. Users have Defender / Malwarebytes. Already out-of-scope in PROJECT.md. |
| **Auto-update / self-updater** | Users expect modern apps to update themselves. | Requires signing infra, update server (network dependency, kills "zero network"), code-signing cost. For a portable single-file tool, "download new .exe" is adequate. | Show version number; user downloads new build manually. |
| **MSI / setup installer** | Enterprise users sometimes prefer MSI for deployment. | Kills "pose, lance, zappe" portable-exe positioning. Adds packaging complexity. | Ship portable `.exe`. Explicitly out-of-scope in PROJECT.md. |
| **Treemap (colored-rectangle) visualization** | WinDirStat's signature feature; users conditioned to expect it. | *Gray area.* Not inherently bad, but: (a) genuinely non-trivial to build well (squarified treemap algorithm + high-performance rendering of 100K+ rects in WPF = real work), (b) DiskScout's three-pane coupling is the differentiator, not visualization, (c) scope drift risk for v1. | Defer to v2 (see MVP). The size-sorted tree with percentage bars covers 90% of the "where did my space go?" question. |
| **Scheduled / unattended scans with email reports** | TreeSize Pro feature. | Requires a service, SMTP config, credential storage. Enterprise-only value. | Out of scope. Personal-use = manual scan. |
| **Plugin system / extensibility** | Power users love it. | Massive API surface commitment, versioning burden, security review of third-party code. Not justified at the scale of a personal tool. | Export JSON; users script externally if they want. |

## Feature Dependencies

```
[Scan engine (P/Invoke)]
    ├──feeds──> [Tree pane]
    ├──feeds──> [Programs pane (real size)]
    ├──feeds──> [Orphans pane]
    └──feeds──> [Scan persistence]
                    ├──enables──> [Delta comparison]
                    └──enables──> [Reopen saved scan]

[Registry inventory reader]
    ├──feeds──> [Programs pane]
    └──feeds──> [Orphans pane (fuzzy matcher)]

[Fuzzy Levenshtein matcher]
    └──requires──> [Registry inventory reader] + [Scan engine (AppData enumeration)]

[Orphan classifier]
    └──requires──> [Fuzzy matcher] + [Temp-age heuristic] + [Size threshold heuristic]

[Export (CSV/HTML)]
    └──requires──> [Completed scan] + [Programs pane data] + [Orphans pane data]

[Delta comparison]
    └──requires──> [Scan persistence (JSON schema v1)] + [Path-keyed tree diff]

[Cross-pane selection (click-through)]
    └──requires──> [All three pane ViewModels] + [Shared selection service]

[Cancel scan]
    └──requires──> [CancellationToken threaded through P/Invoke] + [Handle release]
```

### Dependency Notes

- **Delta comparison requires scan persistence** — the JSON schema (`schemaVersion: 1`) must be stable before delta is built. Schema migration logic is deferred to v2.
- **Orphan classifier requires both scan and registry** — orphan detection cannot start until both data sources are fully loaded. UI must handle partial states ("scanning... orphans will appear when done").
- **Fuzzy matcher requires registry first** — the set of known DisplayName/Publisher strings must be built before AppData folder names can be matched against it. Order matters in the pipeline.
- **Export requires completed scan** — disable Export menu items while scan is in progress. Already implied by MVVM `CanExecute`.
- **Cross-pane selection depends on all three pane ViewModels existing** — selection-sync wiring is the last integration step, not a pane feature. Build panes independently first.
- **Real-size-per-program requires the Tree to be scanned first** — the Programs pane cannot show real size until `InstallLocation` paths can be looked up in the scan tree. Implies the scan tree is indexed by path (dictionary), not just hierarchical.

## MVP Definition

### Launch With (v1)

Minimum viable product — what's needed to validate the "registry + remnants + tree" thesis.

- [ ] **Scan engine with P/Invoke FindFirstFileEx + parallel roots** — without sub-3-min scans on a 500 GB SSD, nothing else matters. Core perf gate.
- [ ] **Volume selection + live progress + clean cancel** — table stakes; without these the tool feels broken.
- [ ] **Size-sorted tree pane with percentage bars + lazy/virtualized rendering + "Open in Explorer"** — the baseline "where did my space go" answer.
- [ ] **Installed Programs pane** (registry read from all three hives + real InstallLocation size + sort by size) — differentiator #1.
- [ ] **Orphans pane** (unmatched AppData via fuzzy Levenshtein, empty Program Files, Temp >30d, orphan MSI patches, each with "reason" column) — differentiator #2.
- [ ] **Cross-pane selection** (click in one pane → others highlight the related path) — unlocks the three-pane value prop.
- [ ] **JSON scan persistence (schema v1)** — required for delta + reopen.
- [ ] **Export CSV + HTML for each of the three panes** — table stakes reporting.
- [ ] **Portable single-file `.exe` with admin manifest** — the deployment story.
- [ ] **Robust error handling** (access-denied / long-path / I/O logged and skipped, scan never aborts on one bad file) — production-quality gate.

### Add After Validation (v1.x)

Features to add once the core three-pane thesis is validated on real users.

- [ ] **Delta comparison between two persisted scans** (appeared / disappeared / grew / shrank) — trigger: users ask "what changed since last time?". Probably within a few months.
- [ ] **Reopen saved scan from file** — trigger: users want to share scans with someone (e.g., for troubleshooting a family member's PC).
- [ ] **"Why was this flagged?" explainer for orphans** (show which DisplayName the folder almost-matched, the Levenshtein score, and why it fell below threshold) — trigger: users distrust the fuzzy matcher until they can see its reasoning.
- [ ] **Keyboard shortcuts (F5, Ctrl+E, Esc)** — trigger: power-user complaints.
- [ ] **Configurable thresholds** (fuzzy threshold, Temp age, empty-folder size) exposed in a Settings panel — trigger: false positives or negatives from the defaults.
- [ ] **Remember window size / split positions** — trigger: any user feedback about resizing annoyance.

### Future Consideration (v2+)

Features to defer until product-market fit is established.

- [ ] **Treemap visualization** (fourth pane or alternate view) — trigger: explicit user demand *and* v1 three-pane proven. Real engineering cost; don't speculate.
- [ ] **JSON schema migration** (`schemaVersion: 2`) — trigger: first need to change the persisted shape. Don't build it until it's needed.
- [ ] **File type / extension breakdown pane** (WinDirStat-style) — trigger: multiple users asking "what's the total size of all my `.mp4`s?"
- [ ] **Multi-scan time-series ("show me the last 6 scans of this folder, sized over time")** — trigger: delta-comparison users asking for trend charts. Niche.
- [ ] **Command-line mode** (`diskscout.exe scan C:\ --export scan.json`) — trigger: users wanting scripted / scheduled scans. Note: this starts to erode the "personal GUI tool" positioning; evaluate carefully.
- [ ] **Dark mode** — trigger: user request volume. Low effort, easy win, just not v1.

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| P/Invoke scan engine | HIGH | HIGH | P1 |
| Volume select + progress + cancel | HIGH | MEDIUM | P1 |
| Size-sorted tree pane (virtualized) | HIGH | MEDIUM | P1 |
| Installed Programs pane (registry + real size) | HIGH | MEDIUM | P1 |
| Orphans pane (4 heuristics + fuzzy match) | HIGH | HIGH | P1 |
| Cross-pane selection coupling | HIGH | MEDIUM | P1 |
| JSON persistence (schema v1) | MEDIUM | LOW | P1 |
| Export CSV + HTML (3 panes) | MEDIUM | MEDIUM | P1 |
| Portable single-file `.exe` | HIGH | LOW | P1 |
| Robust error handling (skip-and-log) | HIGH | MEDIUM | P1 |
| Admin manifest | HIGH | LOW | P1 |
| Delta comparison (scan diff) | HIGH | MEDIUM | P2 |
| "Why was this flagged?" explainer | MEDIUM | LOW | P2 |
| Reopen saved scan | MEDIUM | LOW | P2 |
| Configurable thresholds UI | MEDIUM | LOW | P2 |
| Keyboard shortcuts | MEDIUM | LOW | P2 |
| Window/split state persistence | LOW | LOW | P2 |
| Treemap visualization | MEDIUM | HIGH | P3 |
| File-extension breakdown pane | LOW | MEDIUM | P3 |
| Command-line mode | LOW | MEDIUM | P3 |
| Dark mode | LOW | LOW | P3 |
| Duplicate detection | — | — | **REJECTED (anti-feature)** |
| In-app deletion | — | — | **REJECTED (anti-feature, product rule)** |
| Registry cleaning | — | — | **REJECTED (anti-feature)** |
| Cloud sync / telemetry | — | — | **REJECTED (anti-feature)** |
| Background monitoring / tray | — | — | **REJECTED (anti-feature)** |
| Multi-user / server scanning | — | — | **REJECTED (out of scope)** |
| MSI installer | — | — | **REJECTED (out of scope)** |
| Auto-updater | — | — | **REJECTED (kills zero-network)** |

**Priority key:**
- P1: Must have for launch (v1 MVP). All twelve items above form the "ship-or-don't-ship" set.
- P2: Should have, add when possible (v1.x, post-validation).
- P3: Nice to have, future consideration (v2+).

## Competitor Feature Analysis

| Feature | WinDirStat | TreeSize Free | WizTree | SpaceSniffer | RidNacs | Revo Uninstaller | DiskScout (planned) |
|---------|------------|---------------|---------|--------------|---------|------------------|---------------------|
| Size-sorted tree view | Yes | Yes | Yes | No (treemap only) | Yes | No | **Yes (primary pane)** |
| Treemap visualization | Yes (signature) | Yes | Yes | Yes (signature) | No | No | **No (v2 deferred)** |
| Percentage bar column | No | Yes | Yes | No | Yes (signature) | No | **Yes** |
| Scan speed (500 GB SSD) | ~3–5 min (post-2024) | ~3 min | ~1 min (MFT direct) | ~2 min | ~2 min | N/A | **Target <3 min (FindFirstFileEx)** |
| Portable `.exe` | Yes | No (installer) | Yes | Yes | Yes | Partial | **Yes** |
| In-app deletion | Yes | Yes | Yes | Yes | No | Yes | **No (product rule)** |
| Installed-programs inventory | No | No | No | No | No | Yes (own primary view) | **Yes (primary pane)** |
| Real size per installed program | No | No | No | No | No | Partial (per app) | **Yes (distinguishing feature)** |
| Orphan / remnant detection | No | No | No | No | No | Yes (post-uninstall only) | **Yes (standalone pane)** |
| Fuzzy AppData-to-program matching | No | No | No | No | No | Proprietary/undisclosed | **Yes (Levenshtein 0.7)** |
| Scan delta / comparison | No | Pro only ($) | No | No | No | No | **Yes (v1.x)** |
| JSON scan persistence | No | Yes (own format) | Yes (own format) | No | No | No | **Yes (versioned)** |
| CSV / HTML export | No (basic txt) | Yes | Yes (CSV) | Limited (text) | Yes (HTML) | No | **Yes (CSV + HTML, 3 panes)** |
| Duplicate detection | No | Pro only ($) | Yes | No | No | No | **No (anti-feature)** |
| Cloud / telemetry | No | Opt-in analytics | No | No | No | Yes (update checks) | **No (zero network)** |
| License | Free / OSS | Free + Paid Pro | Free personal / Paid commercial | Free | Free personal | Free + Paid Pro | **Free (personal use, implied)** |

**Reading the matrix:** DiskScout's unique column-combination — installed-program inventory + real-size-recomputed + orphan detection + fuzzy matching + scan delta + zero-network + read-only — does not appear in any single competitor. The closest partial overlaps are: (a) Revo for leftover detection (but only during uninstall flow, not as an inventory), and (b) TreeSize Pro for scan delta (paid). This validates the thesis that DiskScout occupies real whitespace.

## Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Competitor feature inventory | HIGH | Multiple 2026 comparison articles (AlternativeTo, Tenorshare, XDA, WindowsForum) corroborate; product features verified against vendor sites (diskanalyzer.com, jam-software.com, windirstat.net). |
| Scan-speed benchmarks | MEDIUM | "46x faster" (WizTree vs WinDirStat) is vendor marketing; the qualitative ordering (WizTree > TreeSize ≈ RidNacs > WinDirStat) is consistent across reviewers. Exact seconds vary by hardware. |
| Anti-feature reasoning | HIGH | Privacy/telemetry concerns in 2026 are broadly documented; the read-only positioning is a direct logical consequence of the PROJECT.md product rule. |
| Fuzzy-match threshold (0.7) | MEDIUM | Value defined in PROJECT.md / CLAUDE.md, not empirically validated against a labeled dataset. Expect tuning in v1.x based on false-positive feedback. |
| Orphan-heuristic coverage | MEDIUM | The four chosen heuristics (AppData fuzzy, empty Program Files, Temp >30d, orphan MSI patches) are reasonable but not exhaustive. Other candidates (orphan Start Menu shortcuts, orphan shell extension registrations) exist but are deferred. |
| MVP scope sizing | HIGH | The P1 set is consistent with the "Active" list in PROJECT.md; complexity estimates derived from standard WPF/.NET 8 effort norms. |

## Sources

- [AlternativeTo — Best TreeSize Alternatives: Top Disk Usage Analyzers in 2026](https://alternativeto.net/software/treesize/)
- [Tenorshare — The 9 Best Windows/Linux/Mac Disk Space Analyzers for 2026](https://4ddig.tenorshare.com/remove-duplicates/best-disk-space-analyzer.html)
- [WindowsForum — Top 10 WinDirStat Alternatives for Faster Disk Cleanup and Visualization](https://windowsforum.com/threads/top-10-windirstat-alternatives-for-faster-disk-cleanup-and-visualization.384876/)
- [DigitalGeekery — WinDirStat vs TreeSize](https://digitalgeekery.com/windirstat-vs-treesize-which-is-the-better-disk-space-tool/)
- [XDA — Stop using WinDirStat, and switch to this free tool instead](https://www.xda-developers.com/stop-using-windirstat-and-switch-to-this-free-tool-instead/)
- [WizTree — Official site and guides](https://diskanalyzer.com/)
- [WizTree — What's new (v4.30, March 2026)](https://diskanalyzer.com/whats-new)
- [WinDirStat — Official site](https://windirstat.net/)
- [WinDirStat — GitHub repo](https://github.com/windirstat/windirstat)
- [WinDirStat — Wikipedia](https://en.wikipedia.org/wiki/WinDirStat)
- [TreeSize — Official site and what's new](https://www.jam-software.com/treesize)
- [TreeSize — Changes log (Feb 2026 perf improvements)](https://www.jam-software.com/treesize/changes.shtml)
- [SpaceSniffer — Portable Freeware listing](https://www.portablefreeware.com/index.php?id=2360)
- [SpaceSniffer — Download / features](https://space-sniffer.com/)
- [RidNacs — SnapFiles / Portable Freeware coverage](https://www.snapfiles.com/software/system/diskspace.html)
- [Revo Uninstaller — Pro FAQ (leftover scanning algorithms)](https://www.revouninstaller.com/online-manual/frequently-asked-questions/)
- [Technibble — Utility to list orphaned Program Files folders (thread)](https://www.technibble.com/forums/threads/utility-to-list-remove-orphaned-program-files-folders.36422/)
- [Microsoft Support — Removing Invalid Entries in Add/Remove Programs (registry Uninstall keys)](https://support.microsoft.com/en-us/topic/removing-invalid-entries-in-the-add-remove-programs-tool-0dae27c1-0b06-2559-311b-635cd532a6d5)
- [MiniTool — Remove Remnants of Uninstalled Software](https://www.minitool.com/news/remove-remnants-of-uninstalled-software.html)
- [SoftwareKeep — How To Remove Software Leftovers on Windows 10/11](https://softwarekeep.com/blogs/how-to/how-to-remove-software-leftovers-on-windows)
- [Hacker News — Show HN: Delta disk analyzer (scan comparison category)](https://news.ycombinator.com/item?id=47207754)
- [FolderSizes — Trend Analyzer (scan delta feature precedent)](https://www.foldersizes.com/screens/trend-analyzer)
- [DiskBoss — Disk space analyzer exports (CSV/HTML/PDF)](https://diskboss.com/disk_space_analyzer.html)
- [DiskSavvy — Report management options](https://www.disksavvy.com/disksavvy_report_management.html)
- [Syncfusion — 3 Steps to Lazy Load Data in WPF TreeView in MVVM Pattern](https://www.syncfusion.com/blogs/post/3-steps-to-lazy-load-data-in-wpf-treeview-mvvm)
- [Microsoft Learn — Improve the Performance of a TreeView (WPF virtualization)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-improve-the-performance-of-a-treeview)

---
*Feature research for: Windows desktop disk usage analyzer with registry-driven installed-program inventory and remnant/orphan detection*
*Researched: 2026-04-24*
