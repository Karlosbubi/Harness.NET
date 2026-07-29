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

## Assistive-technology checkpoint

The production `Harness.Host --ui=avalonia` process was inspected through the Linux
AT-SPI bus, rather than through a headless control-tree surrogate. The accessible
tree exposed the real Files/search, source editor region, Conversation, Run output,
Goal context, Git, header, layout, provider, and workspace controls. Dock menu, pin,
maximize/restore, close, title chrome, and proportional splitters have contextual
names instead of template type names. AT-SPI actions successfully selected the
Workspace page and opened the real workspace-management dialog.

`./eng/verify-avalonia-atspi.py` makes that checkpoint repeatable in a graphical
Linux session. With isolated XDG directories and a temporary real Git repository it:

- registers, selects, and explicitly trusts the repository through the production
  workspace dialogs;
- creates a local-only goal, enters a manual plan, approves the plan and capabilities,
  and proves the isolated goal worktree contains editable source;
- opens `Program.cs` and the project file from that worktree, switches and focuses
  them through accessible editor commands, and searches real Git-tracked text;
- saves private layout, restarts the production process, and observes restoration;
- replaces the private layout with an integrity failure, restarts again, and proves
  the safe default workbench remains accessible.

The verifier restores the session's original accessibility flags, invokes no model,
and removes its temporary repository and XDG state. It passed on 2026-07-29.

Orca 50.2 was then attached to the same production host with an isolated application
profile. Its debug speech-generation trace initially recorded framework containers
between the useful application announcements. Avalonia 12.1's Linux AT-SPI bridge
exports raw peers without applying their control/content classification and falls
back to CLR class names for unnamed peers. Harness.NET now makes only those peers
that Avalonia itself classifies as neither control nor content anonymous and
role-neutral, retaining their semantic descendants.

`./eng/verify-avalonia-atspi.py --with-orca` makes the speech checkpoint repeatable.
It refuses to replace an existing Orca process, isolates Orca's profile, restores
the original AT-SPI and desktop accessibility settings, and rejects speech containing
known Avalonia or Dock implementation type names. Its passing trace records contextual
utterances including
“Conversation model, combo box”, “Open editor documents, combo box”, “Save current
panel layout, button”, “Workspace, page tab”, “Manage workspaces, button”,
“Repository path, entry”, and “Inspect, button”. This verifies the actual screen-reader
speech pipeline rather than inferring output from automation properties, but it is
not represented as a human listening study. No model was invoked and the original
desktop accessibility settings were restored after the run.

The verifier passed on 2026-07-29 without speaking `Grid`, `StackPanel`, `Border`,
content-presenter, `DockableControl`, deferred-content, or visual-layer type names.
Meaningful application regions such as “Editor document navigation, panel” remain.

## Complete production workflow

`./eng/verify-avalonia-workflow.py` configures the production host to use a
deterministic loopback server through the real Ollama HTTP boundary. The server is
owned by the verifier, has no credential or external route, and returns bounded Lead,
Implementer, and Reviewer responses plus typed tool calls. It is test infrastructure,
not a mock or filler mode in the application.

Through the real Avalonia UI and AT-SPI actions, the verifier:

- registers, selects, and trusts a temporary real .NET Git repository;
- starts Lead planning, whose real `inspect_dotnet` tool call precedes a persisted
  bounded delegation;
- terminates the production process at `AwaitingPlanApproval`, verifies that durable
  boundary in SQLite, restarts, and approves the plan and isolated worktree;
- has Implementer apply one exact-baseline `Program.cs` edit and produce successful
  durable Build and Test evidence through the typed tools;
- has Reviewer inspect the real Git diff and list durable tool evidence before
  returning an accepted structured decision;
- records a pending exact-diff request in the UI, separately confirms it, and creates
  the local commit on the isolated goal branch.

The passing verifier asserts that the original repository content and one-commit
`main` history are unchanged; the isolated branch is clean with exactly one additional
commit and a `Harness-Diff-SHA256` trailer. It also queries the private application
database and requires the exact nine-checkpoint workflow from `Started` through
`Accepted`, succeeded FileEdit/Build/Test records, and a committed one-file approval
with a commit SHA. The production provider/tool sequence is asserted exactly, so a
text-only model response cannot satisfy the gate.

Task 033 acceptance is complete. This checkpoint verifies generated Orca speech and
AT-SPI operation; it does not claim to be a human screen-reader listening study.
