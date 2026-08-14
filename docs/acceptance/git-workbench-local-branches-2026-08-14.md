# Git workbench local branches — 2026-08-14

Task 050 remains in progress. This record covers local branch inspection, create,
switch, rename, and deletion in the original workspace.

## Delivered

- The complete Git fingerprint now includes every sorted reference name and target.
  External branch creation, movement, rename, or deletion invalidates displayed
  index, worktree, commit, and reference actions before mutation.
- The Git tool loads local branches during normal refresh and shows current, tip SHA,
  and merged-into-HEAD state. Controls support refresh, create, switch, rename, and
  delete; remote-tracking branches are not presented as local mutation targets.
- Create and rename validate canonical Git reference names and reject collisions.
  Switching uses safe checkout and reports dirty-content conflicts without losing the
  file or changing HEAD. An in-progress Git operation blocks reference mutation.
- Switching or renaming the current branch first uses the existing save/discard/cancel
  document flow. On success, Harness re-inspects and persists the workspace branch and
  dirty state, then reloads dashboard, goal, editor, tool, and model source context.
- Deletion rejects the current branch. Merged deletion is available normally;
  unmerged deletion requires the prominent force checkbox. Preview binds fingerprint,
  exact local name, full tip SHA, merge status, and force policy to an opaque identity.
- Confirmation repeats the exact tip and consequence and stays disabled until the
  developer acknowledges that recovery is not guaranteed. Apply recomputes the
  preview before deleting the reference.
- Branch actions remain developer-only and original-workspace-only. They do not grant
  goal authority or alter the separate goal commit approval contract.

## Deterministic coverage

- create, rename, and switch using the exact displayed reference fingerprint;
- stale rejection after an external branch appears;
- unmerged deletion rejection and explicit forced deletion;
- dirty checkout conflict preserves content and current branch;
- original-context routing and exact Business Logic request mapping;
- force-delete preview identity, tip SHA, consequence, and apply;
- workspace metadata refresh preserves trust, active selection, and entry point;
- accessible branch list, name, force, action, and confirmation controls;
- headless UI create, switch/context refresh, and delete confirmation flow.

Paid providers, credentials, remote Git, and model mutation tools are not used by this
slice.

## Remaining Task 050 work

Tags, developer worktrees, and stash management follow next. History, blame, merge
editing, remotes, restart, large-repository, and final accessibility coverage remain
open.

## Verification

- Focused Data Access developer Git tests: 23 passed.
- Focused Business Logic branch and workspace tests: 11 passed.
- Focused headless Avalonia Git/branch tests: 8 passed.
- `dotnet test Harness.slnx --no-build --no-restore -p:UseSharedCompilation=false -m:1`:
  767 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.
