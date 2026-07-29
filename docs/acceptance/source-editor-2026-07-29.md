# Source editor acceptance checkpoint — 2026-07-29

This checkpoint records the production Avalonia host on Linux against a temporary
real .NET 10 Git repository. The workflow registers and trusts the repository,
creates a durable goal and manual plan, explicitly approves its isolated worktree,
and opens the real `Program.cs` through the production Business Logic document
boundary. It invokes no model provider.

## Wide production review

At 1600×1000 window pixels, the source document keeps the tracked Files tool, central
editor, goal context, Conversation, and Run output available together. The editor
surface now presents:

- a repository-relative breadcrumb and exact approved goal branch;
- a truthful **EDITABLE**, **READ ONLY**, or **TRUNCATED** access badge;
- compact Save, Reload, and Close actions with Ctrl+S/Ctrl+W tooltips;
- a dedicated code surface with current-line emphasis, line numbers, rectangular
  selection, and scroll-below-document behavior;
- theme-aware C# keyword, type, method/number, string, comment, preprocessor, and
  punctuation colors instead of AvaloniaEdit's unreadable light-theme defaults; and
- a compact status bar with the durable access description, caret line/column,
  selection length, UTF-8, and detected LF/CRLF/mixed/no-line-break state.

![Wide approved-goal source editor](source-editor-wide-2026-07-29.png)

## Compact production review

At 900×650 window pixels, Dock collapses the side tools to their chrome and preserves
the active source document. The breadcrumb yields space first, while branch, access,
Save/Reload/Close, code, and caret/format status remain readable and do not overlap.
The Conversation tool remains keyboard-restorable below the editor.

![Compact approved-goal source editor](source-editor-compact-2026-07-29.png)

## Repeatable evidence

`./eng/capture-source-editor.py` reproduces both images through the real Linux host,
AT-SPI actions, a temporary repository, and isolated XDG directories. It performs no
inference and removes its temporary repository and application state.

`Harness.Presentation.Avalonia.Tests` verifies editor interaction defaults, truthful
original/approved-worktree access state, breadcrumb/context text, current caret and
line-ending status, exact-baseline save, dirty-document decisions, theme refresh,
compact rendering, focus, scaling, and layout recovery. All 82 tests pass.

`./eng/verify-avalonia-atspi.py` passes against the production host after this change,
including repository trust, goal/plan approval, editable source, multi-document
switching, search, restart/layout restoration, and corrupt-layout fallback. A clean
solution build completes with zero warnings.

This checkpoint does not claim semantic highlighting, diagnostics, completion, or
refactoring. Those remain Tasks 042-044 and are deliberately represented as absent
rather than fabricated.
