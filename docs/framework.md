# Framework rules

This file defines the current engineering and agent rules for Harness.NET.

## Engineering baseline

| Area | Rule |
|---|---|
| Product | .NET development for one local developer. |
| Runtime | .NET 10 and modern C#. |
| Correctness | Nullable enabled; compiler warnings are errors. |
| Layers | Data Access → Business Logic → Presentation. |
| Contracts | Only interfaces, records, and enums cross runtime layer boundaries. |
| Domain types | Use enums for closed sets and single-value records for distinct identifiers, paths, hashes, money, units, limits, and validated values. |
| Delivery | End-to-end feature slices with tests and documentation. |
| Configuration | Configurable features ship typed Settings ownership, UI, validation, persistence, and status. |
| State | Prefer immutable data and explicit boundaries. Use LINQ and functional composition where clear. |
| Reactivity | Rx.NET where streams or state reduction benefit from it. |
| Persistence | SQLite, Dapper, explicit SQL, and DbUp migrations. |
| Logging | `ILogger` at DI boundaries; Serilog implementation; optional OTLP. |
| Testing | xUnit, architecture tests, integration tests, deterministic fakes, and opt-in live tests. |
| UI | Avalonia default; Terminal.Gui retained; no web frontend. |
| Process | One process until a measured problem requires separation. |
| Code intelligence | In-process Roslyn behind implementation-neutral contracts; future local LSP remains replaceable. |
| Agent tools | Typed, scoped, and usually on demand; no unrestricted shell or generic execute-by-name tool. |

## Layer rules

```text
Data Access -> Business Logic -> Presentation
```

- Data Access defines upward contracts and internal adapters.
- Business Logic owns policy, state transitions, and use cases.
- Presentation owns UI adaptation and no business rules.
- `Harness.UI.Avalonia` is app-neutral and references no Harness runtime layer.
- Host may reference all layers only to compose dependencies.
- The architecture analyzer enforces direction and contract shape.

Provider SDK, MCP SDK, Roslyn, MSBuild, future LSP, database-driver, debugger, and
platform types stay inside their owning adapters.

## Agent workflow

- Lead inspects the trusted original workspace, communicates with the user, and
  produces a plan.
- A plan contains 1–12 ordered tasks. Each task has a title, objective, file areas,
  and acceptance criteria.
- Plan approval creates an isolated goal branch and worktree.
- Implementer receives one task per call. Writes outside its normalized file areas
  fail before mutation.
- Reviewer inspects the worktree diff and durable evidence but cannot mutate, Build,
  or Test.
- Review findings may cause bounded correction cycles. Reaching the configured limit
  pauses for user input.
- Completed task reports are reconciled after interruption. Uncertain calls are not
  replayed automatically.
- Activity, prompts, model output, tool calls, results, usage, approvals, and evidence
  remain inspectable.

## Trust and authority

- Repository trust is required before project evaluation, analyzer/generator loading,
  Build, Test, Run, or other repository code execution.
- Untrusted repositories allow bounded lexical viewing only.
- Code intelligence never performs implicit Restore.
- Plan approval grants only scoped repository-local operations in the goal worktree.
- Network access, Restore, package changes, destructive operations, budget extension,
  sensitive external access, and Git commit use separate typed authority.
- OpenRouter selection authorizes calls only under the goal’s stored spend mode. It
  never grants repository mutation.
- Commit approval binds goal, run, branch, expected HEAD, full diff SHA-256, message,
  and author. Revalidate immediately before commit.
- Harness.NET does not merge, rebase, cherry-pick, push, or create a pull request
  automatically.

## Model-authored changes

- Use deterministic compiler or IDE operations when they can answer the question.
- Apply C# candidates in memory first.
- Reject a candidate that introduces a compiler error.
- Record introduced warnings and analyzer findings as typed evidence.
- Apply accepted multi-file changes atomically with exact baseline checks.
- Validate the persisted result.
- Unsupported files are `NotApplicable`, not compiler-valid.
- Semantic rename is a Roslyn-resolved preview/fingerprint/apply operation. Agents do
  not emulate rename with text replacement.
- Closed cross-document actions report every affected path before apply. Apply
  recomputes the action and fingerprint, enforces every path grant and baseline,
  writes one atomic batch, and validates the complete persisted set.
- Manual editor buffers remain permissive. Diagnostics do not block typing or save.

## Remote spending

New goals default to `Unlimited` remote spend. Users may select `Capped` or
`LocalOnly` in Settings or before planning.

- `Unlimited` has no Harness aggregate monetary ceiling.
- `Capped` rejects requests whose reservation would exceed the remaining goal cap.
- `LocalOnly` rejects every remote model call.
- Provider pricing must be available before a remote call.
- Reserve estimated cost before the call; reconcile actual returned cost afterward;
  release rejected reservations.
- Reports show active reservations, reconciled cost, released reservations, remaining
  amount, overage, provider, model, operation, and request attribution.
- Monetary values use micro-USD internally and render as USD at the UI boundary.
- Token use is evidence, not a user-configured limit.
- Unlimited calls omit an application token ceiling. Capped calls may derive a
  provider limit from remaining money and output price.
- Paid live tests require explicit user authorization and the smallest practical
  hard-coded monetary ceiling.

## Providers and reasoning

- Microsoft Agent Framework stays behind the Business Logic role interface.
- Data Access owns Ollama and OpenRouter chat and embedding adapters.
- Models are selected per role through provider-neutral records.
- Current production roles require chat and `tools` capability.
- Startup discovers configured catalogs without inference and validates saved routes.
- Provider default reasoning remains enabled for tool calls.
- Reasoning text and protected provider details survive tool loops. Protected details
  return only to the originating provider and are not ordinary assistant output.
- The deterministic structured local-file proposal path disables reasoning.
- OpenRouter credentials use Linux Secret Service with environment fallback and are
  never written to SQLite, logs, checkpoints, XML, or UI snapshots.
- Shipped defaults are defined in `src/Harness.Host/harness.xml`.

## MCP

- Use official MCP C# SDK 2.x and stateless Streamable HTTP discovery.
- Data Access owns transport and SDK mapping. Business Logic owns agent eligibility.
- A connection must be enabled.
- An agent-visible tool must declare read-only and non-destructive behavior.
- Missing or conflicting annotations reject the tool.
- Catalogs, schemas, descriptions, results, and connection counts are bounded.
- Do not expose a generic MCP invocation function.
- Stdio, OAuth, resources, prompts, Apps, tasks, subscriptions, and mutating tools need
  separate feature decisions.
- Inbound control uses the same official stateless Streamable HTTP SDK boundary. It is
  disabled by default, loopback-only, bearer-authenticated, client/tool allowlisted,
  bounded, and audited.
- Inbound MCP adapts existing typed Business Logic commands. It does not grant trust,
  mutation, execution, spend, capture, disclosure, or desktop authority.
- Mutating inbound calls bind the current application-instance identity. Isolated
  evaluation uses temporary paths, volatile secrets, and a disposable fixture.
- Every configurable feature must ship its typed Settings contract, validation,
  persistence, runtime status, and lifecycle actions in its first implementation
  slice. A later Settings retrofit is not an accepted delivery state.

## IDE capability catalog

- Keep a small direct bootstrap toolset for workspace, files, text, Git, project
  status, semantic retrieval, evidence, and toolset discovery.
- Grant additional typed toolsets for one bounded role turn after role, phase, trust,
  context, file-area, and authority checks.
- Requesting a toolset does not invoke it or grant authority.
- Persist toolset grants and calls as workflow evidence.
- Exclude Unreal-specific tools and unrestricted shell execution.
- See [ADR 016](decisions/016-model-accessible-ide-capabilities.md) and the
  [capability map](agent-ide-capabilities.md).

## Framework sources

Framework layers, from general to specific:

```text
global user -> repository guidance -> private workspace overlay -> goal -> task -> role
```

The more specific rule wins unless an earlier rule is locked. A same-level conflict
requires clarification.

- Markdown records intent, conventions, and decisions.
- Typed configuration records enforceable policy.
- Skills record reusable procedures.
- `AGENTS.md` and existing repository documentation are shared repository sources.
- Harness.NET does not create a `.harness` directory.
- Private overlays and summaries stay in Harness.NET storage.
- Promotion shows a diff and lets the user choose global private storage, workspace
  private storage, `AGENTS.md`, or an existing documentation file.

## Persistence and retrieval

- SQLite retains prompts, responses, tool calls/results, approvals, checkpoints,
  usage, summaries, artifacts, vectors, and application preferences until deletion.
- Persist checkpoints after completed workflow boundaries.
- Index only eligible Git-tracked source, project, Markdown, and text configuration.
- Exclude ignored, generated, binary, secret, and oversized content.
- Per rebuild: at most 10,000 entries, 1 MiB per file, and 32 MiB accepted UTF-8 text.
- Activate a new vector generation only after every chunk and vector is durable.
- Partition by provider, model, dimensions, and chunking version.
- Status inspection performs no inference.
- Rebuild and preview show route, privacy, partition, usage, and cost.
- Each role may retrieve 1–8 bounded matches from its active source context.

## Repository tools

- Accept Git repositories with at least one `.slnx`, `.sln`, or `.csproj`.
- Require explicit selection when several entry points exist.
- Each approved goal gets a dedicated branch and worktree.
- Canonicalize and confine every path.
- Typed operations cover bounded file/tree/search, Git status/diff, .NET metadata,
  edits, Build, Test, approved Restore/package work, Roslyn diagnostics/navigation,
  semantic retrieval, and worktree lifecycle.
- LibGit2Sharp handles supported Git operations. A structured Git CLI adapter handles
  worktrees and required gaps.

## Presentation and platform

- Conversation is the primary goal surface. Typed cards show plans, authority,
  progress, partial completion, recovery, evidence, Restore, commit, and handoff.
- Natural language does not authorize consequential work.
- Settings owns ordinary application defaults. Goal overrides remain explicit and
  available on demand.
- Files opened by a user are editable by default in the active trusted original
  workspace. A selected approved goal targets its worktree instead.
- Search, editor, Git, and diff use the same source context.
- Run output shows typed Build/Test/Restore evidence and is not a terminal.
- Visual verification requests one consented XDG Screenshot frame. Captures are
  bounded, goal-scoped, revocable, private, and withheld from remote models unless
  Settings explicitly enables disclosure. No video, background capture, or input
  control is allowed.
- Avalonia is the default UI. Terminal.Gui remains available with `--ui=terminal`.
- Linux is the release gate.
- Presentation owns native UI integration. Data Access owns XDG, filesystem,
  Secret Service, and process integration. Host selects implementations.

## Backup and recovery

- Backups are non-overwriting, integrity-checked v2 ZIP archives.
- Include a consistent SQLite snapshot and optional validated private layout, each
  with size and SHA-256.
- Exclude credentials, logs, caches, worktrees, and repositories.
- Pending schema migrations create and verify a recovery archive before mutation.
- In-app restore stages verified data. The next process start publishes it before
  SQLite initialization and retains rollback data.
- Treat backups as sensitive.

## Observed development environment

Recorded on 2026-07-26:

| Item | Value |
|---|---|
| Ollama | `0.32.3` at `http://192.168.1.101:11434` |
| Shipped chat default | `gemma4:latest`, 8B, `Q4_K_M` |
| Shipped embedding default | `embeddinggemma`; absent during the recorded check |
