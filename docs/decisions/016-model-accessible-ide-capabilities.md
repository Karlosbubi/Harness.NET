# ADR 016: Model-accessible IDE capabilities

- Status: Accepted
- Date: 2026-08-10
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 012](012-roslyn-code-intelligence.md), [ADR 013](013-chat-first-desktop-workflow.md), [ADR 015](015-stateless-mcp-connections.md)

## Context

The editor has deterministic .NET features that agents cannot all use. Rider 2026.2
provides a useful capability inventory, but copying its schemas would import another
IDE’s contracts and authority model. Sending every tool schema on every call would
waste context. Generic terminal and dynamic tool routers would bypass typed authority.

## Decision

### Catalog ownership

Harness.NET owns an IDE capability catalog for Lead, Implementer, and Reviewer. Rider
is a breadth reference only. Built-in tools work without Rider or an external MCP
server.

Each catalog entry records identity, category, description, schema, eligible roles,
source-context and trust requirements, authority class, availability, and module.
Business Logic owns catalog and role policy. Data Access owns Roslyn, Git, process,
debugger, database, profiler, and other adapters. Presentation owns user actions and
status.

External MCP tools remain separately attributed and cannot impersonate built-ins.

### Tool exposure

Keep a small direct bootstrap set:

- bounded file and text inspection;
- workspace, project, Git, and source-context status;
- semantic retrieval and durable evidence where available;
- toolset discovery and request.

A model may request a closed toolset for its next step. Business Logic checks role,
goal phase, trust, source context, delegated paths, and current approvals. Only the
next bounded turn receives the granted typed schemas. Requesting a toolset does not
invoke a tool or grant new authority. Grants expire at the role-call or task boundary
and are recorded.

Do not add a generic `execute_tool(name, arguments)` or “execute Roslyn action” API.

### Authority classes

- `Inspect`: bounded reads of trusted project, source, metadata, diagnostics, symbols,
  Git, evidence, snapshots, and configured sources.
- `TransformPreview`: side-effect-free proposed changes, conflicts, baselines, and
  fingerprint.
- `WorkspaceMutation`: approved goal-worktree writes with delegated paths, exact
  baselines, atomic application, and validation.
- `RepositoryExecution`: Build, Test, Run, Debug, analyzer/generator, notebook, and
  profiler operations after trust, with typed targets and bounded output.
- `ExternalOrSensitive`: database, network, Restore, attach, dumps, screenshots, and
  credentials under their specific privacy and approval rules.
- `DestructiveOrIntegration`: package change, database/debug mutation, commit, and
  similar exact-target decisions.

Broad module enablement never replaces a required approval. Debug evaluation may run
code. Database read-only claims require server-enforced permissions, not model SQL
classification.

### Deterministic operations

Use compiler or IDE services for diagnostics, symbols, navigation, call/type
hierarchy, formatting, imports, refactoring, test discovery, and post-edit checks.
Models do not recreate these operations with prose or text search.

Every semantic multi-file transformation uses preview/fingerprint/apply. Apply checks
context, baselines, delegated paths, and fingerprint, writes atomically, and records
diagnostics and diff evidence. Model patches still pass Roslyn candidate validation.

### Target scope

The catalog may cover:

- workspace, solution/project graph, dependencies, readiness, and diagnostics;
- bounded tree, ranged reads, glob/text/regex/symbol search, open-document context,
  and source/metadata navigation;
- symbols, definitions, references, call/type hierarchy, tests, and quality checks;
- exact patches and closed formatting/refactoring operations;
- asynchronous Build, Test, launch, process lifecycle, and structured output;
- .NET debugger lifecycle and inspection with separately classified evaluation;
- Git roots, status, diff, and existing exact commit flow;
- secret-backed database inspection and bounded queries;
- profiling, dumps, notebooks, and analyzer development;
- portal-mediated visual verification from Task 045.

Exclude Unreal-specific operations, including assets, Blueprints, actors, viewports,
and engine screenshots. Exclude unrestricted terminal commands. A future command
module must identify executable, arguments, environment, working directory, authority,
and output without accepting a shell string.

### Settings and evidence

Settings → Agent tools shows source, category, health, roles, direct/on-demand state,
authority, and unavailable reason. Users may disable optional modules or choose safe
exposure defaults. Settings cannot weaken trust or approval.

Conversation and run evidence show toolset requests, grants, calls, truncation,
cancellation, results, and durable mutation/execution evidence.

## Consequences

- Agents can use deterministic IDE results instead of guessing.
- Prompt size stays bounded as the catalog grows.
- Authority remains per operation instead of becoming one broad mode.
- Debugger, database, profiling, notebook, and other modules remain separate feature
  slices; a catalog row does not mean delivery.

## Alternatives considered

- Rider-compatible schemas couple Harness.NET to another IDE.
- Sending every schema reduces available context and tool-selection accuracy.
- A universal executor is not an authority boundary.
- An unrestricted terminal conflicts with ADR 005.
- Debugger and SQL reads can have side effects and cannot be treated as harmless.
