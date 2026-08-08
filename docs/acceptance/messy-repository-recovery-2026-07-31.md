# Messy-repository recovery acceptance — 2026-07-31

## Accepted behavior

- The real Harness.NET solution (more than 700 tracked files and multiple projects) is
  loaded through the production in-process Roslyn engine. Cold load, warm diagnostics,
  completion p95, navigation, cancellation, and retained-memory limits remain bounded;
  degraded project issues are surfaced rather than hidden.
- A dirty user repository remains untouched when an approved goal worktree is created.
  Source saves inside that worktree use exact SHA-256 compare-and-swap baselines; an
  external mid-goal edit becomes an actionable reload/overwrite/cancel conflict rather
  than silent loss.
- An actual Git merge conflict prevents exact-commit inspection with
  `conflicts_present`. Avalonia names the conflict count, explains that commit approval
  is blocked, and directs the user to resolve and stage with Git before refreshing.
  After explicit resolution and staging, the same production committer produces a new
  complete diff fingerprint and can proceed through normal approval.
- Semantic rebuilds are generation based. A ready partition remains searchable while a
  replacement receives 1,000 chunks; aborting the interrupted rebuild leaves that ready
  generation current and excludes partial content.
- A definitive provider outage or exhausted cost cap moves the exact Lead, Implementer,
  or Reviewer role to durable `NeedsDirection`. Neither restart nor provider recovery
  replays it. Avalonia chat and the TUI expose an explicit retry with the prior recovery
  notice, a capability-qualified replacement model, required bounded user guidance,
  selected output ceiling, possible prior cost, and aggregate-cap disclosure. Every
  non-terminal goal can instead be explicitly aborted; its evidence and worktree remain
  auditable while it disappears from the resumable-goal list.
- A remote cap can only be increased through a separate typed action on an active trusted
  goal. The old cap, new cap, required reason, and approval time are stored atomically in
  schema 21. A stale or decreasing request fails; an extension enables only future cost
  reservations and never retries a call by itself.
- Interrupted Lead and Implementer results reconcile only from already durable plan/task
  boundaries. Unknown calls remain paused. Corrupt run-output evidence renders an honest
  error without leaking raw JSON; corrupt layout state falls back to the known default,
  and schema upgrades retain the verified pre-migration recovery archive.

## Deterministic checks

- `RoslynCodeIntelligenceEngineTests.Actual_harness_workspace_meets_the_bounded_foreground_session_budget`
  exercises the real multi-project repository without restore or a language server.
- `GitGoalWorktreeManagerTests.Creates_an_idempotent_goal_worktree_without_touching_dirty_user_state`,
  source-editor conflict tests, and
  `LibGitGoalCommitterTests.Conflict_is_blocked_until_the_user_resolves_and_stages_it`
  cover dirty and conflicting Git state. A headless production-control test covers the
  corresponding conflict guidance.
- `SqliteSemanticIndexStoreTests.Interrupted_large_rebuild_keeps_the_ready_generation_searchable`
  covers load and cancellation without inference.
- Workflow tests inject provider and budget failures for every role boundary, reject a
  stale/wrong-role retry, prove retry resumes from the matching durable state with new
  guidance, and prove abort is durable, idempotent, and excluded from continuation.
  Goal/store/cost-ledger tests prove increase-only CAS, audit persistence, and a future
  reservation after extension.
- Existing workflow reconciliation, corrupt evidence, corrupt layout, SQLite integrity,
  backup, and upgrade tests remain part of the repository-wide deterministic gate.

No model provider, network operation, unrestricted agent shell, or paid check is used by
this acceptance slice.
