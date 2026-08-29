# Refactor baseline and delegation plan

This document is the groundwork for Task 060. It records the measured state of the
repository on 2026-08-24, the decomposition targets, and the ordered slices an
implementer executes. The binding constraints live in
[ADR 025](decisions/025-workbench-composition-and-refactor-guardrails.md); the
acceptance criteria live in [the task ledger](tasks/README.md#060--workbench-composition-refactor).
This plan changes structure and developer experience, not behavior.

Implementation status: PR #2 merged slice 060.0 and activated the shrink-only
source-size test shared with Task 061. Slice 060.1 is implemented: `FilesTool`,
`SearchTool`, and `WorkbenchToolContext` now own the Files panel behavior and focused
tests. Slices 060.2 and 060.3 are also implemented: `GitChangesTool` owns exact
staging, patch units, destructive previews, and developer commit entry;
`GitBranchesTool` and `GitWorktreesTool` own branches, tags, linked worktrees, and
stashes. Slice 060.4 is also implemented: `GitRemotesTool`, `GitHistoryTool`, and
`GitConflictsTool` own synchronization, history/blame, exact conflict editing, and
the conflict editor's Roslyn session lifecycle. Slice 060.5 is in progress:
`RunOutputTool`, `ProblemsTool`, and bounded document-intelligence and interaction
collaborators, exact semantic rename, deterministic transformations, and semantic
navigation are extracted while the remaining `DocumentsHost` work continues.
Slices 060.6–060.8 remain and continue to follow the sequencing and evidence rules below.

## Measured baseline (2026-08-24)

Commit `16f3085`, Linux x64, SDK `10.0.400` (forced past the stale `global.json`
pin).

### Build and tests

| Gate | Result |
|---|---|
| Build | 15/15 projects, zero warnings under `TreatWarningsAsErrors`. |
| Tests | 813 executed, 761 passed, 52 failed. |
| Failures | All 52 in `RoslynCodeIntelligenceEngineTests` (50) and `RoslynWorkspaceCompatibilityTests` (2). Root cause: `global.json` pins `10.0.201` / `rollForward: latestPatch`; no `10.0.2xx` SDK is installed, so `MSBuildWorkspace` loads degrade. Environmental, not a code regression. |

### Source shape

| Project | Files | Lines |
|---|---|---|
| Harness.BusinessLogic | 332 | 23,159 |
| Harness.DataAccess | 317 | 28,297 |
| Harness.Presentation.Avalonia | 44 | 24,350 |
| Harness.Presentation.Terminal | 14 | 3,038 |
| Harness.UI.Avalonia | 13 | 505 |
| Harness.Host | 8 | 907 |
| Harness.Analyzers | 1 | 137 |

Business Logic and Data Access average ~70 and ~89 lines per file — healthy,
contract-shaped code. Presentation.Avalonia averages ~554 and concentrates 56% of
its lines in four files.

### Hot spots

| File | Lines | Evidence |
|---|---|---|
| `src/Harness.Presentation.Avalonia/WorkbenchDockHost.cs` | 6,389 | 85 fields, ~228 members, 19 nested types; touched in 30 of the last 60 commits. Owns every dock tool: file tree, search, Git staging, branches, tags, worktrees, stashes, remotes, history, conflicts, run output, problems, document sessions, diagnostics views, and layout wiring. |
| `src/Harness.Presentation.Avalonia/AvaloniaPresentationStore.cs` | 2,606 | 20+ injected services; all shell commands and state reduction in one class. |
| `src/Harness.Presentation.Avalonia/SettingsWindow.cs` | 2,335 | Every settings section inline. Touched in 20 of the last 60 commits. |
| `src/Harness.Presentation.Avalonia/GoalDialog.cs` | 2,275 | Goal planning, approval, evidence, and recovery UI inline. |
| `src/Harness.Presentation.Avalonia/MainWindow.cs` | 1,599 | Shell chrome plus command palette, keybinding, and navigation wiring. |
| `tests/Harness.Presentation.Avalonia.Tests/PresentationControlTests.cs` | 5,523 | One test class covering all workbench tools. |

Reference points for the 800-line budget already in the codebase:
`SourceEditorSurface` (614), `ConversationWorkflowCard` (627), `VimEditorController`
(703), `WorkbenchDockLayoutCodec` (759).

Data Access adapters `LibGitDeveloperGitRepository` (1,962) and
`RoslynCodeIntelligenceEngine` (1,896) are large but cohesive single-technology
adapters behind tested contracts; they are explicitly out of scope.

### Developer-experience gaps

1. `global.json` fails on any machine without a `10.0.2xx` SDK (verified: plain
   `dotnet build` errors before compiling).
2. No continuous verification exists (`.github/` absent); AGENTS.md mandates
   branch-and-PR flow but nothing runs the suite on a PR.
3. `.codex/config.toml` holds a live MCP bearer token and is not ignored;
   `git check-ignore` does not match it.
4. `.agents/` is empty and dead since 2026-08-10.
5. 719 `[Fact]` vs 28 `[Theory]`: near-duplicate facts exist where parameterized
   cases would be clearer (secondary; address opportunistically within touched
   files only).
6. The SDK pin is duplicated into tests: `RoslynWorkspaceCompatibilityTests` and
   the ledger's engine tests assert the literal `10.0.201` and write it into
   temporary `global.json` fixtures. Changing the pin without these breaks the
   suite; slice 060.0 must derive the expected version from the repository
   `global.json` or a single shared constant.
7. Observed once during the 2026-08-24 baseline run against the stale pin: the
   repository's own `global.json` was deleted from the working tree by the failing
   suite. No production or test code visibly deletes it; slice 060.0 must
   reproduce under the failing-SDK condition and fix the leak before relying on
   the suite for refactor verification. Until then, check `git status` after
   local runs.

## Goals

- **Architecture:** presentation composition matches the granularity of the rest of
  the codebase, so Stage 3 tools land as units, not accretion.
- **DX:** any supported SDK builds the repo; every PR is verified; secrets cannot
  drift into history; test classes map one-to-one to units.
- **UX:** uniform tool-panel chrome and empty/busy/error presentation across tools;
  every tool action reachable through the command palette and keybindings catalog;
  accessibility identifiers preserved and covered.

## Non-goals

New features, layer changes, contract changes, UI-toolkit changes, Terminal.Gui
rework, Data Access adapter splits, and anything listed under ADR 025 scope limits.

## Target structure

```text
src/Harness.Presentation.Avalonia/
  Workbench/
    WorkbenchDockHost.cs          # dock arrangement, layout, cross-tool navigation only
    WorkbenchToolContext.cs       # shared services + state accessor handed to units
    FilesTool.cs                  # file tree + filter + status
    SearchTool.cs                 # workspace text search
    GitChangesTool.cs             # staging, patch units, discard/clean/commit entry
    GitBranchesTool.cs            # branches + tags
    GitWorktreesTool.cs           # worktrees + stashes
    GitRemotesTool.cs             # remotes, fetch, integration, push
    GitHistoryTool.cs             # paged history, blame entry
    GitConflictsTool.cs           # conflict list + three-way document entry
    RunOutputTool.cs              # goal + developer run output
    ProblemsTool.cs               # diagnostics list
    DocumentsHost.cs              # source/virtual document sessions, switcher
  Store/
    AvaloniaPresentationStore.cs  # state stream, gating, cancellation (thin)
    <Feature>Commands.cs          # per-feature handlers (Workspace, Goal, Settings, …)
  Settings/
    SettingsWindow.cs             # shell + section composition (thin)
    <Section>SettingsSection.cs   # one section per file
  Goals/
    GoalDialog.cs                 # shell (thin)
    <Aspect>GoalSection.cs
```

Names are indicative; the reviewer of each slice fixes final names. Each unit
receives a `WorkbenchToolContext` (services, state accessor, prompt, cancellation)
instead of the current 15-parameter constructor spread. Nested choice records
(`BranchChoice`, `StashChoice`, …) move with their tool.

Tests split the same way: `FilesToolTests`, `GitChangesToolTests`, … replacing
`PresentationControlTests` region by region; `AvaloniaPresentationStoreTests` splits
alongside the store.

## Slices

Each slice is one branch, one draft PR, one reviewable result, with the full suite
and the relevant acceptance evidence green. Order matters; do not parallelize
slices that touch `WorkbenchDockHost`.

| Slice | Scope | Exit criteria |
|---|---|---|
| 060.0 | DX baseline: `global.json` → major-pin with `latestFeature`; deduplicate the SDK version out of test assertions and fixtures (gap 6); reproduce and fix the `global.json` working-tree deletion (gap 7); ignore `.codex/` and local agent dirs; remove empty `.agents/` or document it; add hosted Linux x64 PR verification (restore, build, deterministic tests). | Plain `dotnet build` succeeds on a machine with any 10.x SDK ≥ pin; the 52 environment-dependent test failures are gone; a failing-SDK suite run leaves the working tree clean; first green PR run recorded. |
| 060.1 — implemented | Introduce `Workbench/` folder, `WorkbenchToolContext`, and the size-budget architecture test with the initial burn-down allowlist. Extract **FilesTool** and **SearchTool** as the pattern-setting units, with their tests split out. | Extracted units are ≤ 800 lines; the host budget tightened from 6,389 to 6,060 lines and the monolithic test budget from 5,523 to 5,410; layout round-trip and production AT-SPI evidence re-verified. |
| 060.2 — implemented | Extract **GitChangesTool** (staging, patch units, destructive previews). | Exact-fingerprint staging, opaque patch-unit selection, destructive preview/confirmation, unsaved-buffer blocking, and developer commit preview/confirmation re-verified; host budget tightened from 6,060 to 5,798 lines. |
| 060.3 — implemented | Extract **GitBranchesTool**, **GitWorktreesTool** (branches, tags, worktrees, stashes). | Exact reference/repository/worktree-set fingerprints, destructive previews, workspace-transition guards, and matching acceptance records re-verified; host budget tightened from 5,798 to 5,309 lines and the monolithic test budget from 5,217 to 4,901. |
| 060.4 — implemented | Extract **GitRemotesTool**, **GitHistoryTool**, **GitConflictsTool**. | Remote/history/conflict acceptance records re-verified, including exact conflict save/stage separation, unsaved-result transitions, Roslyn session reuse/cancellation/shutdown, and production AT-SPI; host budget tightened from 5,309 to 4,570 lines and the monolithic test budget from 4,901 to 4,824. |
| 060.5 — in progress | Extract **RunOutputTool**, **ProblemsTool**, **DocumentsHost** (document sessions, diagnostics views, switcher). Task 052 has no open branch ahead of `main`; its delivered CodeLens-run slice is included. Run output, Problems, Roslyn lifecycle/interactions, rename, transformations, navigation, virtual documents, inspection, and CodeLens are extracted; session hosting remains. | `WorkbenchDockHost` ≤ 800 lines and leaves the allowlist. |
| 060.6 | Store decomposition into per-feature command handlers; `AvaloniaShellState` contract unchanged. | Store tests split per feature; store leaves the allowlist. |
| 060.7 | `SettingsWindow` and `GoalDialog` section decomposition. | Both leave the allowlist; settings and chat-first workflow acceptance evidence re-verified. |
| 060.8 | UX consistency pass enabled by the units: shared tool chrome (header, busy, empty, error presentation), command-palette and keybinding coverage audit for every tool action. | Palette lists every tool action; keybinding catalog complete; Orca/AT-SPI checks pass; allowlist empty. |

Slice 060.0 is independent and should land first and fast. Slices 060.1–060.5 are
strictly sequential. 060.6 and 060.7 may proceed in parallel with each other after
060.5. 060.8 closes the task.

Task 061 (architecture enforcement and composition seams, ADR 026) is a separate
track over Analyzers, Host, and architecture tests. It shares no files with 060's
presentation slices and may run fully in parallel; only the size-budget test
infrastructure from 060.1 is shared, and whichever task lands it first, the other
reuses it.

## Delegation protocol

- One slice per feature branch (`refactor/060-<n>-<slug>`), draft PR per AGENTS.md.
- A slice PR contains no behavior change; if a bug is found mid-slice, fix it in a
  separate branch first and rebase.
- Required evidence per PR: full suite result, the acceptance scripts named in the
  slice's exit criteria, and a before/after line-count table for touched files.
- The reviewer checks the ADR 025 gates: dock/layout compatibility, automation
  identifiers, keybindings, budget test, allowlist direction.
- In-flight coordination: Task 052 is in progress; slices 060.5+ must not start
  while a 052 slice holding the same files is open.

## Risks

| Risk | Mitigation |
|---|---|
| Layout persistence breaks silently (ADR 011). | Every slice includes the save-before/restore-after layout check; codec compatibility path updated in the same slice as any identifier change. |
| AT-SPI or automation identifiers drift. | Identifiers move verbatim with their controls; `eng/verify-avalonia-atspi.py` runs in each slice. |
| Behavior change hides in event-wiring moves. | Test split precedes or accompanies each extraction; suite count must not drop. |
| Merge friction with Task 052. | Sequencing rule above; 060.0–060.4 avoid 052's files. |
| Refactor stalls half-done. | Allowlist makes remaining debt visible; task closes only at empty allowlist (ledger criteria). |
