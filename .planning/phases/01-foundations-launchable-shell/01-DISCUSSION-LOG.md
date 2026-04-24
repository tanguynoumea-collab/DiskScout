# Phase 1: Foundations & Launchable Shell - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-24
**Phase:** 01-foundations-launchable-shell
**Areas discussed:** UAC Denied Behavior, Empty-State UI, Domain Models Shape, Window Chrome & Branding

---

## Gray Area Selection

| Option | Description | Selected |
|--------|-------------|----------|
| UAC denied | What happens if the user denies the UAC prompt? | ✓ (deferred to Claude) |
| Empty-state UI | What shows in the 3 empty tabs before the first scan? | ✓ (deferred to Claude) |
| Domain models shape | `FileSystemNode` & co: record class / sealed class / struct, flat vs nested refs | ✓ (deferred to Claude) |
| Window chrome & branding | Standard vs custom chrome, `[Admin]` suffix, version display | ✓ (deferred to Claude) |

**User's choice:** "No preference" — delegated all four areas to Claude's coherent judgment.
**Notes:** Subsequent message confirmed full autonomy: "gère tous les discuss en prenant les décisions les plus cohérentes et lance en automatique les planner et le exe".

---

## UAC Denied Behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Silent exit (Windows default) | Process terminates with code 0 when user denies UAC; no custom UI | ✓ |
| Pre-elevation shell | Launch non-elevated, show dialog explaining admin requirement, Retry button | |
| Restart helper | Detect non-elevated state, spawn elevated self, exit the launcher | |

**Rationale:** Matches standard Windows admin-tool behavior (regedit, services.msc). Simplest path. No pre-elevation shell avoids the maintenance cost of a second executable entry point.

---

## Empty-State UI

| Option | Description | Selected |
|--------|-------------|----------|
| Completely blank tab | No content until scan completes | |
| Placeholder text + CTA | Centered icon + one-line copy pointing to the Scanner button | ✓ |
| Illustration + onboarding | Branded illustration + "Comment ça marche" expander | |

**Rationale:** Blank tab confuses first-time users. Full onboarding is overkill for a personal tool. Placeholder text + CTA is the middle ground; implementation via reusable `EmptyStatePanel` UserControl.

---

## Domain Models Shape

| Option | Description | Selected |
|--------|-------------|----------|
| `class` with properties | Traditional mutable POCO, setter-driven | |
| `sealed record class` + flat `ParentId` | C# 12 immutable record, flat tree representation | ✓ |
| `struct` for all models | Stack-allocated, no GC pressure per node | |
| `record class` with nested `Children` | Immutable records with direct parent→children refs | |

**Rationale:** Flat `ParentId` matches PERS-02 (JSON persistence must be flat to avoid stack-overflow on 1M+ trees — recursive serialization blows up). Records give immutability + value equality + primary constructors. `ScanProgress` kept as `readonly record struct` (hot-path, zero allocation). Nested `Children` rejected because it would force two representations (nested in memory, flat on disk) and a migration step.

---

## Window Chrome & Branding

| Option | Description | Selected |
|--------|-------------|----------|
| Standard Windows chrome + dark title bar via DWM | Default chrome, force dark mode flag, `[Admin]` suffix in title | ✓ |
| Custom frameless chrome with acrylic/mica | Bespoke title bar, draggable region, modern Win11 aesthetic | |
| Standard light chrome | System default everything | |

**Rationale:** Custom chrome adds DPI + snap-layout + accessibility headaches for little payoff. `DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE = 20` gives a dark title bar on Win10 20H1+ / Win11 without custom code. Version omitted from title (reserved for a future About dialog).

---

## Claude's Discretion

All four discussed areas were delegated to Claude. Decisions documented in CONTEXT.md with explicit rationale so downstream planner/executor can treat them as locked.

Additional planner-level choices (not part of gray-area discussion, but flagged for cohesion):
- Single `DiskScout.App` project vs split (App + Tests) — recommended: split with empty `DiskScout.Tests` scaffolded in Phase 1
- Brush palette exact hex values — Claude's discretion during implementation
- Icon sourcing — placeholder `.ico` provided; user can replace later

## Deferred Ideas

None surfaced during discussion.
