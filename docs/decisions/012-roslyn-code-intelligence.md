# ADR 012: Roslyn code intelligence and verified transformations

- Status: Accepted
- Date: 2026-07-29
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 007](007-semantic-contract-types.md), [ADR 010](010-docked-desktop-workbench.md)

## Context

Text editing and later Build feedback are not enough for a .NET IDE. Users and models
need compiler diagnostics, completion, navigation, and symbol-aware transformations.
Project evaluation may execute configured analyzers and generators, so it requires
workspace trust.

## Decision

### Boundary

Use in-process Roslyn first. Keep Roslyn and MSBuild types in Data Access. Expose
capability-oriented interfaces, records, and enums for loading, synchronization,
diagnostics, completion, symbols, navigation, and transformations.

Business Logic owns trusted source-context lifecycle and validation policy.
Presentation owns editor input, transient buffers, popup placement, adornments, and
Problems rendering. No Roslyn, MSBuild, LSP, or AvaloniaEdit type crosses its owner.

A future local LSP adapter may implement the same contracts. Do not use LSP messages
as application contracts and do not add a second implementation before it is needed.

### Compatibility

Pin one compatible Roslyn Workspaces/Features, MSBuild Workspace, and MSBuild Locator
set. Register the SDK selected by the workspace `global.json` before loading MSBuild
types. Support `.slnx`, `.sln`, and `.csproj` on Linux without implicit restore.

Headless tests and self-contained Linux publish must include required Roslyn build-host
assets. Missing SDKs, assets, references, or workloads produce a typed degraded state.
If in-process isolation or publication fails, record an ADR amendment before using a
local process or LSP host.

### Workspace lifecycle

- Load one trusted original workspace or approved goal worktree at a time.
- Key sessions by workspace, entry point, source kind, root, and relevant project
  version.
- Load asynchronously with progress and cancellation.
- Keep chat and editing available while intelligence loads or fails.
- Never restore, download SDKs, or install workloads.
- Invalidate or update state on workspace, entry-point, worktree, file, or accepted
  mutation changes.
- Dispose removed goal-worktree sessions.

### Buffer freshness

Presentation sends immutable snapshots containing source-context identity, relative
path, persisted baseline hash, increasing buffer version, UTF-8 text, and optional
caret/span. Results return the same identity and version. Presentation discards stale
results. New requests cancel older diagnostic or interactive work. Roslyn does not run
on the UI thread.

Use explicit Ready, Loading, Degraded, Cancelled, Failed, and Stale states. An empty
result is not an error state.

### Manual and model edits

Manual editing remains permissive. Diagnostics do not block typing or save.

Model-authored changes to compiler-managed files must pass this sequence:

1. Verify source context, exact baselines, and delegated paths.
2. Apply changes to an in-memory candidate solution.
3. Compare baseline and candidate diagnostics for affected projects.
4. Reject new compiler errors before disk write.
5. Record introduced warnings and analyzer findings.
6. Apply accepted files atomically with baseline checks.
7. Validate the persisted result and record mismatches or failures.

Existing diagnostics are classified as retained, resolved, or introduced. If required
code intelligence is unavailable, fail closed. Unsupported changes are explicitly
`NotApplicable`; they are not described as compiler-valid.

Recommendations with patches use the same validation before being marked ready to
apply. Free-form advice receives no validation claim.

### Deterministic transformations

Expose closed preview/fingerprint/apply operations. Rename, document/selection/
changed-span formatting, exact-range paste formatting, supported typed-trigger
formatting, import organization, unused-import cleanup, and missing-type import fixes
are implemented. Changed spans come from the current and persisted Roslyn syntax
trees; automatic formatting remains settings-managed and never saves. Roslyn resolves
symbol identity or the exact import candidate,
conflicts, affected paths, baselines, and diagnostic changes. Missing-import discovery
returns a namespace only after an in-memory insertion binds the unresolved type at the
caret. Apply recomputes context, grants, baselines, and fingerprint, then writes all
files atomically or none. Record the applied diff and diagnostics.

Do not provide generic “execute Roslyn action” or model-authored text-search rename.

### Trust and privacy

Workspace trust covers project evaluation and configured analyzer/generator loading.
Untrusted workspaces receive lexical viewing only. Source and interactive semantic
data stay local. A remote language service requires a separate privacy decision.

Do not persist transient buffers, completion lists, or hover content. Persist bounded
validation and transformation evidence where the workflow requires audit history.

### Performance

Initial targets on the recorded development machine:

- warm diagnostics within 750 ms after the last edit;
- warm completion p95 within 200 ms;
- no compiler work on the UI thread.

Record missed targets rather than extending timeouts to hide them.

## Consequences

- Editor and agents share one compiler-backed definition of source state.
- In-process Roslyn adds memory, load, analyzer, and lifecycle costs.
- Business Logic and Presentation can accept a future local LSP implementation.
- Semantic transformations require atomic multi-file writes.
- Trust UI must name project evaluation and analyzer/generator execution.

## Alternatives considered

- Build-only validation is too late for editing and agent preflight.
- LSP messages would leak transport into application contracts.
- An external language server adds process and deployment cost before it is needed.
- Text-search rename ignores compiler symbol identity.
- Blocking manual saves on diagnostics breaks normal editing.
