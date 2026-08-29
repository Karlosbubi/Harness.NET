# Developer Test Debug acceptance — 2026-08-29

This record covers the sixteenth Task 052 slice: exact Linux Test Debug through the
managed debugger lifecycle.

## Delivered behavior

- Only an exact Test Explorer source leaf can request Test Debug. Business Logic
  opens a short-lived Roslyn session for the selected project, discovers by the exact
  fully qualified name, requires one matching semantic test ID/project/source range,
  and closes the session in `finally` before execution proceeds.
- Data Access starts fixed `dotnet test <exact project> --no-restore --filter
  FullyQualifiedName=<exact test>` arguments with `VSTEST_HOST_DEBUG=1`. No launch
  profile, Restore, shell, arbitrary filter, executable, PID, or environment boundary
  crosses from Presentation.
- Linux discovery walks a bounded descendant graph across every task's kernel child
  list, accepts exactly one live command containing the managed `testhost.dll`
  argument, and rechecks both ancestry and command identity immediately before DAP
  attach. Multiple, exited, unrelated, or changed candidates fail closed.
- The verified source location becomes the initial test breakpoint. The existing
  Debug workspace owns threads, stacks, scopes, variables, stepping, Continue, Pause,
  Stop, output, source navigation, and final cleanup. Adapter and test-parent output
  remains bounded and process-local.
- Test Debug is visibly unavailable on non-Linux platforms until an equally direct,
  bounded parent-process implementation is delivered. Arbitrary attach remains
  excluded everywhere.

## Verification

- Data Access fakes prove exact no-Restore arguments, framework/configuration,
  testhost-only attach, immediate ancestry recheck, and cleanup without attach when
  ancestry changes.
- Business Logic tests prove that an exact Roslyn-verified source location enters the
  Test Debug session, the session exposes the test identity rather than a project
  entry target, and no process identity crosses the contract.
- Headless Avalonia tests prove exact-test dispatch and automatic activation of the
  debugger workspace.
- An explicit live Linux x64 test started a nested .NET 10 exact test with
  `VSTEST_HOST_DEBUG=1`, found its waiting `testhost` through the owned process tree,
  attached pinned NetCoreDbg 3.2.0-1092 over stdio, bound and hit the exact source
  breakpoint, continued the test, observed clean termination, and left no process.
- The live run also exposed and fixed two integration defects: Linux children may be
  owned by a non-main task, and NetCoreDbg requires absent optional breakpoint
  conditions to be omitted rather than serialized as JSON `null`.
