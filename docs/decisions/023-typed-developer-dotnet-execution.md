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

Run identity, source context, state, exit code, duration, and failure survive restart.
Raw stdout and stderr are bounded but process-local because executed applications may
print credentials; Harness.NET does not silently turn output into a durable secret
store. The UI states when output expired after restart.

Run capability is available for a Roslyn-proven project entry point. Debug capability
remains unavailable until a pinned, licensed, integrity-checked debugger adapter
implements launch, breakpoints, threads, stacks, scopes, variables, stepping, and
termination. Harness.NET must not label an ordinary process launch as Debug. A Debug
lens is emitted only when that capability is present.

Agent execution remains outside this slice. Adding a model-callable Run or Debug
operation requires the role, phase, trust, target, and authority policy in Task 052;
the developer UI does not imply agent authority.

## Consequences

- Run CodeLens can bind an exact declaration to a validated project without parsing
  UI text or accepting a shell command.
- Unsaved code is never presented as the code being executed.
- Output and cancellation remain inspectable in the Run output tool.
- Solution Build/Rebuild actions and command-palette entries share the same typed
  lifecycle without granting a generic task or shell capability.
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
