# Live agent activity status — durable workflow slice

Task 071 remains in progress. This slice makes long local-model calls observable
without presenting synthetic progress.

## Delivered behavior

- The workbench header shows a compact status pill only while a goal workflow
  operation is actively executing.
- The pill reports the current durable phase or role, elapsed operation time, and the
  age of the latest checkpoint observed during this operation.
- Expanding the pill shows the operation name and at most eight timestamped durable
  workflow checkpoints. Before the first checkpoint it says so explicitly.
- The flyout exposes the existing bounded workflow cancellation action.
- Business Logic now carries each persisted checkpoint timestamp in
  `GoalWorkflowActivityView`; Presentation does not infer it from render time.
- A one-second text refresh keeps elapsed and age values current without animation,
  focus changes, percentage claims, hidden reasoning, prompts, or provider payloads.

## Scope still open

Provider streaming state and individual typed-tool start/result events are not yet
part of the Presentation state stream, so this slice does not pretend to distinguish
those sub-phases. Goal/evidence navigation, multi-operation coalescing, completion
notifications through Task 063, graphical accessibility evidence, and a bounded live
local-model capture remain for later Task 071 slices.

## Verification

Pure projection coverage uses fixed timestamps for idle, active, stalled, pre-first-
checkpoint, elapsed-time, last-update-age, no-percentage, and bounded-timeline states.
Workflow tests verify that persisted checkpoint timestamps cross the Business Logic
boundary. All 4 status-focused tests, 313 Business Logic tests, 174 Avalonia
Presentation tests, 22 terminal Presentation tests, and 4 architecture tests pass.
The complete solution builds with zero warnings and errors, the repository metadata
check passes, and `git diff --check` is clean.
