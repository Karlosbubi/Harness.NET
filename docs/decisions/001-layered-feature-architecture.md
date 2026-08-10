# ADR 001: Layered feature architecture

- Status: Accepted
- Date: 2026-07-26
- Boundary type shape amended by: [ADR 007](007-semantic-contract-types.md)

## Context

Harness.NET needs stable project boundaries and complete feature delivery.

## Decision

Where sensible, application features use three layers:

```text
Data Access -> Business Logic -> Presentation
```

Project references and contracts move directly upward: Business Logic references
Data Access, and Presentation references Business Logic. Only interfaces, records,
and enums may cross those boundaries. A composition root may reference all
implementations only to configure dependency injection.

Deliver each feature through every required layer. A Roslyn analyzer enforces project
references and public boundary types. Reviewer checks use the same rules.

## Consequences

- Layer internals cannot leak mutable implementation types to another layer.
- Presentation remains free of business logic and persistence details.
- Each feature can be tested end to end.
- Composition must be explicit, and DI registrations must not become a general
  route for bypassing layer direction.
- Compile-time diagnostics enforce project references and contract shape once
  projects exist.

## Alternatives considered

- Horizontal-only delivery delays usable features.
- Unconstrained vertical slices do not preserve layer direction.
