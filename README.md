# Harness.NET

Harness.NET is a local-first workspace for collaborating with AI agents on .NET
software development under an explicit, user-owned engineering framework. The
detailed product workflow and architectural constraints are documented in this
repository.

## Current status

Framework discovery is complete and the Stage 1 walking skeleton is running. It has
compile-time layer enforcement, XDG paths, Secret Service access, SQLite migrations,
redacted local logs, optional OTLP, an adaptive Terminal.Gui shell, and an Ollama
provider adapter.

The current usable workflow is a durable local-model conversation: instructions
submitted in the TUI are persisted before inference, streamed through Business Logic,
and reloaded from SQLite on restart. Provider failures are recorded in the transcript.
Compact and wide layouts can also inspect, register, select, and explicitly trust a
Git-backed .NET workspace. Repository tools and multi-agent execution are not
implemented yet.

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

The wide TUI model panel can refresh the Ollama catalog, display capabilities, and
persist the selected conversation model. Live provider verification can be repeated
with:

```bash
HARNESS_OLLAMA_INTEGRATION_ENDPOINT=http://192.168.1.101:11434 \
HARNESS_OLLAMA_INTEGRATION_MODEL=gemma4:latest \
dotnet test tests/Harness.DataAccess.Tests --filter Category=LiveIntegration
```
