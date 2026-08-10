# ADR 002: TUI-first non-web presentation

- Status: Accepted
- Date: 2026-07-26

## Context

Harness.NET needs an initial UI without tying Business Logic to that UI. Web UI is
outside the product scope.

## Decision

The first UI uses Terminal.Gui v2. Its layout adapts to narrow terminals.

Presentation remains replaceable. Avalonia or gRPC may be added without changing
Business Logic. Web frontends are excluded.

## Consequences

- Business use cases must be callable through presentation-neutral interfaces and
  record contracts.
- The TUI is an adapter and composition participant, not the owner of orchestration.
- Terminal input, layout, and cancellation require tests.
- The initial release is Linux first while contracts remain portable.
- Avalonia and gRPC remain options, not dependencies added in advance.

## Alternatives considered

- Web UI is outside scope.
- Avalonia-first was deferred for the initial iteration.
- API-only does not provide the required first-party UI.
