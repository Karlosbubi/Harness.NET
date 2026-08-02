# Goal-branch handoff acceptance — 2026-07-31

## Accepted behavior

- A schema-backed exact commit in `Committed` state produces a completed
  `Goal branch ready` card in Avalonia conversation. It names the full isolated branch,
  an abbreviated commit SHA for scanning, and that the branch is local only.
- Expanded handoff detail states that the original branch is unchanged and gives the
  deliberate choices: push the branch to a chosen remote, open a pull request, or
  inspect and merge it through the user's normal Git workflow.
- The same detail explicitly says Harness.NET will not push, open a PR, merge, rebase,
  or modify the original branch automatically. No credential, remote, network, or new
  Git mutation boundary is introduced.
- `Review branch in Git` activates and refreshes the existing goal-scoped Git tool,
  which shows the real isolated branch and HEAD through the typed inspection boundary.
- The TUI shows the same local-only branch, full commit SHA, unchanged-original-branch
  statement, and manual next steps both immediately after commit and whenever the
  committed approval is reopened.

## Deterministic checks

- Avalonia projection tests start from a committed `GoalCommitApprovalView`, require one
  completed handoff card, assert branch/local-only/non-automation language, and expose
  only the typed Git-review action.
- Terminal formatter tests require the exact branch and commit SHA plus push/PR/manual
  integration and non-automation language.
- Existing acceptance and commit tests continue proving exact fingerprint revalidation,
  original-worktree isolation, interrupted-commit reconciliation, and the absence of
  merge or network behavior.

No provider, remote, network operation, merge, push, PR creation, or paid check is used
by this acceptance slice.
