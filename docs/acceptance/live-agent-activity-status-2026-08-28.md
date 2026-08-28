# Live agent activity status

Task 071 is complete. Long local-model calls are observable without presenting
synthetic progress or exposing model content.

## Delivered behavior

- The workbench header shows a compact status pill only while a goal workflow
  operation is actively executing.
- The pill reports the current durable phase or role, elapsed operation time, and the
  age of the latest observable provider, typed-tool, evidence, or workflow update.
- Expanding the pill shows the operation name and at most eight timestamped items from
  each bounded workflow, typed-evidence, and session-activity source. Before the first
  checkpoint it says so explicitly.
- While an edit, transformation, Build, Test, Restore, visual capture, or toolset grant
  has durable `Running` evidence, that operation replaces the coarser waiting phase.
  The display never includes its request or result payload.
- The flyout exposes the existing bounded workflow cancellation action.
- Business Logic now carries each persisted checkpoint timestamp in
  `GoalWorkflowActivityView`; Presentation does not infer it from render time.
- A one-second text refresh keeps elapsed and age values current without animation,
  focus changes, percentage claims, hidden reasoning, prompts, or provider payloads.
- Typed evidence is loaded immediately, then polled while active. Results are accepted
  only for the goal and operation that requested them, and the timeline excludes
  checkpoints from earlier operations.
- A session-only Business Logic activity stream records only goal, role, closed
  lifecycle phase, safe operation identity, and timestamps. Provider calls distinguish
  contacting from receiving; all typed functions, including read-only inspection and
  eligible MCP functions, distinguish running, completed, failed, and cancelled.
- Session history retains only 32 sanitized items and Presentation shows at most eight.
  Prompt/response text, hidden reasoning, model identifiers, tool arguments, tool
  results, credentials, and provider payloads never enter the contract.
- Host composition adds one singleton activity owner and one read-only interface
  mapping in the Goals module. The registration-parity guard still proves every
  pre-split descriptor and separately asserts these two reviewed additions.
- Concurrent provider and typed-tool activity coalesces into one deterministic count.
  Matching durable and session tool states are not double counted. A review correction
  is labeled as an Implementer retry.
- The flyout navigates to the selected goal Conversation and enables workflow-evidence
  navigation only when durable evidence exists. Existing cancellation remains present.
- Completion, failure, and cancellation continue to hand off to the shared Task 063
  workbench-event surface rather than creating another notification system.

## Verification

Pure projection coverage uses fixed timestamps for idle, active, stalled, pre-first-
checkpoint, elapsed-time, last-update-age, retry, coalescing, no-percentage, bounded-
timeline, and operation-isolation states. Business Logic tests exercise provider and
tool start/receive/complete lifecycles, broken-observer isolation, sanitized contracts,
and the 32-item retention bound. Headless Avalonia renders the status at a 320-pixel
window width, proves it does not steal composer focus, verifies screen-reader names and
navigation controls, and proves the affordance disappears after recovery/completion.

An explicitly authorized live test used `harness-ornith:9b-v1` through Ollama at an
8,192-token maximum. The real stream transitioned from waiting to receiving, exposed
no prompt or response content in the activity snapshot, retained only its sanitized
completion, and passed in about seven seconds. The model remained fully loaded in GPU
VRAM afterward and the Proxmox kernel recorded no new AMD GPU, PSP, or reset errors.

All 867 deterministic .NET tests pass: 16 analyzer, 4 architecture, 325 Business
Logic, 291 Data Access, 22 Host, 185 Avalonia Presentation, 22 terminal Presentation,
and 2 Avalonia UI tests. The 10 status-focused Presentation tests, 6 deterministic
activity-lifecycle tests, explicit live Ollama test, architecture suite, and reviewed
Host registration-parity test pass. The complete solution builds with zero warnings
and errors, repository metadata verifies, and `git diff --check` is clean.

The release gate also exposed an older recovery-fixture omission: migration 031 added
the role reasoning policy, but the simulated schema-17 rollback removed migration
records only through 030. The fixture now replays 031 and prints bounded startup logs
on future recovery failures. Self-contained Linux x64 publish, clean startup,
persistence, verified backup, integrity, restore, and schema-17-to-31 recovery all
pass with the documented `sqlite3` prerequisite supplied from an unprivileged local
package extraction on this runner.
