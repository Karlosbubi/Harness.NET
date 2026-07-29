# ADR 012: Roslyn code intelligence and verified transformations

- Status: Accepted
- Date: 2026-07-29
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 007](007-semantic-contract-types.md), [ADR 010](010-docked-desktop-workbench.md)

## Context

Harness.NET currently edits C# and .NET project files through exact-baseline text
mutations and shows lexical syntax highlighting in AvaloniaEdit. It does not maintain
a compiler model for an open solution, so manual and model-authored changes can
introduce syntax, binding, or analyzer errors that remain invisible until a later
Build. The editor also has no completion, quick information, signature help, symbol
navigation, references, code actions, or semantic rename.

Code intelligence is central to the product rather than decorative editor chrome.
Harness.NET is specifically a .NET development environment, and agent suggestions
must be checked by deterministic language services whenever the compiler can answer
the question. Text generation must not substitute for a symbol-aware operation such
as rename.

Roslyn can provide the required compiler, workspace, and refactoring facilities in
process. A language-server implementation may later provide isolation, another
language, or a replaceable host, but choosing an LSP wire protocol as the application
contract would leak transport concerns into Business Logic and Presentation.

Loading a real solution can evaluate project files and can load configured analyzers
or source generators. Those components are executable repository or dependency code,
not passive source inspection, and must remain inside the workspace-trust boundary.

## Decision

### Ownership and replaceable boundary

Use an in-process Roslyn workspace as the first code-intelligence implementation.
Roslyn and MSBuild types remain inside Data Access. Data Access exposes only semantic
interfaces, records, and enums for workspace loading, document synchronization,
diagnostics, completion, symbol information, navigation, references, code actions,
and transformations.

Business Logic owns the trusted source-context lifecycle and maps the Data Access
results into presentation-neutral code-intelligence contracts. Presentation owns
editor gestures, transient buffer text, popup placement, diagnostic adornments, and
Problems-tool rendering. No Roslyn, MSBuild, LSP, or AvaloniaEdit type crosses its
owning boundary.

The Data Access interface is capability-oriented rather than Roslyn-shaped. A future
LSP-backed module may implement the same interface and be selected by Host
composition without changing Business Logic or Presentation contracts. The initial
delivery does not run both implementations or add an LSP process in advance.

### Compatibility checkpoint

Before feature implementation, prove the Roslyn host against the actual application
shape. `Harness.DataAccess` already uses packaged `Microsoft.Build` construction APIs,
while an SDK-evaluating `MSBuildWorkspace` commonly locates an installed SDK and must
control which MSBuild assemblies load. Registration also has to happen before any
MSBuild type is loaded. The checkpoint therefore must:

- pin one coherent set of `Microsoft.CodeAnalysis.Workspaces.MSBuild`, C# Workspaces,
  Features, and MSBuild-locator packages compatible with the repository's existing
  Roslyn and Microsoft.Build versions;
- prove SDK discovery from the selected workspace so its `global.json` participates;
- prove `.slnx`, `.sln`, and `.csproj` loading on Linux without restore;
- decide whether the existing non-evaluating inspector can share the located MSBuild
  runtime or must be separated from it;
- prove Headless tests and the self-contained linux-x64 publication, including the
  Roslyn build-host assets required at runtime; and
- report a missing or incompatible installed SDK as Degraded rather than implying
  that the self-contained Harness runtime contains a development SDK.

If an in-process host cannot satisfy assembly isolation, cancellation, or publication
requirements, record an amendment before selecting a dedicated local Roslyn process
or LSP-backed module. That fallback must implement the same semantic boundary and is
not authorization for a remote language service.

### Workspace identity and lifecycle

- Load the explicitly selected `.slnx`, `.sln`, or `.csproj` for one resolved source
  context: either the trusted original workspace or an approved goal worktree.
- Require explicit repository trust before project evaluation or configured analyzer
  and source-generator loading. The trust explanation names those effects.
- Never perform an implicit package restore. Missing SDKs, assets, references, or
  workloads produce an honest degraded workspace with actionable issues.
- Key every session by semantic workspace identity, entry point, source-context kind,
  repository/worktree root, and relevant project-system version. Never reuse compiler
  state across an identity change.
- Load asynchronously with progress and cancellation. Editor and chat remain usable
  while intelligence is loading, degraded, unavailable, or restarting.
- Start with one active foreground session and bounded reusable state. Task 036 may
  introduce a measured least-recently-used cache for multiple workspaces; unbounded
  compiler workspaces are prohibited.
- Dispose a goal-worktree session when its worktree is removed. Workspace switches,
  entry-point changes, external file changes, and accepted mutations invalidate or
  incrementally update the matching session rather than silently serving stale data.

### Document synchronization and result freshness

Presentation sends immutable document snapshots containing semantic context identity,
repository-relative path, exact persisted baseline hash, monotonically increasing
buffer version, current UTF-8 text, and optional caret/span request. Business Logic
validates the path and context before forwarding the snapshot.

Diagnostics and interactive results return the same context and buffer version. The
adapter applies a result only when both still match the active document. Editing is
debounced and cancellable; a newer request supersedes older completion, quick-info,
navigation, or diagnostic work. Compiler work never blocks the UI thread.

Diagnostics use semantic path, range, severity, stable diagnostic ID, message,
source, project, and buffer version values. Closed result states distinguish Ready,
Loading, Degraded, Cancelled, Failed, and Stale rather than encoding those conditions
as empty diagnostic lists.

### Human edits and model-authored edits

Manual editing remains responsive and permissive. Roslyn reports syntax, compiler,
and enabled analyzer diagnostics continuously, but incomplete code is not prevented
from being typed or saved. The editor exposes diagnostics inline and in a real
Problems tool, and Build/Test evidence remains independently available.

Every model-authored mutation receives a code-intelligence validation disposition
before the durable mutation boundary writes it. Changes to C#, project files, and
other inputs represented by the loaded .NET workspace are checked against an
ephemeral candidate Roslyn solution. A documentation-only or otherwise unsupported
file change is recorded as NotApplicable rather than falsely described as compiler-
valid:

1. Resolve the exact approved goal source context and verify all baseline hashes and
   delegated file-area grants.
2. Apply the proposed text changes only to an in-memory candidate solution.
3. Compare candidate diagnostics with the matching baseline, at least across every
   affected project.
4. Reject the mutation before writing when it introduces a new compiler Error.
5. Preserve introduced warnings and analyzer findings as structured validation
   evidence visible to the agent, reviewer, and user.
6. Apply the accepted files through one atomic, baseline-protected batch mutation and
   incrementally update the live compiler workspace.
7. Re-run validation against the applied state. A mismatch or post-apply failure is
   explicit durable evidence and cannot be reported as a successful verified edit.

Existing repository diagnostics do not make unrelated work impossible. Validation
compares stable baseline and candidate identities and reports retained, resolved, and
introduced diagnostics. A model call cannot describe its own text as compiler-valid;
only the code-intelligence result supplies that evidence. If code intelligence is
unavailable, a model-authored change that requires compiler validation fails closed
rather than being marked complete. NotApplicable remains an explicit, reviewable
result for a change outside the compiler workspace.

An agent recommendation that includes an actionable candidate patch uses the same
pipeline before Presentation labels it validated or ready to apply. Free-form design
advice may remain conversational, but it cannot display a compiler-valid badge or be
converted into a trusted edit without a deterministic validation result.

### Deterministic transformations

Expose semantic code actions as typed preview-first operations. Rename is the first
required transformation:

- The request identifies the source context, document, position, resolved symbol
  identity, new validated identifier, and allowed file areas.
- Roslyn resolves references and conflicts; a model never constructs the replacement
  file list or performs repository-wide textual substitution.
- The preview contains every affected repository-relative path, exact baseline hash,
  bounded change summary, conflicts, and candidate diagnostics.
- Applying a preview revalidates its context, symbol, baselines, grants, and preview
  fingerprint, then writes all affected files atomically or none of them.
- Post-apply diagnostics and the complete applied diff become evidence. An agent may
  request the operation only within its existing role and task capabilities; a human
  may invoke it in an editable approved goal context.

The same contract shape may later support organize-usings, formatting, compiler code
fixes, and additional refactorings. Each operation stays explicit; there is no generic
"execute arbitrary Roslyn action" tool.

### Trust, analyzers, and privacy

Workspace trust explicitly covers project evaluation and the loading of configured
analyzers/source generators for code intelligence. Untrusted repositories receive
bounded lexical viewing only. Restore remains separately approval-gated, and code
intelligence never downloads SDKs, workloads, packages, or language servers.

Source text, symbols, diagnostics, and completion inputs remain local for the
in-process implementation. A future remote language service would require a new
privacy and authorization decision; it cannot be introduced as an ordinary module
configuration change.

Transient buffers, completion lists, and hover content are not persisted. Durable
model-edit and transformation evidence records bounded diagnostic identities,
summaries, fingerprints, and the existing approved diff/audit content without adding
repository metadata.

### Performance and acceptance

The implementation establishes representative small and large .NET workspaces and
records cold load, warm diagnostic, completion, navigation, memory, and cancellation
behavior. The initial interaction targets are a non-blocking UI, updated diagnostics
within 750 ms of the last edit in a warm workspace, and a warm completion response
within 200 ms at the 95th percentile on the recorded development machine. Missed
targets remain visible evidence and drive profiling; they are not hidden by extending
timeouts.

Deterministic tests cover stale-result rejection, missing restore assets, workspace
switches, cancellation, baseline diagnostic comparison, model-edit rejection,
warning evidence, atomic multi-file rollback, rename conflicts, path grants, and
post-apply verification. Integration tests use real small and representative large
solutions without invoking a model or paid provider. Avalonia tests cover inline
diagnostics, Problems navigation, completion keyboard behavior, quick info, go to
definition, and accessible names.

## Consequences

- Harness.NET gains a .NET-specific correctness boundary shared by the editor and
  agents rather than two unrelated notions of valid source.
- In-process Roslyn provides the richest initial .NET integration but introduces
  measurable memory, project-load, analyzer-execution, and lifecycle responsibilities.
- Business Logic and Presentation remain independent of the implementation and can
  accept a future local LSP-backed module.
- Atomic multi-file mutation becomes required before semantic rename can ship.
- Trust copy and acceptance tests must acknowledge analyzer and generator execution.

## Alternatives considered

- Treating `dotnet build` as the only validation was rejected because feedback is too
  late for editing and model review.
- Using LSP messages as cross-layer contracts was rejected because transport and
  server-specific shapes would define product behavior.
- Starting with an external language server was deferred because Roslyn is the
  product's primary .NET integration and the accepted architecture prefers one
  process while practical.
- Letting models implement rename with text search was rejected because the compiler
  can resolve symbol identity and references deterministically.
- Blocking manual saves whenever diagnostics exist was rejected because temporarily
  invalid buffers are normal during interactive editing.
