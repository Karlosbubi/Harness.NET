# ADR 006: Memory, observability, and recovery

- Status: Accepted
- Date: 2026-07-26

## Context

Runs need local audit history and safe restart behavior. Vector data must remain
compatible with its embedding configuration. Model content is sensitive.

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
- Normal tests use deterministic fakes. Live model tests are opt-in.

### Explicit role retry and budget recovery amendment (2026-07-31)

A failed or uncertain role call becomes a durable `NeedsDirection` state. Recovery of
the application, provider, or network does not restart it. The user may retry the
failed role from the last durable checkpoint after reviewing recovery, cost, and tool
evidence. The retry uses the same mutation baselines and aggregate cost policy.

Budget extension is a separate decision. It is increase-only, requires a reason, and
uses compare-and-swap state. Persist the old cap, new cap, reason, and approval time.
Extension does not retry a call; retry does not extend a cap. Reject decreases and
stale requests.

## Alternatives considered

- Curated-only history was rejected because it weakens audit and recovery.
- A separate vector service was rejected to preserve the single-process deployment.
- Required external telemetry was rejected in favor of useful local defaults.
