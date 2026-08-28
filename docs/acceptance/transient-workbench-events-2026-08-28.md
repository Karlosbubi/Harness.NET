# Transient workbench events

Task 063 adds one bounded notification path for long-operation outcomes without
making transient UI state durable or model-visible.

## Delivered behavior

- Business Logic exposes immutable semantic event identifiers, bounded messages,
  severity, source, timestamp, and a closed optional navigation target.
- Presentation keeps at most four notifications in memory for the current session.
  Equivalent events coalesce and move to the newest position; overflow always drops
  the oldest item.
- Information and success expire after eight seconds, warnings after fifteen, and
  errors after thirty. The pure queue accepts an explicit time, so tests never wait
  on wall-clock timers.
- Avalonia overlays non-modal cards without moving focus. Every card has a keyboard
  close action, Escape dismissal, and an optional details action.
- Only the card entering or changing in the visible queue receives an Avalonia live
  setting. The setting is cleared after dispatch, and unchanged expiry passes retain
  the existing card rather than recreating its AT-SPI live region.
- Goal planning, production, and retry workflow completion, cancellation, and failure
  publish through the same surface. Details navigate to Conversation. Task 053 may
  add attention-state producers without creating another channel.
- Events remain Presentation-session state: no storage, repository metadata, prompt,
  log, backup, or telemetry path was added.

## Verification

Five focused tests cover message bounds, coalescing, ordering, overflow, typed
expiry, semantic goal publication, navigation, Escape dismissal, visibility, and
single live-region activation. The full Avalonia Presentation suite passes with 182
tests. The architecture gate passes without increasing a source-size budget.

The complete non-live solution gate passes with 850 tests. The solution builds with
zero warnings and errors, repository metadata validation passes, and
`git diff --check` is clean. This environment has no graphical display, so the
standard live-setting-to-AT-SPI bridge is verified structurally and in Avalonia
headless tests; the existing graphical AT-SPI release gate remains required on the
Linux release workstation.
