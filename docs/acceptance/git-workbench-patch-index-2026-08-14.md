# Git workbench hunk and line index slice — 2026-08-14

Task 050 remains in progress. This record covers exact hunk and changed-line stage
and unstage on top of the file index slice.

## Delivered

- The shared active-context Git snapshot contains bounded hunk and changed-line
  choices for staged and unstaged patches.
- Each choice has an opaque SHA-256 identity bound to the complete repository
  fingerprint, direction, path, kind, and exact patch. Presentation receives no
  executable arguments or patch payload.
- Applying a choice resolves it again from a freshly opened repository and rejects a
  changed fingerprint before starting Git.
- Data Access invokes only `git apply --cached` with fixed arguments and sends the
  internally generated patch through stdin. It disables terminal prompting, captures
  output without persisting it, supports cancellation and process-tree termination,
  and returns sanitized errors.
- A partial replacement can stage or unstage its deletion and addition independently.
  The remaining index/worktree difference is recomputed after every action.
- The Git panel filters choices by the selected file, distinguishes hunk and line
  actions and their direction, disables inapplicable actions, and provides an
  accessible Whole file action to clear a partial selection.

Whole-file behavior remains the fallback when no hunk or line is selected. Multi-unit
batch selection is intentionally not exposed; each exact index publication is one
reviewed choice.

## Deterministic coverage

- whole-hunk stage and unstage;
- individual replacement-line stage and unstage;
- exact index content after each partial operation;
- stale fingerprint rejection before index mutation;
- active original/approved-goal context and opaque identity forwarding;
- accessible hunk/line list, direction enforcement, and selected-unit dispatch;
- incomplete truncated hunks are not offered as actions.

Paid providers and network Git operations are not used by this slice.

## Verification

- Focused Data Access Git tests: 13 passed after the slice, including 10 index
  mutation cases and quoted Unicode path handling.
- Focused Business Logic tests: 2 passed.
- Focused headless Avalonia Git tests: 3 passed.
- `dotnet test Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`:
  739 passed, 0 failed, 0 skipped.
- The solution build completed with zero compiler warnings.
