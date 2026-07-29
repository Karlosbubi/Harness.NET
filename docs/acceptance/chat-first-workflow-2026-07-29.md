# Chat-first workflow acceptance — 2026-07-29

Task 040 was exercised in the production Linux Avalonia host against an isolated XDG
state root and a real temporary Git-backed .NET workspace. The journey used no paid or
live model call: it registered and trusted the workspace, created a private draft from
the composer, wrote a deterministic manual plan from its inline card, explicitly
approved that exact plan, and created the isolated goal worktree.

## Visual evidence

- [Wide chat workflow](chat-workflow-wide-2026-07-29.png) — the normal desktop layout
  keeps conversation open as the primary lower workbench, shows the approved plan card
  and state badge, and leaves the editor/document region usable.
- [Compact chat workflow](chat-workflow-compact-2026-07-29.png) — side tools collapse to
  tabs while conversation, its card, and composer remain available without horizontal
  clipping.

The inspection caused two final layout corrections: compact width no longer collapses
the bottom conversation solely because the window is narrow, and the inner-height
threshold only collapses it near the minimum-height layout. The default conversation
share is 45%, and the redundant internal heading was removed to prioritize cards.

## Keyboard and accessibility evidence

`./eng/verify-avalonia-atspi.py` passes against the production host. Its representative
journey now uses stable accessible composer names, the chat-first creation path, the
manual-plan fallback, and the inline plan approval action. It also verifies the normal
workspace, editor/search, layout persistence/recovery, and private-state isolation
checks already covered by the production gate.

The routine `Goals and plans` modal is no longer reachable from navigation, the goal
inspector, or the command palette. Goal creation/continuation, settings, plan/run
progression, Restore decisions, and exact-commit decisions originate in conversation.
Semantic context remains a focused inspector, and policy-required confirmations remain
focused dialogs rather than an orchestration chain.
