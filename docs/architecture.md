# Accepted Architecture

## Process and layers

Harness.NET begins as a single-process modular application. It uses direct upward
layer references and a dedicated composition root:

```text
Data Access -> Business Logic -> Presentation -> UI Toolkit (Avalonia-only support)
      \              |              /
       +---------- Host/DI ----------+
```

The solution contains Data Access, Business Logic, Avalonia and Terminal Presentation,
an app-neutral Avalonia UI toolkit, Host, and architecture-test projects with central
package management. A Roslyn analyzer now enforces layer direction and public
contract shape during every runtime build.

Only interfaces, records, and enums form layer contracts. Prefer enums for closed
sets and immutable single-value records where primitive values have distinct domain
meaning. Implementations remain internal where practical; DI composition is the
documented exception. Provider SDK payloads
remain inside Data Access. Microsoft Agent Framework objects remain behind the
Business Logic agent-role boundary. Roslyn, MSBuild, and any future LSP protocol
objects remain inside the code-intelligence implementation boundary.

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
| Code intelligence | Versioned compiler diagnostics, semantic navigation, and typed transformations for one trusted source context. |

## Module responsibilities

- **Data Access:** SQLite/Dapper repositories, DbUp migrations, SQLite vector
  connector, Ollama/OpenRouter connectors, Git adapters, file access, typed process
  tools, keyring access, the in-process Roslyn/MSBuild implementation, and Serilog
  sinks.
- **Business Logic:** goals, plans, roles, delegation, policy evaluation, approvals,
  budgets, checkpoints, context assembly, retrieval coordination, trusted code-
  intelligence lifecycle and validation policy, and workflow state.
- **UI toolkit:** public Avalonia controls, semantic themes, accessibility helpers,
  and adaptive layouts with no dependency on another Harness runtime project.
- **Presentation:** Avalonia and Terminal.Gui adapters consuming only Business Logic
  interfaces, records, commands, and streams. Avalonia owns its Rx.NET view state,
  chat-first workflow cards, transient editor buffers, and focused native desktop
  capabilities such as pickers, clipboard, screen geometry, and accessibility.
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

Live editor buffers follow a separate transient path: Presentation sends an immutable
context-, baseline-, and version-bound document snapshot; Business Logic validates
the source context; Data Access computes semantic results; and Presentation discards
any result whose context or buffer version is stale. Roslyn work never runs on the UI
thread and transient buffers are not persisted.

## Storage boundary

- Global private framework and typed configuration use XDG configuration storage.
- SQLite stores operational state, private overlays, summaries, full run history,
  approvals, checkpoints, usage, artifacts, vector data, and the preferred theme ID.
- Bounded color-token user themes are read from the XDG configuration theme directory.
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

Role scopes are closed semantic sets. Before plan approval, the Lead can read,
search, and inspect only the trusted original workspace. The Implementer can read,
search, inspect, atomically edit, build, and test only an approved active goal
worktree. The independent Reviewer can read the same worktree diff and durable tool
evidence but cannot edit, build, or test. Restore, package, commit, and unrestricted
shell capabilities are absent from all automatically invoked role tool sets.
Each Implementer call also carries the delegated task's normalized file-area grant;
atomic edit calls outside those repository-relative areas fail before reaching the
mutation service. Review correction calls receive the union of the accepted tasks'
areas, while build and test remain bound to the registered goal entry point.

Model-authored mutations are first applied to an in-memory compiler solution. A new
compiler Error rejects the mutation before disk write; warnings and analyzer findings
become evidence. Accepted multi-file transformations revalidate all baselines and
apply atomically, then run post-apply validation. Semantic rename is a closed typed
operation over a Roslyn-resolved symbol and preview fingerprint, not a text-search
tool. Manual buffers remain permissive and show diagnostics without blocking typing
or save.

Microsoft Agent Framework function declarations and calls map through provider-neutral
records before Data Access serializes Ollama or OpenRouter payloads. Tool names,
roles, calls, results, and scopes use enums or semantic single-value records. Remote
cost reservation estimates include tool schemas and accumulated tool traffic, and
each function-call round remains attributed to the goal, role, provider, and model.
Reasoning follows the same boundary: displayable text and opaque structured continuity
data cross as Harness records, with Microsoft protected reasoning content carrying the
provider-specific value between tool rounds. Provider-default reasoning is not disabled
by tool availability. Ollama named tool results and OpenRouter reasoning details are
round-tripped, while streamed completed calls are emitted exactly once.
Every role also receives a typed semantic-context function. Business Logic binds its
query to the goal's active trusted workspace, a closed 1-8 result limit, and strict
remote privacy; Data Access alone owns vector and embedding-provider details. Remote
query embeddings use the same atomic goal reservation and reconciliation boundary.

## Required qualities

- Provider and agent-framework types stop at their owning boundary.
- Roslyn, MSBuild, and LSP types stop at the Data Access implementation boundary;
  diagnostics and semantic operations cross layers only as Harness records and enums.
- Tools are capability-based, path-checked, cancellable, and correlated.
- Workflow transitions and cost reconciliation are atomic where state consistency
  requires it.
- Logs and OTLP telemetry redact secrets and omit model content by default.
- Domain behavior is testable without Git, SQLite, a TUI, or a running model server.
- Architecture rules fail compilation through the analyzer and remain review criteria.
- Linux-specific Presentation and Data Access behavior is selected through focused
  Host-composed capabilities rather than operating-system checks in Business Logic or
  one unrestricted platform service.
