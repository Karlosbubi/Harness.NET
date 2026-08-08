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
  is goal-bound, strictly private, and cost-accounted.
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
- Completed the ADR 013 chat-first workflow replacement. Typed inline
  cards carry plans, progress, evidence, validation, consequential decisions, and
  handoff; role/model defaults move to searchable Settings and goal-specific
  overrides use progressive disclosure. Durable plan, remote-spend, Restore,
  destructive, budget, and exact-commit authority remains explicit. (Task 040)
  The Settings foundation is complete: all settled categories are searchable;
  Appearance owns persisted theme selection; Models & roles owns typed, validated,
  schema-19 role/provider/model defaults; unavailable categories are explicit;
  and remote authority remains goal-bound. `docs/settings.md` records ownership.
  The next increment is also present: durable goal, plan, run, task, evidence,
  capability/Restore, and exact-commit records now project into immutable conversation
  cards with explicit normal and degraded states. The composer now creates a
  selected private draft from the user's first objective in a trusted workspace with
  conservative review defaults, no remote budget, and no model call; existing goals
  are offered as inline Continue choices. Typed plan generation, approve/change,
  production continuation, and cancellation actions sit on their matching cards and
  retain bounded-call disclosures and focused confirmation. Correlation-bound Restore
  approve/deny and exact-diff request/approve/deny/resume also run from their matching
  cards while preserving one-use and stale-fingerprint checks. Budget extensions and
  other destructive decisions remain for later card migrations.
  Draft goal cards now also reveal progressive limits/routes on demand: an exact
  remote USD cap is a typed, trust-required, goal-bound authorization that is disabled
  by default and becomes immutable once planning begins; stale draft snapshots fail.
- Build the ADR 012 in-process Roslyn service behind replaceable semantic contracts.
  Its SDK/MSBuild/self-contained-publish compatibility checkpoint now passes for
  `.csproj`, `.sln`, `.slnx`, missing-SDK degradation, and the real `Harness.slnx`.
  Implementation-neutral Data Access and Business Logic contracts now enforce trust,
  source-context identity, confined paths, cancellation, and buffer freshness against
  a deterministic fake. The composed Roslyn adapter now loads one foreground context,
  reports progress and bounded failures, synchronizes exact-baseline in-memory text,
  runs compiler/configured-analyzer diagnostics, and disposes safely without restore.
  Versioned C# buffers now surface syntax, compiler, and configured analyzer
  diagnostics inline and in a navigable, filterable Problems tool with measured cold,
  warm, memory, and cancellation behavior. Model-authored candidate changes fail closed
  when they introduce compiler errors and retain warning/analyzer evidence. (Task 042)
- Add completion, quick info, signature help, go-to-definition, and find-references
  over the exact active trusted source context, with stale-result rejection and
  measured warm interaction latency. (Task 043)
- Add preview-first semantic rename over a Roslyn-resolved symbol and atomic multi-file
  baselines. The editor and agents share the same typed operation; text-search rename
  is not an accepted agent behavior. (Task 044)

### Planned: controlled visual verification

- Add a Linux-first visual verification capability through XDG Desktop Portal
  Screenshot, with ScreenCast/PipeWire considered only where a sequence of frames is
  demonstrably necessary. The preferred target is the Harness.NET window or a
  user-selected application window, never silent unrestricted desktop capture.
  (Task 045)
- Make every capture an explicit, visible developer action or a clearly disclosed,
  revocable capture session. Portal consent, cancellation, denial, unavailable-portal,
  multi-monitor, scaling, and Wayland behavior must be honest product states.
- Store captures as bounded, goal-scoped visual evidence with the active workspace,
  goal, app/window identity, timestamp, initiating actor, and related model/tool action.
  A Visual verification document/tool shows the same frame and action context to the
  developer before or while it is made available to a model.
- Give models typed operations to request a capture and inspect an approved capture;
  do not give them a generic desktop API, unrestricted video feed, pointer/keyboard
  control, or authority to capture other applications. Remote-model disclosure and
  privacy routing apply before image content leaves the machine.
- Use the capability for bounded visual checks after UI changes: compare the rendered
  result with the requested outcome, identify layout/focus/error-state problems, and
  attach observations to the workflow evidence. Visual inspection complements rather
  than replaces AT-SPI, deterministic UI tests, build/test evidence, or human review.
- Keep portal interaction behind focused platform contracts so a future operating
  system can replace the Linux implementation. Record the capture/session, retention,
  privacy, and model-tool boundaries in an ADR before implementation.

### Planned: documentation-aware lookup

- Add explicit documentation support for the platform and core libraries Harness.NET
  works with, initially .NET/SDK APIs, Avalonia, Rx.NET, Serilog, Microsoft Agent
  Framework, Roslyn, Dock, Dapper, SQLite, and the configured test/tooling stack.
  Resolve documentation against the dependency version actually used by the active
  workspace instead of silently answering from an unrelated current release.
  (Task 046)
- Introduce one Business Logic lookup manager shared by documentation retrieval and
  general web research. It routes a bounded query through available evidence sources:
  exact local/package documentation, curated local or vector-indexed documentation,
  configured MCP documentation sources, and web search only when local/configured
  evidence is absent, stale, conflicting, or insufficient.
- Keep documentation out of ambient prompts. Lead, Implementer, Reviewer, and the
  developer invoke typed lookup/search actions when a concrete question needs support.
  The manager returns a small ranked evidence set, permits progressive refinement,
  deduplicates overlapping passages, and stops escalating once the question is
  adequately supported.
- Preserve source URI, library/package identity, version, retrieval time, source kind,
  authority, and cache/index generation on every result. Answers and model actions can
  cite the exact supporting material, surface version mismatches, and distinguish
  authoritative documentation from examples, community discussion, and inference.
- Manage sources in Settings: availability, version coverage, indexing/cache state,
  MCP connection status, refresh policy, offline behavior, retention, and failures.
  Documentation content and indexes remain private Harness.NET state and never add
  metadata to the user's repository.
- Apply existing privacy, network, credential, and cost boundaries before MCP or web
  access. Query escalation must be visible in the workflow evidence; no provider key,
  workspace source, or private goal content is sent merely to discover documentation.
- Record an ADR before implementation covering lookup sufficiency/escalation policy,
  adapter boundaries, MCP and web trust, version resolution, caching/index identity,
  citations, licensing/retention, and deterministic testing without live services.

### Workflow friction (delivered)

- Users can retain multiple trusted workspaces, switch through a dirty-document
  preflight, and restore each workspace's selected goal context. (Task 036)
- Large/messy repository recovery is proven for dirty bases, mid-goal conflicts,
  index rebuilds under load, provider outages, budget exhaustion, and
  corrupted/interrupted state outside the representative-repo gate. (Task 037)
- A committed goal branch surfaces its exact local branch/SHA and deliberate manual
  push, PR, or merge handoff without network or integration automation. (Task 039)

### Continuing product-quality work

- In-app restore now verifies and stages v1/v2 backups behind explicit confirmation;
  cold-start publication revalidates integrity and retains rollback material in both
  Avalonia and the TUI. (Task 041, complete)
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
below every item above until the professional-IDE baseline and Tasks 045-046 close.

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
