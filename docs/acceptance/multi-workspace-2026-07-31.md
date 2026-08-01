# Multi-workspace acceptance — 2026-07-31

## Accepted behavior

- Registered repositories remain durable Harness.NET workspaces with independent
  trust, entry point, branch, and goal state. Exactly one workspace is active; selecting
  another registered workspace does not require inspecting or registering it again.
- Avalonia identifies the active workspace explicitly in the workspace manager and
  shows each repository root, branch, and trust state. The selected active row cannot
  be switched to redundantly.
- Before Avalonia changes the active workspace, every open source document goes through
  the existing save, discard, or cancel flow. Cancel leaves the current workspace and
  dirty document active. Accepted documents close before the workspace service changes,
  so an editor from one Roslyn context is never presented under another workspace.
- During an application session, Avalonia remembers the selected goal independently for
  each workspace. Returning to a workspace restores that goal and its plan, model
  selections, cost, workflow, commit approval, and capability approval views when the
  goal still exists. Persisted goals remain the authority across application restarts.
- The TUI workspace manager marks the active repository with `[ACTIVE]` and displays its
  root, branch, and trust state. Its workspace frame title also names the active
  repository, while the existing registered-workspace selector performs the switch.

## Deterministic checks

- An Avalonia store test selects distinct goals in two workspaces and proves that each
  goal context is restored across repeated switches.
- A headless production-control test dirties an editable source document and proves that
  cancel prevents a workspace transition while discard permits it and closes the old
  document.
- Terminal formatter tests prove active and inactive rows expose unambiguous status and
  repository identity.

No model provider, network operation, remote language server, or paid check is used by
this acceptance slice.
