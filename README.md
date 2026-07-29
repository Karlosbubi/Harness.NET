# Harness.NET

Harness.NET is a local-first workspace for collaborating with AI agents on .NET
software development under an explicit, user-owned engineering framework. The
detailed product workflow and architectural constraints are documented in this
repository.

## Current status

The current build is a `0.1.0-dev.1` development preview, not an alpha or release
candidate. Framework discovery and the production service slices are substantially
implemented. The application has
compile-time layer enforcement, XDG paths, Secret Service access, SQLite migrations,
redacted local logs, optional OTLP, adaptive Avalonia and Terminal.Gui shells, and an
Ollama provider adapter. The OpenRouter adapter adds dynamic chat/embedding discovery,
streaming, strict privacy routing, and fail-closed goal budgets with attributed
reservation and reconciled-spend reports. The Avalonia goal workspace and TUI Goals
menu create local-only or explicitly capped goals and manage versioned plan
approval/denial. Both adapters show reserved exposure, reconciled spend, remaining
budget, overage, and per-request attribution; they discover configured chat catalogs
and persist an explicit provider/model choice independently for the lead,
implementer, and reviewer.

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
For a trusted active workspace, Avalonia exposes real bounded tracked-text search,
file reading in a syntax-aware editor, Git status and diff inspection, and parsed
.NET solution/project metadata. It does not display a fabricated file tree, terminal,
problem count, build result, or goal progress.
The modal inspector has been replaced by the first Dock-based workbench: source and
diff content open in a central document region while real workspace/search, Git,
goal context, and conversation controls occupy tool regions. Panel movement, hiding,
floating, explicit save/reset, restart restoration, corrupt-state fallback, and
private backup/recovery are implemented without persisting transient editor content.
An approved selected goal opens source from its isolated worktree in an editable
AvaloniaEdit tab; exact-baseline saves flow through the durable typed mutation
boundary, and dirty switching/closing plus external-change conflicts require explicit
decisions. Search, Git state, diff, and source resolve to that same approved worktree;
without one they identify and inspect the original workspace, whose source remains
read-only. Truncated source also remains read-only. Bounded run-output separation
is implemented as a distinct Dock tool over durable typed Build/Test/Restore evidence
without adding a terminal. The rendered Dock content boundary, minimum-size fallback,
keyboard restoration, floating ownership, accessible names, and 200% scaling are now
covered, with real wide/compact review and a repeatable production AT-SPI workflow
recorded under `docs/acceptance`. Production Orca generates contextual speech for
representative controls without announcing visual framework implementation types.
The explicit-goal edit/build/test/review/exact-commit workflow tail tracked by ADR
010 and Tasks 027 and 033 remains a release blocker.
The Avalonia and TUI Framework surfaces show the resolved engineering rules and
guidance with locks, provenance, privacy, and validation issues, and edit only the
private workspace overlay without adding repository metadata.
Avalonia and the TUI create durable goals with review-cycle limits and optional remote
caps, propose and inspect versioned plans, and require confirmation before plan
approval provisions an isolated worktree. Avalonia disables approval until the active
workspace is trusted and explains the exact repository-local capabilities granted.
Trusted-workspace tools now provide confined file reads, tracked-text search,
bounded Git status/diff evidence, non-evaluating .NET metadata, approved atomic file
edits, and cancellable .NET execution in isolated goal worktrees. Agent role
execution now runs lead, implementer, and reviewer prompts through Microsoft Agent
Framework behind semantic Business Logic contracts, with each role using its
configured local default or goal-specific selection. Remote role execution carries
the goal identity, strict privacy policy, and a required output-token ceiling into
the cost-controlled provider boundary. The production coordinator durably runs Lead
planning, approved Implementer work, and independent Reviewer decisions with closed
role tool scopes. Lead plans persist 1-12 ordered tasks with file areas and acceptance
criteria; Implementer executes one task per call and completed reports recover without
replay. Reviewer findings drive bounded correction passes until acceptance or the
configured cycle limit. Avalonia and the TUI show pending and maximum remaining calls,
role routes,
output ceilings, aggregate cap, active reservations, reconciled spend, and remaining
budget before model calls. Both can start bounded Lead planning, continue approved
Implementer/Reviewer work, cancel an active run, and inspect durable tasks, activity,
and evidence.
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

Publish the self-contained development preview with:

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
