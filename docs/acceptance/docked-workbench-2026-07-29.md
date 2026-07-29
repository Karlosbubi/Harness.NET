# Docked workbench acceptance checkpoint — 2026-07-29

This checkpoint records the production Avalonia host on Linux x64 with isolated,
empty XDG configuration, data, and state directories. It contains no seeded
workspace, goal, conversation, evidence, or diagnostic content.

## Hands-on visual review

- Wide, 2040×1275 rendered pixels: real Files, Goal context, Conversation, and Run
  output Dock regions surround the center document region; the honest no-workspace
  state is readable and all header commands remain visible.
- Minimum window, 1275×956 rendered pixels on the scaled desktop: side and bottom
  production content collapses behind its Dock chrome, leaving the center document
  readable; compact header labels are removed while every command remains reachable.

![Wide honest empty state](workbench-wide-empty.png)

![Minimum-size honest empty state](workbench-compact-empty.png)

## Deterministic evidence

`Harness.Presentation.Avalonia.Tests` verifies that a real opened AvaloniaEdit source
editor belongs to the rendered window visual tree, not merely a Dock context object.
The same suite verifies compact tool restoration through Ctrl+Shift+E,
Ctrl+Shift+G, Ctrl+J, and F6; focusable targets; explicit automation names; floating
window ownership; layout recovery; and a 200% framebuffer whose pixel dimensions
double without changing logical layout.

This is a checkpoint, not Task 033 completion. A hands-on screen-reader pass and the
complete representative restart, corrupted-layout, and multi-document workflow
matrix remain open.
