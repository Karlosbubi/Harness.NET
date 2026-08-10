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

## Alternatives considered

- Exposing all discovered tools treats advisory metadata as authority.
- A generic invocation function bypasses per-tool schemas and policy.
- Stdio launches third-party processes and inherits environment state before that
  authority is defined.
- Shipping Settings later repeats an existing usability failure.
