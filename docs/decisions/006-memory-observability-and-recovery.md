# ADR 006: Memory, observability, and recovery

- Status: Accepted
- Date: 2026-07-26

## Context

Runs must be auditable and recoverable, and semantic memory must remain compatible
with the model that created it. Model content is also sensitive operational data.

## Decision

Use Dapper with explicit SQLite SQL and DbUp embedded migrations. Persist full local
run history, approvals, tool results, usage, summaries, artifacts, and safe-boundary
checkpoints until explicit deletion.

Index eligible Git-tracked text with configurable Ollama or OpenRouter embeddings.
Use the SQLite vector connector inside Data Access and partition indexes by provider,
model, dimensions, and chunking version.

Use Serilog as the `Microsoft.Extensions.Logging.ILogger` implementation for redacted
rolling JSON logs. Provide optional OTLP export with model content disabled by
default. Store provider secrets in Linux Secret Service with environment fallback.

## Consequences

- Interrupted workflows resume from completed step boundaries; uncertain tool calls
  are not replayed automatically.
- Detailed exchanges remain locally auditable and appear summarized but expandable
  in the TUI.
- Changing an embedding configuration creates or rebuilds a compatible index.
- Normal tests use deterministic fakes; opt-in Ollama evaluations protect behavioral
  planning, tool-selection, and review expectations.

### Explicit role retry and budget recovery amendment (2026-07-31)

A role call that fails or becomes uncertain remains a durable `NeedsDirection`
boundary. Harness.NET identifies the exact failed role but never restarts it merely
because the application, provider, or network recovered. After inspecting the recovery
notice, cost ledger, and tool evidence, the user may explicitly retry that exact role
from the last durable checkpoint. The retry itself is another checkpoint and is subject
to the same typed mutation baselines and aggregate cost cap.

Remote-cap recovery is a separate authority decision. An active trusted goal may receive
an increase-only compare-and-swap budget extension with a required reason. The old cap,
new cap, reason, and approval time are durable audit state. Extending a cap does not retry
a model call, and retrying does not extend a cap. Decreases and stale extensions fail
closed.

## Alternatives considered

- Curated-only history was rejected because it weakens audit and recovery.
- A separate vector service was rejected to preserve the single-process deployment.
- Required external telemetry was rejected in favor of useful local defaults.
