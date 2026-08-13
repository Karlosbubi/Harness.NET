# ADR 019: Inbound MCP control and evaluation

- Status: Accepted
- Date: 2026-08-11
- Amended: 2026-08-13
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 013](013-chat-first-desktop-workflow.md), [ADR 015](015-stateless-mcp-connections.md), [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 017](017-portal-visual-verification.md)

## Context

Harness.NET can consume MCP tools but cannot expose its own state and typed actions to
an evaluation agent. Filesystem scripts miss live buffers, goal state, evidence and
application identity. Generic remote control would bypass Harness authority and could
control the developer's desktop or repositories.

## Decision

### Ownership and transport

Harness.NET exposes an optional stateless Streamable HTTP MCP server through the
official C# SDK 2.x. Data Access owns SDK and HTTP types, transport, connection
accounting and protocol mapping. Business Logic owns the closed
tool catalog, exact application/source identities, eligibility, approvals, commands,
audit records and result contracts. Presentation reports status and adapts explicit
developer-visible focus actions. SDK types do not cross Data Access.

The server is disabled by default, binds only to an IP loopback address, uses one
configured endpoint path, and does not persist MCP session IDs. It intentionally has
no bearer-token or other inbound authentication. This endpoint is a local development
interface, like a local IDE integration, and is not suitable for exposure through a
non-loopback proxy or port forward. Loopback validation is therefore a hard startup
condition rather than a configurable default.

Each request may carry a configured client identifier. When the client allowlist is
empty, a missing identifier is recorded as `local-anonymous`; a non-empty allowlist
requires an exact identifier match. The identifier provides audit attribution and
selects a client allowlist entry; it is not an identity proof
and another process running as the same OS user can spoof it. Tool allowlists,
approvals, workspace trust, source-context checks, typed authority and bounded results
remain the authorization boundaries. Harness must not describe the client identifier
or its allowlist as authentication.

An application-instance identifier is regenerated on process start. Results also
identify the active workspace, source context, goal/run/session and data freshness
where applicable. Clients must not infer continuity from an endpoint alone.

### Modes

Normal dogfooding mode adapts the running application. It retains all workspace trust,
goal worktree, baseline, cost, capture, disclosure, execution and approval checks.
MCP cannot create a broader command path.

Isolated evaluation mode uses a separate temporary configuration and database root,
a disposable fixture repository and worktrees, fake providers by default or an
explicitly selected Ollama provider, and no stored credentials or normal workspace.
Reset destroys only that identified evaluation state. Evaluation snapshots may expose
Harness-owned rendered frames and accessibility identities. Actions may activate only
allowlisted Harness accessibility identities in that isolated instance.

An evaluator may also pre-seed the disposable fixture repository inside that same
evaluation root. Registration resolves exactly one tracked `.slnx` or `.sln` entry
point, or exactly one tracked `.csproj` when no solution is present. Missing or
ambiguous entry points fail closed. This does not accept an external repository path,
change normal workspace registration, or allow reset to address anything outside the
identified temporary fixture root.

### Tool policy

Tools are individually allowlisted and declare read-only, mutation, execution,
sensitive, destructive and idempotency metadata. Business Logic checks mode, client,
active source context, role, trust, approval and limits before dispatch. Every call and
denial is attributed to a client and written as bounded inspectable audit evidence.

Initial tools cover health and application identity plus bounded inspection of the
active workspace, tracked files, project graph, Git state and later the existing
document, Roslyn, goal, evidence, Build/Test, accessibility and consented visual
contracts as those typed facades are registered. An unavailable capability is reported
as unavailable; it is not approximated through a generic command.

Goal lifecycle tools adapt the same goal, model-routing, workflow, acceptance, and
commit services used by the desktop UI. They cover creation, draft settings, model
discovery and selection, planning, retry, resume, abort, plan decisions, budget
extension, accepted-change preview, commit approval, and commit decision. Calls carry
the current application instance and exact goal, plan, run, approval, baseline, or
operation identities required by the underlying command. Enabling an MCP tool does
not approve a plan, spending increase, worktree mutation, or commit.

Planning, retry, and resume can outlive an HTTP request timeout, especially with local
models. These commands start one bounded in-process operation per goal and return its
identity immediately. Durable workflow checkpoints remain the source of truth and are
polled through goal inspection. A separate exact-identity cancellation command stops
the active call; shutdown cancels active operations and the workflow records its
uncertain boundary. This coordinator is not a general job runner and accepts no
delegate, tool name, prompt, executable, or command string.

Collection tools are server-filtered and continuation-paged. Goal inspection accepts
an exact goal identity and does not duplicate workflow prompts or durable evidence;
those remain available through the separately paged evidence tool. Model discovery
accepts provider, role, and text filters before paging. A large provider catalog or
goal history must not become one model-context-sized response.

Harness.NET never exposes generic shell, SQL, click/type, coordinates, global input,
desktop control, arbitrary command names, silent screen capture, secret reads or raw
dependency-injection service dispatch.

### Settings and lifecycle

Settings ships with the first server slice. It owns enablement, mode, loopback
endpoint, client and tool allowlists, per-tool
approval, request timeout and result limits, audit retention, health, active clients,
disconnect, reset and restart state. Safe validation rejects non-loopback endpoints,
unknown tools and normal paths in evaluation mode.

Startup validates settings before binding. Disable, client revocation and shutdown
stop accepting new requests immediately and cancel bounded in-flight work.
The workbench shows a persistent active-control indicator while the endpoint is live.

## Consequences

- External agents can dogfood Harness through the same typed boundaries as its UI and
  internal agents.
- Evaluation is reproducible without exposing the developer environment.
- MCP adds transport, observability and lifecycle work but grants no new authority.
- Any local process can attempt to call the endpoint or spoof an allowlisted client
  identifier. The feature is appropriate only for a single-user loopback development
  environment; stronger multi-user or remote isolation requires a new decision.
- Adding a tool requires its normal Business Logic slice, Settings policy, metadata,
  deterministic tests and audit mapping first.

## Alternatives considered

- gRPC alone does not provide standard agent discovery and invocation.
- A universal execute-by-name endpoint collapses policy into string dispatch.
- Desktop automation can escape Harness and cannot establish source or approval state.
- Reusing normal configuration for tests risks credentials and repository mutation.
