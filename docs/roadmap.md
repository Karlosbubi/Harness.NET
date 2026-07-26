# Delivery Outline

The roadmap is stage-gated. A stage completes through its decisions and evidence,
not merely because code exists.

## Stage 0: Framework discovery (complete)

- Defined product scope, first workflow, and success boundary.
- Accepted layered architecture, TUI-first presentation, and single-process hosting.
- Defined collaboration roles, approvals, budgets, recovery, memory, and visibility.
- Selected persistence, provider, retrieval, Git, logging, migration, and test stacks.
- Verified the development Ollama server and recorded its installed model inventory.

Exit evidence: accepted framework and architecture decision records.

## Stage 1: Walking skeleton

- Scaffold the accepted layers, composition root, analyzer, tests, and central
  dependency management.
- Add startup configuration, XDG paths, keyring access, SQLite/DbUp initialization,
  Serilog, OpenTelemetry, and graceful cancellation.
- Add the Terminal.Gui adaptive shell using fake Business Logic contracts.
- Connect Ollama and OpenRouter behind Data Access interfaces and verify streaming,
  usage mapping, tool-call mapping, and provider failure handling.
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
