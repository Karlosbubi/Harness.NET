# Git workbench developer commit and amend — 2026-08-14

Task 050 remains in progress. This record covers ordinary developer commit and amend
in the trusted original workspace.

## Delivered

- Compose accepts a bounded commit message, create or amend, and an explicit hook
  policy. Configured hooks run by default; bypass requires selecting `--no-verify`.
- Preview resolves the original workspace again and requires the displayed complete
  Git fingerprint. It rejects goal worktrees, conflicts, no staged changes, missing
  Git identity, truncated diffs, and amend on an unborn branch.
- The exact review shows branch or detached state, HEAD or unborn state, effective
  `user.name` and `user.email`, message, hook policy, staged paths, and the complete
  staged diff. Amend also states that it replaces HEAD and that reflog retention is a
  possible, not guaranteed, recovery route. Apply recomputes this preview and compares
  its opaque identity before starting Git.
- The Data Access adapter invokes only fixed `git commit --file=-`, optional `--amend`,
  and optional `--no-verify`. The message travels through stdin. Terminal prompting
  is disabled, output is drained without retention, cancellation kills the process
  tree, and failures are sanitized.
- Only the index is committed. Unstaged content remains in the working tree. The
  returned state carries the new HEAD and fingerprint.
- Every dirty original-workspace editor buffer blocks the workflow before compose.
- Developer commits do not read, create, approve, or complete goal commit approvals.

## Deterministic coverage

- staged-only commit with adjacent unstaged content preserved;
- configured hook rejection and explicit bypass;
- amend replaces HEAD while preserving its parents;
- exact staged preview and initial commit on an unborn branch;
- detached-HEAD commit remains detached;
- effective identity, hook policy, message, fingerprint, and operation survive the
  Business Logic boundary;
- goal-worktree rejection;
- accessible compose and exact-preview controls;
- end-to-end Presentation preview, confirmation, and apply dispatch.

Paid providers, credentials, and network Git operations are not used by this slice.

## Remaining Task 050 work

Branch, tag, worktree, and stash management are next. Destructive reference actions
will reuse the existing preview/confirm/apply contract. History, blame, merge editing,
remotes, restart, large-repository, and final accessibility coverage remain open.

## Verification

- Focused Data Access developer Git tests: 19 passed.
- Focused Business Logic developer Git tests: 6 passed.
- Focused headless Avalonia Git tests: 8 passed.
- `dotnet test Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`:
  757 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.
