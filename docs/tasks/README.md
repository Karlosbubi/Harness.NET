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
| 002 | Done | Add the layer-boundary Roslyn analyzer | 001 | Invalid references and non-interface/record contracts produce diagnostics. |
| 003 | Done | Add XDG configuration and Secret Service access | 001 | Paths resolve predictably and secrets never enter ordinary configuration. |
| 004 | Done | Initialize SQLite with Dapper and DbUp | 001, 003 | Startup creates and migrates a versioned database idempotently. |
| 005 | Done | Configure Serilog and OpenTelemetry | 003 | Redacted JSON logs work locally and OTLP remains opt-in. |
| 006 | Done | Build the adaptive Terminal.Gui shell | 001, 003 | Fake workspace, activity, detail, composer, and status regions render and collapse. |
| 007 | Done | Add the Ollama chat/embedding connector | 001, 003, 005 | Model discovery, streaming chat, embeddings, cancellation, and failures map to records. |
| 008 | Pending | Add the OpenRouter connector and cost accounting | 001, 003, 005 | Discovery, streaming, embeddings, routing policy, and cost caps are verified. |
| 009 | Pending | Wrap Microsoft Agent Framework in agent roles | 001, 007 | Lead, implementer, and reviewer run behind Business Logic interfaces. |
| 010 | Pending | Add tracked-text semantic indexing | 004, 007, 008 | Compatible index partitions rebuild and retrieve eligible repository chunks. |
| 011 | Pending | Run a checkpointed fake workflow through the TUI | 004, 006, 009 | A persisted fake run pauses, resumes, and exposes expandable evidence. |
| 012 | Pending | Publish the Linux x64 walking skeleton | 011 | A self-contained binary starts with correct XDG storage and graceful shutdown. |

## v1.0 usability backlog

The walking skeleton proves technology choices. The following slices are required
before Harness.NET is a usable v1.0 product. **Partial** means supporting code exists
but the end-user workflow is not complete.

| ID | Status | User capability | Current gap | Done when |
|---|---|---|---|---|
| 013 | Done | Hold a durable local-model conversation | Successful live inference still depends on the configured server being reachable. | TUI instructions stream through Business Logic to Ollama, persist, reload after restart, and show actionable provider failures. |
| 014 | Partial | Configure and verify model providers | Named XML modules and per-role routing validate at startup, but only Ollama/MainLlm is consumed and TUI model selection remains conversation-wide and wide-layout only. | Configuration validates endpoints, discovers capabilities, selects models per role, and reports health without exposing secrets. |
| 015 | Done | Register and trust a .NET workspace | - | A user can add a Git repository, select a solution/project, explicitly trust it, reopen it, and see dirty/base state. |
| 016 | Missing | Load the user's engineering framework | Preferences exist only in Harness.NET's design documents. | Global, repository, and private framework layers load with precedence, locks, validation, and an inspectable effective view. |
| 017 | Missing | Let agents inspect safely | No repository, Git, or .NET inspection tools are available to agents. | Typed, path-confined read/search/status/diff/project/build-information tools run only in a trusted workspace and return bounded records. |
| 018 | Missing | Let agents implement and verify | No editing, build, test, restore, or mutation policy exists. | Approved runs can use typed edit/build/test tools with cancellation, output limits, correlation, and separate restore/network approval. |
| 019 | Missing | Isolate work with Git | No branches or worktrees are created. | Each approved goal uses a validated branch/worktree, preserves dirty user state, and never merges/rebases automatically. |
| 020 | Missing | Create goals and approve plans | There is no goal, plan, approval, or policy state machine. | Goals, caps, plans, revisions, approvals, and denials persist and every consequential transition is validated. |
| 021 | Missing | Coordinate lead, implementer, and reviewer agents | Microsoft Agent Framework is not integrated and no roles execute. | Role prompts and tool scopes are wrapped behind Business Logic interfaces and a lead can delegate bounded tasks. |
| 022 | Missing | Resume interrupted work safely | Only schema state persists; there are no run checkpoints. | Runs checkpoint at safe boundaries, resume completed steps, and mark uncertain calls without automatic replay. |
| 023 | Missing | Review evidence and accept results | There is no independent review loop or commit approval. | Diff, tests, tool evidence, review findings, cycle caps, and explicit commit approval work end to end. |
| 024 | Missing | Retrieve relevant repository context | The embedding adapter exists but no tracked-text index does. | Eligible Git-tracked text is chunked, partitioned by embedding configuration, rebuilt, searched, and filtered by policy. |
| 025 | Partial | Use remote models under a cost cap | Secret storage exists; OpenRouter, routing, pricing, and reconciliation do not. | Remote use requires approval, streams through the provider boundary, and enforces estimated plus reconciled per-goal caps. |
| 026 | Partial | Operate and distribute v1.0 reliably | Logging and cancellation exist, but packaging, upgrades, backup/export, and recovery tests do not. | A self-contained Linux x64 release passes clean-install, migration, outage, cancellation, recovery, and representative-repository acceptance tests. |

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
  model selection in the wide TUI. Typed XML defines named provider modules and
  validates main/reviewer/tool routing; consuming non-main routes remains incomplete.
- The XML-selected `MainLlm` was verified through the composed TUI against Ollama;
  it persisted `HARNESS_XML_OK` with 34 input and 7 output tokens.
- Task 015 has deterministic Git inspection, SQLite registry, single-active-workspace
  selection, entry-point validation, and explicit trust-transition coverage. The TUI
  now provides a workspace-management modal and separate trust confirmation; narrow
  layouts reach the same commands through the top-level Workspace menu. Dashboard
  snapshots resolve active workspace context once per operation and retain a stable
  value throughout streaming.
