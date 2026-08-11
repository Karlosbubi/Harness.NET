# Documentation, dependency, and SBOM acceptance — 2026-08-11

Task 046 is implemented as deterministic Business Logic policy over focused Data
Access adapters. No live model or paid provider was used for acceptance.

## Proven behavior

- ADR 018 fixes lookup authority, sufficiency, privacy, cache identity, citations,
  dependency evidence, candidate policy, retention, and SBOM ownership.
- Documentation lookup runs only when explicitly requested. It orders exact restored
  package/SDK files, configured local indexes, named closed read-only MCP tools, and
  configured HTTPS search endpoints. It stops on sufficient evidence.
- Results are bounded by count and characters and expose source, requested/actual
  version, freshness, confidence, rank, citation, conflicts, and escalation reasons.
- Offline mode blocks live MCP, web, and package-registry requests and can use stale
  cached evidence with a visible stale label.
- Core-library lookup can derive one unambiguous restored package version or .NET
  target-framework version before searching. The catalog covers .NET, Avalonia, Rx.NET, Serilog,
  Microsoft Agent Framework, Roslyn, Dock, Dapper, SQLite, and xUnit.
- Dependency inspection reads project references, `VersionOverride`, inherited
  declaration conditions, `Directory.Packages.props`, `packages.lock.json`, and existing
  `obj/project.assets.json`. Tests prove that it neither creates assets nor runs Restore.
- Exact NuGet candidate validation reads service-index, registration, package archive,
  manifest, target-framework/runtime assets, dependency, advisory, license,
  provenance, listing/deprecation, and published/computed SHA-512 evidence.
- CycloneDX 1.6 JSON uses sorted stable package URLs, components, relationships,
  evidence, and hexadecimal SHA-512 values converted from NuGet content hashes. It has
  no timestamp or random serial number; equal evidence produces equal bytes and SHA-256.
- Package preview returns dependency and SBOM diffs without export or project mutation.
  Export is a separate developer-only operation to an absolute JSON path and requires
  explicit overwrite authority.
- Settings exposes source, index, MCP route, web/NuGet endpoint, refresh, cache age,
  retention, offline, limits, status, lookup, inspection, validation, diff, preview,
  and export controls.
- Every role receives named lookup, dependency, validation, and preview tools. There
  is no generic web request, MCP invoke, Restore, package mutation, or SBOM export tool.

## Deterministic coverage

Focused tests cover:

- source ordering, sufficiency, exact-version resolution, stale cache, conflicts,
  deduplication, context limits, cancellation, MCP failure, unsafe MCP rejection, and
  web fallback;
- declared, central, locked, direct, transitive, restored, unresolved, and conflicting
  package graphs;
- exact registration absence, package archive bounds, framework/runtime assets,
  dependencies, advisories, license, provenance, and integrity;
- offline registry refusal, conservative candidate decisions, reproducible SBOM,
  package/SBOM diffs, missing restored graphs, and explicit atomic export;
- searchable Settings controls and closed agent-tool exposure.

## Verification commands

```text
dotnet test tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj --no-restore -p:UseSharedCompilation=false -m:1
dotnet test tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj --no-restore -p:UseSharedCompilation=false -m:1
dotnet test tests/Harness.Presentation.Avalonia.Tests/Harness.Presentation.Avalonia.Tests.csproj --no-restore -p:UseSharedCompilation=false -m:1
dotnet test Harness.slnx --no-build --no-restore -p:UseSharedCompilation=false -m:1
dotnet build Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1
./eng/verify-linux-x64-publish.sh
git diff --check
```

The final gate output is recorded in the Task 046 commit history. Live NuGet and web
requests are user-configured runtime behavior; adapter acceptance uses deterministic
HTTP fakes and makes no paid calls.
