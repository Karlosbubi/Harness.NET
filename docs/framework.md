# Accepted Framework

This document is the working contract for building Harness.NET and for the agent
behavior Harness.NET will provide.

## Engineering baseline

| Area | Accepted decision |
|---|---|
| Product | Support .NET software development for one local developer. |
| Runtime | Start on .NET 10 and modern, idiomatic C#. |
| Correctness | Enable nullable analysis and treat compiler warnings as errors. |
| Architecture | Prefer Data Access, Business Logic, and Presentation layers where sensible. |
| Boundaries | Only interfaces, records, and enums cross layer boundaries, moving upward except for DI composition. |
| Domain types | Default to semantic enums and immutable single-value records; retain primitives only where they carry no distinct domain meaning. |
| Delivery | Implement new behavior as end-to-end feature slices. |
| Configuration | Ship typed Settings ownership and management with every configurable feature slice; raw keys alone are not delivered UX. |
| Style | Prefer functional composition, immutable data, and LINQ where idiomatic. |
| Reactivity | Use Rx.NET for event streams and state management where it fits. |
| Observability | Use structured logging and OpenTelemetry. |
| Persistence | Use Dapper, explicit SQL, SQLite, and DbUp embedded migrations. |
| Testing | Use xUnit, architecture enforcement, integration tests, and opt-in model evaluations. |
| Presentation | Use Avalonia by default, retain Terminal.Gui v2, allow future gRPC adapters, and avoid web frontends. |
| Process | Remain in one application process as long as practical. |
| Code intelligence | Use in-process Roslyn first behind implementation-neutral contracts; keep a future local LSP module replaceable. |

## Layer and dependency rules

The direct project-reference direction is:

```text
Data Access -> Business Logic -> Presentation
```

- Data Access exposes only interface, record, and enum contracts upward and contains
  the corresponding implementations.
- Business Logic references Data Access contracts and exposes its own interfaces,
  records, and enums to Presentation.
- Presentation references Business Logic contracts and contains no business rules.
- The app-neutral Avalonia UI toolkit references no Harness runtime layer; Presentation
  may consume its public controls, semantic themes, and accessibility infrastructure.
- A composition root may reference all implementations only to configure DI.
- A custom Roslyn analyzer enforces reference direction and boundary type rules.
- The reviewer role also treats violations as explicit findings.

## Collaboration model

- A persistent lead owns the goal, repository inspection, plan, delegation, and
  user communication.
- Lead plans use a closed structured contract containing 1-12 ordered, independently
  bounded tasks with concrete file areas and acceptance criteria. Tasks and their
  semantic Pending/InProgress/Completed state persist before plan approval.
- An implementer owns approved changes and verification.
- The implementer receives one delegated task per call. A durable completed task
  report is reconciled after interruption; an uncertain call is never replayed.
  Atomic edit tools reject paths outside that task's normalized file-area grant.
- An independent reviewer owns diff, architecture, and evidence review.
- Model-authored source changes are preflighted by deterministic code intelligence.
  They may not introduce a new compiler error, and warnings/analyzer findings become
  structured evidence rather than prose supplied by the model.
- Specialist exchanges are summarized in the activity timeline and fully expandable.
- Each goal requires a review-cycle limit. Reaching it pauses the run for user input.
- Local inference, elapsed time, and typed tool calls have no automatic quota but
  remain visible and cancellable.
- OpenRouter goals use an explicit spend mode. New goals default to unlimited remote
  spend; users can prominently opt into an aggregate monetary cap or local-only mode
  globally and per goal. The connector reserves an estimated maximum before each
  request and reconciles it with returned usage cost.
- Remote-cost evidence distinguishes active reservations, reconciled charges, and
  released reservations. The goal cost report exposes the cap, reserved exposure,
  actual spend, remaining budget, and any overage, with provider, model, operation,
  and request attribution. Remote calls fail closed when pricing or authorization
  is unavailable; live provider checks must use the smallest practical bounded request.
- Cost summaries remain inspectable from goal creation through completion. Monetary
  inputs and reports use explicit micro-USD domain values internally and render USD
  at presentation boundaries without hiding sub-cent reservations.
- Before production continuation, Presentation shows the pending delegated-call count,
  maximum remaining review/correction calls, selected routes, aggregate cap, active
  reservations, spend, and remaining budget. Token usage remains observable but is not
  a user-configured execution limit.

## Approval and trust policy

- A repository must be explicitly trusted once before build or test execution.
- Workspace trust also covers project evaluation and configured analyzer/source-
  generator execution for code intelligence. Untrusted repositories receive bounded
  lexical viewing only, and code intelligence never performs an implicit restore.
- Plan approval grants repository-local edits, builds, tests, searches, and
  inspection through typed tools in the goal worktree.
- Network access, package changes/restores, destructive actions, budget extensions,
  and Git commits require explicit approval.
- Selecting OpenRouter models for a goal authorizes model calls for that goal only.
- Provider/model authorization is recorded independently for lead, implementer, and
  reviewer. A remote configured default never grants implicit spending authority.
  Remote planning authorization is separate from plan approval and grants no
  repository mutation capability.
- Accepted work is committed to the isolated goal branch after approval. Harness.NET
  records the complete diff and its SHA-256 as a pending request, then requires a
  separate explicit approve/deny action. It revalidates branch, HEAD, and diff before
  committing and does not merge, rebase, or cherry-pick automatically.

## Framework representation

The framework is layered:

- Markdown records intent, conventions, architecture, and decisions.
- Typed configuration records enforceable capabilities, providers, budgets, locks,
  and privacy settings.
- Skills record reusable procedures.

Instruction precedence, from general to specific, is:

```text
global user -> repository guidance -> private workspace overlay -> goal -> task -> agent role
```

The more specific rule wins unless a rule is locked at the layer that defines it.
Conflicting rules at the same specificity pause for clarification.

`AGENTS.md` and existing repository documentation are the native shared workspace
sources. Harness.NET does not create a `.harness` directory. Private workspace
preferences and summaries live in Harness.NET storage.

When promoting a conversational preference, the lead proposes a diff and rationale.
The user chooses its destination: global private framework, private workspace
overlay, `AGENTS.md`, or a suitable existing documentation file.

## Models and providers

- Microsoft Agent Framework is the agent engine.
- Business Logic defines an agent-role abstraction around Microsoft's agent
  abstractions; Microsoft types do not cross into Presentation.
- Data Access provides Ollama and OpenRouter chat and embedding connectors.
- Data Access owns official MCP SDK 2.x clients and stateless Streamable HTTP transport;
  Business Logic owns MCP tool eligibility and agent exposure.
- Models are configurable per role through provider-neutral Business Logic records.
- Interactive startup discovers every configured provider catalog without inference,
  validates persisted role defaults, and exposes an immutable availability snapshot
  to both interactive adapters. Explicit refresh replaces that snapshot.
- Business Logic, not Presentation, qualifies models for roles. All current production
  roles require a chat model declaring `tools`; typed default and goal-selection
  commands reject incompatible models even when invoked outside the UI.
- Provider instances are named XML modules. Global routing selects a configured
  module for the main, reviewer, and tool roles without coupling upper layers to an
  implementation type.
- The current development Ollama endpoint is `http://192.168.1.101:11434`.
- `gemma4:latest` is the current default chat model for all roles.
- `embeddinggemma` is the default local embedding model and must be installed before
  local semantic indexing is available.
- OpenRouter discovers available chat and embedding models dynamically.
- OpenRouter uses normal routing by default. A workspace may require both
  no-collection and zero-data-retention routing.
- Provider API keys use Linux Secret Service with environment-variable fallback and
  are never persisted in SQLite, logs, checkpoints, or framework files.

## Context, memory, and persistence

- SQLite retains full prompts, responses, tool requests/results, approvals,
  checkpoints, usage, summaries, and artifact references until explicit deletion.
- Step checkpoints are written after each completed workflow boundary. Interrupted
  work resumes only from a safe boundary, never by replaying an uncertain tool call.
- Approved summaries and private workspace notes may influence later goals.
- Semantic retrieval indexes eligible Git-tracked source, project, Markdown, and
  text configuration files while excluding ignored, generated, binary, secret, and
  oversized content.
- Tracked-text ingestion is bounded to 10,000 index entries, 1 MiB per file, and
  32 MiB of accepted UTF-8 text per rebuild. A newly built generation becomes active
  only after every chunk and vector is durable, so cancellation or failure preserves
  the preceding compatible partition.
- Index partitions include provider, model, vector dimensions, and chunking version.
  Changing any of them creates or rebuilds a compatible partition.
- Embedding generation is configurable between Ollama and OpenRouter.
- The SQLite vector connector remains isolated inside Data Access.
- Compatible-index status inspection performs no inference. Explicit Avalonia and TUI
  rebuild and preview actions show embedding access, route, partition state, and goal
  cost state; remote rebuild requires confirmation and remains fail-closed at the goal
  cap. Both adapters expose bounded source matches, usage, cancellation, and cost.
- Lead, Implementer, and Reviewer may retrieve 1-8 bounded semantic matches through a
  typed goal-context tool. Queries are mapped to the active trusted goal workspace,
  strict remote privacy, and separately attributed embedding usage.

## Repository and tool policy

- The first version accepts Git repositories containing at least one `.slnx`,
  `.sln`, or `.csproj` entry point.
- Multiple entry points require explicit selection.
- Every approved goal receives a dedicated branch and worktree.
- Typed tools cover file reads, tracked-file listing, text search, patch application,
  .NET build/test/restore/package operations, Git status/diff, and worktree lifecycle.
- In-process Roslyn provides compiler diagnostics and semantic operations behind Data
  Access contracts. Its implementation types do not cross into Business Logic.
- Prefer typed compiler operations whenever Roslyn can determine the result. Semantic
  rename resolves symbol identity and references, previews all affected baseline-
  protected files, applies them atomically, and validates the result; agents do not
  emulate rename through repository-wide text replacement.
- No unrestricted shell is available to agents.
- Enabled MCP endpoints use the stateless `2026-07-28` discovery path. Only tools
  explicitly declaring read-only, non-destructive behavior enter agent tool lists;
  ambiguous tools fail closed, and mutating MCP requires a future typed approval.
- LibGit2Sharp handles supported Git operations. A structured Git CLI adapter handles
  worktrees or other required operations LibGit2Sharp does not support.

## Presentation and operations

- Conversation is the primary goal workflow. Typed inline cards expose plans,
  capability and cost decisions, progress, validation, evidence, Restore, exact
  commit, and branch handoff while detailed artifacts remain available as documents
  and tools. Conversational language alone never authorizes a consequential action.
- One searchable Settings surface owns ordinary application preferences, including
  editor, appearance, accessibility, model/role defaults, privacy, storage, and
  advanced module configuration. Goal-specific overrides use progressive disclosure;
  saved routes or credentials never authorize remote spending.
- Settings exposes Ollama and OpenRouter provider availability, discovered model and
  compatibility counts, remote pricing readiness, and failures without displaying
  credentials. Role pickers contain only models qualified for that respective role.
- Named provider endpoint, chat/embedding defaults, embedding dimensions, timeouts,
  and OpenRouter secret references are editable through typed commands. They persist
  to the private XDG XML override and explicitly require restart because active
  provider instances and embedding partition identity are immutable. OpenRouter keys
  are write-only and go directly to Linux Secret Service.

- Avalonia is the default interactive adapter. It currently provides the durable
  conversation stream, provider/model selection, persisted semantic themes, safe
  XDG user palettes, workspace inspection/registration/selection, explicit trust,
  durable goal creation, optional remote caps, versioned plan proposal/denial,
  trust-gated plan approval with isolated worktree provisioning, and an adaptive
  accessible desktop shell. It also discovers and selects goal-bound role models,
  renders attributed remote-cost state, starts bounded Lead planning with an explicit
  compatible model defaulted from the effective Lead route, cancels active planning,
  continues approved Implementer/Reviewer work, and exposes durable task, activity,
  and evidence snapshots. Goal-scoped semantic status, confirmed rebuild, cancellable
  search, source matches, embedding usage, and attributed cost are available without
  leaking vector-provider types into Presentation. Avalonia also exposes the complete
  exact-diff fingerprint, durable pending request, separate approve/deny decision, and
  interrupted approved-commit resumption without merge or network behavior. Its
  application-operations dialog creates deliberately confirmed, non-overwriting
  backups and reports integrity evidence. Goal management also creates, inspects,
  approves, and denies Restore capability requests bound to one exact goal,
  correlation, and registered entry point; it does not execute Restore directly or
  grant general network access. Its framework dialog resolves effective rules and
  guidance with locks, provenance, privacy, and validation issues, and edits only the
  private workspace overlay in Harness.NET storage.
- The docked source editor opens user-selected files from the active trusted original
  workspace as editable by default. A selected approved goal switches new source
  documents to its isolated worktree. Both use confined exact-baseline saves, while
  agent/model mutation authority remains restricted to approved goal worktrees.
  Tracked-text search, Git
  state, and diff resolve through the same Business Logic-owned context so adjacent
  panels cannot describe a different tree than the active editor. Saves use the
  durable typed mutation/evidence boundary and compare-and-swap; dirty close, switch,
  reset, and exit paths require save/discard/cancel, while external changes require
  explicit reload or baseline-protected overwrite.
- The bottom workbench edge separates durable conversation from typed run output.
  Run output projects persisted Build, Test, and Restore evidence into real state,
  correlation, timing, exit, cancellation, truncation, stdout, and stderr fields.
  Presentation does not interpret stored audit JSON, and no terminal or synthetic
  diagnostic stream is exposed.
- Terminal.Gui v2 provides an adaptive full-screen layout: workspace/goals on the
  left, transcript/activity in the center, plan/diff/evidence tabs on the right,
  and a composer plus status/budget footer.
- Side regions collapse on narrow terminals while the active workflow remains usable.
- The Terminal.Gui adapter remains available through `--ui=terminal`.
- The initial release is a self-contained Linux x64 binary and keeps process, path,
  and presentation contracts portable for later platforms.
- Linux remains the product gate. Native windows, pickers, clipboard, notifications,
  screen geometry, shortcuts, and accessibility sit behind focused Presentation
  capabilities. XDG storage, filesystem behavior, Secret Service, and process
  execution sit behind focused Data Access capabilities. Host selects implementations;
  feature code does not scatter platform checks or depend on one generic platform API.
- Harness.NET uses XDG-managed config, data, state, and cache locations.
- Typed runtime defaults ship as XML; an XDG XML file overrides them. Environment
  and command-line values remain optional, higher-precedence operational overrides.
- Serilog implements `Microsoft.Extensions.Logging.ILogger` and writes redacted
  rolling JSON logs. OTLP export is optional and model content is disabled by default.
- Normal tests use deterministic fake model and agent clients. Opt-in Ollama
  evaluations cover planning, tool selection, and review behavior.
- Configured credentials never authorize test spending. Paid-provider checks require
  explicit user authorization and use the smallest practical bounded request.
- Model selectors search the complete discovered catalog across configured providers,
  while showing only models compatible with the selected role. Remote models remain
  visible for local-only goals, but visibility never bypasses the goal spend mode and
  explicit-confirmation requirements.
- A provider or budget failure pauses only the exact failed role. Recovery requires an
  explicit compatible model and optional fresh user guidance before a
  retry. Any non-terminal goal can instead be aborted and removed from continuation;
  abort preserves its durable history, evidence, tasks, and worktree and grants no
  cleanup authority.
- Deliberate application-state backup creates a non-overwriting, integrity-checked
  version-2 archive with a consistent SQLite snapshot and optional validated private
  workbench-layout state, each with size and hash evidence, while excluding
  credentials, logs, caches, worktrees, and repositories. Pending migrations create
  the same recovery point automatically and abort if it cannot be verified.

## Environment observation

Observed on 2026-07-26:

| Service | Observation |
|---|---|
| Ollama | Version `0.32.3` at `http://192.168.1.101:11434`. |
| Chat model | `gemma4:latest`, 8B, `Q4_K_M`, advertising completion, tools, and thinking. |
| Embedding model | `embeddinggemma` selected but not installed during discovery. |
