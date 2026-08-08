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
still apply. Every request retains its positive output-token maximum, published-price
preflight, durable estimated-cost reservation, and reconciled actual-cost evidence.

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
- Existing per-call model confirmation remains a route/privacy disclosure, not the
  mechanism that grants aggregate spend authority.
