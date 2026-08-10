# ADR 007: Semantic contract types

- Status: Accepted
- Date: 2026-07-27
- Amends: [ADR 001](001-layered-feature-architecture.md)
- Extended by: [ADR 012](012-roslyn-code-intelligence.md)

## Context

Distinct identifiers, paths, money, limits, roles, and states cross layer boundaries.
Using the same primitive type for each makes accidental interchange possible.

ADR 001 originally restricted public layer contracts to interfaces and records.
Closed semantic sets are represented more clearly and safely by enums, so that
restriction needs a narrow amendment.

## Decision

Use semantic types as the default throughout new and changed code:

- Use enums for closed, stable sets of named states, kinds, roles, and operations.
- Use immutable single-value records for identifiers, paths, hashes, validated
  values, monetary amounts, units, limits, and other primitives whose domain meaning
  should prevent accidental interchange.
- Use records to compose domain contracts from those semantic values.
- Permit interfaces, records, and enums as public layer-boundary types. Concrete
  service implementations and ordinary classes remain internal.
- Map semantic types explicitly at persistence, provider, process, and presentation
  edges. Validate undefined enum values and invalid single-value records at the
  boundary that accepts them.

Apply the rule to new and changed code. Do not rewrite stable contracts only for type
style. Keep a primitive when it has no separate domain meaning. Do not add wrappers
that add no distinction or validation.

## Consequences

- APIs make invalid or ambiguous values harder to pass accidentally.
- Exhaustive enum handling makes workflow changes visible to the compiler and tests.
- Persistence and serialization adapters must map semantic values deliberately.
- Some closely related types are duplicated across layers to preserve dependency
  direction rather than leaking a lower-layer contract upward.
- The architecture analyzer must accept public enums in runtime layer projects.

## Alternatives considered

- Continuing to use primitive strings everywhere was rejected because runtime
  validation is weaker and intent is less visible.
- Sharing one domain assembly across all layers was rejected because it would weaken
  the accepted direct layer direction.
- Rewriting every existing contract would add risk without changing behavior.
