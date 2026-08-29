# Developer project debugger acceptance — 2026-08-29

This record covers the fifteenth Task 052 slice. It completes managed project-entry
Debug; owned Test Debug remains the next slice.

## Delivered behavior

- Debug becomes available only when the application-private pinned NetCoreDbg payload
  verifies Ready. Harness never resolves an adapter from `PATH`, accepts a custom
  executable, or opens an adapter network listener.
- A Debug CodeLens originates from Roslyn's exact project entry-point declaration.
  Start reuses Run's trusted original-workspace/approved-worktree resolution,
  inspected project and framework, persisted-source SHA-256 check, and bounded typed
  profile, working-directory, argument, and environment validation.
- Data Access launches the fixed `dotnet` host and exact `dotnet run --no-restore`
  arguments through NetCoreDbg's private standard streams. DAP headers, JSON depth,
  message size, request duration, collections, text, and event buffering are bounded.
- The accessible Debug workspace shows the verified Roslyn entry-point breakpoint,
  state, threads, call stack, scopes, expandable variables, and transient bounded
  debuggee/adapter output. It offers Continue, Pause, Step Over, Step Into, Step Out,
  Stop, and exact confined source navigation.
- Source paths reported outside the active source root are withheld. Natural exit,
  adapter failure, explicit Stop, cancelled start, and application shutdown dispose
  the adapter and owned process tree. Expression evaluation, variable mutation,
  arbitrary attach, and TCP transport remain absent.

## Verification

- DAP framing tests cover fragmented headers and bodies, malformed or duplicate
  lengths, unsupported headers, invalid JSON, oversized input/output, truncation, and
  serialized writes.
- The fake adapter lifecycle covers initialize, launch, breakpoints,
  configuration-done, stopped events, threads, stack, scopes, variables, stepping,
  disconnect, exact arguments, and absence of server configuration.
- Business Logic tests cover adapter readiness, breakpoint mapping, stopped-state
  inspection and commands, explicit Stop, natural termination cleanup, and reuse of
  the exact Roslyn-entry source-baseline lifecycle.
- Headless Avalonia tests cover Debug-specific nonpersistent launch overrides and the
  accessible status, thread, stack, scope, variable, navigation, and Continue path.
- An explicit live local test launched the built .NET 10 Harness host through the
  verified NetCoreDbg 3.2.0-1092 Linux x64 artifact, stopped at entry, enumerated
  threads and stack frames, disconnected, and observed adapter exit code zero.

## Remaining Task 052 work

Owned Test Debug remains. It must start one exact existing test operation with
`VSTEST_HOST_DEBUG=1`, discover and revalidate only that operation's waiting testhost
descendant, and attach without accepting a PID from UI or model input.
