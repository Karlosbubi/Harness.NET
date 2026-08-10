# ADR 015: Stateless MCP connections and agent tool safety

- Status: Accepted
- Date: 2026-08-10
- Extends: [ADR 003](003-agent-and-provider-architecture.md), [ADR 005](005-isolated-goal-execution.md), [ADR 013](013-chat-first-desktop-workflow.md)

## Context

Harness.NET needs first-class Model Context Protocol support for version-aware
documentation and other focused external capabilities. MCP endpoints are independent
trust boundaries: merely configuring one must not grant its tools repository mutation,
desktop control, secrets, or unrestricted side effects. The 2026-07-28 MCP revision
also removes HTTP session state and the initialization handshake, so adopting an older
stateful client lifecycle would create migration debt immediately.

Configurable capabilities have also too often gained a runtime implementation before
their ordinary management surface. A feature is not usable when its configuration is
available only through hand-edited files.

## Decision

Use the stable official C# MCP SDK 2.x and Streamable HTTP. Do not force a legacy
protocol version: the client starts with the stateless `2026-07-28` `server/discover`
flow and may use the SDK's compatibility negotiation for an older endpoint. Harness.NET
does not persist or resume MCP session identifiers. Stdio and stateful HTTP transports
are outside this decision because they add process execution, inherited-environment,
or server-to-client authority that needs a separate review.

Data Access owns SDK transports, protocol objects, connection configuration persistence,
discovery, invocation, cancellation, and disposal. It maps those details into immutable
MCP connection, tool, and result contracts. Business Logic owns which discovered tools
become agent functions and namespaces their model-facing names by connection. MCP SDK
types do not cross the Data Access boundary.

The initial agent policy is deliberately read-only:

- a connection must be explicitly enabled in private Harness.NET configuration;
- a tool must declare `readOnlyHint: true` and must not declare
  `destructiveHint: true`;
- missing or contradictory annotations fail closed;
- rejected tools remain counted and explained in Settings, but are never sent to a
  model or invocable through an arbitrary-name escape hatch;
- advertised catalogs, per-connection eligible tools, descriptions, and schemas are
  bounded before entering agent context; duplicates and excess entries fail closed;
- all invocations remain cancellable and their results flow through the normal agent
  transcript and goal audit boundary.

Adding mutating, open-world, desktop-control, repository, or credential-bearing MCP
tools requires a later typed capability and approval decision. Endpoint enablement is
not that approval.

MCP connections are owned by the searchable Settings window from the first delivered
slice. Settings can add, edit, enable/disable, and remove named endpoints; it shows the
negotiated protocol, eligible/rejected tool counts, failures, and restart state without
performing inference. Values persist only to the private XDG `harness.xml`. Active
connection instances are immutable for the process, so changes clearly require restart.

Going forward, every configurable feature slice must identify its settings owner and
ship its typed management surface, validation, persistence, status, and documentation
with the runtime behavior. A raw configuration key is not considered delivered product
functionality.

## Consequences

- Stateless remote MCP lookup works without session affinity or persisted protocol state.
- MCP SDK churn remains isolated in Data Access.
- Servers with incomplete safety metadata cannot silently expand agent authority.
- Users can manage the feature without editing XML, while environment and command-line
  precedence remain explicit.
- OAuth, write-capable tools, resources, prompts, tasks, Apps, stdio, and stateful
  subscriptions remain future end-to-end slices rather than accidental partial support.

## Alternatives considered

- Exposing every discovered tool was rejected because server annotations are advisory
  metadata, not user authorization for side effects.
- A generic `call_mcp_tool(name, json)` function was rejected because it bypasses schema,
  discovery, and per-tool policy.
- Stdio-first support was rejected because launching third-party processes and choosing
  inherited environment variables need a separate executable and secret policy.
- Hand-edited configuration followed by a later Settings page was rejected because it
  repeats the usability gap this product is trying to eliminate.
