# Documentation map

For a cold start, read [vision](vision.md) → [framework](framework.md) →
[architecture](architecture.md) → [roadmap](roadmap.md). Then select the relevant
accepted decisions and task record before changing code.

| Document | What it answers |
|---|---|
| [Vision](vision.md) | What Harness.NET is, who it serves, and what is out of scope. |
| [Framework](framework.md) | Binding engineering, authority, privacy, testing, and UI rules. |
| [Architecture](architecture.md) | Current layers, feature map, data flow, and enforced boundaries. |
| [Configuration](configuration.md) | Shipped and private configuration ownership and precedence. |
| [Settings](settings.md) | Typed Settings ownership and user-facing configuration surfaces. |
| [Roadmap](roadmap.md) | Product sequence, ongoing work, and deferred scope. |
| [Task ledger](tasks/README.md) | Status, dependencies, and acceptance criteria for numbered work. |
| [Agent IDE capabilities](agent-ide-capabilities.md) | Typed developer/model capability coverage. |
| [Dependency maintenance](dependencies.md) | Monthly review, notices, and prerelease exit policy. |
| [DX/UX review](dx-ux-review.md) | Evidence behind Tasks 062–068. |

## Collections

- [Decision records](decisions/README.md) define settled constraints and proposed
  architectural changes.
- [Acceptance records](acceptance/README.md) hold deliberately versioned evidence and
  distinguish machine-local reproduction output.
- [Workbench refactor baseline](refactor-baseline.md) owns Task 060 sequencing and
  structural measurements.
- [Mockups](mockups/README.md) are design references, not runtime implementation.
- The repository [verification catalog](../eng/README.md) maps scripts to
  prerequisites and acceptance surfaces.

When adding a top-level document or durable collection, add it here. Local Markdown
targets, ADR index statuses, acceptance artifact labels, notice versions, and preview
dependency records are checked by `eng/verify-repository-metadata.py` in CI.
