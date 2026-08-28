# Dependency maintenance

Runtime dependency design and evidence boundaries are fixed by
[ADR 018](decisions/018-documentation-dependency-evidence-and-sbom.md). Contributor
review cadence, notices consistency, and preview-package exit rules are fixed by
[ADR 027](decisions/027-contributor-verification-and-dependency-governance.md).

## Monthly review

From a clean feature branch, without enabling live or paid-provider tests:

```bash
dotnet list Harness.slnx package --outdated --include-transitive --no-restore
dotnet list Harness.slnx package --vulnerable --include-transitive --no-restore
python3 eng/verify-repository-metadata.py
```

Review direct changes separately from transitive findings. For an actionable update,
open a dedicated draft pull request and record:

- old and proposed exact versions and why the update is needed;
- restored direct/transitive graph and vulnerability result;
- license, provenance, deprecation, and notices impact;
- the narrow adapter tests, warnings-as-errors build, and full deterministic suite;
- migration or rollback implications for persisted state and published output.

If a finding is accepted without change, record the bounded reason and next review
condition in the relevant task or decision. Do not enable automated dependency-update
pull requests.

## Prerelease production dependency

`Microsoft.SemanticKernel.Connectors.SqliteVec` is pinned centrally at
`1.74.0-preview`. ADR 027 defines its stable-release/replacement exit condition and
the evidence required for any interim preview update. A second prerelease production
dependency requires its own accepted decision before merge.

`THIRD-PARTY-NOTICES.md` lists components whose license text is explicitly shipped.
The metadata verifier ensures each listed package and version matches
`Directory.Packages.props`; adding or updating such a component updates both files in
one pull request.

## Review record

2026-08-25, NuGet.org, `--include-transitive --no-restore`:

- no known vulnerable package was reported in any of the 15 solution projects;
- newer direct versions exist across Roslyn, Microsoft Agent Framework, Avalonia,
  Dock, MCP, OpenTelemetry, SQLite, test tooling, and other adapters;
- `Microsoft.SemanticKernel.Connectors.SqliteVec` `1.74.0-preview` was not listed by
  the source as an update candidate, so ADR 027's pinned-preview exit rule remains;
- no version changed in this governance PR because the candidates span patch, minor,
  and major adapter boundaries and require dedicated compatibility evidence.

Next review: by 2026-09-25, or earlier when a dependency-specific security,
compatibility, or feature need opens a dedicated pull request.
