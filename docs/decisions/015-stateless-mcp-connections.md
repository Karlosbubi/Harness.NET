# ADR 015: Stateless MCP connections

- Status: Accepted
- Date: 2026-08-10
- Extends: [ADR 003](003-agent-and-provider-architecture.md), [ADR 005](005-isolated-goal-execution.md), [ADR 013](013-chat-first-desktop-workflow.md)

## Context

MCP is needed for external documentation and focused tools. Configuring an endpoint
must not grant repository writes, desktop control, credentials, or other side effects.
The 2026-07-28 protocol uses stateless HTTP discovery.

## Decision

Use the stable official C# MCP SDK 2.x with Streamable HTTP. Start with the stateless
`2026-07-28` `server/discover` flow and allow SDK compatibility negotiation for older
servers. Do not persist MCP session IDs.

Stdio and stateful HTTP are outside this decision. They need separate process,
environment, server-to-client, and lifecycle policy.

Data Access owns SDK transport, protocol objects, private connection persistence,
discovery, invocation, cancellation, and disposal. It maps results to Harness records.
Business Logic owns tool eligibility and model-facing names. SDK types do not cross
the Data Access boundary.

Initial agent policy:

- the connection is explicitly enabled;
- the tool declares `readOnlyHint: true`;
- the tool does not declare `destructiveHint: true`;
- missing or conflicting annotations reject the tool;
- rejected tools remain visible in Settings but are not sent to a model;
- connection, catalog, description, schema, and result sizes are bounded;
- duplicate or excess entries fail closed;
- invocation is cancellable and recorded through normal run evidence;
- there is no generic `call_mcp_tool(name, json)` function.

Mutating, desktop, repository, credential, prompt, resource, task, Apps, OAuth, stdio,
or subscription support requires a separate end-to-end decision. Endpoint enablement
is not approval for those capabilities.

One narrow exception supports Harness-to-Harness delegation. A connection explicitly
configured as `HarnessControl` may expose selected tools that are not read-only when:

- the endpoint is loopback;
- the initialized server identifies itself as `Harness.NET`;
- a stable client ID and write-only bearer token are configured in Settings, with the
  token stored through Secret Service rather than XML;
- every exposed tool is in that connection's exact allowlist and starts with
  `harness_`;
- destructive and mutating annotations remain visible to the model-facing adapter;
- control tools are available only to the Lead role, while ordinary read-only MCP
  tools retain their existing role behavior; and
- the remote Harness server still enforces application/source/goal/plan/run/operation,
  spending, trust, worktree, and approval identities for every action.

This exception does not add a generic invocation function or treat the bearer token as
repository, spending, plan, or commit approval. Cyclic delegation topologies are not a
supported execution strategy; operators configure a directed controller-to-worker
connection. A worker with no outbound control connection cannot recurse. Durable
delegation-depth metadata and automatic cycle detection remain prerequisites for
enabling arbitrary mutual/self-control.

Settings ships with the transport. It can add, edit, enable, disable, remove, and
refresh named endpoints; show protocol, eligible/rejected counts, failures, and
restart state; and persist private XDG XML. Active connections do not change during a
process lifetime, so edits require restart.

Every future configurable feature must ship typed Settings ownership, UI, validation,
persistence, runtime status, and documentation with the adapter.

## Consequences

- Stateless MCP needs no session affinity or persisted protocol state.
- SDK changes remain in Data Access.
- Incomplete safety metadata does not expand agent authority.
- Configuration does not require hand-editing XML.
- A Harness instance can use a separately configured Harness instance as a typed Lead
  sub-agent without relaxing normal MCP connections.

## Alternatives considered

- Exposing all discovered tools treats advisory metadata as authority.
- A generic invocation function bypasses per-tool schemas and policy.
- Stdio launches third-party processes and inherits environment state before that
  authority is defined.
- Shipping Settings later repeats an existing usability failure.
