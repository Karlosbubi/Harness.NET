# Git workbench local tags — 2026-08-14

Task 050 remains in progress. This record covers local tag inspection, creation, and
deletion in the original workspace.

## Delivered

- Normal Git refresh lists local tags with exact names, peeled commit SHAs, annotation
  state, and annotation messages.
- Creation targets the exact displayed HEAD and Git fingerprint. It validates the tag
  reference, rejects collisions and in-progress Git operations, and refreshes the
  complete reference state after mutation.
- Lightweight creation needs only a name. Annotated creation requires a non-empty,
  bounded message and configured `user.name` and `user.email`; credentials are neither
  read nor exposed.
- Deletion preview binds the exact fingerprint, name, and peeled target SHA to an opaque
  identity. Confirmation repeats the target and consequence and stays disabled until
  the developer acknowledges that recovery is not guaranteed. Apply recomputes the
  preview before deleting the exact reference.
- Tag actions remain developer-only and original-workspace-only. They grant no goal,
  network, remote, or model authority.

## Deterministic coverage

- lightweight and annotated creation followed by exact deletion;
- configured identity and bounded-message requirements;
- stale rejection after an external reference change;
- original-context routing and exact Business Logic request mapping;
- deletion preview identity and revalidation;
- accessible tag inputs, actions, list, and destructive confirmation controls; and
- headless UI annotated creation and exact deletion flows.

Paid providers, credentials, remote Git, and model mutation tools are not used by this
slice.

## Remaining Task 050 work

Developer worktrees and stash management follow next. History, blame, merge editing,
remotes, restart, large-repository, and final accessibility coverage remain open.

## Verification

- Focused Data Access developer Git tests: 26 passed.
- Focused Business Logic developer Git tests: 10 passed.
- Focused headless Avalonia tag tests: 3 passed.
- `dotnet test Harness.slnx --no-build --no-restore -p:UseSharedCompilation=false -m:1`:
  775 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.

## Live dogfood

The live-copy synchronization and reference-fingerprint check are recorded in a
follow-up commit after the executable is rebuilt and restarted.
