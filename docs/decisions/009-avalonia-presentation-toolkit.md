# ADR 009: Avalonia presentation toolkit and desktop adapter

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 001](001-layered-feature-architecture.md), [ADR 002](002-non-web-presentation.md)
- Extended by: [ADR 010](010-docked-desktop-workbench.md)

## Context

The presentation-neutral Business Logic contracts are ready for a graphical adapter.
The desktop UI also needs reusable theming, accessibility, and adaptive-layout
infrastructure without coupling those concerns to Harness.NET orchestration.

## Decision

Add `Harness.UI.Avalonia` as an app-neutral toolkit layered on Avalonia and
`Harness.Presentation.Avalonia` as the Harness-specific adapter. The toolkit has no
reference to another Harness runtime project. The adapter references Business Logic
and the toolkit; Host remains the composition root.

Toolkit controls and infrastructure may be public classes because they are a UI
framework API rather than application layer contracts. This exception applies only
to `Harness.UI.Avalonia`; Presentation continues to expose only interfaces, records,
and enums. The analyzer enforces the distinct toolkit layer and reference direction.

Use Rx.NET inside the Avalonia adapter to reduce commands, asynchronous streams, and
snapshots into immutable view state. Do not introduce shared Business Logic event
streams until another adapter requires them.

Avalonia becomes the default interactive frontend. The Terminal.Gui adapter remains
available through `--ui=terminal`; operational non-UI modes remain unchanged.

Extend Avalonia's controls and Fluent theme with semantic Harness resources. Persist
a semantic selected-theme identifier and load bounded declarative color-token XML
from XDG configuration. Never load user AXAML, code, external resources, or fonts.

The initial `AdaptiveWorkspace` and modal inspection surfaces are bootstrap
infrastructure, not the final desktop information architecture. The v1 desktop
requires a central multi-document editor workbench and movable tool panels as
specified by ADR 010.

## Consequences

- Avalonia-specific APIs remain outside Business Logic.
- Theme and accessibility behavior is reusable by future Harness desktop surfaces.
- The toolkit requires its own public-API and architecture tests.
- Missing or invalid user themes fall back safely without losing the preferred ID.
- The TUI remains supported but is no longer the default interactive adapter.

## Alternatives considered

- Putting reusable controls in the application adapter was rejected because it would
  mix app orchestration with UI infrastructure.
- ReactiveUI was deferred because Rx.NET plus immutable adapter state is sufficient.
- Loading arbitrary AXAML was rejected because markup can instantiate executable
  types and cannot be treated as a safe palette format.
