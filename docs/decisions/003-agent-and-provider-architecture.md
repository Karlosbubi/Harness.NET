# ADR 003: Agent and provider architecture

- Status: Accepted
- Date: 2026-07-26
- Amended: 2026-08-08, 2026-08-10, 2026-08-28

## Context

Agent and provider libraries must not define Business Logic or Presentation APIs.
Model selection, tool compatibility, privacy, and spending need explicit policy.

## Decision

Use Microsoft Agent Framework behind a Business Logic role interface for Lead,
Implementer, and Reviewer. Keep Microsoft types behind that interface.

Data Access owns Ollama and OpenRouter chat and embedding adapters. Provider payloads
map to Harness records before crossing the boundary. Models are configurable per
role. OpenRouter catalogs are discovered at runtime.

OpenRouter authorization is goal-scoped. [ADR 014](014-default-remote-spend-policy.md)
defines unlimited, capped, and local-only modes. A workspace may require
no-collection and zero-data-retention routing.

### Model discovery and selection

- Data Access normalizes provider-declared capabilities.
- Business Logic defines role requirements.
- Current production roles require chat and `tools` support.
- Selection commands reject incompatible routes even if the UI is bypassed.
- Interactive startup discovers every configured catalog without inference.
- Discovery reports provider failures and invalid saved routes. It does not authorize
  spending or replace a saved route.
- Every model selector searches the complete discovered catalog, then applies the
  required role filter.
- Catalog visibility, role compatibility, and spending authority are separate.
- Plan generation selects and persists an explicit Lead route before the call.

### Provider Settings

Settings exposes each Ollama and OpenRouter endpoint, chat and embedding defaults,
embedding dimensions, and timeouts. It writes validated changes to the private XDG
configuration override. Existing unrelated settings remain unchanged.

Provider instances and index partition identity do not change during a process
lifetime. Provider configuration changes therefore require restart.

OpenRouter credentials are write-only. Settings writes them to Linux Secret Service
and reports only configured, missing, or unavailable state. XML, SQLite, logs, and UI
snapshots do not contain the value. Saving a credential does not authorize inference.

### Output and retry

Token counts are evidence, not user-configured limits. Capped remote calls may derive
a provider output boundary from published pricing and remaining money. Unlimited
calls omit an application token ceiling.

A failed role retry may change the model, add guidance, do both, or do neither.
Guidance does not expand file, tool, mutation, or spending authority.

### Reasoning and tool calls

Tool availability does not by itself disable model reasoning. The role policy
controls ordinary calls: `ProviderDefault` omits a reasoning override and `Disabled`
requests none. The deterministic structured local-file proposal path always requests
no reasoning because it expects a small machine-readable result.

Ordinary role defaults also own a portable two-state reasoning policy:
`ProviderDefault` or `Disabled`. Fresh routes retain `ProviderDefault` because forcing
reasoning off can suppress structured planning or a model's action/tool protocol. This
quality-preserving default applies to local and remote providers. Existing persisted
routes also migrate to `ProviderDefault`, so an upgrade does not silently change
established behavior. Settings exposes and persists the policy beside each role model,
letting measured local latency justify an explicit `Disabled` choice. Goal-specific
model selection changes the model route, while the role reasoning policy remains in
force. Provider-specific effort levels are not exposed as portable choices because
support and semantics differ by model.

Harness carries reasoning text and optional provider-specific JSON through typed
records. Business Logic maps opaque JSON to Microsoft protected reasoning content and
returns it unchanged on the next call to the same provider. It is not rendered as
ordinary assistant output.

Ollama receives prior assistant `thinking` and named tool results. OpenRouter receives
prior `reasoning_details`. Stream adapters accumulate partial tool calls and emit each
completed call once, including when a provider sends a later usage-only chunk.

## Consequences

- Provider SDK and Agent Framework types remain inside their adapters.
- Capability and failure mapping is required for every provider.
- OpenRouter reserves cost before a call and reconciles returned cost afterward.
- Semantic indexes remain partitioned by provider and model configuration.
- Opaque reasoning state is scoped to the active provider conversation.
- Every role preserves provider planning and action/tool behavior by default, while an
  explicit per-role setting can trade deliberation for local responsiveness.
- Typed tools, workspace scope, privacy, approval, monetary controls, and Roslyn
  validation are unchanged by reasoning support.

## Alternatives considered

- A custom agent loop would duplicate framework behavior.
- Provider types in Business Logic would couple workflows to wire formats.
