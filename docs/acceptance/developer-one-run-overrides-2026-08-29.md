# Developer one-run overrides acceptance — 2026-08-29

This record covers the twelfth Task 052 slice. It specializes a developer-confirmed
Run without adding a shell, generic task surface, implicit Restore, persistent launch
configuration, or agent execution authority.

## Delivered behavior

- Run CodeLens opens an accessible confirmation for an optional exact inspected
  project launch profile, workspace-relative working directory, application arguments,
  and environment entries. Defaults remain an explicit `--no-launch-profile` Run.
- Arguments and `NAME=value` environment entries use one line per semantic item. The
  visible summary includes profile, argument count, environment names, and relative
  working directory, never environment values.
- Business Logic re-resolves the exact trusted original workspace or approved goal
  worktree, revalidates the Roslyn entry point and source baseline, and accepts only a
  Project-kind profile from that inspected project. Inputs are limited to 32 arguments,
  32 distinct environment names, bounded item/aggregate sizes, and one bounded relative
  directory. Runner-owned telemetry/no-logo variables cannot be overridden.
- Data Access repeats the bounds, confines the existing non-symbolic working directory,
  uses `ProcessStartInfo.ArgumentList`, inserts application arguments only after `--`,
  and applies environment entries directly. No command is interpreted by a shell.
- The complete override object and all environment values are process-local. Durable
  execution state keeps the already-approved project/source lifecycle only; listing
  after start reconstructs no override.

## Verification

- Runner tests prove the exact launch-profile/application separator, argument identity,
  confined working directory, environment application, default no-profile behavior,
  and rejection on non-Run operations.
- Business Logic tests prove exact inspected-profile validation, typed mapping into one
  runner call, transient start projection, and absence from reconstructed history.
- Headless Avalonia tests prove semantic parsing, accessible field names, safe visible
  summaries, hidden environment values, and invalid environment-row rejection.
- The release gate passed repository metadata, 12 local-model regression tests, all
  927 non-live deterministic .NET tests (16 + 4 + 344 + 320 + 22 + 197 + 22 + 2),
  and the schema-37 Linux x64 publish/backup/downgrade/upgrade/recovery smoke.

## Remaining Task 052 work

Test Debug, Hot Reload, and the debugger adapter remain open.
