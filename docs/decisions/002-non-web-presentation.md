# ADR 002: TUI-first non-web presentation

- Status: Accepted
- Date: 2026-07-26

## Context

Harness.NET needs an initial interaction surface but should support multiple .NET
frontends without moving business behavior into any one frontend. Web-based user
interfaces are contrary to the desired development framework.

## Decision

The first iteration uses Terminal.Gui v2 as an adaptive full-screen terminal user
interface. Its layout contains workspace/goals, transcript/activity,
plan/diff/evidence, and composer/status regions. Side regions collapse on narrow
terminals.

Presentation remains modular so that an Avalonia application or an API, likely
gRPC, can be added without changing Business Logic. Web-based frontends are excluded.

## Consequences

- Business use cases must be callable through presentation-neutral interfaces and
  record contracts.
- The TUI is an adapter and composition participant, not the owner of orchestration.
- Terminal interaction and cancellation behavior become first-iteration quality
  concerns.
- The initial release is Linux first while contracts remain portable.
- Avalonia and gRPC remain options, not dependencies added in advance.

## Alternatives considered

- A web frontend was rejected by preference.
- Avalonia-first was deferred because the TUI is the chosen first iteration.
- API-only was deferred because an interactive first-party workflow is required.
