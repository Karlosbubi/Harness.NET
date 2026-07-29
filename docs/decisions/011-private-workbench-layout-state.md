# ADR 011: Private workbench layout state and recovery

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 008](008-application-state-backup.md)
- Refines: [ADR 010](010-docked-desktop-workbench.md)

## Context

The Dock-based desktop can move, hide, pin, resize, and float production tool
panels. A v1 desktop must restore those choices after restart, recover safely from
malformed or incompatible state, and expose an immediate default-layout reset.

Dock exposes the model state needed to capture tool order, proportions,
hidden/pinned state, and floating-window bounds. Its general System.Text.Json
serializer was evaluated at the pinned revision, but MVVM `Tool` implements both
`ITool` and `IDocument`, and the generic graph contains computed MDI state plus live
references that are broader than Harness.NET's recovery contract. The live graph
also contains transient source documents and Presentation control contexts that must
never become durable application state. Deserializing an unchecked graph could
create duplicate or unknown panes, invalid proportions, excessive nesting, or
off-screen windows.

ADR 008 currently defines an application-state archive containing only SQLite and a
manifest. ADR 010 deliberately places machine-local desktop layout under XDG state,
so backup and recovery require an explicit archive-format amendment rather than
silently moving layout into SQLite or excluding it.

## Decision

Persist one bounded `harness-workbench-layout-v1` JSON envelope at
`$XDG_STATE_HOME/harness.net/workbench-layout.json`. Data Access owns atomic,
user-private file I/O, size limits, schema decoding, and SHA-256 integrity. Business
Logic maps that store through immutable layout payload/result contracts.
Presentation alone maps the live Dock graph to a closed version-1 layout DTO and
interprets that DTO. The durable schema admits only roots, proportional docks and
splitters, tool docks, document docks, the six known production panes, bounded
floating windows, and their layout properties. It does not serialize Dock runtime
types.

Before saving, Presentation traverses the graph and omits transient file, diff, plan,
and evidence documents. Before activation, it rejects malformed, duplicate,
unsupported, excessively deep, or incomplete structural graphs; rebinds only the
known production pane contexts; normalizes non-finite or unsafe proportions; and
clamps or docks floating windows that are not safely visible on current screens. A
rejected layout leaves the default layout active and reports an actionable recovery
status. Reset deletes only this exact private state file and immediately rebuilds the
known default layout.

Save layout automatically during orderly desktop shutdown and after explicit layout
actions. Do not persist conversation content, repository content, source buffers,
provider objects, service objects, Avalonia controls, or Dock host-window instances.
Do not write any layout metadata to a user repository.

Amend the deliberate application-state archive to
`harness-backup-v2`. It retains the consistent SQLite snapshot and manifest and,
when a valid layout file exists, adds that exact file plus byte-count and SHA-256
evidence. A corrupt layout causes backup creation to fail rather than silently
publishing incomplete recovery state. Recovery remains offline and restores the
layout only to the XDG state root; current Presentation validation still runs before
the graph becomes active. Version-1 archives remain readable by the documented
manual recovery process.

## Acceptance before changing status

- Atomic store tests cover missing, round-trip, overwrite, corrupt, oversized,
  unsupported, hash-mismatch, cancellation, and reset behavior.
- Headless workbench tests cover restart restoration, moved/hidden panels, transient
  document removal, duplicate/unknown pane rejection, invalid proportions,
  off-screen floating state, and immediate reset.
- Dock types remain confined to Avalonia Presentation and no repository metadata is
  created.
- Backup tests and the Linux x64 release verifier prove optional layout inclusion,
  manifest integrity, offline extraction, and safe startup after recovery.
- Keyboard-accessible save/reset commands and an honest recovery status are visible
  in the desktop.

## Consequences

- Layout is portable as part of a deliberate sensitive backup but is still treated
  as untrusted machine-specific input after recovery.
- Backup v2 gains one optional bounded file and corresponding integrity evidence.
- Dock model mapping compatibility becomes part of the pinned desktop dependency
  gate; an incompatible future Dock upgrade requires a layout-version migration or
  safe default reset.

## Alternatives considered

- Persisting the raw live Dock graph was rejected because it includes transient
  documents and cannot safely rehydrate Presentation contexts.
- Storing layout in SQLite was rejected because monitor/window layout is machine-local
  XDG state and should not require a database migration for every Dock schema change.
- Excluding layout from backup was rejected because it would contradict the stated
  v1 recovery contract.
- Silently accepting partial or unknown graphs was rejected because missing product
  panels and off-screen windows make recovery less useful than a known default.
