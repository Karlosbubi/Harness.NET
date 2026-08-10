# Architecture Decision Records

Use a decision record for choices that constrain dependencies, project boundaries,
data ownership, execution policy, deployment, or public contracts.

## States

- **Proposed:** concrete enough to review, not binding.
- **Accepted:** current repository direction.
- **Superseded:** replaced by another decision.
- **Rejected:** considered and deliberately not chosen.

Copy `000-template.md` to the next zero-padded number. Keep the title stable and
link superseding records in both directions.

## Index

| ADR | Status | Decision |
|---|---|---|
| [001](001-layered-feature-architecture.md) | Accepted | Layered feature architecture |
| [002](002-non-web-presentation.md) | Accepted | TUI-first non-web presentation |
| [003](003-agent-and-provider-architecture.md) | Accepted | Agent and provider architecture |
| [004](004-framework-and-storage.md) | Accepted | Framework and storage ownership |
| [005](005-isolated-goal-execution.md) | Accepted | Isolated and approved goal execution |
| [006](006-memory-observability-and-recovery.md) | Accepted | Memory, observability, and recovery |
| [007](007-semantic-contract-types.md) | Accepted | Semantic contract types |
| [008](008-application-state-backup.md) | Accepted | Application-state backup and upgrade recovery |
| [009](009-avalonia-presentation-toolkit.md) | Accepted | Avalonia presentation toolkit and desktop adapter |
| [010](010-docked-desktop-workbench.md) | Accepted | Docked desktop workbench and real editor documents |
| [011](011-private-workbench-layout-state.md) | Accepted | Private workbench layout state and recovery |
| [012](012-roslyn-code-intelligence.md) | Accepted | Roslyn code intelligence and verified transformations |
| [013](013-chat-first-desktop-workflow.md) | Accepted | Chat-first desktop workflow and settings ownership |
| [014](014-default-remote-spend-policy.md) | Accepted | Unlimited-by-default remote spend with prominent opt-in controls |
| [015](015-stateless-mcp-connections.md) | Accepted | Stateless MCP connections and agent tool safety |
