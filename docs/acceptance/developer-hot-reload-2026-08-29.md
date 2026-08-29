# Developer Hot Reload acceptance — 2026-08-29

This record covers the thirteenth Task 052 slice. Hot Reload is a developer-selected,
closed project lifecycle; it is not Debug, a terminal, a generic watcher, or agent
execution authority.

## Delivered behavior

- The one-run confirmation exposes a typed Run/Hot Reload choice and safe summary.
- Business Logic reuses exact trusted source-context, Roslyn entry-point, saved-baseline,
  inspected profile, and bounded override validation. Hot Reload receives a distinct
  operation identity and the existing concurrency, Stop, output, and restart rules.
- Data Access invokes `dotnet watch --non-interactive --project <exact project> run
  --no-restore` as an argument list. Browser launch/refresh and emoji output are
  suppressed, rude edits restart without prompting, and runner-owned variables cannot
  be overridden.
- Cancellation kills the complete watch/application process tree. Output is bounded
  and process-local. Schema 38 records a distinct Hot Reload run mode while preserving
  the existing strict Run declaration invariant and restart reconciliation.

## Verification

- Runner tests prove exact non-interactive watch arguments, framework/application
  ordering, browser suppression, rude-edit policy, and no selected launch profile.
- Business Logic tests prove distinct runner/store/view identity and cancellation.
- SQLite tests prove schema-38 durable round-trip; headless Avalonia tests prove the
  accessible choice and visible mode summary.
- The release gate passed repository metadata, 12 local-model regression tests, all
  930 non-live deterministic .NET tests (16 + 4 + 345 + 322 + 22 + 197 + 22 + 2),
  and the schema-38 Linux x64 publish/backup/downgrade/upgrade/recovery smoke.

## Remaining Task 052 work

Test Debug and the debugger adapter remain open.
