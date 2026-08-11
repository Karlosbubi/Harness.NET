# Architecture

## Projects and references

Harness.NET is a single-process modular application.

```text
Data Access -> Business Logic -> Presentation -> Harness.UI.Avalonia
      \              |              /
       +----------- Host -----------+
```

- Data Access contains persistence and external adapters.
- Business Logic contains policy, use cases, and workflow state.
- Presentation contains Avalonia and Terminal.Gui adapters.
- `Harness.UI.Avalonia` contains app-neutral Avalonia controls and themes and
  references no Harness runtime project.
- Host is the composition root.
- The analyzer project enforces references and public boundary types.

Only interfaces, records, and enums cross runtime layer boundaries. Prefer enums for
closed sets and single-value records for values with distinct domain meaning.
Implementations remain internal except where DI construction requires visibility.

Provider and MCP SDK types remain in Data Access. Microsoft Agent Framework types
remain behind the Business Logic role interface. Roslyn, MSBuild, and future LSP
types remain in the code-intelligence adapter.

## Core records

| Record | Meaning |
|---|---|
| Workspace | Registered Git repository, selected .NET entry point, trust, and private settings. |
| Framework | Layered rules, locks, and procedures. |
| Goal | User outcome, role routes, spend mode, and review limit. |
| Plan | Ordered bounded tasks that require approval before mutation. |
| Run | One checkpointed goal attempt. |
| Task | One delegated unit with file areas and acceptance criteria. |
| Artifact | Patch, plan, report, decision, or verification result. |
| Approval | Typed authority for an exact consequential action. |
| Evidence | Diff, diagnostic, Build/Test, review, usage, visual capture, or tool result. |
| Source context | Trusted original workspace or approved goal worktree plus entry point and identity. |

## Request flow

1. Presentation sends a record command to Business Logic.
2. Business Logic validates state and authority.
3. Data Access performs database, provider, filesystem, Git, Roslyn, process, MCP,
   documentation, package-registry, or platform work and returns Harness records.
4. Business Logic persists the completed boundary and advances state.
5. Presentation refreshes correlated state.

Long-running operations accept cancellation. Persist a completed tool result before
the next model call. Mark interrupted calls uncertain and do not replay them.

Editor buffers are transient. Presentation sends immutable context-, baseline-, and
version-bound text. Business Logic validates context. Data Access computes semantic
results. Presentation discards stale context or buffer versions. Roslyn does not run
on the UI thread.

## Storage

- XDG configuration: provider/MCP modules, documentation/package sources, framework
  settings, and themes.
- SQLite: goals, conversations, prompts, outputs, tools, approvals, checkpoints,
  usage, artifacts, vectors, summaries, overlays, and preferences.
- XDG state: logs, worktree state, workbench layout, and private bounded visual captures.
- XDG cache: disposable documentation evidence keyed by source, version, query, schema,
  and privacy mode.
- Linux Secret Service: credentials, with configured environment fallback.
- User repository: goal branches and user-approved source or existing guidance only.

Harness.NET does not create a metadata directory in a user repository.

## Agent authority

Lead reads the trusted original workspace and cannot mutate it. Implementer reads and
writes only an approved goal worktree and delegated file areas. Reviewer reads the
same worktree and evidence but cannot write, Build, or Test.

Agents receive typed tools, not shell strings. Paths are canonicalized and confined.
Restore, package work, commit, external access, and destructive operations remain
separate authority decisions.

Model-authored compiler-managed changes are applied to an in-memory solution first.
New compiler errors block the write. Warnings and analyzer findings become evidence.
Accepted multi-file changes use exact baselines and atomic writes, followed by
validation. Semantic rename uses Roslyn symbol identity and a preview fingerprint.
Manual editing remains permissive.

## Models and tools

Business Logic maps Microsoft tool declarations and messages to provider-neutral
records. Data Access serializes Ollama or OpenRouter requests.

Reasoning text and optional protected provider JSON cross through Harness records.
The Agent Framework carries protected data between tool calls. Ollama receives prior
thinking and named tool results; OpenRouter receives `reasoning_details`. Completed
streamed tool calls are emitted once.

Remote cost estimates include messages and tool schemas. OpenRouter reserves cost
before a call and reconciles returned cost. Every call remains attributed to goal,
role, provider, model, and operation.

Semantic retrieval is bound to the role’s active source context, a 1–8 result limit,
and the goal’s privacy and spending policy.

MCP transport and SDK mapping stay in Data Access. Business Logic exposes only
enabled tools that explicitly declare read-only and non-destructive behavior.

The Business Logic research manager owns documentation lookup order, sufficiency,
ranking, citations, version matching, cache freshness, offline behavior, and bounded
context. Data Access owns exact package/SDK files, configured index roots, MCP mapping,
HTTPS search, NuGet v3 metadata, and cache files. Documentation is requested through a
typed operation; it is not added to routine prompts.

Dependency evidence comes from project and central package XML, NuGet lock files, and
existing restored assets. Inspection does not run Restore or project targets. Exact
candidate validation reports incomplete registry facts as unknown. Business Logic
generates stable CycloneDX JSON from the resolved graph and owns package/SBOM previews.
The Data Access exporter writes only an explicitly selected destination.

Visual capture policy stays in Business Logic. The Linux Data Access adapter uses
only the XDG Screenshot portal for a single interactive frame. Presentation supplies
application context and UI scale, renders the exact stored bytes, and never invents
window or display identity omitted by the portal. Remote inspection requires a
separate saved opt-in.

## Platform boundary

Linux is the release target. Presentation owns windows, pickers, clipboard,
notifications, shortcuts, screen geometry, and accessibility. Data Access owns XDG,
filesystem, Secret Service, process behavior, and the replaceable Linux portal
adapter. Host composes these focused
capabilities. Business Logic contains no platform checks.

## Required checks

- architecture analyzer and architecture tests;
- nullable and warnings-as-errors build;
- deterministic domain tests without providers or UI;
- focused adapter integration tests;
- explicit opt-in for live providers and paid requests;
- redacted logs and telemetry without model content by default;
- cancellation and stale-state tests for long-running work;
- atomic workflow and cost reconciliation where state consistency requires it.
