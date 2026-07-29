# Delivery Outline

The roadmap is stage-gated. A stage completes through its decisions and evidence,
not merely because code exists.

Delivery proceeds in bite-sized, independently verifiable chunks. Each completed
chunk is committed regularly using a Conventional Commits description, with its
tests and relevant plan/task updates included. Unrelated roadmap work is kept in
separate commits.

## Stage 0: Framework discovery (complete)

- Defined product scope, first workflow, and success boundary.
- Accepted layered architecture, TUI-first presentation, and single-process hosting.
- Defined collaboration roles, approvals, budgets, recovery, memory, and visibility.
- Selected persistence, provider, retrieval, Git, logging, migration, and test stacks.
- Verified the development Ollama server and recorded its installed model inventory.

Exit evidence: accepted framework and architecture decision records.

## Stage 1: Walking skeleton (complete)

- Completed: scaffolded the runtime layers, composition root, central dependency
  management, architecture tests, and compile-time boundary analyzer.
- Completed: XDG paths, Secret Service access, SQLite/DbUp initialization, redacted
  Serilog output, optional OpenTelemetry, and graceful cancellation.
- Completed: adaptive Terminal.Gui shell using fake Business Logic contracts.
- Completed: Ollama discovery, streaming chat, embeddings, usage, cancellation, and
  provider failure mapping behind Data Access records, with live LAN verification.
- Completed: durable local conversation path from the TUI through Business Logic to
  Ollama, including schema-versioned history, incremental snapshots, token usage,
  reload, scrollable transcript, and persisted provider failures.
- Completed: provider health, typed chat/embedding capability discovery, and
  persisted model selection are available in the TUI. Typed XML supplies named
  provider modules and all main/reviewer/tool routes provide local defaults. Each
  goal can override lead, implementer, and reviewer independently; remote execution
  is goal-bound, strictly private, output-capped, and cost-accounted.
- Completed: Git-backed workspace inspection, durable registration, active
  selection, entry-point validation, and explicit trust exist behind Business Logic
  contracts. A compact/wide TUI modal exposes registration and selection with a
  separate trust confirmation, and the dashboard consumes the active workspace;
  a top-level menu preserves access in narrow layouts.
- Completed: typed framework rules resolve precedence, provenance, locks,
  validation failures, and same-level conflicts. Bounded XDG `framework.md` and
  repository `AGENTS.md` loading preserve provenance and privacy, while private
  overlays persist in SQLite. Business Logic composes these with resolved rules and
  source failures, and named XML rules supply typed precedence and locks. A
  Avalonia and TUI framework surfaces expose the effective view and private-overlay
  editor.
- Completed: typed repository inspection includes trusted-workspace identity
  checks, path-confined bounded UTF-8 file reads, and bounded search over Git-indexed
  text. Git inspection supplies bounded status and diff evidence with branch and
  HEAD identity. Non-evaluating .NET inspection parses solution, project, reference,
  target-framework, language, and SDK-policy metadata into bounded records.
- Completed: schema-versioned draft goals persist against the active workspace
  with validated review-cycle and optional remote-cost caps. Versioned plan
  proposals and atomic approval/denial transitions are exposed through Avalonia and
  the TUI;
  approval also persists a worktree-bound capability grant. Goal inspection shows
  local-only authorization or the cap, reservations, reconciled spend, remainder,
  overage, and per-provider/model request attribution.
- Completed: a structured, cancellable Git adapter creates deterministic goal
  branches/worktrees under XDG state, records the base commit, retries idempotently,
  and preserves dirty original-worktree state. Approval provisions isolation first,
  then persists the decision and active worktree grant atomically; failed provisioning
  leaves the plan pending. Interrupted-run reconciliation remains part of Task 022.
- Completed: approved goals can perform correlated, path-confined compare-and-swap
  text creation/replacement and typed build/test execution only inside their
  persisted worktree grant. Builds and tests are cancellable, bound their output,
  and disable implicit restore; stale hashes, symlinks, oversized content, inactive
  workspaces, and revoked trust are rejected. Requests are persisted before tool
  execution and completed with correlated result evidence; incomplete calls remain
  visible for recovery. Restore requires a separate durable user decision bound to
  the exact goal, correlation, and registered entry point before the network-capable
  process can start.
- Completed: Microsoft Agent Framework is wrapped behind semantic Business Logic
  contracts. Lead, implementer, and reviewer agents run with distinct prompts and
  consume the configured main, tool, and reviewer model routes without exposing
  framework types to Presentation.
- Completed: a deterministic walking-skeleton workflow persists every safe boundary
  in SQLite, pauses after the lead plan, resumes implementation and review after a
  restart, and exposes full checkpoint evidence through the adaptive TUI.
- Completed: Lead planning returns a strict bounded delegation contract. Schema 17
  persists ordered tasks and reports; the coordinator executes one task per scoped
  Implementer call, reconciles completed reports after interruption, independently
  reviews the combined result, and exposes tasks and worst-case remaining call counts
  through presentation-neutral snapshots and the TUI.
- Completed: a repeatable linux-x64 publish profile produces a self-contained,
  compressed executable with external native libraries and shipped XML defaults.
  The artifact starts without an installed .NET runtime, uses isolated XDG storage,
  and shuts down cleanly on SIGTERM.
- Completed: OpenRouter discovery, streaming, embeddings, strict privacy routing, goal-scoped
  cost reservation/reconciliation, and structured cost reports are implemented.
  Explicit per-role goal selections authorize pre-approval planning without
  authorizing repository mutation; approved goals retain remote embedding support.
  Typed tool-call mapping remains behind the same boundary.
- Completed: bounded Git-tracked UTF-8 ingestion filters generated, binary, secret,
  and oversized content; deterministic overlapping chunks are embedded through the
  configured provider. SQLite vector generations are partitioned by provider, model,
  dimensions, and chunking version and switch atomically after successful rebuilds.
- Completed: presentation-neutral status and goal-context services expose compatible
  semantic partitions to every production role through a bounded typed tool. TUI
  controls inspect status without inference and explicitly rebuild or preview context
  with route, privacy, source provenance, usage, and goal-cost transparency.

Exit evidence at the time: clean build, architecture diagnostics, deterministic
tests, and a TUI shell exercising a persisted demonstration run. That demonstration
workflow was subsequently removed from production composition after the real goal
workflow replaced it.

## Stage 2: First complete repository workflow (complete)

- Register and trust Git-backed .NET workspaces and select their entry points.
- Load global, repository, and private workspace framework layers with locks.
- Index eligible tracked text through configurable Ollama/OpenRouter embeddings.
- Implement goal creation, role/provider selection, review and cost caps, and plan
  approval.
- Create isolated branches/worktrees and expose the typed inspection, edit, .NET,
  and Git tools.
- Coordinate lead, implementer, and reviewer roles with bounded delegation and
  role-specific tool scopes.
- Persist step checkpoints, expandable exchanges, artifacts, evidence, and cost.
- Commit accepted work to the goal branch after explicit approval.

Exit evidence: the deterministic release gate completes an end-to-end change in a
representative .NET repository through trusted registration, isolation, typed edit,
approved restore, build/test evidence, independent review state, exact-diff approval,
and branch commit. Restart/reconciliation tests inject interruption at durable
workflow boundaries without replaying uncertain calls.

## Stage 3: Path to productive daily use (active)

The Stage 2 scripted acceptance gate (`eng/verify-v1-release.sh` and
`eng/verify-v1-desktop-release.sh`) proves one representative, scripted repository
workflow end to end: registration/trust, plan approval, isolated edit/build/test,
independent review, and exact commit, surviving an injected interruption. That gate
passing is necessary but not sufficient. As of 2026-07-29, Harness.NET is not yet
something its author can use productively for real day-to-day .NET development.
`1.0.0` marks a verified walking-skeleton-to-full-workflow milestone, not a
productivity milestone. Stage 3 is the active work of closing that gap and is
tracked as concrete tasks in `docs/tasks/README.md`.

### Current focus: match a professional IDE baseline

Before any further unique feature, the daily editing and reviewing surfaces must
feel like a competent general-purpose IDE (the bar is JetBrains Rider/Air- or
Zed-class), not a prototype text box wired to a raw diff string. This is the
present priority, ahead of the rest of Stage 3 below.

- Reduce the form density of Framework rule management and Operations, fix source
  viewing/editing, and add a real diff viewer: the effective framework view is
  currently one long formatted text dump (rules, full guidance-document bodies,
  and issues all inline with no filtering); the Operations backup destination
  requires typing an absolute path by hand instead of a native save picker; and
  Git diff is a single raw unified-diff string with no syntax-aware decoration.
  Add both an inline decorated view (for reviewing model-made changes in place)
  and a side-by-side comparison view (for evaluating working-tree/branch git
  state). The goal-approval dialog chain is tracked separately as Task 040.
  (Task 035)
- Consolidate the goal lifecycle's dialog chain: creating and progressing one goal
  currently steps through up to 14 separate modal windows (new goal, model
  routing, remote-model authorization, output limits, plan approval, restore
  approval/request/decision, commit approval/confirmation, semantic context,
  semantic rebuild confirmation), almost all defined in one 1967-line file.
  Genuine human-authority checkpoints — plan, restore, and commit approval — stay
  distinct and undiminished; informational/config steps are consolidated into
  fewer, clearer surfaces. (Task 040)
- Validate every edit, model-authored or hand-typed, with real Roslyn/LSP
  diagnostics instead of discovering a syntax or type error only at Build time.
  (Task 042)
- Add intellisense on top of that foundation: completion, hover/quick-info, and
  go-to-definition for the loaded solution/project. (Task 043)

### Workflow friction

- Let a user move between more than one trusted workspace without re-registration
  overhead; only one workspace is active at a time today. (Task 036)

### Missing core capability

- Prove the workflow on large, real, messy repositories: dirty bases, mid-goal
  conflicts, index rebuilds under load, provider outages, budget exhaustion, and
  corrupted/interrupted state, not only the single scripted representative-repo
  gate. (Task 037)
- Add an in-app restore-from-backup flow: Operations can only create a backup
  today; recovery into a fresh install is proven solely by
  `eng/verify-v1-release.sh`, not available to a user without the script.
  (Task 041)
- Make the handoff after an approved commit explicit: the app deliberately does not
  push, open a PR, or merge, but it also does not yet tell the user what to do next
  with the accepted goal branch. (Task 039)
- Continue hands-on Avalonia usability and visual-quality regression review across
  future production workspace, goal, evidence, and recovery changes.
- Keep the accepted ADR 010 workbench matrix under regression coverage as new
  panels and documents are added. Production AT-SPI covers real repository
  registration/trust, manual goal/plan approval, isolated editable-worktree source,
  search, multi-document switching/focus, restart, and corrupt-layout fallback; its
  isolated Orca 50.2 mode generates contextual speech without framework
  implementation type-name announcements. A deterministic-loopback production
  workflow also proves Lead planning, typed edit/build/test, independent review,
  process restart, and exact branch commit through the real UI.
- Add other platforms or gRPC only through existing Business Logic contracts when a
  concrete workflow justifies them.

Deferred within Stage 3: opt-in Ollama behavioral evaluation and regression
datasets for planning, tool-selection, and review quality (Task 038) are parked
below every item above until 035, 036, 037, and 039-043 close.

Stage 3 exits when a user can run real, non-scripted development work through the
app, across multiple sessions and repositories, without the friction or gaps above
— not when another scripted gate passes.

## Deferred until justified

- Distributed workers or message brokers
- Multi-user accounts, tenancy, or shared authorization
- Web-based presentation
- Plugin marketplace
- Unrestricted agent shells
- Automatic merging, rebasing, or pull-request creation
- Fully autonomous background operation
