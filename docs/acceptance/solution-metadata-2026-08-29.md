# Static Solution metadata acceptance — 2026-08-29

This record covers the second Task 052 slice. It is a bounded static project-system
foundation, not MSBuild evaluation, execution, or a claim that Task 052 is complete.

## Delivered behavior

- Workspace navigation has a keyboard- and AT-SPI-named Solution tab beside the
  existing application/workspace controls; no new durable Dock pane or private
  layout schema is introduced.
- Inspection resolves the registered entry point against the exact trusted original
  workspace or active approved goal worktree. A goal worktree receives the same
  workspace-relative entry point rather than leaking the original absolute path.
- The tree shows the solution/project entry point, `global.json` SDK policy, bounded
  projects, declared target frameworks, SDK, language version, nullable mode, and
  declared project/package references. It also shows typed project kind,
  declared-or-conventional configurations, startup candidacy, the selected installed
  SDK, and whether workload manifests are available. Project and entry-point
  navigation reuses the typed document boundary.
- The adapter reads bounded `.sln`, `.slnx`, project XML, and `global.json` metadata.
  It does not Restore, evaluate MSBuild targets, load analyzers/generators, or execute
  repository code.
- Missing, outside-workspace, oversized, and malformed project declarations appear as
  typed loading-issue nodes while healthy projects remain usable. Messages disclose
  no absolute machine path.
- Refresh is a typed `KeybindingCommand` and command-palette action. Busy, success,
  bounded-result, cancellation, trust, and error states use the shared live status
  indicator.

## Verification

- Business Logic tests prove original/goal-worktree context resolution, relative
  entry-point mapping, translation, and trust rejection before Data Access.
- Existing adapter tests cover `.sln`/`.slnx`, multi-targeting, SDK policy, project
  kind, configurations, startup candidacy, selected SDK/workload-manifest health,
  project and package references, bounds, missing entry points, and malformed metadata.
- Headless Avalonia coverage verifies the semantic tree, source-context request,
  project metadata, shared status, and explicit automation name.
- The complete typed workbench catalog remains identical to the keybinding enum tail,
  now with 46 actions.
- Final verification passed the warning-free solution build, all 872 deterministic
  .NET release-gate tests, repository metadata checks, Linux x64 publish, and the
  production Avalonia AT-SPI workflow.

## Remaining Task 052 work

Launch profiles and typed overrides, Build/Rebuild, Test Explorer, coverage, Hot
Reload, and a real debugger adapter remain open. None is inferred from display text
or represented as available before its typed adapter exists.
