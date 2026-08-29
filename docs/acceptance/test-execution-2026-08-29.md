# Selected Test execution acceptance — 2026-08-29

This record covers the fifth Task 052 slice. It extends the compiler-backed Test
Explorer with one exact developer Test operation; it does not add a generic task,
terminal, implicit Restore, test Debug, coverage, or model execution authority.

## Delivered behavior

- A discovered test carries its stable Roslyn hash, fully qualified name, and project
  into a typed Business Logic start request. The service accepts only a 64-character
  lowercase discovery hash and a bounded filter-safe .NET test name.
- Business Logic re-resolves the trusted original workspace or approved goal
  worktree, confirms that the project remains in the active static solution
  inspection, and then allocates one of the existing bounded developer-operation
  slots.
- Data Access starts `dotnet test <project> --no-restore --filter
  FullyQualifiedName=<test>` directly through `ProcessStartInfo.ArgumentList`.
  Framework and configuration remain typed optional arguments. No shell, command
  string, launch profile, or implicit Restore is used.
- Test uses the existing process-tree stop behavior and bounded process-local
  stdout/stderr. Exit code, duration, cancellation, error, source context, project,
  and exact test identity are durable; potentially sensitive raw output is not.
- Schema 33 expands the closed execution-operation set to Test and enforces that only
  Test rows carry test identity while only Run rows carry entry-point identity.
  Restart reconciliation marks abandoned Test operations interrupted.
- Each Test Explorer leaf exposes accessible Open and Run controls. A successful
  start activates and refreshes Run output, where the exact test, live state,
  duration, exit/failure, transient streams, and shared Stop action are visible.

## Deterministic verification

- Runner tests prove the exact argument sequence, no Restore, optional framework and
  configuration, filter metacharacter rejection before process start, and the shared
  process-tree cancellation path.
- Business Logic tests prove inspected-project resolution, discovery-identity
  rejection, exact Test mapping, successful durable history, failure exit/duration,
  transient stderr, and cancellation.
- SQLite tests prove schema-31 through schema-33 migration, old Run classification,
  typed Test identity round-trip, constraints, and absence of raw output fields.
- Headless Avalonia coverage proves exact workspace/project/test request mapping,
  Run output activation/refresh, source navigation, late Roslyn progress isolation,
  hierarchy, status, and accessibility names.

- The final release gate passed repository metadata, 12 local-model regression tests,
  all 893 deterministic .NET tests (16 + 4 + 333 + 301 + 22 + 193 + 22 + 2), and the
  schema-33 Linux x64 publish/backup/recovery smoke.
- The production Avalonia AT-SPI workflow passed Test Explorer discovery and then the
  complete Build, goal-worktree editor, Roslyn quick-fix/save, search, and
  restart/layout recovery workflow. Exact Run-control request mapping is covered by
  the headless production composition test; the fixture deliberately carries no
  downloaded test adapter and therefore does not spend or Restore to execute it.

## Remaining Task 052 work

Multi-selection and project/type test runs, rerun shortcuts, adapter-level case
results and richer filters, Test Debug, coverage, typed one-run launch overrides, Hot
Reload, and the debugger adapter remain open.
