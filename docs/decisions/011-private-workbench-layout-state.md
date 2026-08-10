# ADR 011: Private workbench layout state

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 008](008-application-state-backup.md)
- Refines: [ADR 010](010-docked-desktop-workbench.md)

## Context

The desktop must restore moved, hidden, resized, pinned, and floating tools. Raw Dock
graphs contain transient documents, live controls, computed state, and broad runtime
types. They are not a safe persistence format.

## Decision

Store one `harness-workbench-layout-v1` JSON envelope at
`$XDG_STATE_HOME/harness.net/workbench-layout.json`.

- Data Access owns private atomic file I/O, size limits, schema decoding, and SHA-256.
- Business Logic exposes immutable payload and result records.
- Presentation maps between Dock and the closed layout DTO.
- The DTO allows roots, proportional docks, splitters, tool docks, document docks,
  the seven known tools, bounded floating windows, and required layout properties.
- Do not serialize Dock types, Avalonia controls, services, source buffers,
  conversation content, provider objects, or transient source/diff/plan/evidence
  documents.

Before activation, reject malformed, duplicate, unknown, incomplete, or excessively
deep graphs. Normalize unsafe proportions. Clamp or dock off-screen floating windows.
Keep the default layout active and report the failure.

Save during orderly shutdown and explicit layout actions. Reset deletes only the
layout file and activates the default layout.

Backup format `harness-backup-v2` may include the exact validated layout file plus
its byte count and SHA-256. A corrupt layout blocks backup publication. Recovery
restores the file to XDG state, then Presentation validates it before use. Version-1
archives remain readable.

## Acceptance

Tests cover missing, round-trip, overwrite, corruption, oversize, unsupported format,
hash mismatch, cancellation, reset, panel movement, hidden tools, transient document
removal, invalid structure, off-screen windows, backup inclusion, recovery, and Linux
x64 publish. Dock types remain inside Avalonia Presentation.

## Consequences

- Layout is private machine-specific state, even when included in a backup.
- Backup v2 has one optional bounded entry.
- A Dock upgrade may require a layout migration or default reset.

## Alternatives considered

- Raw Dock serialization persists unsafe and transient runtime state.
- SQLite couples machine-local layout changes to database migrations.
- Excluding layout from backups would not restore the desktop state promised by the
  backup workflow.
