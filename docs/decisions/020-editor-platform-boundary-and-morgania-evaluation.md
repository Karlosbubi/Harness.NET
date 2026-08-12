# ADR 020: Editor platform boundary and Morgania evaluation

- Status: Accepted
- Date: 2026-08-12
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md),
  [ADR 010](010-docked-desktop-workbench.md), and
  [ADR 012](012-roslyn-code-intelligence.md)

## Context

Harness.NET currently uses AvaloniaEdit for text rendering and input. Presentation
code also owns completion, signature help, quick info, diagnostics, navigation, and
the live document buffer. Direct AvaloniaEdit use in the workbench makes another
editor costly to evaluate.

Morgania is RoslynPad's Avalonia editor. The inspected RoslynPad 22.1 source can build
on Linux and its focused editor tests pass, but it is not a stable dependency for
Harness.NET. The evidence is recorded in
[the Task 048 evaluation](../acceptance/morgania-editor-evaluation-2026-08-12.md).

## Decision

Editor-platform objects remain inside Presentation. Business Logic continues to own
document access, exact-baseline saves, Roslyn requests, stale-result policy, and model
write validation. Presentation owns a small editor adapter for text, selection,
caret, input events, diagnostics, focus, theme updates, and disposal. Dock and the
workbench use that adapter rather than a third-party editor type where practical.
AvaloniaEdit popup windows currently require its native `TextArea`; that transitional
escape hatch stays inside Presentation and must be removed or replaced before another
editor can cut over.

AvaloniaEdit remains the production editor. Do not vendor Morgania, RoslynPad's
`vs-editor-api` tree, or Roslyn EditorFeatures into Harness.NET. Do not consume
Morgania from an unversioned branch, a locally packed source tree, or a private build
feed.

Reconsider Morgania only after all four editor packages are published from a pinned
release with integrity metadata and a support policy. The matching upstream smoke
test must pass on Harness.NET's supported SDK and Linux runtime. A new evaluation
must also show one shared live buffer, exact stale-result rejection, acceptable
Wayland and X11 input and accessibility, bounded startup and memory cost, clean
disposal, and a smaller maintenance surface than the AvaloniaEdit adapter.

The adapter is not permission to keep two permanent editor stacks. Removing
AvaloniaEdit requires a separate accepted cutover after the replacement passes the
complete desktop gate.

## Consequences

- Current editing behavior and rollback remain available.
- Roslyn and editor implementation types do not cross the Presentation boundary.
- Later editor trials have a defined integration point.
- Morgania's current feature set is a useful behavior reference, not a production
  dependency.
- Task 048 is complete for the inspected revision. A future admissible Morgania
  release requires a new evaluation and an ADR amendment; it does not block work on
  the retained AvaloniaEdit adapter.
