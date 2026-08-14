# Git workbench developer stashes — 2026-08-14

Task 050 remains in progress. This record covers local developer stash inspection,
creation, application, and deletion in the trusted original workspace.

## Delivered

- The Git workbench adds an accessible Stashes tab beside Changes, Branches, Tags,
  and Worktrees.
- Inspection lists at most 500 local stash reflog entries with selector, exact commit
  SHA, base SHA, creation time, bounded subject, and truncation state.
- Creation binds the complete displayed Git fingerprint, requires a bounded message,
  and makes inclusion of untracked files an explicit choice. Ignored files are never
  included by this action. LibGit2Sharp creates the stash in-process so its message
  never appears in a process argument or environment value.
- Apply binds the selected stash commit SHA and displayed repository fingerprint,
  restores its index state, and deliberately keeps the stash. A conflict is returned
  as current Git state with the stash still present; Harness does not silently resolve
  or delete it.
- Deletion resolves the exact selected commit to its current reflog selector only
  below Presentation. Its preview repeats selector, commit, base, time, message,
  consequence, and the lack of guaranteed recovery. Apply recomputes the preview and
  requires acknowledgement.
- Every mutation is original-workspace only and rejects stale state, current conflicts,
  and in-progress Git operations. The closed apply/drop adapter disables terminal
  prompting, drains Git output, and kills its process tree on cancellation. No shell
  or generic Git endpoint exists.

## Deterministic coverage

- tracked and staged content plus explicitly included untracked content round-trip;
- untracked content remains in place when it is not included;
- exact deletion still targets the selected commit after reflog selectors shift;
- changed working state rejects apply before mutation;
- conflicting apply reports the conflict and retains the stash;
- Business Logic preserves original-context, fingerprint, message, untracked policy,
  commit identity, destructive preview, and goal-context denial; and
- headless Avalonia covers accessible create/apply controls, exact delete confirmation,
  and the acknowledgement-gated dialog.

No provider, credential, remote Git, network, model mutation, or goal-commit authority
is used by this slice. Stash messages are displayed only as user-created repository
metadata and are neither logged nor sent to models by this feature.

## Remaining Task 050 work

History graph, file timeline, blame, commit detail/diffs, three-way merge editing,
remote synchronization, submodule/restart/large-history acceptance, and the final
accessibility and publish gates remain open.

## Verification

- Focused Data Access developer Git tests: 35 passed.
- Focused Business Logic developer Git tests: 16 passed.
- Focused headless Avalonia stash tests: 3 passed.
- Wider headless Avalonia Git regression tests: 16 passed.
- Full deterministic solution: 797 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.
- Changed-file formatting and whitespace verification completed without findings.
- The patch credential/machine-path scan had no matches. The tracked-tree scan found
  only the existing source literals that detect private-key markers.
