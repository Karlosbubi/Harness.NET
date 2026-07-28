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
| Domain types | Prefer semantic enums and immutable single-value records over ambiguous primitives. |
| Delivery | Implement new behavior as end-to-end feature slices. |
| Style | Prefer functional composition, immutable data, and LINQ where idiomatic. |
| Reactivity | Use Rx.NET for event streams and state management where it fits. |
| Observability | Use structured logging and OpenTelemetry. |
| Persistence | Use Dapper, explicit SQL, SQLite, and DbUp embedded migrations. |
| Testing | Use xUnit, architecture enforcement, integration tests, and opt-in model evaluations. |
| Presentation | Start with Terminal.Gui v2; allow future Avalonia and gRPC adapters; avoid web frontends. |
| Process | Remain in one application process as long as practical. |

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
- Specialist exchanges are summarized in the activity timeline and fully expandable.
- Each goal requires a review-cycle limit. Reaching it pauses the run for user input.
- Local inference, elapsed time, and typed tool calls have no automatic quota but
  remain visible and cancellable.
- OpenRouter goals require an aggregate monetary cap. The connector reserves an
  estimated maximum before each request and reconciles it with returned usage cost.
- Goals are local-only by default. Harness.NET never treats a configured credential
  as spending authorization, never permits an uncapped remote request, and requires
  an explicit per-goal cap before remote inference.
- Remote-cost evidence distinguishes active reservations, reconciled charges, and
  released reservations. The goal cost report exposes the cap, reserved exposure,
  actual spend, remaining budget, and any overage, with provider, model, operation,
  and request attribution. Remote calls fail closed when pricing or authorization
  is unavailable; live provider checks must use the smallest practical bounded request.
- Cost summaries remain inspectable from goal creation through completion. Monetary
  inputs and reports use explicit micro-USD domain values internally and render USD
  at presentation boundaries without hiding sub-cent reservations.
- Before production continuation, Presentation shows the pending delegated-call count,
  maximum remaining review/correction calls, per-role output ceilings, selected routes,
  aggregate cap, active reservations, spend, and remaining budget.

## Approval and trust policy

- A repository must be explicitly trusted once before build or test execution.
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
- Models are configurable per role through provider-neutral Business Logic records.
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
- Compatible-index status inspection performs no inference. Explicit TUI rebuild and
  preview actions show embedding access, route, partition state, and goal cost state;
  remote rebuild requires confirmation and remains fail-closed at the goal cap.
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
- No unrestricted shell is available to agents.
- LibGit2Sharp handles supported Git operations. A structured Git CLI adapter handles
  worktrees or other required operations LibGit2Sharp does not support.

## Presentation and operations

- Terminal.Gui v2 provides an adaptive full-screen layout: workspace/goals on the
  left, transcript/activity in the center, plan/diff/evidence tabs on the right,
  and a composer plus status/budget footer.
- Side regions collapse on narrow terminals while the active workflow remains usable.
- The initial release is a self-contained Linux x64 binary and keeps process, path,
  and presentation contracts portable for later platforms.
- Harness.NET uses XDG-managed config, data, state, and cache locations.
- Typed runtime defaults ship as XML; an XDG XML file overrides them. Environment
  and command-line values remain optional, higher-precedence operational overrides.
- Serilog implements `Microsoft.Extensions.Logging.ILogger` and writes redacted
  rolling JSON logs. OTLP export is optional and model content is disabled by default.
- Normal tests use deterministic fake model and agent clients. Opt-in Ollama
  evaluations cover planning, tool selection, and review behavior.

## Environment observation

Observed on 2026-07-26:

| Service | Observation |
|---|---|
| Ollama | Version `0.32.3` at `http://192.168.1.101:11434`. |
| Chat model | `gemma4:latest`, 8B, `Q4_K_M`, advertising completion, tools, and thinking. |
| Embedding model | `embeddinggemma` selected but not installed during discovery. |
