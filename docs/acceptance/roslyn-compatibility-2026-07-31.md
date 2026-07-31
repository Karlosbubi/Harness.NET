# Roslyn compatibility checkpoint — 2026-07-31

This checkpoint proves the process and packaging constraints required before live
code intelligence is connected to editor or agent mutations. It does not claim that
Task 042 validation UX is complete.

## Runtime choice

Harness.NET uses one coherent Roslyn 5.3 package family for C# compiler, Workspaces,
MSBuild Workspaces, and Features, with Microsoft.Build.Locator 1.11.2. The runtime
asks the `dotnet` host for the SDK selected from the workspace root, so a local
`global.json` participates exactly as it does for normal .NET commands. It resolves
that exact installed SDK directory and registers it before the no-inline boundary
that first creates `MSBuildWorkspace`.

MSBuild registration is process-global. The first active workspace establishes the
SDK for the process; selecting a different SDK later returns the actionable
`sdk_change_requires_restart` degraded state instead of attempting an unsafe runtime
switch. A missing `global.json` SDK returns `sdk_unavailable`. The existing workspace
metadata inspector now reads bounded solution/project construction metadata without
loading Microsoft.Build, keeping SDK registration ownership inside code intelligence.

## Compatibility evidence

`RoslynWorkspaceCompatibilityTests` uses temporary .NET 10 projects pinned by
`global.json` and proves that `.csproj`, classic `.sln`, and `.slnx` entry points each
load through `MSBuildWorkspace`. It also loads the repository's real `Harness.slnx`,
including at least ten projects and one hundred documents, and proves the missing-SDK
degraded result. The synthetic checks assert that no `obj/project.assets.json` is
created: probing does not restore.

`DotNetSdkSelectorTests` independently proves exact SDK-directory selection and the
bounded missing-SDK response through a deterministic process fake. The full Data
Access test assembly passes with 99 tests at this checkpoint.

## Linux release evidence

The `linux-x64` profile remains self-contained and single-file. Roslyn's
`BuildHost-netcore` DLLs, dependency manifest, and runtime configuration are marked
external to that bundle because Roslyn launches the build host as a child process.
The application root deliberately contains Microsoft.Build.Locator but not
`Microsoft.Build.dll`; the selected SDK remains the source of MSBuild.

`./eng/verify-linux-x64-publish.sh` asserts that layout, starts the release with no
installed `dotnet` on `PATH`, and completes the existing startup, backup, integrity,
and recovery checks. It reports `linux-x64 publish verification passed`.

The in-process approach satisfies ADR 012's checkpoint, so no architectural amendment
or out-of-process fallback is required. Trust copy and analyzer/source-generator
evaluation remain part of the following Task 042 implementation slices.
