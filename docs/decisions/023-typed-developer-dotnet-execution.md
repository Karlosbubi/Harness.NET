# ADR 023: Typed developer .NET execution

- Status: Accepted
- Date: 2026-08-13
- Extends: [ADR 012](012-roslyn-code-intelligence.md), [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 020](020-editor-platform-boundary-and-morgania-evaluation.md)

## Context

Task 049 requires Run and Debug CodeLens only when a declaration has a valid typed
execution target. Task 052 requires ordinary developer execution without turning the
Run output tool into a terminal or granting agents a generic command surface. The
current CodeLens contract has `Run` and `Debug` enum members but no project, framework,
declaration, source-context, or capability identity. Invoking either action therefore
cannot be safe or accurate.

## Decision

Roslyn identifies an executable declaration from the exact live compilation. An
execution target contains a closed target kind, workspace-relative project path,
target framework when known, and stable declaration identity. A lens carries that
target; Presentation never reconstructs a command from display text.

Business Logic resolves the active trusted original workspace or approved goal
worktree, verifies the project against bounded project inspection, and rejects dirty
editor buffers before execution. Data Access starts `dotnet` directly with an
argument list, a confined project path, no shell, no implicit Restore, bounded output,
process-tree cancellation, and telemetry disabled. Developer runs, builds, and
rebuilds have typed identities and durable state separate from goal tool evidence.
Build and Rebuild use closed operation values, an inspected project and optional
inspected configuration/framework; Rebuild maps to `dotnet build --no-incremental`.
They share the confined, shell-free, no-implicit-Restore runner and never reconstruct
arguments from UI text.

A Test Explorer Run action carries the compiler-discovered stable test hash, fully
qualified test name, and inspected project as semantic values. Business Logic
re-resolves the exact trusted source context and project and accepts only the bounded
closed test-name grammar. Data Access invokes `dotnet test <project> --no-restore
--filter FullyQualifiedName=<name>` through the same direct argument-list runner.
Containing-type and project nodes derive stable scoped identities from their inspected
project and compiler hierarchy. A closed Exact, Type, Project, or Selection scope selects either
an exact equality filter, a bounded fully-qualified-name prefix filter, or no filter;
each selection starts exactly one `dotnet test` process rather than fan-out child
processes. Business Logic recomputes group identities before granting execution.
Selection carries 2–24 distinct compiler-discovered exact names from one inspected
project. Harness sorts and hashes them, then constructs the VSTest OR filter inside
Data Access; neither Presentation nor a model may supply filter syntax.
Test operations direct the standard TRX logger to a unique Harness.NET-private cache
directory. Data Access parses at most 2,000 cases from bounded XML with DTD processing
disabled and deletes the directory immediately. Fully qualified name, closed outcome,
and duration are durable; adapter display text is process-local because parameterized
names may contain runtime values. Raw XML, test output, stacks, and failure messages
are never persisted by this lifecycle.

Coverage is imported only through a developer-selected workspace-relative Cobertura
XML file. Data Access confines the report and every mapped C#, F#, or Visual Basic
source to the exact active original workspace or approved goal worktree, rejects
symbolic and oversized inputs, disables DTD processing and external resolution, and
bounds files and instrumented lines. Business Logic durably records the source
context, report path and SHA-256, closed format, bounded producer/version, timestamps,
unmapped count, truncation, and exact relative source-line hit counts. Persistence
retains only the latest ten imports per exact source context. It does not
persist report XML, machine paths, branch-rate summaries, stacks, messages, or source
content. Presentation labels uncovered lines as evidence rather than defects and
navigates them through the existing typed document boundary. Harness never searches
for or automatically adopts a stale coverage report and does not imply that a test
collector or adapter is installed.
There is no shell string, implicit Restore, adapter discovery process outside
`dotnet test`, or model-facing execution authority. The operation records test
identity, state, exit code, duration, cancellation, and errors; stdout/stderr remain
bounded and process-local under the existing privacy rule.

Run identity, source context, state, exit code, duration, and failure survive restart.
Raw stdout and stderr are bounded but process-local because executed applications may
print credentials; Harness.NET does not silently turn output into a durable secret
store. The UI states when output expired after restart.

Run capability is available for a Roslyn-proven project entry point. Debug capability
remains unavailable until a pinned, licensed, integrity-checked debugger adapter
implements launch, breakpoints, threads, stacks, scopes, variables, stepping, and
termination. Harness.NET must not label an ordinary process launch as Debug. A Debug
lens is emitted only when that capability is present.

Every developer Run confirmation exposes optional typed one-run overrides: one exact
inspected project-profile name, one workspace-relative existing working directory, up
to 32 application arguments, and up to 32 distinct environment entries. Presentation
accepts one argument or `NAME=value` entry per line and summarizes only the profile,
argument count, environment names, and relative directory. Business Logic revalidates
the profile against the exact inspected project and source context. Data Access
revalidates all bounds, confines the directory, passes arguments through
`ProcessStartInfo.ArgumentList` after `--`, and sets environment entries directly; no
shell or command string exists. Runner-owned telemetry/no-logo variables remain locked.
Overrides and environment values are process-local and
are absent from durable lifecycle metadata, restart reconstruction, logs, and status.

Agent execution remains outside this slice. Adding a model-callable Run or Debug
operation requires the role, phase, trust, target, and authority policy in Task 052;
the developer UI does not imply agent authority.

## Consequences

- Run CodeLens can bind an exact declaration to a validated project without parsing
  UI text or accepting a shell command.
- A visible confirmation can specialize one Run without creating a persistent launch
  configuration or exposing environment values in lifecycle history.
- Unsaved code is never presented as the code being executed.
- Output and cancellation remain inspectable in the Run output tool.
- Solution Build/Rebuild actions and command-palette entries share the same typed
  lifecycle without granting a generic task or shell capability.
- Compiler-discovered Test Explorer rows can start one exact test and reuse the same
  cancellation, transient-output, failure-history, and restart-reconciliation path.
- Project and containing-type rows reuse that lifecycle through a closed scoped
  selector and start one process per user action.
- Arbitrary same-project multi-selection remains one bounded process and persists its
  exact sorted member identities for restart-safe history.
- Adapter case summaries survive restart without turning TRX or failure output into a
  durable content store; incomplete or malformed result capture remains explicit.
- Explicit Cobertura imports provide restart-safe, exact-context line navigation with
  provenance while remaining independent of test execution and collector availability.
- Debug is visibly absent rather than misleading until its actual adapter is ready.
- The same typed target and execution identity can later back Solution, Test Explorer,
  launch profiles, Hot Reload, and debugger UI.

## Alternatives considered

- Treating `dotnet run` as Debug was rejected because it provides no debugger.
- Running a command string from CodeLens was rejected because it bypasses confinement
  and creates a generic execution surface.
- Reusing goal Build/Test evidence was rejected because normal developer runs may
  target the original workspace and have different authority and lifecycle.
- Implicitly saving before Run was rejected because manual edits belong to the user.
