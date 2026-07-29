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

Before any further unique feature, the daily editing, Git, and agent-collaboration
loop must meet the interaction quality of a professional IDE. JetBrains Rider/Air,
Cursor, and Zed are quality references rather than feature-parity or visual-copy
targets. Harness.NET retains its own design and differentiates through deep .NET
integration and personal tailoring. This is the present priority, ahead of the rest
of Stage 3 below.

- Completed: Task 035's source/editor visual pass and wide/compact production
  acceptance. The bounded file tree, command palette/quick open, scannable Framework
  inspector, native backup picker, IDE headerbar, inline/side-by-side decorated diff,
  and theme-aware source editor now have recorded real-host evidence.
- Replace the goal dialog chain with the ADR 013 chat-first workflow. Typed inline
  cards carry plans, progress, evidence, validation, consequential decisions, and
  handoff; role/model/output defaults move to searchable Settings and goal-specific
  overrides use progressive disclosure. Durable plan, remote-spend, Restore,
  destructive, budget, and exact-commit authority remains explicit. (Task 040)
  The Settings foundation is complete: all settled categories are searchable;
  Appearance owns persisted theme selection; Models & roles owns typed, validated,
  schema-19 role/provider/model/output defaults; unavailable categories are explicit;
  and remote authority remains goal-bound. `docs/settings.md` records ownership.
- Build the ADR 012 in-process Roslyn service behind replaceable semantic contracts.
  Begin with its SDK/MSBuild/self-contained-publish compatibility checkpoint.
  Versioned live buffers then surface syntax, compiler, and configured analyzer
  diagnostics inline and in Problems. Model-authored candidate changes fail closed
  when they introduce compiler errors and retain warning/analyzer evidence. (Task 042)
- Add completion, quick info, signature help, go-to-definition, and find-references
  over the exact active trusted source context, with stale-result rejection and
  measured warm interaction latency. (Task 043)
- Add preview-first semantic rename over a Roslyn-resolved symbol and atomic multi-file
  baselines. The editor and agents share the same typed operation; text-search rename
  is not an accepted agent behavior. (Task 044)

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
- Keep Linux as the product gate while isolating native windows/pickers/accessibility
  in focused Presentation capabilities and XDG/filesystem/keyring/process behavior in
  focused Data Access capabilities. Add another platform or gRPC only when a concrete
  workflow justifies its implementations.

Deferred within Stage 3: opt-in Ollama behavioral evaluation and regression
datasets for planning, tool-selection, and review quality (Task 038) are parked
below every item above until 035, 036, 037, and 039-044 close.

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
