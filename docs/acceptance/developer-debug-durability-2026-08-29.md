# Developer Debug durability acceptance — 2026-08-29

## Scope

This record accepts the Task 052 durable-result slice for Roslyn-bound project Debug
and exact Linux Test Debug. It does not expand debugger authority or persist live
inspection content.

## Accepted behavior

- Every Debug start is recorded before the adapter or testhost lifecycle begins.
- Project Debug stores the exact project, optional framework, and Roslyn declaration
  identity. Test Debug stores the exact project, optional framework/configuration,
  compiler test hash, fully qualified name, and closed `Exact` scope.
- Completion records the closed terminal state, timestamps, exit code when supplied,
  duration, and bounded safe failure metadata.
- Schema 39 uses a closed `None`, `Project`, or `Test` mode while retaining the older
  Run/Test table constraints. Reading history reconstructs Debug as its own typed
  operation rather than presenting it as Run or Test.
- Restart reconciliation is cutoff-bound and one-shot per application store. Rows
  from a prior process become `Interrupted`; operations created after reconciliation
  cannot be reclassified by a later lazy service initialization.
- The Run output history shows safe Debug lifecycle metadata and directs the developer
  to the Debug workspace for live output. It does not offer the project runner's Stop
  action for a Debug row.
- Adapter/debuggee output, application arguments and environment values, breakpoints,
  threads, stacks, scopes, and variables remain process-local and are absent from the
  database, backup, and restored views.

## Verification

- Warning-free solution build.
- Business Logic tests cover durable project and Test Debug start/completion, user
  stop, natural termination, and safe history reconstruction.
- Data Access tests cover both typed schema modes, migration to schema 39, exact
  identity round trips, and cutoff/one-shot restart reconciliation.
- Presentation tests cover explicit transient-inspection messaging for restored Debug
  history.
- The previously accepted pinned NetCoreDbg live launch and exact owned-test attach
  checks remain the adapter-level lifecycle evidence; this slice changes persistence,
  not DAP framing or process ownership.
