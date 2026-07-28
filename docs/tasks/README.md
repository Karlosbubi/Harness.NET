# Implementation Tasks

Tasks are dependency ordered, independently reviewable, and small enough to finish
with focused verification. A task moves to **Done** only when its acceptance check
passes and its documentation is current.

## Delivery discipline

- Decompose implementation tasks into bite-sized chunks with one coherent outcome
  and a focused verification step.
- Commit each completed, verified chunk promptly instead of accumulating unrelated
  work in the working tree.
- Use Conventional Commits descriptions in the form
  `<type>(optional-scope): concise description`, such as
  `feat(workspaces): persist explicit trust decisions`.
- Keep tests and task/documentation status changes in the same commit as the behavior
  they verify or describe.
- Do not mix unrelated cleanup or refactoring into a feature commit.

## Stage 1: Walking skeleton

| ID | Status | Task | Depends on | Done when |
|---|---|---|---|---|
| 001 | Done | Scaffold layer projects and boundary tests | - | The solution builds and reference-direction tests pass. |
| 002 | Done | Add the layer-boundary Roslyn analyzer | 001 | Invalid references and non-interface/record/enum contracts produce diagnostics. |
| 003 | Done | Add XDG configuration and Secret Service access | 001 | Paths resolve predictably and secrets never enter ordinary configuration. |
| 004 | Done | Initialize SQLite with Dapper and DbUp | 001, 003 | Startup creates and migrates a versioned database idempotently. |
| 005 | Done | Configure Serilog and OpenTelemetry | 003 | Redacted JSON logs work locally and OTLP remains opt-in. |
| 006 | Done | Build the adaptive Terminal.Gui shell | 001, 003 | Fake workspace, activity, detail, composer, and status regions render and collapse. |
| 007 | Done | Add the Ollama chat/embedding connector | 001, 003, 005 | Model discovery, streaming chat, embeddings, cancellation, and failures map to records. |
| 008 | Done | Add the OpenRouter connector and cost accounting | 001, 003, 005 | Discovery, streaming, embeddings, routing policy, and cost caps are verified. |
| 009 | Done | Wrap Microsoft Agent Framework in agent roles | 001, 007 | Lead, implementer, and reviewer run behind Business Logic interfaces. |
| 010 | Done | Add tracked-text semantic indexing | 004, 007, 008 | Compatible index partitions rebuild and retrieve eligible repository chunks. |
| 011 | Done | Run a checkpointed fake workflow through the TUI | 004, 006, 009 | A persisted fake run pauses, resumes, and exposes expandable evidence. |
| 012 | Done | Publish the Linux x64 walking skeleton | 011 | A self-contained binary starts with correct XDG storage and graceful shutdown. |

## v1.0 usability backlog

The walking skeleton proves technology choices. The following slices are required
before Harness.NET is a usable v1.0 product. **Partial** means supporting code exists
but the end-user workflow is not complete.

| ID | Status | User capability | Current gap | Done when |
|---|---|---|---|---|
| 013 | Done | Hold a durable local-model conversation | Successful live inference still depends on the configured server being reachable. | TUI instructions stream through Business Logic to Ollama, persist, reload after restart, and show actionable provider failures. |
| 014 | Done | Configure and verify model providers | - | Configuration validates endpoints, discovers capabilities, selects models per role, and reports health without exposing secrets. |
| 015 | Done | Register and trust a .NET workspace | - | A user can add a Git repository, select a solution/project, explicitly trust it, reopen it, and see dirty/base state. |
| 016 | Done | Load the user's engineering framework | - | Global, repository, and private framework layers load with precedence, locks, validation, and an inspectable effective view. |
| 017 | Done | Let agents inspect safely | - | Typed, path-confined read/search/status/diff/project/build-information tools run only in a trusted workspace and return bounded records. |
| 018 | Done | Let agents implement and verify | - | Approved runs can use typed edit/build/test tools with cancellation, output limits, correlation, and separate restore/network approval. |
| 019 | Done | Isolate work with Git | - | Each approved goal uses a validated branch/worktree, preserves dirty user state, and never merges/rebases automatically. |
| 020 | Done | Create goals and approve plans | - | Goals, caps, plans, revisions, approvals, and denials persist and every consequential transition is validated. |
| 021 | Partial | Coordinate lead, implementer, and reviewer agents | Role-specific typed tools and provider function-call loops are enforced, but durable bounded delegation and production orchestration are not coordinated. | Role prompts and tool scopes are wrapped behind Business Logic interfaces and a lead can delegate bounded tasks. |
| 022 | Partial | Resume interrupted work safely | The deterministic walking skeleton resumes from persisted safe boundaries and incomplete tool calls remain identifiable, but production-run reconciliation is absent. | Runs checkpoint at safe boundaries, resume completed steps, and mark uncertain calls without automatic replay. |
| 023 | Partial | Review evidence and accept results | Tool requests/results are durable and queryable, but there is no independent review loop or commit approval. | Diff, tests, tool evidence, review findings, cycle caps, and explicit commit approval work end to end. |
| 024 | Partial | Retrieve relevant repository context | A presentation-neutral service now filters and chunks tracked text, atomically rebuilds compatible SQLite vector partitions, and retrieves matches; production context assembly and workflow/TUI controls remain. | Eligible Git-tracked text is chunked, partitioned by embedding configuration, rebuilt, searched, and filtered by policy. |
| 025 | Done | Use remote models under a cost cap | - | Remote use requires approval, streams through the provider boundary, and enforces estimated plus reconciled per-goal caps. |
| 026 | Partial | Operate and distribute v1.0 reliably | A verified self-contained walking-skeleton package exists, but upgrades, backup/export, hardening, and production recovery acceptance remain. | A self-contained Linux x64 release passes clean-install, migration, outage, cancellation, recovery, and representative-repository acceptance tests. |

### v1.0 release gate

All tasks 013-026 must be **Done**. A release candidate must complete a representative
.NET repository change from workspace registration through explicit commit approval,
survive an injected interruption, and leave both the user repository and private
Harness.NET state auditable.

## Task 001 acceptance

- Runtime projects are `Harness.DataAccess`, `Harness.BusinessLogic`,
  `Harness.Presentation.Terminal`, and `Harness.Host`.
- References point directly upward: Business Logic to Data Access, Presentation to
  Business Logic, and Host to all three solely as composition root.
- Central package management is enabled.
- Architecture tests assert the allowed runtime project-reference graph.
- `dotnet build` and `dotnet test` complete with zero warnings.

## Current verification

- The architecture analyzer has focused diagnostic tests and runs in every runtime
  project build.
- XDG resolution, Secret Service fallback, DbUp migrations, JSON-log redaction,
  responsive layout policy, and Ollama payload mapping have deterministic tests.
- The host and Terminal.Gui lifecycle have been smoke-tested against isolated XDG
  directories.
- On 2026-07-27, the production Ollama adapter passed live discovery and streaming
  against `gemma4:latest`, including completion/tool/thinking capabilities and token
  usage. The composed TUI path persisted `HARNESS_APP_OK` with 25 input and 7 output
  tokens.
- Task 013 has orchestration tests plus a real TUI-to-SQLite failure-path check. A
  submitted turn was persisted before the provider call, its structured connection
  failure was persisted, and the history reloaded through the same Business Logic
  service. Successful token streaming is now also covered by the opt-in live test.
- Task 014 now exposes provider health, discovered capabilities, refresh, and durable
  conversation model selection in the wide TUI. Typed XML defines named provider
  modules and validates main/reviewer/tool/embedding routing; all chat routes provide
  local role defaults and the embedding route is consumed by semantic indexing.
  Schema 14 persists explicit goal-specific lead, implementer, and reviewer choices.
  Goal catalog discovery distinguishes chat from embedding models, preserves named
  module attribution, reports per-provider failures without exposing secrets, and
  shows published remote input/output/request pricing. Agent execution resolves the
  selected goal/role route and carries strict privacy plus a required output-token
  ceiling into remote requests.
- Task 010 uses the pinned Microsoft SQLite vector connector solely inside Data
  Access. Deterministic tests prove Git-index eligibility and secret/generated/binary
  filtering, stable bounded chunks, provider/model/dimension/version partitioning,
  native cosine retrieval, atomic compatible replacement, and preservation of the
  ready generation when a rebuild is aborted. Business Logic enforces active trust,
  batches provider-neutral embeddings, validates vector shapes, accounts for remote
  usage, and exposes rebuild/search records without connector types.
- On 2026-07-28, the OpenRouter embedding path returned a 1,536-dimensional vector
  from `openai/text-embedding-3-small` for one short input. The opt-in live test
  enforced a five-microdollar reservation ceiling before sending the request.
- Task 025 now requires an explicit remote provider/model choice for each goal role
  and a positive goal cap. The durable selection authorizes only that provider/model
  for the goal before plan approval, allowing a remote lead to propose a plan without
  granting mutation rights. Every role call is goal-scoped and output-capped;
  reservations and reconciled charges remain enforced atomically and visible in the
  goal cost report. Approved goals retain goal-scoped remote embedding support.
- Task 009 wraps Microsoft Agent Framework's `ChatClientAgent` behind semantic
  Business Logic contracts. Deterministic tests run lead, implementer, and reviewer
  prompts through separate configured provider/model routes and verify invalid
  requests, incomplete composition, and provider-failure mapping without exposing
  framework types to Presentation.
- Task 021 now maps Microsoft Agent Framework functions through provider-neutral
  semantic definitions, calls, and results into Ollama and OpenRouter. Deterministic
  tests prove a complete model-tool-model loop and closed role scopes: Lead is
  read-only against the trusted original workspace, Implementer gains only approved
  worktree edit/build/test capabilities, and Reviewer is read-only with durable
  evidence access. Restore, commit, package, and shell capabilities remain absent.
  OpenRouter reservations conservatively include tool schemas and tool traffic.
- Task 011 persists semantic workflow runs and ordered checkpoints in schema 11.
  Deterministic store and orchestration tests prove plan-time pause, process-restart
  resume, recovery after interruption at an already persisted implementation
  boundary, independent review completion, and stale-transition rejection. The TUI
  Workflow menu starts or resumes the run and expands full checkpoint evidence in a
  scrollable view through presentation-neutral Business Logic contracts.
- Task 012 has a checked-in linux-x64 publish profile and repeatable lifecycle
  verifier. The compressed self-contained executable starts with `PATH` and
  `DOTNET_ROOT` pointing to nonexistent locations, loads the shipped XML defaults,
  creates its database and logs only under isolated XDG data/state roots, and exits
  with status zero after SIGTERM. Native libraries remain beside the executable so
  the runtime does not extract bundle content into a non-XDG home directory.
- The XML-selected `MainLlm` was verified through the composed TUI against Ollama;
  it persisted `HARNESS_XML_OK` with 34 input and 7 output tokens.
- Task 015 has deterministic Git inspection, SQLite registry, single-active-workspace
  selection, entry-point validation, and explicit trust-transition coverage. The TUI
  now provides a workspace-management modal and separate trust confirmation; narrow
  layouts reach the same commands through the top-level Workspace menu. Dashboard
  snapshots resolve active workspace context once per operation and retain a stable
  value throughout streaming.
- Task 016 has a provider-neutral rule resolver with deterministic precedence,
  provenance, locks, validation, and unresolved same-level conflict reporting.
  Bounded readers load global XDG Markdown and root repository `AGENTS.md` with
  privacy and provenance metadata. Workspace-private overlays persist in SQLite
  without repository metadata. Business Logic composes all three document layers,
  effective rules, and source failures into one snapshot. Named XML rules bind into
  immutable configuration and the resolver. The top-level Framework menu renders
  the effective snapshot in a scrollable view and edits the private workspace
  overlay with a supported multiline editor.
- Task 017 has begun with a typed file-inspection boundary. Business Logic requires
  the requested workspace to be active and explicitly trusted. Data Access accepts
  only confined relative paths, rejects symbolic-link hops and non-UTF-8 files, and
  bounds returned content to 64 KiB with explicit truncation metadata. Tracked-text
  search enumerates the Git index, shares the confinement policy, skips oversized or
  non-text files, and returns bounded line records with truncation metadata. Git
  inspection returns branch and HEAD identity, bounded status records, and a
  combined index/worktree diff capped at 128 KiB. Non-evaluating .NET metadata
  inspection uses the MSBuild solution parser plus bounded XML/JSON readers for
  projects, target frameworks, SDK/language settings, references, and `global.json`.
- Task 020 has durable draft goals tied to the active workspace. Goal creation
  validates title/objective bounds, review-cycle limits, and optional remote-model
  budgets before schema-versioned SQLite persistence. Plan proposals increment
  revisions and wait for a decision. Approval requires active workspace trust;
  denial requires a reason. Goal, plan, and decision transitions persist atomically
  and reject stale or duplicate decisions. The TUI now creates and inspects goals,
  proposes plans, confirms worktree-granting approval, records denial reasons, and
  displays local-only authorization or fully attributed remote-cost totals. Goal
  identifiers, plan identifiers and revisions, review caps, money, states, decision,
  approval, and worktree values cross Presentation through semantic records/enums.
- Task 019 has a structured Git worktree adapter with canonical goal identifiers,
  deterministic Harness-owned branch/path names, bounded diagnostics, cancellation,
  and idempotent retry. Tests verify that worktrees start at the recorded base commit
  while dirty state in the user's original worktree remains unchanged. Approval
  provisions the worktree before atomically persisting the approval and active grant;
  provisioning failure leaves the plan pending and grants no mutation capability.
- Task 018 has an approved mutation boundary for file creation and replacement.
  Business Logic requires an approved goal, active persisted worktree grant, active
  workspace, current trust, and correlation identifier. Data Access independently
  confines paths, rejects symbolic links and stale SHA-256 expectations, caps UTF-8
  content at 1 MiB, and atomically replaces the destination. The same authorization
  boundary now exposes typed Build, Test, and separately approved Restore operations
  against the registered entry point in the goal worktree. The process adapter
  disables implicit restore for Build and Test, confines the entry point, cancels
  the process tree, and returns exit, duration, bounded output, and truncation
  evidence. Schema version 9 persists every approved
  tool request before execution and completes it with full correlated result evidence.
  Duplicate correlations cannot replay a tool, incomplete calls remain identifiable
  for recovery, and a presentation-neutral Business Logic service exposes the audit
  trail. Tool operations, kinds, states, identifiers, and correlations use semantic
  enums and single-value records under ADR 007. Schema version 10 adds durable
  Restore approval requests and decisions. Approval is scoped to one goal,
  correlation, capability, and registered entry point; denied, missing, mismatched,
  or replayed requests cannot start the process. Build and Test continue to force
  `--no-restore`, while only an explicitly approved Restore may resolve dependencies.
