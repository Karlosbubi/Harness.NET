# ADR 003: Agent and provider architecture

- Status: Accepted
- Date: 2026-07-26

## Context

Harness.NET needs multi-step, human-in-the-loop collaboration without allowing an
agent framework or inference provider to define its business and presentation APIs.

## Decision

Use Microsoft Agent Framework as the agent engine. Business Logic owns an agent-role
abstraction for lead, implementer, and reviewer roles; Microsoft types remain behind
that boundary.

Data Access provides Ollama and OpenRouter chat and embedding connectors. Models are
configurable per role. Ollama defaults to `gemma4:latest` for chat and
`embeddinggemma` for retrieval. OpenRouter model lists are discovered dynamically.

OpenRouter is authorized per goal and requires an aggregate cost cap. Normal routing
is the default; a workspace can require no-collection and ZDR routing.

## Consequences

- Provider SDK and Microsoft Agent Framework types are mapped to internal interfaces
  and records before moving upward.
- Provider capabilities and failures must be normalized without hiding useful detail.
- OpenRouter costs are reserved before calls and reconciled from returned usage.
- Model-specific indexes are partitioned and never mixed.

## Alternatives considered

- Owning a complete custom agent loop was rejected in favor of the established .NET
  agent framework.
- Building Business Logic directly around provider SDKs was rejected because it
  would couple workflows and presentation to provider payloads.
