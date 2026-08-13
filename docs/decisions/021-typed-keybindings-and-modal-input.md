# ADR 021: Typed keybindings and modal input boundary

- Status: Accepted
- Date: 2026-08-13
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md), [ADR 010](010-docked-desktop-workbench.md)

## Context

Workbench shortcuts were duplicated between Avalonia key handlers, toolbar labels,
and command-palette text. Some advertised shortcuts did not invoke anything. Raw
framework gestures are not a stable settings contract, and unrestricted command
strings or executable import formats would make configuration unsafe.

Task 049 also requires a later optional Vim mode. Modal input must not replace the
ordinary command catalog or take ownership of text composition, accessibility, or
desktop-reserved keys.

## Decision

Business Logic owns a closed `KeybindingCommand` catalog and typed key/modifier
gestures. It validates the complete binding set before persistence or activation:

- every known command has one configuration entry, which may deliberately be empty;
- a command may have at most eight alternate gestures;
- duplicate gestures and cross-command conflicts fail validation;
- unmodified typing, navigation, Escape, desktop close/session/lock shortcuts, and
  Linux virtual-terminal shortcuts cannot be assigned;
- one normalized binding snapshot drives editor dispatch, shell dispatch, Settings,
  command-palette labels, and the header hint.

Data Access persists only normalized command names and gestures in private SQLite
state. A marker distinguishes built-in defaults from a deliberately custom empty
binding set. Corrupt or obsolete stored commands fail closed to the defaults and are
reported in status.

Portable configuration is the bounded `harness-keybindings-v1` JSON document. Import
requires the exact closed object shape, format, command names, string gestures, item
limits, and whole-set validation. Unknown fields and commands are rejected. The
document cannot name paths, scripts, types, or arbitrary actions. Export is an
explicit copy action.

Presentation maps Avalonia keys to the typed gesture; Avalonia types do not cross its
boundary. Focused controls keep ordinary text input and unmodified navigation. Vim is
an optional Presentation-owned modal state machine over the same live editor buffer.
Its typed Standard/Vim preference is stored with the keybinding configuration.
AvaloniaEdit's input-method client is wrapped to observe preedit state; modal exit is
suspended while composition is active. Configured commands run before modal input,
unrelated Control/Alt/Meta gestures pass through, and the visible mode text remains
available to AT-SPI. Separate acceptance evidence covers the delivered behavior.

## Consequences

- Runtime behavior and displayed shortcuts can no longer drift independently.
- Private database schema version 28 adds the keybinding configuration and binding
  tables. Version 29 adds the closed editor input mode; application backup includes
  both with the database.
- New configurable workbench commands must be added to the closed catalog, default
  policy, Settings surface, runtime dispatcher, and command discovery together.
- Vim remains an input adapter, not a second editor. Roslyn, dirty state, save,
  diagnostics, undo, and document lifecycle continue to use the same live buffer.

## Alternatives considered

- Avalonia `KeyGesture` strings in Presentation would leak toolkit syntax into
  persisted policy and duplicate validation.
- Per-control shortcut settings would preserve conflicts and stale discovery labels.
- Loading scripts, arbitrary command IDs, or framework markup would create an
  executable configuration surface.
- Treating Vim as a separate editor would split the live buffer and Roslyn state.
