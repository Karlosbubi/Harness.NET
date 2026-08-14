# Git workbench file index slice — 2026-08-14

Task 050 remains in progress. This record covers the first dependency-ready slice:
shared Git state plus exact file-level stage and unstage.

## Delivered

- ADR 024 fixes ownership for index, destructive, credential, and remote behavior.
- Inspection reports index, working-tree, and conflict state from the active original
  workspace or approved goal worktree.
- The snapshot contains separate staged and unstaged bounded diffs and a complete
  SHA-256 state fingerprint. The fingerprint includes HEAD, branch/detached state,
  repository operation, index bytes, changed paths, statuses, and working files.
- The Git tool labels staged and unstaged paths and provides keyboard-accessible
  Stage and Unstage actions.
- Every index mutation reopens the repository, recomputes the state, and rejects a
  stale fingerprint before changing the index. A stale result refreshes the UI.
- Untracked paths appear in status without placing their content in a diff.

Line and hunk selection, destructive actions, developer commit/amend, reference and
stash management, history, merge editing, and remote operations are not delivered by
this slice.

## Verification

- `dotnet build Harness.slnx --no-restore -m:1 -p:UseSharedCompilation=false`:
  passed with zero warnings.
- Data Access focused tests: passed, including exact-path stage/unstage, stale index,
  path containment, staged/unstaged separation, bounded diffs, and untracked-content
  exclusion.
- Business Logic focused test: passed, preserving source context, expected
  fingerprint, operation, and path.
- Headless Avalonia focused tests: 2 passed, covering accessible actions, conflict
  guidance, and the exact selected-path request.
- `dotnet test Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`:
  732 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.

Paid providers and network Git operations were not used.
