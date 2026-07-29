# ADR 010: Docked desktop workbench and real editor documents

- Status: Proposed
- Date: 2026-07-28
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md)

## Context

The first Avalonia slice proves composition, theming, conversation, and the complete
Business Logic workflow boundary. Its primary surface is still a conversation view,
while workspace source, Git state, plans, and evidence open in modal dialogs. That is
not the IDE-style agent workspace described by the product design and is not an
acceptable v1 desktop information architecture.

Harness.NET needs a persistent central document area for real files, diffs, plans,
and evidence. Explorer/search, source control, goal/activity, conversation, and
output belong in resizable tool panels that users can tab, move, hide, and restore.
Implementing a custom docking engine would be a large, specialized effort with
substantial accessibility, focus, floating-window, serialization, and pointer-input
risk.

The Dock project is an MIT-licensed Avalonia docking system with document and tool
docks, `ItemsSource` support, floating windows, themes, dependency-injection support,
and multiple layout serializers. Its current 12.x control packages target Avalonia
12 and .NET 10. The package family is modular, however, and its model integration
packages do not all publish on the same cadence. Compatibility must therefore be
proved against Harness.NET's pinned Avalonia version before adoption is accepted.

## Compatibility checkpoint

The first implementation checkpoint pins `Dock.Avalonia`,
`Dock.Avalonia.Themes.Fluent`, and `Dock.Model.Mvvm` at stable version `12.0.0.2`.
The three packages are published from the same Dock source revision, expose .NET 10
assets, and restore beside Avalonia 12.1.0 and AvaloniaEdit 12.0.0. Dock remains a
Presentation-only dependency; no Dock contract crosses into Business Logic, Data
Access, or the reusable presentation toolkit.

A code-first layout now constructs successfully under Avalonia Headless with the
Dock Fluent theme, and a focused interaction check opens a real bounded
`WorkspaceFileView` in AvaloniaEdit while retaining and activating center document
tabs. The complete Linux x64 lifecycle verifier also passes with the Dock assemblies
inside the self-contained publication. Keyboard/focus traversal, floating-window
ownership, compact layout, assistive-technology behavior, restoration, and measured
multi-document performance remain required before this record can become Accepted.

## Proposed decision

Use Dock as the preferred docking engine, subject to a narrow compatibility and
publish spike. Do not build a Harness-specific drag/drop docking engine unless that
spike records a concrete blocker and this decision is amended.

Prefer Dock's framework-neutral Avalonia model and collection-backed document/tool
templates. Do not introduce ReactiveUI merely to integrate docking: the existing
Rx.NET presentation store remains responsible for reducing asynchronous Business
Logic snapshots into immutable application state. Dock owns only desktop layout,
active-document, and panel interaction state.

The default layout is:

- a central `DocumentDock` containing real workspace file, Git diff, plan, and
  evidence documents;
- workspace navigation and tracked-text search tools at the start edge;
- goal/activity and source-control tools at the end edge;
- conversation and durable run output tools at the bottom edge.

Every document and tool must be backed by a typed Business Logic contract or an
honest empty/error/loading state. The application must never create example files,
diagnostic counts, diffs, test results, branches, progress, or activity. An
unrestricted terminal remains prohibited; the bottom output tool presents bounded
typed execution evidence only.

Source documents initially open read-only from `WorkspaceFileView`. Editing and save
commands become available only when they can target the active approved goal
worktree through typed compare-and-swap mutation contracts. Dirty indicators must
represent actual unsaved content. Closing or switching a dirty document requires an
explicit save/discard/cancel decision.

Persist only a versioned, validated desktop-layout description in Harness.NET's
private XDG state. Never write layout metadata into a user repository. Reject or
repair missing, duplicate, unknown, off-screen, or invalid dockables and retain a
safe default-layout reset. Dock types and serialized graphs remain inside
Presentation; Business Logic and Data Access contracts do not expose them.

## Acceptance before changing status

- Pin a mutually compatible stable package set for Avalonia 12.1 and .NET 10.
- Verify Fluent-theme token overrides, keyboard/focus behavior, screen-reader names,
  scaling, floating-window ownership, and compact-layout fallback.
- Verify headless construction plus Linux x64 single-file publishing.
- Demonstrate open/activate/close for real source and diff documents without fake
  defaults and without leaking Dock types across the Presentation boundary.
- Measure startup and tab-switch behavior with a representative multi-project
  workspace and choose content caching deliberately.

## Consequences

- The modal workspace inspector has been removed. Its real file/search/Git behavior
  now lives in the workbench; incomplete layout and editing behavior remains tracked
  explicitly rather than being represented by placeholder panels.
- Dock dependencies remain confined to `Harness.Presentation.Avalonia`; the reusable
  `Harness.UI.Avalonia` toolkit stays application- and docking-engine-neutral.
- Layout persistence is private application state with the closed, validated schema
  and backup/recovery contract accepted in ADR 011.
- The desktop release gate gains interaction, accessibility, restoration, and
  visual-acceptance coverage for the workbench.

## Alternatives considered

- Extending `AdaptiveWorkspace` into a custom docking system is rejected unless the
  dependency spike identifies an unresolvable blocker; splitter layout alone does
  not provide document tabs, docking targets, floating tools, or restoration.
- Keeping workspace inspection in modal dialogs is rejected because it prevents
  side-by-side source, diff, goal, evidence, and conversation work.
- ReactiveUI integration is not selected because it would duplicate the accepted
  Rx.NET state-reduction architecture.
- Shipping fixed or illustrative IDE panels is rejected because v1 permits neither
  mock content nor non-functional product chrome.
