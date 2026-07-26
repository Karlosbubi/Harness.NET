# Accepted Architecture

## Process and layers

Harness.NET begins as a single-process modular application. It uses direct upward
layer references and a dedicated composition root:

```text
Data Access -> Business Logic -> Presentation
      \              |              /
       +---------- Host/DI ----------+
```

The future solution will contain Data Access, Business Logic, Terminal Presentation,
Host, Analyzer, and test projects. Project creation and package pinning are deferred
until application scaffolding is authorized.

Only interfaces and records form layer contracts. Implementations remain internal
where practical; DI composition is the documented exception. Provider SDK payloads
remain inside Data Access. Microsoft Agent Framework objects remain behind the
Business Logic agent-role boundary.

## Business concepts

| Concept | Responsibility |
|---|---|
| Workspace | A trusted Git-backed .NET repository plus private Harness.NET settings. |
| Framework | Layered guidance, enforceable policy, locks, and reusable skills. |
| Goal | The user-owned outcome, provider choices, cost cap, and review-cycle cap. |
| Plan | A reviewable proposal that must be approved before mutation. |
| Agent role | Lead, implementer, or reviewer behavior wrapped by Business Logic. |
| Run | One checkpointed attempt to complete a goal. |
| Task | A bounded unit delegated by the lead. |
| Artifact | A patch, file, plan, decision, report, or verification result. |
| Approval | User authorization for a plan or consequential capability. |
| Evidence | Build, test, diff, review, usage, or other completion proof. |

## Module responsibilities

- **Data Access:** SQLite/Dapper repositories, DbUp migrations, SQLite vector
  connector, Ollama/OpenRouter connectors, Git adapters, file access, typed process
  tools, keyring access, and Serilog sinks.
- **Business Logic:** goals, plans, roles, delegation, policy evaluation, approvals,
  budgets, checkpoints, context assembly, retrieval coordination, and workflow state.
- **Presentation:** Terminal.Gui views and view state consuming only Business Logic
  interfaces, records, commands, and observable events.
- **Host/composition:** lifecycle, configuration, DI registration, cancellation,
  startup migrations, and presentation selection.
- **Analyzer:** compile-time diagnostics for reference direction and cross-layer
  contract shape.

## Data flow

1. Presentation sends a record command to a Business Logic interface.
2. Business Logic validates state and policy, then calls Data Access interfaces.
3. Data Access maps SDK, database, filesystem, Git, or process results into records.
4. Business Logic advances and checkpoints the workflow.
5. Presentation observes correlated run events and refreshes the active regions.

Long-running calls accept cancellation. A completed tool call is persisted before
the next workflow step. An interrupted call is marked uncertain and is not replayed
automatically.

## Storage boundary

- Global private framework and typed configuration use XDG configuration storage.
- SQLite stores operational state, private overlays, summaries, full run history,
  approvals, checkpoints, usage, artifacts, and vector data.
- Logs and active worktree state use XDG state locations; disposable caches use the
  XDG cache location.
- User repositories receive only goal branches, accepted changes, and explicitly
  approved edits to existing guidance. No Harness.NET metadata directory is added.
- Secrets reside in Linux Secret Service with environment fallback.

## Tool boundary

Agents select typed capabilities; they do not construct shell commands. Paths are
canonicalized and constrained to the trusted goal worktree. Build and test operations
execute repository code only after workspace trust. Restore and package operations
remain separately approval-gated because they may use the network or change project
metadata.

## Required qualities

- Provider and agent-framework types stop at their owning boundary.
- Tools are capability-based, path-checked, cancellable, and correlated.
- Workflow transitions and cost reconciliation are atomic where state consistency
  requires it.
- Logs and OTLP telemetry redact secrets and omit model content by default.
- Domain behavior is testable without Git, SQLite, a TUI, or a running model server.
- Architecture rules fail compilation through the analyzer and remain review criteria.
