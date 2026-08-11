# ADR 018: Documentation, dependency evidence, and SBOM

- Status: Accepted
- Date: 2026-08-11

## Context

Harness.NET needs current library documentation without adding large reference dumps to
every model prompt. It also needs package and supply-chain facts that do not depend on a
model's memory. Documentation sources can disagree or describe a different release.
Project files, central package files, restored assets, package registries, advisories,
and caches each answer different dependency questions. Network lookup also changes the
privacy and availability boundary.

The existing semantic index is workspace- and goal-oriented. The existing MCP boundary
exposes configured, read-only typed tools. Neither is a generic web browser or package
manager, and neither may become an unrestricted execution path.

## Decision

One Business Logic research manager owns lookup policy and returns bounded evidence. It
queries source classes in this order and stops when the accumulated evidence is
sufficient:

1. exact documentation shipped with the selected SDK or restored package;
2. a version-matched local documentation index;
3. explicitly configured read-only MCP documentation tools;
4. configured web sources, only when online lookup is enabled and earlier evidence is
   insufficient.

Lookup is always explicit. Routine chat and role prompts do not receive documentation
automatically. A result contains its source class, source identity, requested and actual
version, retrieval time, freshness, confidence, citation, rank, and any reason the
manager escalated to the next source class. Exact version matches outrank newer or more
popular material. Conflicting claims remain separate evidence; the manager does not
invent a consensus. Results are deduplicated by normalized citation, version, and
content digest, and are limited by configured result and character bounds.

Documentation adapters use a shared typed query/result contract. Local package and
index adapters are Data Access modules. MCP lookup may invoke only configured tools that
are read-only, non-destructive, closed-world, and explicitly marked as documentation
sources. Web lookup uses configured HTTPS origins and sends only the search terms,
library identity, and requested version. It never sends workspace content. Offline mode
disables MCP and web lookup. Cancellation and source failures produce typed evidence and
allow policy-controlled escalation; they are not converted into empty success.

Cache identity is the source identity, normalized library, requested version, query,
adapter schema version, and relevant privacy mode. Cached results preserve their
original citation and retrieval time. Settings control enabled sources, index roots,
refresh policy, offline mode, cache age, retention, and result/context limits. Private
cache and configuration live under Harness.NET's XDG storage, never in a user
repository. Expired entries may be reported as stale but are not silently labelled
fresh. Retention cleanup is deterministic.

A separate deterministic dependency inspector reads project files,
`Directory.Packages.props`, NuGet lock files, and existing `obj/project.assets.json`
files. It reports declared, central, direct, transitive, and restored versions and the
evidence path for each fact. It does not run restore or evaluate arbitrary build targets.
Missing, conditional, unresolved, or conflicting versions remain explicit.

Candidate validation queries configured NuGet-compatible sources for an exact package
and version. It reports source and registration identity, listing and deprecation state,
prerelease status, target-framework and runtime asset compatibility, dependency groups
and transitive effects, advisories, license expression or URL, repository provenance,
and published integrity hashes when available. An unavailable field remains unknown.
Policy rejects unavailable exact versions, disallowed prereleases, known incompatible
targets, and known integrity conflicts. It does not treat missing advisory, license, or
provenance data as proof of safety.

The SBOM generator is deterministic and owned by Business Logic. It serializes the
resolved graph as CycloneDX JSON with sorted components and relationships, stable
package URLs, versions, hashes, licenses, and provenance. Repeated generation from the
same evidence produces identical bytes; volatile identifiers and timestamps are
excluded. Package-change previews include both the dependency-graph diff and SBOM diff.
No project file is changed by research or preview. SBOM export is an explicit developer
operation to a selected path and never occurs during agent lookup.

Developer and model surfaces use the same Business Logic contracts. Agent access is
through named typed read-only tools for documentation lookup, dependency inspection,
candidate validation, and SBOM preview. There is no generic MCP invoke, web request,
package mutation, restore, shell, or export tool. Evidence records source, version,
freshness, confidence, citation, and escalation reason.

The initially accepted version-matched library catalog covers .NET, Avalonia, Rx.NET,
Serilog, Microsoft Agent Framework, Roslyn, Dock, Dapper, SQLite, and xUnit. Additional
restored dependencies can be researched through the same bounded contracts.

## Consequences

- Documentation is available when needed without consuming every prompt's context.
- Package and SBOM claims can be reproduced from files and registry evidence rather
  than model output.
- Network failure and incomplete registries are visible instead of being mistaken for
  safety or absence.
- Exact MSBuild evaluation remains out of scope for the safe file reader; conditional
  declarations that cannot be resolved statically are reported as conditional.
- Supporting another documentation index, MCP schema, registry, advisory feed, or SBOM
  format requires a focused Data Access adapter behind the existing contracts.
- Package mutation remains a separately authorized future operation; this slice
  supplies the deterministic preview that such an operation must consume.

## Alternatives considered

- Put core documentation into every system prompt: rejected because it is stale,
  version-ambiguous, and wastes context on unrelated turns.
- Let models choose and browse arbitrary sources: rejected because it weakens privacy,
  reproducibility, citations, and authority boundaries.
- Run `dotnet restore` or MSBuild during inspection: rejected because a read operation
  would execute repository logic and mutate caches or project state.
- Treat NuGet metadata or vulnerability silence as authoritative safety: rejected
  because feeds are incomplete and absence of evidence is not evidence of absence.
- Generate an SBOM with timestamps and random serial numbers: rejected because the
  output could not be reproduced or reviewed as a stable diff.
