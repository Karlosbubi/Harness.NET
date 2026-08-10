# ADR 014: Default remote spending

- Status: Accepted
- Date: 2026-08-08

## Context

The earlier mandatory cap made a configured OpenRouter route unusable until the user
entered a per-goal amount. It also mixed monetary policy with model output-token
settings.

## Decision

New goals use one of three typed modes:

- `Unlimited`: default; Harness.NET applies no aggregate monetary ceiling.
- `Capped`: reject a request that would exceed the explicit aggregate USD cap.
- `LocalOnly`: reject every remote model call.

Provider billing and account limits still apply to Unlimited. Every remote request
still requires published pricing, reserves estimated cost before the call, and
reconciles returned cost afterward.

Settings stores the default for new goals. Goal creation shows the same modes and may
override the default before planning starts. Existing approved and running goals keep
their stored authority.

Do not expose or persist user output-token ceilings. Token usage remains evidence.
Unlimited requests omit an application `max_tokens`. For Capped goals, the adapter may
derive a request-local provider maximum from remaining money and published output
price. That value is an enforcement detail, not a user setting.

Plan tasks should be independently useful in order. Role reports state completed,
verified, and remaining work. If the next call cannot fit the cap, persist completed
tasks and evidence, mark partial completion, and offer cap change, retry, or abort.
Do not replay automatically.

The cost store represents Unlimited with its maximum supported micro-USD value.
Business Logic maps that sentinel to the typed mode immediately. Presentation never
shows the sentinel as a dollar amount.

## Consequences

- A configured remote route works for a new goal without entering a cap.
- Hard cost control is opt-in and remains prominent in Settings and goal creation.
- Missing credentials or pricing still fail closed.
- Token limits are not confused with monetary policy.
- Per-call route/privacy confirmation does not replace the goal spend mode.
