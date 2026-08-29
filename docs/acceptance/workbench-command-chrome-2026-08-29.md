# Workbench command and chrome acceptance — 2026-08-29

This record covers the presentation UX portion of Task 060 slice 060.8.

- The fixed command palette and the closed keybinding catalog share one typed
  `KeybindingCommand` identity per action. A runtime validator rejects an unbound,
  duplicate, or missing palette mapping before the palette opens.
- `WorkbenchPaletteCatalog` maps 45 core actions from Files, Git changes, branches,
  tags, worktrees, stashes, remotes, history, conflicts, Run output, Problems, and
  goal context. Invoking an action restores its panel and selects its Git section
  before running the existing typed operation.
- Commands without a safe universal gesture remain deliberately unbound. Settings
  can assign them through the existing whole-catalog conflict validation; neither
  the palette nor keybindings introduce executable strings or new authority.
- Core Files, Git, Run output, and Problems status surfaces use the shared
  `StatusIndicator`, preserving their explicit empty, busy, unavailable, error, and
  ready messages while announcing changes politely through AT-SPI. Dock's shared
  `ToolChromeControl` continues to own panel headers and controls.
- Deterministic coverage compares the 45-action catalog directly with the typed enum
  tail, rejects catalog drift, checks shared live status controls and accessible Dock
  chrome, and preserves the complete keybinding round trip.

The production AT-SPI verifier opens the real command palette, settings, workspace
and goal flows, exercises Roslyn editor actions, persists/restarts the Dock layout,
and checks explicit automation names. The global source-size guard has no remaining
Avalonia production or test entries; 19 non-Avalonia entries remain visible and keep
Task 060 open.
