# ADR 009: Avalonia presentation toolkit and desktop adapter

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 001](001-layered-feature-architecture.md), [ADR 002](002-non-web-presentation.md)
- Extended by: [ADR 010](010-docked-desktop-workbench.md), [ADR 013](013-chat-first-desktop-workflow.md)

## Context

Harness.NET needs a graphical adapter and reusable Avalonia theme, accessibility, and
layout code without moving application behavior into the toolkit.

## Decision

Add `Harness.UI.Avalonia` as an app-neutral toolkit layered on Avalonia and
`Harness.Presentation.Avalonia` as the Harness-specific adapter. The toolkit has no
reference to another Harness runtime project. The adapter references Business Logic
and the toolkit; Host remains the composition root.

Toolkit controls and infrastructure may be public classes because they are a UI
framework API rather than application layer contracts. This exception applies only
to `Harness.UI.Avalonia`; Presentation continues to expose only interfaces, records,
and enums. The analyzer enforces the distinct toolkit layer and reference direction.

Use Rx.NET inside the Avalonia adapter to produce immutable view state from commands,
streams, and snapshots. Do not add shared Business Logic streams until another
adapter needs them.

Avalonia becomes the default interactive frontend. The Terminal.Gui adapter remains
available through `--ui=terminal`; operational non-UI modes remain unchanged.

Extend Avalonia's controls and Fluent theme with semantic Harness resources. Persist
a semantic selected-theme identifier and load bounded declarative color-token XML
from XDG configuration. Never load user AXAML, code, external resources, or fonts.

Use Avalonia's platform storage provider for desktop file and folder selection. The
Presentation adapter may translate a platform-selected local folder into the existing
workspace-inspection request, but storage-provider types and picker lifecycle remain
inside Presentation. Keep an editable path fallback for desktops without a folder
picker and never treat selection as repository trust or execution approval.

ADR 010 replaces the initial `AdaptiveWorkspace` and modal inspector with a
multi-document workbench and movable tools.

## Consequences

- Avalonia-specific APIs remain outside Business Logic.
- Theme and accessibility behavior is reusable by future Harness desktop surfaces.
- The toolkit requires its own public-API and architecture tests.
- Missing or invalid user themes fall back safely without losing the preferred ID.
- Native picker availability is a Presentation capability; repository inspection,
  registration, and trust still cross the existing typed Business Logic boundary.
- The TUI remains supported but is no longer the default interactive adapter.

## Alternatives considered

- Putting reusable controls in the application adapter was rejected because it would
  mix app orchestration with UI infrastructure.
- ReactiveUI was deferred because Rx.NET plus immutable adapter state is sufficient.
- Loading arbitrary AXAML was rejected because markup can instantiate executable
  types and cannot be treated as a safe palette format.
