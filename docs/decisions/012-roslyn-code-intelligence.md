# ADR 012: Roslyn code intelligence and verified transformations

- Status: Accepted
- Date: 2026-07-29
- Amended: 2026-08-28
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

### Compiler-backed test discovery

Discover source tests from the exact active Roslyn solution and resolved attribute
symbols. The bounded catalog recognizes xUnit, NUnit, and MSTest test attributes,
including derived attributes, and returns stable semantic identities, framework,
parameterization, bounded traits, project path, source path, and exact source range.
Search and paging operate over this catalog. File names, text patterns, restored test
adapters, and executed discovery processes are not authoritative test evidence.
Framework filtering uses the same closed xUnit/NUnit/MSTest value before deterministic
paging. Presentation may additionally filter the exact source-context lifecycle view;
it does not reinterpret compiler framework identity or invent a test result.

Presentation may build a project/type/test hierarchy and navigate through the typed
source destination. Discovery never restores, loads a test assembly, or executes
repository code. Running or debugging a discovered test uses the separately governed
typed developer execution lifecycle in ADR 023; discovery alone grants no process or
agent authority.

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

The closed catalog may admit a provider's cross-document result only by explicit
policy. The preview reports every physical source file, whether the active document
changes, exact persisted baselines, complete replacement text, diagnostic deltas, and
one fingerprint. Added or removed documents, project/reference changes, generated or
external targets, inconsistent linked files, more than 100 files, and previews over
10 MiB fail closed. Add Parameter and Replace Property/Method are the first admitted
cross-document providers; all other providers remain document-confined.

Do not provide generic “execute Roslyn action” or model-authored text-search rename.

#### Amendment: bounded candidate repair before model retry

Before rejecting a model-authored C# candidate, Business Logic may ask the existing
closed Roslyn transformation path to repair an introduced missing-type diagnostic.
This is a deterministic compiler stage, not another agent role or a probabilistic
classifier.

The stage is deliberately narrow:

- it considers only introduced compiler `CS0246` or `CS0103` diagnostics in the
  candidate document;
- it examines at most four diagnostics and requires exactly one compiler-proven
  missing-import namespace at each position;
- it applies only the existing `AddMissingImport` preview to the in-memory candidate,
  preserves the original exact baseline and delegated file-area checks, and records
  every applied namespace as typed mutation evidence;
- it accepts the resulting candidate only after the normal complete candidate
  validation reports no introduced compiler warning or error.

Zero or several candidates, multi-file output, a changed preview, unsupported source,
or any remaining diagnostic keeps the original fail-closed behavior and leaves the
decision to the Implementer or developer. The stage does not repair nullable-flow
warnings, tests, behavior, arbitrary syntax, or select among general code actions.
Expanding that scope requires measured acceptance evidence and another reviewed
decision amendment.

### Virtual source navigation

Definition, usage, and implementation results may point to repository source,
source-generator output, or metadata. Generated and metadata results use opaque
handles bound to the active Roslyn session, exact source path, buffer version, and
source-text hash. Resolving a handle recomputes the project and compilation identity.
The result names the project version, target framework, configuration, assembly, and
compilation identity.

Generated documents come from Roslyn's public source-generator document APIs.
Metadata documents are locally generated public/protected signature views built with
public Roslyn symbol and syntax APIs. They do not claim to decompile method bodies.
Do not depend on Roslyn's internal metadata-as-source services. Full decompilation
requires a separately reviewed maintained dependency, license and attribution review,
package/SBOM evidence, tests, and an amendment to this decision.

#### Amendment: bounded metadata decompilation

Adopt `ICSharpCode.Decompiler` `10.1.1.8388` behind the Data Access boundary for
metadata navigation. This is the stable ILSpy 10.1.1 engine release from upstream
commit `1377eb6e7351b21112858c8c1df39848f40181ec`, published from
`refs/heads/release/10.1`. The package and upstream project use the MIT license. The
NuGet archive SHA-512 is
`1512c9e2748b6745616110fbbc96726876264f39307b1d19405090abe03bbfb87f4aa5ba0490112160ef8ab6fbe996eb17e6cf02c0bda1311115f0cf2e1bea37`.
Its declared .NET Standard 2.0 dependencies are `System.Collections.Immutable >=
9.0.0` and `System.Reflection.Metadata >= 9.0.0`; the .NET 10 runtime already supplies
both. The package contains an SPDX 2.2 manifest and a repository signature. No source
is copied or vendored.

The adapter receives only Roslyn-resolved metadata symbols and exact compilation
references. It decompiles one containing type from an existing local managed assembly,
never a user-supplied arbitrary path. Framework facade/reference assemblies may map to
the matching loaded .NET runtime assembly by exact assembly identity. Resolution is
local and bounded: no download, Restore, process launch, assembly load, reflection,
or project-code execution. Output remains a session-bound, size-limited, read-only
virtual document. If an implementation assembly or method body is unavailable, the
existing signature view remains as an explicitly labeled fallback with a typed issue.

`ICSharpCode.Decompiler` types stay in Data Access. Business Logic, Presentation,
model tools, and inbound MCP continue to use the existing virtual-document contracts.
Package upgrades require the normal dependency, license, integrity, API, publish, and
SBOM review. Rollback removes the package and adapter and restores the existing
signature renderer without changing those upper-layer contracts.

Virtual documents are bounded, read-only, session-local, excluded from layout and
ordinary document persistence, and never written into a user repository. Role tools
resolve their text before closing a short-lived session so a model never receives an
unusable handle. Stale handles fail closed.

### Exact-context inspection

Expose syntax tree, semantic symbol details, generated-source inventory, and
Intermediate Language through four named read-only operations on one typed request.
Each result carries the exact source path, buffer version, project version, target
framework, configuration, assembly, and compilation identity. Bound item counts and
text size; cancellation and stale buffers fail closed.

Build IL by emitting the exact Roslyn compilation to memory and reading ECMA-335
metadata. Do not execute project code, restore, write an assembly, invoke an arbitrary
disassembler, or accept a free-form Roslyn query. Presentation, role tools, and
inbound MCP use the same Business Logic contract. Transient developer views are
read-only and excluded from normal layout persistence.

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
