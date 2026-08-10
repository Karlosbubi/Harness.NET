# ADR 010: Docked desktop workbench

- Status: Accepted
- Date: 2026-07-28
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md)
- Extended by: [ADR 012](012-roslyn-code-intelligence.md), [ADR 013](013-chat-first-desktop-workflow.md)

## Context

The initial Avalonia UI put source, Git, plans, and evidence in dialogs. Harness.NET
needs persistent documents and movable tools. Building a docking engine would add
large focus, accessibility, pointer, floating-window, and persistence costs.

## Decision

Use Dock in `Harness.Presentation.Avalonia`. Pin `Dock.Avalonia`,
`Dock.Avalonia.Themes.Fluent`, and `Dock.Model.Avalonia` at `12.0.0.2`. Dock types do
not cross into Business Logic, Data Access, or `Harness.UI.Avalonia`.

Rx.NET remains the Avalonia state mechanism. Dock owns layout, active-document, and
panel interaction state only.

Default layout:

- central documents for workspace overview, source, Git diff, plans, and evidence;
- Files and search at the start edge;
- Git and goal context at the end edge;
- Conversation, Run output, and Problems at the bottom edge.

Every pane uses production state or an explicit empty, loading, unavailable, or error
state. Do not show sample files, fake diagnostics, fake branch state, or fake progress.
Run output is a typed view of durable Build/Test/Restore evidence, not a terminal.

### Source editing

Files opened by the user are editable when the active original workspace is trusted.
If an approved goal is selected, edits target its worktree. Saves use confined,
exact-baseline compare-and-swap writes. Truncated files, failed reads, untrusted or
inactive workspaces, and rejected paths remain read-only.

Dirty close, tab switch, layout reset, workspace switch, and exit require
save/discard/cancel. External changes require explicit reload, overwrite/recreate, or
keep-editing choice.

This user-edit path does not grant agent authority. Model writes still require an
approved goal worktree, delegated paths, durable evidence, and compiler validation
where applicable.

The Files tree reads bounded, existing, confined Git-tracked paths through Business
Logic. It is not a general filesystem browser.

### Layout state

Persist only the closed, versioned layout schema defined by ADR 011 in private XDG
state. Do not persist transient documents or Dock runtime graphs. Reject unknown,
duplicate, invalid, or off-screen state and provide a default reset.

## Acceptance

Completed checks cover:

- Avalonia 12.1 and .NET 10 package compatibility;
- headless construction and Linux x64 publish;
- real source and diff open, activate, close, refresh, and caching;
- exact-baseline editing and conflict handling;
- keyboard and focus behavior, compact layout, floating ownership, accessible names,
  200% scaling, AT-SPI, and Orca output;
- restart and corrupt-layout fallback;
- deterministic Lead/Implementer/Reviewer edit, Build/Test, review, and exact commit
  through the production UI.

Recorded measurements and screenshots are in
[docked-workbench-2026-07-29.md](../acceptance/docked-workbench-2026-07-29.md).

## Consequences

- Dock remains a Presentation dependency.
- The modal workspace inspector is removed.
- Layout persistence uses a closed private schema.
- Desktop release checks include workbench interaction, accessibility, restoration,
  and scaling.

## Alternatives considered

- A custom docking engine duplicates specialized behavior and testing.
- Modal workspace inspection prevents simultaneous source, diff, evidence, and chat.
- ReactiveUI would duplicate the existing Rx.NET adapter state.
- Fixed illustrative IDE panels would misrepresent application state.
