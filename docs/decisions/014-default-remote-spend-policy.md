# ADR 014: Default remote-spend policy

- Status: Accepted
- Date: 2026-08-08

## Context

Requiring a positive per-goal cap before any OpenRouter call made a configured remote
route unexpectedly unusable during ordinary chat-first work. Model selection and
planning became a multi-dialog recovery exercise even when the user had deliberately
configured a provider and preferred remote model.

This replaces only the default-authorization portions of ADR 003, ADR 013, and the
accepted framework. Provider credentials, pricing visibility, usage attribution,
reservation reconciliation, and explicit model selection remain unchanged.

## Decision

New goals authorize unlimited remote-model spend by default. “Unlimited” means no
application-enforced aggregate monetary ceiling; provider billing and account limits
still apply. Every request retains its published-price preflight, durable estimated-cost
reservation, and reconciled actual-cost evidence.

### Monetary-only execution amendment (2026-08-08)

Harness.NET no longer exposes, persists, or accepts user-configured model output-token
ceilings. Token usage remains observable evidence, not an execution policy. Unlimited
goals omit `max_tokens` and allow the provider/model to apply its native output behavior.
For a Capped goal, the OpenRouter adapter may derive a request-local provider maximum
from the currently remaining micro-USD budget and published output price. That value is
an implementation detail of enforcing the monetary cap, not a user token setting.

Lead plans order independently useful end-to-end slices so each completed prefix is a
coherent partial result. Role prompts require incremental validation and an explicit
completed/verified/remaining report when execution stops. If the monetary boundary
rejects the next role call, the workflow records `PartiallyCompleted` above its durable
`NeedsDirection` checkpoint, preserves completed tasks and evidence, and offers cap
extension/removal, explicit retry, or abort without automatic replay.

Cost control is a prominent opt-in with three typed modes:

- **Unlimited** — the convenient default for new goals;
- **Capped** — fail closed when an explicit aggregate USD limit would be exceeded;
- **Local only** — reject every remote model call.

Settings owns the persisted default for newly created goals. Goal creation shows the
same three choices and allows an explicit override. Draft goal settings may change the
mode before planning begins. Upgrade maps existing pre-planning goals that still carry
the old implicit local-only default to Unlimited; approved and running goals retain
their stored authority.

The existing positive integer budget column remains the cost-store boundary. Unlimited
is represented there by the maximum supported micro-USD amount and is mapped immediately
to the typed `Unlimited` mode above Business Logic. Presentation must never render that
storage sentinel as a dollar cap.

## Consequences

- A configured OpenRouter route works for a newly created goal without first entering
  an arbitrary cap.
- Users who want hard cost control must deliberately opt into a cap or local-only mode;
  both choices remain visible in Settings and goal creation.
- Remote calls still fail closed when credentials or pricing are unavailable.
- Monetary caps remain enforceable without making users guess a model-specific token
  budget; unlimited goals are not accidentally constrained by an application ceiling.
- Existing per-call model confirmation remains a route/privacy disclosure, not the
  mechanism that grants aggregate spend authority.
