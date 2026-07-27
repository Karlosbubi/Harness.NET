# ADR 001: Layered feature architecture

- Status: Accepted
- Date: 2026-07-26
- Boundary type shape amended by: [ADR 007](007-semantic-contract-types.md)

## Context

Harness.NET needs stable boundaries without organizing delivery into disconnected
horizontal phases. The user's established framework favors familiar layers while
developing useful behavior one feature at a time.

## Decision

Where sensible, application features use three layers:

```text
Data Access -> Business Logic -> Presentation
```

Project references and contracts move directly upward: Business Logic references
Data Access, and Presentation references Business Logic. Only interfaces, records,
and enums may cross those boundaries. A composition root may reference all
implementations only to configure dependency injection.

New behavior is delivered as end-to-end feature slices spanning the necessary layers.
A custom Roslyn analyzer enforces reference direction and boundary type shape; the
reviewer role applies the same rules as review conventions.

## Consequences

- Layer internals cannot leak mutable implementation types to another layer.
- Presentation remains free of business logic and persistence details.
- Feature work can be reviewed and verified end to end.
- Composition must be explicit, and DI registrations must not become a general
  route for bypassing layer direction.
- Compile-time diagnostics enforce project references and contract shape once
  projects exist.

## Alternatives considered

- A conventional horizontal-layer delivery plan was rejected because it delays
  complete, useful workflows.
- An unconstrained vertical-slice architecture was rejected because it would not
  preserve the desired layer direction and contract discipline.
