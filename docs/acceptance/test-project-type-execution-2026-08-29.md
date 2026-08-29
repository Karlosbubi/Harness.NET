# Project and containing-type Test execution acceptance — 2026-08-29

This record covers the eighth Task 052 slice. It extends the existing developer Test
lifecycle to project and containing-type selections; it adds no shell, implicit
Restore, generic task authority, model execution authority, or test adapter.

## Delivered behavior

- Exact, Type, and Project are closed semantic scopes across Presentation, Business
  Logic, Data Access, and durable storage. Project and type identities are stable
  SHA-256 values derived from scope, inspected project, and selector.
- Business Logic re-resolves the trusted source context and inspected project, rejects
  forged group identities and filter metacharacters, and records the selected scope.
- Data Access starts exactly one direct `dotnet test <project> --no-restore` process.
  Exact uses `FullyQualifiedName=<test>`, Type uses the bounded
  `FullyQualifiedName~<type>.` prefix, and Project adds no filter. No selection fans
  out into one process per test.
- Project and containing-type Test Explorer rows expose accessible Run, Rerun, and
  Stop controls and join the newest scoped duration, exit, state, and failure history.
  Exact tests keep Open and exact source navigation.
- Schema 34 stores the closed scope. Existing schema-33 Test rows migrate to Exact,
  preserving their previous meaning and restart history; raw output remains
  process-local.

## Deterministic verification

- Runner tests prove the exact Type and Project argument sequences and one-process
  boundary alongside exact-filter and unsafe-filter coverage.
- Business Logic tests prove deterministic group identity validation, scope mapping,
  persistence, reconstruction, and forged-identity rejection.
- SQLite tests prove all three scope values round-trip and schema-33 Test rows migrate
  to Exact.
- Headless Avalonia coverage proves deterministic project/type nodes and requests,
  accessible controls, exact navigation, filtering, history, and cancellation.

- The full release gate passed repository metadata, 12 local-model regression tests,
  all 901 deterministic .NET tests (16 + 4 + 336 + 306 + 22 + 193 + 22 + 2), and the
  schema-34 Linux x64 publish/backup/recovery smoke.
- The production Avalonia AT-SPI workflow passed Test Explorer discovery and the full
  Build, goal-worktree editor, Roslyn quick-fix/save, search, layout restart, and
  corrupt-layout recovery path against the schema-34 host.

## Remaining Task 052 work

Multi-selection, adapter-level case results, Test Debug, coverage, typed one-run
launch overrides, Hot Reload, and the debugger adapter remain open.
