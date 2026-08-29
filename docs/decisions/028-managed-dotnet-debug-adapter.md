# ADR 028: Managed .NET debug adapter

- Status: Accepted
- Date: 2026-08-29
- Extends: [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 023](023-typed-developer-dotnet-execution.md)

## Context

Task 052 cannot expose Debug until Harness.NET has a real debugger with a distributable
license, pinned provenance, verified integrity, bounded transport, and complete session
lifecycle. Microsoft's `vsdbg` is proprietary and is not a suitable redistribution
dependency. Resolving an arbitrary executable from `PATH`, accepting a user-entered
adapter path, or downloading an unpinned latest release would weaken the execution
boundary and make support state ambiguous.

Samsung NetCoreDbg is MIT licensed, supports CoreCLR on Linux, Windows, and macOS, and
implements the Debug Adapter Protocol over standard input/output. Release
`3.2.0-1092` publishes platform archives and SHA-256 digests. Harness.NET validated
the Linux x64 artifact against .NET 10 before accepting this decision.

## Decision

Harness.NET manages NetCoreDbg `3.2.0-1092` as an optional, application-private tool.
The supported platform catalog, release URL, archive digest, exact payload names,
payload sizes and payload digests, license URL, and license digest are pinned in code.
Installation is an explicit developer action in Settings. It downloads into a bounded
temporary cache file, verifies the archive before extraction, rejects links, traversal,
unknown or duplicate payloads, verifies every extracted file, installs atomically into
the XDG data directory, and re-verifies the installed payload before reporting Ready.
No model can install, replace, or select a debugger executable.

The product never searches `PATH` for a debugger and never persists a machine-specific
adapter path. Missing, unsupported, corrupt, installing, and ready states are explicit.
Removing a managed adapter is also an explicit developer action and cannot affect a
running session. Updating the pinned adapter requires a source change, renewed license
and compatibility evidence, and updated digests.

The adapter is launched only with `--interpreter=vscode` and communicates over private
standard streams. Harness.NET does not enable NetCoreDbg's TCP server. The Data Access
boundary owns DAP framing and adapter-specific JSON. Business Logic exposes typed
launch/owned-test-attach, breakpoint, thread, stack, scope, variable, stepping, pause,
continue, and termination contracts. Attach remains limited to a process that Harness
started for an exact Test Debug operation; arbitrary process attach is a later,
separately approved risk class. Expression evaluation and value mutation remain absent
until they receive their own explicit developer decisions.

Exact Test Debug starts one confined `dotnet test --no-restore` operation with the
existing typed selector and `VSTEST_HOST_DEBUG=1`. Harness identifies only the waiting
testhost descendant of that owned process, verifies the ancestry immediately before
attach, and attaches the same pinned adapter. It never accepts a PID from UI text or a
model.

## Consequences

- Debug availability reflects a verified executable, not a hopeful command label.
- Installation is reproducible, inspectable, offline after completion, and independent
  of a user repository.
- Debug sessions cannot open a listener or substitute a different adapter.
- Test Debug can reach test code without pretending that the `dotnet test` parent is
  the debuggee.
- The initially pinned platform matrix follows upstream release assets; unsupported
  runtime identifiers remain visibly unsupported.

## Alternatives considered

- Redistributing `vsdbg` was rejected because its license and product restrictions do
  not provide the open redistribution boundary Harness.NET needs.
- Resolving `netcoredbg` from `PATH` was rejected because version, license, and payload
  integrity would be unknown.
- A configurable absolute adapter path was rejected because it creates a durable
  machine-specific execution setting and weakens reproducibility.
- Launching `dotnet test` under a debugger was rejected because tests execute in a
  separate testhost process.
- A DAP TCP server was rejected because private standard streams are sufficient and do
  not add a network listener.
