# ADR 027: Contributor verification and dependency governance

- Status: Accepted
- Date: 2026-08-25
- Extends: [ADR 018](018-documentation-dependency-evidence-and-sbom.md), [ADR 025](025-workbench-composition-and-refactor-guardrails.md)

## Context

The repository has deterministic verification, but its contributor entry points and
evidence rules were implicit. Acceptance records named ignored `artifacts/` paths
whose contents were not retained, the 828-test suite had no supported fast tier,
`eng/` and `docs/` had no maps, and dependency review had no cadence. The sole
production prerelease dependency,
`Microsoft.SemanticKernel.Connectors.SqliteVec` `1.74.0-preview`, also had no exit
condition.

Ignored run output cannot be retroactively treated as durable acceptance evidence:
the referenced files are absent and may contain repository, prompt, log, path, or
model content that was never reviewed for publication.

## Decision

Acceptance evidence is either committed deliberately below `docs/acceptance/` after
redaction and size review, or it is machine-local reproducibility output. Records may
describe and name machine-local output, but must not present it as durable repository
evidence. `artifacts/` remains ignored. Repository verification checks local Markdown
targets and rejects ambiguous `Artifact:` labels in acceptance records.

Every xUnit assembly declares `Tier=Fast` or `Tier=Adapter`. Tests that contact a
configured external/local service additionally declare `Tier=Live`; deterministic
adapter runs exclude that trait. Hosted verification runs the Fast tier first, then
all non-Live tests. Test projects run sequentially at solution level because
Avalonia.Headless has process-global teardown state; the Avalonia test assembly also
disables test-collection parallelism.

Contributor instructions remain pointers rather than a second policy source:
`CONTRIBUTING.md` links the working agreement and the documentation and verification
maps. `docs/README.md` and `eng/README.md` own those maps.

Dependencies receive a monthly, human-reviewed outdated and vulnerability check.
Results are recorded only when they produce an action or accepted deferral; no bot
opens update pull requests. The notices checker verifies that every explicitly
distributed notice entry matches the centrally pinned package version.

`Microsoft.SemanticKernel.Connectors.SqliteVec` stays pinned at `1.74.0-preview`
until either a stable release provides the vector-store operations used by
`SqliteSemanticIndexStore` with the existing deterministic retrieval tests passing,
or a reviewed replacement satisfies the same Business Logic contracts. A monthly
review may update the preview only through a dedicated dependency PR with restored
graph, retrieval, migration, notices, build, and full-suite evidence. Any additional
preview production dependency requires an accepted decision with an exact exit
record.

## Consequences

- A cold contributor has one short route into architecture, tasks, scripts, and
  verification without duplicating repository rules.
- CI fails fast on pure tests, excludes live tests explicitly, and then proves the
  complete deterministic suite.
- Machine-local model and usability runs remain useful reproduction data without
  being misrepresented as reviewable history.
- Dependency maintenance is deliberate and visible, with a small monthly manual
  cost and no automated churn.
- The SqliteVec preview remains a known risk, bounded by an exact version, exit
  condition, and deterministic adapter coverage.

## Alternatives considered

- Commit the missing historical artifact directories: rejected because they do not
  exist in the current workspace and were never redaction-reviewed.
- Treat ignored paths as durable evidence: rejected because another clone cannot
  inspect them.
- Mark every test individually: rejected because assembly ownership already defines
  Fast versus Adapter; only Live needs method-level overrides.
- Enable dependency-update bots: rejected because automatic churn conflicts with the
  repository's deliberate dependency and evidence review.
- Remove SqliteVec immediately: rejected because no measured replacement is selected;
  the accepted adapter boundary makes a later replacement focused.
