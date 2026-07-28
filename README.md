# Harness.NET

Harness.NET is a local-first workspace for collaborating with AI agents on .NET
software development under an explicit, user-owned engineering framework. The
detailed product workflow and architectural constraints are documented in this
repository.

## Current status

Framework discovery is complete and the Stage 1 walking skeleton is running. It has
compile-time layer enforcement, XDG paths, Secret Service access, SQLite migrations,
redacted local logs, optional OTLP, an adaptive Terminal.Gui shell, and an Ollama
provider adapter. The OpenRouter adapter adds dynamic chat/embedding discovery,
streaming, strict privacy routing, and fail-closed goal budgets with attributed
reservation and reconciled-spend reports. The Goals menu creates local-only or
explicitly capped goals, manages versioned plan approval/denial, and shows reserved
exposure, reconciled spend, remaining budget, overage, and per-request attribution;
it also discovers configured chat catalogs and persists an explicit provider/model
choice independently for the lead, implementer, and reviewer.

Semantic indexing now reads bounded eligible text directly from the Git index,
filters generated, binary, sensitive, and oversized content, and creates deterministic
overlapping chunks. The configured embedding route writes atomically replaceable
SQLite vector partitions keyed by provider, model, dimensions, and chunking version;
Business Logic exposes rebuild and retrieval records ready for presentation adapters.

The current usable workflow is a durable local-model conversation: instructions
submitted in the TUI are persisted before inference, streamed through Business Logic,
and reloaded from SQLite on restart. Provider failures are recorded in the transcript.
The workspace modal can also inspect, register, select, and explicitly trust a
Git-backed .NET workspace; the Workspace menu remains available in narrow layouts.
The Framework menu shows the resolved engineering rules and guidance with locks,
provenance, privacy, and validation issues, and edits the private workspace overlay.
The Goals menu creates durable goals with review-cycle limits and optional remote
caps, proposes and inspects versioned plans, and requires confirmation before plan
approval provisions an isolated worktree.
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
configured cycle limit. The TUI shows pending and maximum remaining calls, role routes,
output ceilings, aggregate cap, active reservations, reconciled spend, and remaining
budget before model calls.
All production roles can request 1-8 relevant chunks through a typed semantic-context
tool tied to the active goal workspace and strict remote privacy. The Goals menu can
inspect compatible index status without inference, explicitly rebuild after showing
the embedding route and cost state, and preview attributed matches with source lines,
distance, usage, and cost.
Restore is available only after a durable, correlation- and target-bound user
approval. Approved edit/build/test/restore calls retain durable request/result
evidence for later workflow and GUI presentation. A deterministic walking-skeleton
workflow can now be started, paused, resumed after restart, and inspected through
the TUI using the same presentation-neutral checkpoint contracts intended for a
future Avalonia adapter.
Accepted production work has a separate two-step commit flow: Harness.NET records a
pending request containing the exact branch, HEAD, complete diff hash, message, and
author, then requires an explicit approve/deny action before a local commit. It never
integrates the isolated branch automatically.

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

Use `--no-ui` for a non-interactive startup smoke test. Provider modules, role
routing, conversation defaults, and optional OTLP export are defined in the shipped
`harness.xml` and may be overridden through XDG configuration. See
[runtime configuration](docs/configuration.md) and
[implementation tasks](docs/tasks/README.md) for details.

## Linux x64 publish

Publish the self-contained walking skeleton with:

```bash
dotnet publish src/Harness.Host/Harness.Host.csproj \
  -p:PublishProfile=linux-x64 \
  --output artifacts/linux-x64
```

The output contains a compressed executable, the native libraries that remain
external to avoid runtime extraction outside XDG storage, and the shipped
`harness.xml`. It does not require an installed .NET runtime. Run the repeatable
isolated-XDG startup and SIGTERM shutdown check with:

```bash
./eng/verify-linux-x64-publish.sh
```

`--wait-for-shutdown` is a non-interactive operational mode used by lifecycle
checks and service supervisors. It initializes storage, reports readiness, waits,
and exits cleanly when the host receives SIGINT or SIGTERM.

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
