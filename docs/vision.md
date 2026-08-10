# Product scope

Harness.NET is a local-first .NET development environment for one developer working
with AI agents. It turns the developer’s preferred libraries, architecture, quality
rules, and workflow into inspectable configuration and typed operations.

## Principles

- **User-owned rules:** rules are layered, inspectable, editable, and promoted only
  through a user-approved diff.
- **Local-first data:** source, prompts, policy, and application state stay local
  unless the user selects a remote service.
- **Explicit authority:** plans, remote spending, network/package work, destructive
  actions, and commits use typed decisions.
- **Scoped agent work:** after plan approval, agents may inspect, edit, Build, and Test
  only through role-scoped tools in the goal worktree.
- **Deterministic .NET operations:** use Roslyn for diagnostics, navigation,
  validation, and refactoring when the compiler can answer.
- **Chat-first workflow:** conversation starts and continues work; typed cards show
  state, authority, evidence, and recovery.
- **Provider isolation:** provider payloads remain in Data Access.
- **Auditability:** persist prompts, decisions, tool results, usage, checkpoints, and
  evidence; omit secrets from logs and telemetry.
- **Replaceable UI:** Avalonia is the default and Terminal.Gui is retained. Business
  Logic does not depend on either.
- **No web UI:** web presentation is outside scope.
- **Clean repositories:** do not create Harness.NET metadata directories in user
  repositories.

## Repository workflow

1. Register and trust a Git-backed .NET repository.
2. Select a solution or project and optionally build its semantic index.
3. Describe a goal and select any goal-specific model or spending override.
4. Lead inspects the original workspace and proposes a bounded plan.
5. The user approves, changes, or rejects the plan.
6. Approval creates an isolated goal branch and worktree.
7. Implementer edits through typed tools; Roslyn validates model-authored C# changes;
   Build/Test provides separate execution evidence.
8. Reviewer inspects the diff and evidence.
9. Correction repeats within the configured review limit or pauses for direction.
10. The user inspects the result and approves an exact commit on the goal branch.

## Current boundary

The complete scripted repository workflow is implemented on Linux x64. The desktop
also includes editable source, Roslyn diagnostics and navigation, semantic rename,
provider and MCP Settings, multi-workspace state, recovery, and first model-accessible
IDE tools.

The application is not yet a complete daily-use IDE. Remaining work is listed in the
[roadmap](roadmap.md): controlled visual capture, version-matched documentation and
package/SBOM support, and the rest of the typed IDE capability catalog.
