# Harness.NET

Harness.NET is a local-first workspace for collaborating with AI agents on .NET
software development under an explicit, user-owned engineering framework. The
detailed product workflow and architectural constraints are documented in this
repository.

## Current status

The current build is the `1.0.0` Linux x64 release: a scripted acceptance gate
proves one complete, representative repository workflow end to end. That gate
passing does not yet mean the application is productive for real day-to-day .NET
development — see `docs/roadmap.md` Stage 3 and `docs/tasks/README.md` for the
remaining concrete gaps. Chat-first orchestration, multi-workspace use, Roslyn
validation/intellisense/refactoring, and messy-repository recovery are now delivered;
accepted goal branches now include deliberate manual push/PR/merge guidance. The
remaining daily-use gaps, including visual verification, documentation/package
research and the model-accessible IDE capability catalog, are concrete planned tasks.
Task 038 (agent-quality feedback loop) remains explicitly deferred as an optional
evaluation track.

Framework discovery, the production service slices, and the default docked desktop
workflow are implemented. The application has
compile-time layer enforcement, XDG paths, Secret Service access, SQLite migrations,
redacted local logs, optional OTLP, adaptive Avalonia and Terminal.Gui shells, and an
Ollama provider adapter. The OpenRouter adapter adds dynamic chat/embedding discovery,
streaming, strict privacy routing, and fail-closed goal budgets with attributed
reservation and reconciled-spend reports. The Avalonia goal workspace and TUI Goals
menu create unlimited-by-default goals with prominent capped and local-only opt-ins and manage versioned plan
approval/denial. Both adapters show reserved exposure, reconciled spend, remaining
budget, overage, and per-request attribution; they discover configured chat catalogs
at interactive startup, validate saved role defaults, and persist an explicit
provider/model choice independently for the lead, implementer, and reviewer. Settings
shows Ollama and OpenRouter as first-class providers and limits each role picker to
models that declare every capability required by that role. The provider page edits
the private XDG endpoint/model/embedding/timeout override and writes OpenRouter keys
directly to Linux Secret Service without echoing or persisting the credential.

First-class MCP support uses the stable official C# SDK 2.x and the stateless
`2026-07-28` Streamable HTTP lifecycle. Enabled private-XDG connections are discovered
at startup without inference; only explicitly read-only, non-destructive tools are
namespaced into agent roles. Settings owns add/edit/enable/remove, timeout, negotiated
protocol, eligible/rejected counts, failures, and restart state from the initial slice.

Semantic indexing now reads bounded eligible text directly from the Git index,
filters generated, binary, sensitive, and oversized content, and creates deterministic
overlapping chunks. The configured embedding route writes atomically replaceable
SQLite vector partitions keyed by provider, model, dimensions, and chunking version;
Business Logic exposes rebuild and retrieval records ready for presentation adapters.

The current usable workflow is a durable local-model conversation: instructions
submitted in Avalonia or the TUI are persisted before inference, streamed through
Business Logic, and reloaded from SQLite on restart. Provider failures are recorded
in the transcript.
The workspace modal can also inspect, register, select, and explicitly trust a
Git-backed .NET workspace in both Avalonia and the TUI; the TUI Workspace menu
remains available in narrow layouts. Avalonia refreshes the conversation workspace
context immediately after selection, registration, or trust changes and requires a
separate confirmation before granting trust.
The desktop's primary Open workspace actions launch the platform-native folder picker,
then show real tracked .NET solutions and projects before registration. Manual path
entry remains available when a desktop picker is unavailable; choosing a folder does
not grant repository trust.
For a trusted active workspace, Avalonia exposes a real bounded Git-tracked file tree,
tracked-text search, file reading in a syntax-aware editor, Git status and diff
inspection, and parsed .NET solution/project metadata. It does not display a
fabricated terminal, problem count, build result, or goal progress.
The bounded working-tree diff opens in a decorated viewer rather than as raw text:
an inline mode reviews added and removed lines in place with old/new line gutters,
and a side-by-side mode compares Git state across two aligned columns. Both use the
contrast-validated diff theme tokens and follow an effective theme change.
The modal inspector has been replaced by the first Dock-based workbench: source and
diff content open in a central document region while real workspace/search, Git,
goal context, and conversation controls occupy tool regions. Panel movement, hiding,
floating, explicit save/reset, restart restoration, corrupt-state fallback, and
private backup/recovery are implemented without persisting transient editor content.
Files explicitly opened by the user from the active trusted workspace are editable
in AvaloniaEdit by default. If an approved goal is selected, source instead resolves
to its isolated worktree. Both user save paths use confined exact-baseline writes;
agent edits still require the approved goal mutation boundary. Dirty switching/closing
and external-change conflicts require explicit decisions. Search, Git state, diff,
and source resolve to the same context. Truncated source remains read-only. Bounded run-output separation
is implemented as a distinct Dock tool over durable typed Build/Test/Restore evidence
without adding a terminal. The rendered Dock content boundary, minimum-size fallback,
keyboard restoration, floating ownership, accessible names, and 200% scaling are now
covered, with real wide/compact review and a repeatable production AT-SPI workflow
recorded under `docs/acceptance`. Production Orca generates contextual speech for
representative controls without announcing visual framework implementation types.
The production desktop acceptance path also completes an explicit-goal
edit/build/test/review/exact-commit workflow through a deterministic loopback model
server, including restart recovery and exact repository/audit verification.
The Avalonia and TUI Framework surfaces show the resolved engineering rules and
guidance with locks, provenance, privacy, and validation issues, and edit only the
private workspace overlay without adding repository metadata.
Avalonia and the TUI create durable goals with review-cycle limits and optional remote
caps, propose and inspect versioned plans, and require confirmation before plan
approval provisions an isolated worktree. Avalonia disables approval until the active
workspace is trusted and explains the exact repository-local capabilities granted.
When a role call needs direction, its run card can retry the exact role with a
capability-compatible replacement model and optional user guidance. Any non-terminal
goal can instead be confirmed as aborted and immediately
return the interface to new-goal composition; its history, evidence, and worktree are
preserved while it is removed from the continuation list.
Trusted-workspace tools now provide confined file reads, tracked-text search,
bounded Git status/diff evidence, non-evaluating .NET metadata, approved atomic file
edits, and cancellable .NET execution in isolated goal worktrees. Agent tools also
expose Roslyn compiler diagnostics, symbol information, definitions, and
references without requiring a model to construct editor sessions or source snapshots.
Lead queries use the trusted original workspace; Implementer and Reviewer queries use
the approved goal worktree. Settings → Agent tools shows the built-in module source,
health, eligible roles, exposure, authority, and exact model-facing operations. Agent
role execution now runs lead, implementer, and reviewer prompts through Microsoft Agent
Framework behind semantic Business Logic contracts, with each role using its
configured local default or goal-specific selection. Remote role execution carries
the goal identity and strict privacy policy into the cost-controlled provider boundary.
Capped goals derive a provider request boundary from remaining money; unlimited goals
do not send an application token ceiling. The production coordinator durably runs Lead
planning, approved Implementer work, and independent Reviewer decisions with closed
role tool scopes. Lead plans persist 1-12 ordered tasks with file areas and acceptance
criteria; Implementer executes one task per call and completed reports recover without
replay. Reviewer findings drive bounded correction passes until acceptance or the
configured cycle limit. Avalonia and the TUI show pending and maximum remaining calls,
role routes, aggregate cap, active reservations, reconciled spend, and remaining
budget before model calls. Both can start bounded Lead planning, continue approved
Implementer/Reviewer work, cancel an active run, and inspect durable tasks, activity,
and evidence. Starting plan generation explicitly selects a compatible Lead model,
prefilled from the effective configured Lead route; remote selection retains the
spend-policy and confirmation gates. Model selectors search the complete discovered
catalog across configured providers; compatible remote routes remain visible on a
local-only goal but cannot be authorized until that goal switches to unlimited or capped spend.
All production roles can request 1-8 relevant chunks through a typed semantic-context
tool tied to the active goal workspace and strict remote privacy. Avalonia and the
TUI Goals menu can
inspect compatible index status without inference, explicitly rebuild after showing
the embedding route and cost state, and preview attributed matches with source lines,
distance, usage, and cost.
Restore is available only after a durable, correlation- and target-bound user
approval. Avalonia and the TUI can create, inspect, approve, and deny that exact
one-call authorization without granting general network access. Approved
edit/build/test/restore calls retain durable request/result evidence for later
workflow and GUI presentation. The earlier deterministic demonstration workflow is
no longer composed into either shipped frontend; production goal workflow state is
the only workflow shown to users.
Accepted production work has a separate two-step commit flow in Avalonia and the TUI:
Harness.NET records a pending request containing the exact branch, HEAD, complete diff
hash and content, message, and author, then requires an explicit approve/deny action
before a local commit. An interrupted approved commit can be revalidated and resumed.
Neither adapter integrates the isolated branch automatically.
The Avalonia and TUI Operations surfaces create a non-overwriting, integrity-checked
application-state archive with explicit schema, byte-count, and SHA-256 evidence.
Archives include the SQLite state needed for audit and recovery plus optional
validated private workbench layout, but exclude credentials, logs, caches, worktrees,
and repositories. Every pending schema upgrade first creates the same verified
recovery point under XDG data storage.

Start with:

- [Product vision](docs/vision.md)
- [Framework discovery](docs/framework.md)
- [Accepted architecture](docs/architecture.md)
- [Runtime configuration](docs/configuration.md)
- [Settings ownership and delivery](docs/settings.md)
- [Model-accessible IDE capability map](docs/agent-ide-capabilities.md)
- [Delivery outline](docs/roadmap.md)
- [Decision records](docs/decisions/README.md)

## Toolchain

- .NET SDK 10.0.201 (pinned by `global.json`)
- Solution format: XML `.slnx`
- Nullable reference types and warnings-as-errors are shared defaults

## Development

```bash
dotnet restore Harness.slnx
dotnet build Harness.slnx --no-restore
dotnet test Harness.slnx --no-build --no-restore
dotnet run --project src/Harness.Host/Harness.Host.csproj
```

The default interactive frontend is the Avalonia docked desktop workbench. Use
`--ui=terminal` for the complete existing TUI and `--no-ui` for a non-interactive
startup smoke test. Provider modules, role
routing, conversation defaults, and optional OTLP export are defined in the shipped
`harness.xml` and may be overridden through XDG configuration. See
[runtime configuration](docs/configuration.md) and
[implementation tasks](docs/tasks/README.md) for details.

## Linux x64 publish

Publish the self-contained release with:

```bash
dotnet publish src/Harness.Host/Harness.Host.csproj \
  -p:PublishProfile=linux-x64 \
  --output artifacts/linux-x64
```

The output contains a compressed executable, the native libraries that remain
external to avoid runtime extraction outside XDG storage, and the shipped
`harness.xml`. It does not require an installed .NET runtime. Run the complete
deterministic v1 release gate with:

```bash
./eng/verify-v1-release.sh
```

It runs the full test suite and representative-repository acceptance, then verifies
isolated-XDG clean install, SIGTERM cancellation, backup/export, offline recovery,
and migration of the self-contained artifact. It does not load `.env` or invoke a
model provider. Run only the package portion with:

```bash
./eng/verify-linux-x64-publish.sh
```

In a graphical Linux session with `python3-dbus`, run the production accessibility,
manual goal approval, editable-worktree, multi-document, and layout-recovery
workflow with:

```bash
./eng/verify-avalonia-atspi.py
```

It uses a temporary real Git repository and isolated XDG directories, restores the
session's accessibility flags, and never invokes a model. With Orca installed and no
existing Orca process, also verify generated speech and reject framework type-name
announcements with:

```bash
./eng/verify-avalonia-atspi.py --with-orca
```

Run the production edit/build/test/review/exact-commit workflow through the real
Avalonia UI and provider/tool boundaries without external inference with:

```bash
./eng/verify-avalonia-workflow.py
```

For a longer real-model usability exercise, use one local tool-capable Ollama model
to have Harness generate and independently validate a Tic-Tac-Toe app with an
unbeatable minimax opponent:

```bash
./eng/verify-ollama-tictactoe-usability.py \
  --ollama-endpoint http://127.0.0.1:11434 \
  --model gemma4:latest
```

This is opt-in local inference, not a deterministic release check. It configures no
remote provider and preserves its generated repository, Harness state, logs, timings,
and exhaustive solver-validation evidence under `artifacts/usability/`.

The complete Linux desktop 1.0 gate combines the deterministic/package gate, Orca
speech verification, and the production workflow verifier:

```bash
./eng/verify-v1-desktop-release.sh
```

`--wait-for-shutdown` is a non-interactive operational mode used by lifecycle
checks and service supervisors. It initializes storage, reports readiness, waits,
and exits cleanly when the host receives SIGINT or SIGTERM.

Create a deliberate application-state backup without starting the TUI with an
absolute, new `.zip` destination:

```bash
./artifacts/linux-x64/Harness.Host --backup-path=/absolute/path/harness-state.zip
```

Treat the archive as sensitive: it contains persisted prompts, approvals, evidence,
costs, semantic state, and optionally private workbench layout. For recovery, stop
Harness.NET, verify `manifest.json`, extract `harness.db` into a fresh XDG data root
and any recorded `workbench-layout.json` into the corresponding fresh XDG state root,
then start the current binary so additive migrations and independent layout
validation can run. See [ADR 008](docs/decisions/008-application-state-backup.md).

The wide TUI model panel can refresh the Ollama catalog, display capabilities, and
persist the selected conversation model. Live provider verification can be repeated
with:

```bash
HARNESS_OLLAMA_INTEGRATION_ENDPOINT=http://192.168.1.101:11434 \
HARNESS_OLLAMA_INTEGRATION_MODEL=gemma4:latest \
dotnet test tests/Harness.DataAccess.Tests --filter Category=LiveIntegration
```

OpenRouter catalog verification performs no inference and therefore makes no billed
model request. Export `OPENROUTER_API_KEY` without printing it, then run:

```bash
dotnet test tests/Harness.DataAccess.Tests \
  --filter Category=OpenRouterLiveIntegration
```

A separately gated embedding smoke sends one short input and refuses any reservation
above five microdollars. Run it only when explicitly testing paid inference:

```bash
HARNESS_RUN_OPENROUTER_PAID_TESTS=1 \
dotnet test tests/Harness.DataAccess.Tests \
  --filter Category=OpenRouterPaidLiveIntegration
```
