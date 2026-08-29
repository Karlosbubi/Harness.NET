# Harness.NET

Harness.NET is a Linux-first .NET development environment for working with AI
agents. It combines chat, a source editor, Roslyn code intelligence, Git inspection,
typed agent tools, and isolated goal worktrees.

The project is local-first. Source and application state stay local unless the user
selects a remote model or external service. Harness.NET does not add its own metadata
directory to user repositories.

## Status

Version `1.0.0` passes the scripted Linux x64 repository workflow. It is not yet a
complete daily-use IDE.

Delivered:

- Avalonia desktop UI and retained Terminal.Gui UI;
- chat-based goal planning, approval, implementation, review, recovery, and commit;
- Ollama and OpenRouter chat and embedding providers;
- provider discovery, per-role model and reasoning routing, searchable model
  selection, and monetary controls for remote inference;
- stateless MCP 2.x connections with Settings management, read-only tool policy, and
  an explicit loopback Harness-to-Harness Lead delegation mode;
- an optional unauthenticated, loopback-only MCP 2.x server for typed dogfooding and isolated
  evaluation, with client/tool allowlists, audit, revocation,
  disposable fixtures, accessibility state, and Harness-owned evaluation frames;
- on-demand versioned documentation lookup through exact local, indexed, configured
  MCP, and web sources with citations and offline cache policy;
- deterministic declared, central, locked, direct, transitive, and restored NuGet
  evidence, exact package-candidate validation, package/SBOM diffs, and explicit
  CycloneDX 1.6 export;
- Git-backed workspace registration, trust, status, diff, search, branches, and
  isolated goal worktrees;
- a developer Git workbench for exact staging, cleanup, commit/amend, branches, tags,
  linked worktrees, stashes, paged history/blame, three-way conflict editing, and
  explicit fetch, reviewed merge/rebase integration, and push with force-with-lease;
- an editable Avalonia source editor with diagnostics, completion, quick info,
  signature help, semantic classification, occurrence highlighting, folding,
  outline, breadcrumbs, workspace-symbol search, parameter and inferred-type inlay
  hints, lazy CodeLens navigation, definitions, usages, implementations, and semantic
  rename, region navigation, and labeled read-only generated and locally decompiled
  metadata source with an explicit signature fallback, exact-context syntax-tree,
  symbol, generated-source, and IL
  inspection views, plus Roslyn document, selection, changed-span, paste, and on-type
  formatting and import organization, contextual compiler fixes, local/selection and named
  cross-document refactorings,
  labeled document fix-all choices, and typed project-entry-point Run actions with
  cancellation and transient output;
- a source-context-aware static .NET Solution view for entry points, SDK policy and
  selected-SDK/workload-manifest health, projects, target frameworks,
  declared/conventional configurations, startup candidates, project/package
  references, project metadata, and per-project loading failures, with no implicit
  Restore or repository execution; bounded launch-profile discovery exposes only
  profile kinds, flags, and environment-variable names—not argument text, values,
  or executable paths;
- typed per-project and startup-project Build/Rebuild with inspected configuration,
  confined no-Restore process execution, cancellation, transient bounded streams,
  and durable restart-safe lifecycle metadata;
- a compiler-backed Test Explorer for xUnit, NUnit, and MSTest source tests, with
  project/type/test hierarchy, bounded search across names and traits, parameterized
  test labels, exact source navigation, and closed per-test Run actions with
  no-Restore execution, in-place Stop/Rerun, duration and failure history, transient
  output, durable result metadata, and typed framework/lifecycle filtering;
- typed configurable workbench and editor keybindings with conflict validation,
  command discovery, reset, bounded declarative import/export, and optional Vim
  Normal, Insert, Visual, and Visual Line behavior;
- masked developer-only Project User Secrets management over the standard .NET store,
  with separate reveal, copy, add, change, and delete actions and a visual-capture
  interlock;
- Roslyn validation before model-authored source writes, including a bounded
  compiler-proven missing-import repair before an unnecessary model retry;
- typed, role-scoped agent tools for files, Git, .NET metadata, Build/Test, semantic
  retrieval, diagnostics, symbols, navigation, edits, rename, and closed Roslyn
  action discovery/preview/apply;
- a low-noise live agent activity indicator with elapsed time, last observable update,
  bounded provider/tool/checkpoint detail, goal and evidence navigation, deterministic
  multi-operation coalescing, and direct cancellation;
- bounded, session-only workbench notifications for goal outcomes, with deterministic
  coalescing, expiry, keyboard dismissal, accessible announcements, and direct
  navigation back to Conversation;
- XDG Desktop Portal visual verification with per-frame consent, private goal-scoped
  evidence, exact-byte developer preview, typed agent tools, retention, revocation,
  and remote-disclosure controls;
- SQLite persistence, restart recovery, verified application backup, XDG paths,
  Secret Service credentials, structured logs, and optional OTLP export.

Open work is tracked in [the roadmap](docs/roadmap.md) and
[task ledger](docs/tasks/README.md). The semantic tool foundation and inbound MCP
evaluation surface and local-model regression gate are delivered. The roadmap now
continues with the editor and daily-use IDE slices.

Contributors start with [CONTRIBUTING.md](CONTRIBUTING.md) and the
[documentation map](docs/README.md).

## Safety model

- The user trusts a repository before Harness.NET evaluates projects or runs code.
- Plan approval creates an isolated branch and worktree.
- Agents use typed tools. Harness.NET does not expose an unrestricted shell.
- Lead reads the original workspace. Implementer and Reviewer use the approved goal
  worktree.
- Implementer writes are limited to delegated file areas.
- Model-authored C# changes are checked in memory. New compiler errors block the
  write.
- Restore, package changes, remote spending, destructive work, and commits have
  separate authority checks.
- Commits require approval of the exact branch, HEAD, message, author, and diff hash.
- Credentials stay in Linux Secret Service or the configured environment boundary.
- Screenshot requests always use desktop-portal consent. Captures are bounded,
  revocable, and unavailable to remote models unless the user opts in.
- Documentation lookup sends only library, version, and question to configured
  external sources. Offline mode uses local and cached evidence.
- Dependency inspection and SBOM preview never restore packages, execute project
  targets, mutate project files, or export without an explicit developer action.
- Inbound MCP is disabled by default, strictly loopback-only, client/tool allowlisted,
  and audited. It intentionally has no authentication and exposes no shell, SQL, arbitrary command, generic
  click/type, desktop control, credential read, or natural-language authority.
- Outbound Harness control is a separate directed controller→worker mode. It requires
  a loopback worker, client attribution, and exact tool allowlisting, and is never exposed
  to Implementer or Reviewer. Arbitrary cyclic delegation is unsupported.

See [architecture](docs/architecture.md), [framework](docs/framework.md), and
[decision records](docs/decisions/README.md) for the exact rules. Distributed
third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Requirements

- Linux x64 is the current release target.
- .NET SDK 10.0.100 or a later .NET 10 feature band is selected by `global.json`.
- The solution uses the XML `.slnx` format.

## Build and run

```bash
dotnet restore Harness.slnx
dotnet build Harness.slnx --no-restore
dotnet test Harness.slnx --no-build --no-restore \
  --filter Tier!=Live --maxcpucount:1
dotnet run --project src/Harness.Host/Harness.Host.csproj
```

Test assemblies declare a deterministic tier. Use the pure fast loop while iterating,
the complete non-live gate before handoff, and opt into live tests only with the
explicit provider authorization described below:

```bash
dotnet test Harness.slnx --no-build --no-restore \
  --filter Tier=Fast --maxcpucount:1
dotnet test Harness.slnx --no-build --no-restore \
  --filter 'Tier=Adapter&Tier!=Live' --maxcpucount:1
dotnet test Harness.slnx --no-build --no-restore \
  --filter Tier!=Live --maxcpucount:1
```

Avalonia is the default UI. Other modes:

```bash
dotnet run --project src/Harness.Host/Harness.Host.csproj -- --ui=terminal
dotnet run --project src/Harness.Host/Harness.Host.csproj -- --no-ui
```

`--wait-for-shutdown` initializes the application, reports readiness, and waits for
SIGINT or SIGTERM. It is intended for lifecycle checks and service supervision.

`--mcp-evaluation-root /tmp/<dedicated-directory>` isolates configuration, database,
cache, logs, worktrees, credentials, and a deterministic fixture repository. Use it
only with Settings → Harness control → IsolatedEvaluation. Normal repositories are
unavailable in that process.

Provider modules, default routes, and optional OTLP export are defined in
`src/Harness.Host/harness.xml`. Private overrides use XDG configuration. See
[configuration](docs/configuration.md).

## Publish

```bash
dotnet publish src/Harness.Host/Harness.Host.csproj \
  -p:PublishProfile=linux-x64 \
  --output artifacts/linux-x64
```

The result is a self-contained Linux x64 application. It includes the shipped
configuration and external native libraries required by the publish profile.

## Verification

Deterministic release gate:

```bash
./eng/verify-v1-release.sh
```

Linux publish only:

```bash
./eng/verify-linux-x64-publish.sh
```

Desktop accessibility and layout checks, run from a graphical Linux session with
`python3-dbus`:

```bash
./eng/verify-avalonia-atspi.py
./eng/verify-avalonia-atspi.py --with-orca
```

Deterministic desktop goal workflow:

```bash
./eng/verify-avalonia-workflow.py
```

Complete Linux desktop gate:

```bash
./eng/verify-v1-desktop-release.sh
```

Local-model Tic-Tac-Toe usability test:

```bash
./eng/verify-ollama-tictactoe-usability.py \
  --ollama-endpoint http://127.0.0.1:11434 \
  --model ornith:9b
```

This test performs real local inference and writes its repository, state, logs,
timings, and validation evidence under `artifacts/usability/`.

Versioned local-model regression corpus (deterministic and free by default):

```bash
./eng/verify-local-model-regression.py
```

Live runs require `--live` and explicit Ollama models. They run sequentially and do
not configure paid providers. See the
[acceptance record](docs/acceptance/local-model-regression-2026-08-12.md) for live,
comparison, retention, and cleanup commands.

Ollama live adapter test:

```bash
HARNESS_OLLAMA_INTEGRATION_ENDPOINT=http://127.0.0.1:11434 \
HARNESS_OLLAMA_INTEGRATION_MODEL=ornith:9b \
dotnet test tests/Harness.DataAccess.Tests --filter Category=LiveIntegration
```

OpenRouter catalog discovery performs no inference:

```bash
dotnet test tests/Harness.DataAccess.Tests \
  --filter Category=OpenRouterLiveIntegration
```

Paid OpenRouter tests require `HARNESS_RUN_OPENROUTER_PAID_TESTS=1`. Each test has a
small hard-coded monetary ceiling. Do not enable the flag in routine test runs.

## Backup

Create a backup without starting a UI:

```bash
./artifacts/linux-x64/Harness.Host \
  --backup-path=/absolute/path/harness-state.zip
```

The destination must be an absolute path to a new `.zip` file. The archive contains
private application state, including persisted prompts, approvals, evidence, usage,
and optional layout state. It excludes credentials, logs, caches, worktrees, and user
repositories. Treat it as sensitive. See [ADR 008](docs/decisions/008-application-state-backup.md).

## Documentation

- [Documentation map](docs/README.md)
- [Contributing](CONTRIBUTING.md)
- [Product scope](docs/vision.md)
- [Framework rules](docs/framework.md)
- [Architecture](docs/architecture.md)
- [Configuration](docs/configuration.md)
- [Settings ownership](docs/settings.md)
- [Roadmap](docs/roadmap.md)
- [Task ledger](docs/tasks/README.md)
- [Model-accessible IDE capability map](docs/agent-ide-capabilities.md)
- [Decision records](docs/decisions/README.md)
- [Acceptance evidence](docs/acceptance/)
