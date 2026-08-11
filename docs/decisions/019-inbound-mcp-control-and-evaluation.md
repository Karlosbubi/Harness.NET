# ADR 019: Inbound MCP control and evaluation

- Status: Accepted
- Date: 2026-08-11
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 013](013-chat-first-desktop-workflow.md), [ADR 015](015-stateless-mcp-connections.md), [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 017](017-portal-visual-verification.md)

## Context

Harness.NET can consume MCP tools but cannot expose its own state and typed actions to
an evaluation agent. Filesystem scripts miss live buffers, goal state, evidence and
application identity. Generic remote control would bypass Harness authority and could
control the developer's desktop or repositories.

## Decision

### Ownership and transport

Harness.NET exposes an optional stateless Streamable HTTP MCP server through the
official C# SDK 2.x. Data Access owns SDK and HTTP types, transport, authentication
adapter, connection accounting and protocol mapping. Business Logic owns the closed
tool catalog, exact application/source identities, eligibility, approvals, commands,
audit records and result contracts. Presentation reports status and adapts explicit
developer-visible focus actions. SDK types do not cross Data Access.

The server is disabled by default, binds only to an IP loopback address, uses one
configured endpoint path, and does not persist MCP session IDs. Each request requires
a current bearer token. The token is generated with cryptographic randomness and stored
through the Secret Service. Settings may copy a newly rotated token once, only after
an explicit developer action; Harness does not retain a displayable copy or reveal an
existing token. Rotation revokes existing clients immediately. Authentication is not
authorization.

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

Harness.NET never exposes generic shell, SQL, click/type, coordinates, global input,
desktop control, arbitrary command names, silent screen capture, secret reads or raw
dependency-injection service dispatch.

### Settings and lifecycle

Settings ships with the first server slice. It owns enablement, mode, loopback
endpoint, authentication status and rotation, client and tool allowlists, per-tool
approval, request timeout and result limits, audit retention, health, active clients,
disconnect, reset and restart state. Safe validation rejects non-loopback endpoints,
unknown tools, missing authentication and normal paths in evaluation mode.

Startup validates settings before binding. Disable, token rotation, client revocation
and shutdown stop accepting new requests immediately and cancel bounded in-flight work.
The workbench shows a persistent active-control indicator while the endpoint is live.

## Consequences

- External agents can dogfood Harness through the same typed boundaries as its UI and
  internal agents.
- Evaluation is reproducible without exposing the developer environment.
- MCP adds transport, observability and lifecycle work but grants no new authority.
- Adding a tool requires its normal Business Logic slice, Settings policy, metadata,
  deterministic tests and audit mapping first.

## Alternatives considered

- gRPC alone does not provide standard agent discovery and invocation.
- A universal execute-by-name endpoint collapses policy into string dispatch.
- Desktop automation can escape Harness and cannot establish source or approval state.
- Reusing normal configuration for tests risks credentials and repository mutation.
