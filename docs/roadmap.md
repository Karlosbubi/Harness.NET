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

## Stage 1: Walking skeleton (current)

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
- In progress: provider health, capability discovery, and persisted model selection
  are available in the wide TUI. Typed XML supplies named provider modules and
  main/reviewer/tool routing; only the main route is consumed today.
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
  top-level TUI menu exposes the effective view and private-overlay editor.
- Completed: typed repository inspection includes trusted-workspace identity
  checks, path-confined bounded UTF-8 file reads, and bounded search over Git-indexed
  text. Git inspection supplies bounded status and diff evidence with branch and
  HEAD identity. Non-evaluating .NET inspection parses solution, project, reference,
  target-framework, language, and SDK-policy metadata into bounded records.
- In progress: schema-versioned draft goals persist against the active workspace
  with validated review-cycle and optional remote-cost caps. Versioned plan
  proposals and atomic approval/denial transitions now exist below Presentation;
  the TUI workflow and worktree-bound capability grant remain.
- Connect OpenRouter and add typed tool-call mapping behind the same boundary.
- Prove one checkpointed fake workflow through the TUI.

Exit evidence: clean build, architecture diagnostics, deterministic tests, and a
usable TUI shell that exercises a persisted fake run.

## Stage 2: First complete repository workflow

- Register and trust Git-backed .NET workspaces and select their entry points.
- Load global, repository, and private workspace framework layers with locks.
- Index eligible tracked text through configurable Ollama/OpenRouter embeddings.
- Implement goal creation, role/provider selection, review and cost caps, and plan
  approval.
- Create isolated branches/worktrees and expose the typed inspection, edit, .NET,
  and Git tools.
- Run lead, implementer, and reviewer roles through Microsoft Agent Framework.
- Persist step checkpoints, expandable exchanges, artifacts, evidence, and cost.
- Commit accepted work to the goal branch after explicit approval.

Exit evidence: an end-to-end feature or fix completed in a representative .NET
repository, independently reviewed, recovered from an injected interruption, and
committed only after approval.

## Stage 3: Hardening and expansion

- Add opt-in Ollama behavioral evaluations and regression datasets.
- Exercise large repositories, index rebuilds, dirty bases, conflicts, cancellations,
  model outages, budget exhaustion, and corrupted/interrupted state.
- Publish a self-contained Linux x64 release using XDG directories.
- Add other platforms, Avalonia, or gRPC only through existing Business Logic
  contracts and only when a concrete workflow justifies them.

## Deferred until justified

- Distributed workers or message brokers
- Multi-user accounts, tenancy, or shared authorization
- Web-based presentation
- Plugin marketplace
- Unrestricted agent shells
- Automatic merging, rebasing, or pull-request creation
- Fully autonomous background operation
