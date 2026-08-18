# Git workbench conflict editor — 2026-08-15

Task 050 remains in progress. This record covers explicit three-way conflict editing
for the active original workspace or approved goal worktree.

## Delivered

- The Git workbench has an accessible Conflicts tab. It lists at most 500 unresolved
  index paths with exact base, ours, and theirs blob identities and reports when the
  list is truncated.
- Selecting a text conflict shows read-only base, ours, and theirs panes beside one
  editable result. Use-base, use-ours, and use-theirs actions copy only available,
  bounded text. Missing, binary, and oversized sides are identified instead of being
  decoded or changed silently.
- Common conflict markers are parsed into one-based unresolved regions. Incomplete
  markers remain visible as incomplete rather than being treated as resolved.
- C# result edits are sent to a separate in-process Roslyn session after a short
  debounce. The editor shows bounded diagnostics for the exact current result without
  replacing the normal source editor's semantic buffer.
- Save requires the complete displayed Git fingerprint, canonical conflict path, and
  exact SHA-256 of the working result. The UTF-8 result is limited to 1 MiB and is
  written through the existing confined atomic file editor. A stale Git state or file
  hash rejects the operation.
- Save changes only the working result. The index remains conflicted until the
  developer separately chooses **Stage saved result** against the new fingerprint
  and saved-content hash. Data Access rejects that second action if the result changed
  or any displayed marker region remains. Harness does not auto-resolve or auto-stage.
- Harness keeps one semantic buffer per path. A conflict result cannot be opened
  while the same source-context path is open in the source editor, and the source
  editor cannot open a path active in the conflict editor.
- Unsaved result edits participate in the normal save/discard/cancel flow for exit,
  workspace changes, conflict switches, and manual refresh. Automatic Git refresh
  preserves an unsaved result instead of replacing it.

No shell, network, credential, provider, remote Git, goal approval, or automatic merge
authority was added.

## Deterministic coverage

- exact base, ours, and theirs blob/text mapping from a real conflicted index;
- unresolved marker regions and result content hash;
- atomic exact-baseline save that leaves the index conflicted;
- separate exact-fingerprint staging that resolves the selected index path;
- stale working-result rejection without overwriting newer content;
- approved-goal source-context preservation through Business Logic;
- accessible panes and actions, Roslyn submission of the exact result, and separate
  save/stage commands in headless Avalonia; and
- exit cancellation and explicit discard for an unsaved merge result.

Focused verification passed 42 repository tests, 18 Business Logic Git tests, and 18
Avalonia Git tests. The full deterministic solution passed 810 tests with no failures
or skips. The solution build and changed-file formatter completed with zero warnings,
errors, or formatting changes.

## Remaining Task 050 work

Explicit fetch, pull, and push with remote authority and credential isolation remain.
Submodule, network-failure, cancellation, restart, large-repository, AT-SPI, Linux x64
publish, full deterministic verification, secret scanning, and the separate final goal
commit remain open.

## Handoff — 2026-08-18

Work stopped at the developer's request so Task 050 can move to another environment.
The conflict slice is committed and pushed as `afa4a32` (`Add exact three-way conflict
editing`). The primary and `Harness.NET-live` worktrees were both fast-forwarded to
that commit, and the live host started successfully on its configured loopback MCP
endpoint before shutdown.

Resume with remote synchronization. Implement fetch, pull, and push as explicit typed
developer actions under ADR 024, including sanitized remote/refspec display,
divergence preview, credential-source reporting without values, cancellation, network
failure, fast-forward policy, and force-with-lease only. Then finish the remaining
Task 050 acceptance matrix listed above. Do not mark Task 050 delivered based on this
conflict-slice evidence alone.
