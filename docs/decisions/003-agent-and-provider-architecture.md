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

OpenRouter is authorized per goal. [ADR 014](014-default-remote-spend-policy.md)
replaces the former mandatory-cap default with typed unlimited, capped, and local-only
modes. Normal routing is the default; a workspace can require no-collection and ZDR routing.

### Capability-qualified routing amendment (2026-08-08)

Model availability and role compatibility are different facts. Data Access continues
to normalize provider-declared model capabilities; Business Logic owns the role
requirements. Every current production role is instantiated with typed tools, so a
model must be a chat model and declare `tools` support to qualify for Lead,
Implementer, or Reviewer. The role matrix remains explicit even where requirements
currently coincide, allowing later role-specific requirements without moving policy
into Presentation. Default and goal-specific selection commands reject incompatible
models even if a caller bypasses the UI.

Interactive startup performs catalog discovery for every configured provider without
inference. The resulting immutable snapshot includes provider availability, compatible
roles, and validation issues for persisted defaults. Avalonia Settings and the TUI
reuse that snapshot so selection is immediately populated; an explicit refresh may
replace it. Discovery failure is reported per provider and does not authorize remote
spending or silently replace a saved route.

OpenRouter is a first-class Settings provider beside Ollama, with its remote access
class, configured default, discovered compatible-model count, pricing availability,
and discovery failure visible. Provider credentials remain owned by Secret Service or
the configured environment boundary and are never rendered back to the UI.

Plan generation includes an explicit compatible Lead-model choice. It defaults to the
effective configured Lead route (or an existing goal Lead override), persists the
chosen goal-bound route before the Lead call, and retains the spend-policy and
explicit-confirmation requirements for OpenRouter. Implementer and Reviewer keep
their effective role routes until separately overridden.

### Interactive provider configuration amendment (2026-08-08)

The Model providers Settings page exposes the effective configuration of every named
Ollama and OpenRouter module: endpoint, chat and embedding defaults, embedding
dimensions, connect timeout, and request timeout. Validated edits are written as a
minimal private XDG `harness.xml` provider override. Existing unrelated user
configuration is preserved. Provider instances, routing, conversation clients, and
semantic-index partition identity remain immutable for a running process, so these
edits explicitly require restart and never partially mutate an active route.

OpenRouter credentials are write-only on this surface. The secret value crosses a
typed Presentation-to-Business-Logic command and is written to the configured Linux
Secret Service reference; snapshots expose only whether a credential resolves and
the configured environment-fallback name. XML, SQLite, logs, and UI state never
contain or echo the secret. Replacing a credential does not authorize inference or
spending. Because providers resolve credentials per operation, a replacement against
the active reference can be verified by an explicit catalog refresh without restart.

### Complete catalog presentation amendment (2026-08-08)

Every model-selection surface receives the complete discovered chat catalog from all
configured providers, then applies only the Business Logic role-capability constraint
appropriate to that selection. A local-only goal no longer hides compatible remote
models: it may inspect and search them, but selecting one remains blocked by the
goal spend mode and explicit-confirmation boundary.

Avalonia uses one shared searchable typed model picker for plan generation, failed-role
retry, per-goal routing, and role defaults; the conversation model control is searchable
as well. Terminal model lists provide provider/model filtering. Search matches provider,
model, access class, and advertised capabilities and never turns arbitrary entered text
into a route. Catalog visibility, route compatibility, and spending authority remain
separate facts.

### Long-output and retry amendment (2026-08-08)

ADR 014's monetary-only execution amendment supersedes user-configured role output
maxima. Token counts remain usage evidence. Capped remote calls derive any provider
request boundary from published pricing and remaining money; unlimited calls omit an
application output ceiling.

Explicit failed-role retry accepts optional additional guidance. An empty guidance field
means to retry the same bounded work with the selected route, allowing
an unchanged retry or model-only change. Supplying guidance augments the prompt without
expanding tool, file-area, mutation, or spending authority.

### Provider reasoning continuity amendment (2026-08-10)

Reasoning and typed tools are independent model capabilities. Harness must not disable
provider reasoning merely because a request offers tools. Provider-default reasoning is
therefore represented explicitly and omitted from the wire request; only the bounded
structured local-file proposal path requests no reasoning because that path needs a
small deterministic payload rather than agent deliberation. A provider-neutral closed
reasoning-effort set supports deliberate overrides without leaking provider SDK types.

Reasoning continuity is part of the tool protocol. Data Access normalizes displayable
reasoning text and may carry provider-specific structured reasoning as an opaque typed
JSON value. Business Logic maps that value to Microsoft Agent Framework protected
reasoning content so it can be returned unchanged on the next request to the same
provider. Opaque reasoning is not rendered as ordinary assistant output. Ollama receives
its assistant `thinking` text and named tool results; OpenRouter receives its original
`reasoning_details`. Completed streamed tool calls are accumulated and emitted once,
even when a provider sends a later usage-only completion chunk.

These changes do not weaken typed-tool eligibility, workspace scope, privacy routing,
approval, or monetary reservation. They remove a protocol restriction while retaining
the provider-neutral boundary and deterministic validation of every mutation.

## Consequences

- Provider SDK and Microsoft Agent Framework types are mapped to internal interfaces
  and records before moving upward.
- Provider capabilities and failures must be normalized without hiding useful detail.
- OpenRouter costs are reserved before calls and reconciled from returned usage.
- Model-specific indexes are partitioned and never mixed.
- Provider reasoning state remains scoped to the active provider conversation and is
  mapped through Harness records rather than exposing provider payload types upward.

## Alternatives considered

- Owning a complete custom agent loop was rejected in favor of the established .NET
  agent framework.
- Building Business Logic directly around provider SDKs was rejected because it
  would couple workflows and presentation to provider payloads.
