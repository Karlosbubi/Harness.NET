# Git workbench developer worktrees — 2026-08-14

Task 050 remains in progress. This record covers linked developer worktree inspection,
creation, workspace entry, and removal from the original workspace.

## Delivered

- The Git workbench has accessible Changes, Branches, Tags, and Worktrees tabs instead
  of stacking every workflow into one narrow panel.
- Worktree inspection reports the canonical path, branch or detached HEAD, full HEAD
  SHA, dirty and conflict state, lock state and bounded reason, Harness goal ownership,
  workspace registration, and an exact per-worktree Git-state fingerprint.
- A separate complete set fingerprint covers every displayed worktree and changes when
  linked worktrees, refs, HEADs, or their working state change. Create and remove bind
  both the repository fingerprint and this set fingerprint.
- Creation accepts an absolute empty or absent path and exactly one existing local
  branch or valid new branch at the displayed HEAD. It rejects nested worktrees,
  symbolic-link targets, occupied paths, checked-out branches, stale state, unborn
  repositories where needed, and in-progress Git operations.
- Open as workspace sends the selected canonical path through the existing repository
  inspection, entry-point selection, trust, unsaved-document, and source-context reload
  flow. It does not silently trust or select a new source context.
- Removal is available only for a selected linked developer worktree. The original
  worktree, locked worktrees, Harness-managed goal worktrees, and every registered
  workspace are blocked. Dirty or conflicted removal requires an explicit force choice.
- The destructive preview binds repository state, worktree-set state, selected path,
  selected worktree state, branch, HEAD, and force policy to an opaque identity. It
  repeats the exact target and recovery limits and stays disabled until acknowledged.
  Apply recomputes the preview before running the closed Git adapter.
- The adapter uses fixed `git worktree add` or `git worktree remove` argument records,
  disables terminal prompting, drains output without retaining it, kills the process
  tree on cancellation, and exposes no generic Git or shell endpoint.

## Deterministic coverage

- create on a new branch, remove, and recreate from the retained existing branch;
- dirty removal rejection followed by exact forced removal;
- stale linked-set rejection without creating the requested directory;
- Harness-managed worktree detection and removal denial;
- original-context routing and exact repository/set/path/branch mapping;
- dirty force preview, selected-state revalidation, and apply;
- registered and Harness-managed Business Logic protection;
- tabbed Git regression coverage for existing file, hunk, branch, and tag workflows;
- accessible worktree inputs, choices, actions, and confirmation controls; and
- workspace-flow opening, exact creation, and confirmed removal in headless Avalonia.

No provider, credential, remote Git, network, or model mutation authority is used by
this slice. Machine-specific worktree paths are displayed only at runtime and are not
persisted in repository documentation or test fixtures outside synthetic paths.

## Remaining Task 050 work

Stash management follows next. History, blame, merge editing, remotes, restart,
large-repository, submodule, and final accessibility coverage remain open.

## Verification

- Focused Data Access developer Git tests: 30 passed.
- Focused Business Logic developer Git tests: 13 passed.
- Focused headless Avalonia worktree tests: 4 passed.
- Wider headless Avalonia Git workbench regression tests: 17 passed.
- `dotnet test Harness.slnx --no-build --no-restore -p:UseSharedCompilation=false -m:1`:
  786 passed, 0 failed, 0 skipped.
- The solution build completed with 0 warnings and 0 errors.
- `dotnet format --verify-no-changes` passed for every changed C# file. Existing
  unrelated formatting findings in project-secrets files remain outside this slice.

## Live dogfood

The main commit was pushed and the separate live checkout was fast-forwarded to it.
That checkout built with zero warnings and errors and restarted as a normal Harness
desktop process. Live `harness_application`, `harness_git`, and `harness_ui` calls then
confirmed the new application instance, exact pushed HEAD, available closed Git-panel
action, and one untracked private configuration path without returning that file's
contents.

Dogfood also found an MCP catalog ambiguity. `harness_application` called its complete
policy catalog `tools`, although the configured server allowlist did not expose every
entry. The result now reports exact `exposedTools`, labels the complete list
`toolPolicies`, and directs clients to MCP `tools/list` as authoritative discovery.
The focused four-test loopback server suite verifies propagation of the exact exposed
allowlist. Sensitive UI activation remained unexposed and was not bypassed; the four
headless worktree UI tests provide the mutation-path UI evidence.
