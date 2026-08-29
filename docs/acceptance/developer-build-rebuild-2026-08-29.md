# Developer Build/Rebuild acceptance — 2026-08-29

This record covers the third Task 052 slice. It extends ADR 023's developer execution
lifecycle; it does not add a terminal, generic task runner, agent shell, implicit
Restore, Test Explorer, Hot Reload, or Debug.

## Delivered behavior

- Every inspected Solution project exposes Build and Rebuild. Typed command-palette
  actions target the inspected startup candidate, falling back deterministically to
  the first project when no startup candidate exists.
- Business Logic re-resolves the exact trusted original workspace or approved goal
  worktree and validates the workspace-relative project, optional target framework,
  and selected inspected configuration before starting a process.
- Data Access invokes `dotnet` directly through an argument list. Build uses
  `dotnet build <project> --no-restore`; Rebuild adds `--no-incremental`. Neither uses
  a shell, launch profile, implicit Restore, or user-provided command string.
- Configuration values are bounded, trimmed, control-character-free semantic values;
  safe names containing spaces remain supported by the argument-list boundary.
- Run, Build, and Rebuild share an asynchronous typed identity, exact source context,
  bounded process-local stdout/stderr, process-tree cancellation, exit code, duration,
  durable terminal state, and restart reconciliation. Build records no synthetic
  Roslyn declaration identity.
- Run output identifies the operation, project, framework, and configuration and
  reports when transient output expired after restart. The Solution live status says
  when an operation is starting or failed and moves successful starts to Run output.
- The SQLite schema migration defaults existing rows to Run while adding checked
  operation and nullable configuration columns. Backups/restores advertise schema 32.
- Build and Rebuild each have a `KeybindingCommand` identity; the complete workbench
  catalog and keybinding tail contain the same 48 actions.

## Verification

- Runner tests prove exact Build/Rebuild arguments, no Restore, configuration names
  with spaces, invalid-operation rejection, cancellation, and workspace confinement.
- Business Logic tests prove Build/Rebuild mapping, inspected-configuration rejection,
  transient output, durable metadata, and cancellation through the shared lifecycle.
- SQLite tests prove Run migration compatibility and Build/Rebuild configuration
  round-trips without entry-point metadata.
- Headless Avalonia coverage proves startup-project selection, typed request mapping,
  selected configuration, Run output activation, and refresh.
- The final release gate passed repository metadata, 12 local-model regression tests,
  all 883 deterministic .NET tests (16 + 4 + 328 + 297 + 22 + 192 + 22 + 2), and the
  schema-32 Linux x64 publish/backup/recovery smoke. The production Avalonia AT-SPI
  workflow passed after invoking a real startup-project Build through the command
  palette, then completing goal-worktree Roslyn and restart recovery coverage.

## Remaining Task 052 work

Typed one-run launch overrides, Test Explorer, coverage, Hot Reload, and a real
debugger adapter remain open.
