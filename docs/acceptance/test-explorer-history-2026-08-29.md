# Test Explorer history, Rerun, and Stop acceptance — 2026-08-29

This record covers the sixth Task 052 slice. It projects the already typed and durable
selected-Test lifecycle into Test Explorer; it adds no process command, authority, or
persistence schema.

## Delivered behavior

- Every refresh lists at most the existing bounded 200 developer operations for the
  exact original-workspace or approved-goal source context after Roslyn discovery.
  Only typed Test operations with a matching stable discovery ID can decorate a test
  leaf; Build, Rebuild, Run, another workspace, or another goal cannot leak into it.
- The newest matching operation shows Running, Succeeded, Failed, Cancelled, or
  Interrupted state, durable duration, and exit code. Raw stdout/stderr remain in Run
  output and keep their process-local privacy boundary.
- A completed test changes Run to Rerun and starts the same exact typed Test request.
  A running test instead shows Stop and sends only its typed execution identity to
  the shared process-tree cancellation path.
- Open remains independent of lifecycle controls and navigates to the exact Roslyn
  source range. Run/Rerun still activates Run output; Stop refreshes Run output and
  reports its in-progress or error state in Test Explorer.
- History failure degrades the live status but leaves valid compiler discovery and
  navigation visible.

## Verification

- Headless Avalonia coverage joins failed and running histories to two exact test
  identities, rejects cross-kind history through the production filter, preserves
  hierarchy/source navigation, proves exact Rerun mapping and Run output activation,
  and proves Stop sends the running execution identity and refreshes Run output.
- The final release gate passed repository metadata, 12 local-model regression tests,
  all 893 deterministic .NET tests (16 + 4 + 333 + 301 + 22 + 193 + 22 + 2), and the
  schema-33 Linux x64 publish/backup/recovery smoke.
- The production Avalonia AT-SPI workflow passed Test Explorer discovery and the
  complete Build, goal-worktree editor, Roslyn quick-fix/save, search, and
  restart/layout recovery workflow. Failed/running history, Rerun mapping, and Stop
  identity are deterministic headless checks because the isolated production fixture
  intentionally has no prior run history.

## Remaining Task 052 work

Multi-selection and project/type runs, adapter-level case results and richer filters,
Test Debug, coverage, typed one-run launch overrides, Hot Reload, and the debugger
adapter remain open.
