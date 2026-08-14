# Git workbench destructive file actions — 2026-08-14

Task 050 remains in progress. This record covers exact tracked-file discard and
untracked-file deletion in the original workspace.

## Delivered

- The Git panel offers discard only for tracked, unstaged, non-conflicted files and
  cleanup only for exact untracked files. Partial hunk or line selection must be
  cleared before either action.
- Business Logic builds an immutable preview from the active Git fingerprint. It
  identifies the source context and operation, lists every affected path, explains
  the consequence, and states that Harness provides no recovery copy.
- The confirmation dialog repeats the exact paths and consequence. Its destructive
  action remains disabled until the developer selects the explicit confirmation
  checkbox. Controls have accessible names.
- Apply recomputes the preview and rejects a changed preview identity or Git
  fingerprint. Dirty matching editor buffers block the operation before preview.
- Tracked discard uses a fixed `git restore --worktree -- <paths>` adapter and leaves
  the index unchanged. Cleanup deletes only each selected file or symbolic link;
  recursive directory deletion is not supported.
- Git fingerprints hash a symbolic link and its target text without reading the
  target. Deleting an untracked symbolic link cannot delete its target.
- Destructive developer actions are limited to the original workspace. Approved
  goal worktrees retain their separate exact goal-commit and agent authority model.

No remote provider, credential, network Git operation, or agent mutation authority
is involved in this slice.

## Deterministic coverage

- discard restores the worktree from the index without unstaging;
- cleanup deletes one exact untracked file and preserves adjacent files;
- stale-state rejection occurs before deletion;
- symbolic-link cleanup preserves its target;
- preview and apply retain the original source context and reject goal context;
- the headless UI proves preview confirmation, accessible controls, and dirty-buffer
  blocking.

## Remaining Task 050 work

Developer commit and amend are next. Branch deletion and the other destructive
reference operations will reuse this preview/confirmation contract. References,
stash, history, merge editing, remotes, restart, large-repository, and broader
accessibility coverage remain open.

## Verification

- Focused Data Access destructive/index tests: 14 passed.
- Focused Business Logic Git tests: 4 passed.
- Focused headless Avalonia Git tests: 6 passed.
- `dotnet test Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`:
  748 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.
