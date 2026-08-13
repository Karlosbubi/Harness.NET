# Typed Run CodeLens acceptance — 2026-08-13

This record covers the first Task 052 execution slice and Task 049's valid Run
CodeLens requirement. Debug is not part of this slice.

## Delivered behavior

- Roslyn emits Run only for the compilation's actual project entry point.
- The target records the project, target framework, declaration, source path, source
  hash, and live-buffer version.
- Business Logic resolves the trusted original workspace or approved goal worktree,
  inspects the project and framework, and rejects stale or unsaved source.
- Data Access starts `dotnet` with an argument list and no shell, Restore, or launch
  profile. Cancellation terminates the process tree.
- The Run Output tool shows original-workspace and goal-worktree runs, supports Stop,
  and keeps Build/Test evidence separate.
- Run state, target, exit code, duration, and errors are durable. stdout and stderr
  are bounded and process-local because applications may print secrets.
- Editor Settings controls Run visibility. Debug visibility has no effect until a
  debugger capability exists.
- CodeLens discovery is document-wide and bounded. Inline actions have a matching
  keyboard- and AT-SPI-accessible toolbar menu that remains available in compact
  layouts. Source-tab reactivation retries an initial presentation that produced no
  semantic actions, and viewport refresh is debounced to avoid cancellation churn.

## Verification

- Roslyn tests cover exact entry-point discovery outside the visible viewport and
  target mapping across the Data Access/Business Logic boundary.
- runner tests cover exact arguments, confinement, no implicit Restore or launch
  profile, and process-tree cancellation.
- service tests cover typed context validation, stale-source rejection, transient
  output, and cancellation.
- SQLite tests cover lifecycle persistence, restart interruption, and the absence of
  output fields from stored records.
- a headless Avalonia test invokes the accessible CodeLens mirror.
- `eng/verify-avalonia-atspi.py` creates a temporary project and approved goal
  worktree, invokes Run through AT-SPI, observes the run identity in Run Output, then
  navigates a compiler error and applies a Roslyn import fix.
- the complete solution passes 714 tests with zero failures, the focused editor gate
  passes 234 checks, and the self-contained Linux x64 publish gate passes.

The verifier restores its temporary fixture before Harness starts. Harness itself
still uses `--no-restore`.

## Secret boundary

No credential or application output is written to documentation, logs, evidence,
backups, or SQLite. The acceptance fixture contains only synthetic source and uses an
isolated XDG home.

## Remaining work

- Solution and project views, Build/Rebuild UI, Test Explorer, launch profiles, Hot
  Reload, and a real debugger adapter remain Task 052 work.
- Task 049 still needs its final large-solution, latency, memory, cancellation,
  analyzer-failure, repeated-context, keyboard, IME, Orca, scaling, Dock restoration,
  and Linux publication audit.
- The Orca path completes its representative workflow and speaks named controls, but
  Avalonia/Dock still exposes unnamed framework containers such as `Grid`, `Border`,
  `ContentPresenter`, `ScrollContentPresenter`, and `VisualLayerManager`. The strict
  speech-leak assertion remains failing until those peers are hidden or named.
