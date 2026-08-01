# Roslyn interactive assistance acceptance — 2026-07-31

## Accepted behavior

- Completion operates on an immutable exact-baseline buffer and caret. Ctrl+Space
  invokes it explicitly; identifier and member-access input refresh it automatically.
  AvaloniaEdit supplies local filtering, Up/Down/Page navigation, Enter/Tab acceptance,
  and pointer acceptance. Roslyn item rules drive punctuation commit characters.
- Completion items expose an accessible kind, display text, and bounded description.
  Commit asks Roslyn for the actual text change rather than inserting the display label,
  then applies the returned range and caret position to the active buffer.
- A completion list is transient and bound to its path, buffer version, and text hash.
  A changed buffer cannot commit an old item.
- Quick info appears after a 600 ms pointer hover and through Ctrl+K. Bounded Roslyn
  sections render in a styled caret-anchored insight window without persistence.
- Signature help appears on `(` and `,`, follows the active argument, exposes bounded
  XML summary/parameter documentation, highlights the selected parameter, and lets
  Up/Down select overloads.
- F12 opens a source definition at its exact range. Shift+F12 presents bounded source
  references at the caret; selecting one opens its real document and range. Metadata,
  generated, and unavailable destinations are reported honestly instead of inventing
  repository paths.
- Business Logic rejects untrusted/unknown sessions, invalid paths/baselines/carets,
  identity mismatches, and results superseded by a newer live buffer. Cancellation is
  explicit and all compiler work stays off the UI thread.

## Deterministic and production-control checks

- Real synthetic-project adapter tests cover committable completion without disk
  mutation, quick info, documented signature help, active-parameter selection, source
  definition/references, metadata, and unavailable destinations.
- The deterministic Business Logic adapter test starts completion on buffer version 1,
  activates version 2 before the result returns, and proves the older result is Stale.
- Headless production-control tests open a real source tab, invoke Ctrl+Space, inspect
  accessible completion content, commit a Roslyn text change, invoke Ctrl+K quick info,
  verify accessible signature parameter text, and use F12 to move to an exact source
  range. The full Avalonia suite passes 94 tests.

## Representative Harness workspace measurements

Measured against the real `Harness.slnx` foreground session on 2026-07-31:

- cold solution load: 6,018 ms;
- warm diagnostic update: 944 ms;
- warm completion p95 over 20 requests: 27.8 ms (target below 200 ms);
- warm definition navigation: 17.8 ms;
- retained managed memory: 82.9 MiB;
- cancelled request observation: 1.1 ms.

The loaded workspace reported one existing duplicate analyzer-release source warning,
so semantic responses correctly remained Degraded while still returning usable local
results. No restore, model provider, remote language service, or paid operation ran.
