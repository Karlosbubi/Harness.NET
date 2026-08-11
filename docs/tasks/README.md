# Task ledger

This file records task status and acceptance criteria. The
[roadmap](../roadmap.md) records delivery order.

A task is `Done` only when the implementation, focused tests, full build, required
acceptance evidence, and documentation are complete. Configurable features also need
typed Settings ownership, UI, validation, persistence, and status.

Use small commits with one result. Do not mix unrelated cleanup into feature work.

## Completed foundation

| ID | Task | Result |
|---|---|---|
| 001 | Layer projects and boundary tests | Solution structure and reference tests. |
| 002 | Layer-boundary analyzer | Compile-time reference and contract-shape checks. |
| 003 | XDG paths and Secret Service | Private paths and credential boundary. |
| 004 | SQLite, Dapper, and DbUp | Idempotent versioned database startup. |
| 005 | Serilog and OpenTelemetry | Redacted local logs and optional OTLP. |
| 006 | Initial Terminal.Gui shell | Adaptive historical demonstration UI. |
| 007 | Ollama connector | Discovery, chat, embeddings, usage, cancellation, and errors. |
| 008 | OpenRouter connector | Discovery, chat, embeddings, privacy routing, and cost accounting. |
| 009 | Agent Framework boundary | Lead, Implementer, and Reviewer behind Business Logic contracts. |
| 010 | Semantic index | Bounded tracked-text ingestion and compatible vector partitions. |
| 011 | Checkpoint recovery | Persisted safe-boundary resume; historical demo removed from production. |
| 012 | Linux x64 publish | Self-contained startup, XDG behavior, and graceful shutdown. |

## Completed repository workflow

| ID | Task |
|---|---|
| 013 | Durable local-model conversation. |
| 014 | Provider configuration, discovery, role routing, and health. |
| 015 | Git-backed workspace registration, entry-point selection, and trust. |
| 016 | Layered engineering framework with precedence and locks. |
| 017 | Read-only typed inspection tools. |
| 018 | Typed edit, Build, Test, and separately approved Restore tools. |
| 019 | Isolated goal branch and worktree. |
| 020 | Goals, limits, plans, revisions, approvals, and denials. |
| 021 | Lead, Implementer, and Reviewer coordination. |
| 022 | Safe interruption recovery without uncertain-call replay. |
| 023 | Evidence review and exact commit approval. |
| 024 | Goal-scoped repository retrieval. |
| 025 | Remote models with authorization and monetary accounting. |
| 026 | Linux release, migration, outage, cancellation, backup, and recovery gates. |
| 027 | Default Avalonia desktop workflow. |
| 028 | Dock dependency and package boundary. |
| 029 | Central document workbench. |
| 030 | Dockable production tool panels. |
| 031 | Private layout persistence and recovery. |
| 032 | Editable source tabs with exact-baseline saves and conflict handling. |
| 033 | Desktop workflow, accessibility, scaling, and restart acceptance. |

## Completed daily-use work

| ID | Task | Evidence |
|---|---|---|
| 034 | Native workspace opening and first-run flow. | Headless and AT-SPI checks. |
| 035 | File tree, command palette, editor, Git diff, and workbench cleanup. | [Source editor](../acceptance/source-editor-2026-07-29.md). |
| 036 | Multiple trusted workspaces. | [Multi-workspace](../acceptance/multi-workspace-2026-07-31.md). |
| 037 | Dirty, large, interrupted, and degraded repository recovery. | [Messy repositories](../acceptance/messy-repository-recovery-2026-07-31.md). |
| 039 | Post-commit branch handoff. | [Branch handoff](../acceptance/goal-branch-handoff-2026-07-31.md). |
| 040 | Chat-first goals, workflow cards, decisions, recovery, and Settings. | [Chat-first workflow](../acceptance/chat-first-workflow-2026-07-29.md). |
| 041 | Backup inspection and staged cold-start restore. | [Application restore](../acceptance/application-state-restore-2026-07-31.md). |
| 042 | Roslyn workspace, diagnostics, and model-edit validation. | [Compatibility](../acceptance/roslyn-compatibility-2026-07-31.md), [diagnostics](../acceptance/roslyn-live-diagnostics-2026-07-31.md), [edit validation](../acceptance/roslyn-agent-edit-validation-2026-07-31.md). |
| 043 | Completion, quick info, signature help, definition, usage, and implementation navigation. | [Interactive assistance](../acceptance/roslyn-interactive-assistance-2026-07-31.md), [editor verification](../acceptance/editor-intelligence-2026-08-10.md). |
| 044 | Fingerprinted Roslyn rename for users and agents. | [Semantic rename](../acceptance/roslyn-deterministic-rename-2026-07-31.md). |
| 045 | Controlled XDG-portal visual verification. | [Portal visual verification](../acceptance/portal-visual-verification-2026-08-10.md). |
| 046 | Documentation research, dependency validation, and SBOM. | [Documentation and supply-chain evidence](../acceptance/documentation-dependency-sbom-2026-08-11.md). |

## Open tasks

### 038 — local-model quality regression

Status: `Deferred`

Add opt-in Ollama datasets and repeatable measures for planning, tool selection,
implementation, and review quality. Do this after Tasks 045–047.

### 045 — controlled visual verification

Status: `Delivered`

Dependencies: 013, 035, 040.

Problem: UI verification currently uses external screenshot tools and manual image
sharing. Harness.NET cannot bind the inspected frame to the goal and model action.

Acceptance criteria:

1. An ADR defines ownership, consent, privacy, retention, image limits, and platform
   boundaries.
2. Platform-neutral contracts represent capture request, consent, denial,
   cancellation, portal failure, display/window identity, scale, and evidence ID.
3. A Linux XDG Desktop Portal adapter captures one user-approved frame.
4. Captures are bounded, goal-scoped, revocable, and stored outside user repositories.
5. The UI shows the exact frame, goal, time, initiator, related action, and model
   observation.
6. Models can request and inspect approved captures through typed tools.
7. No generic desktop API, background capture, unrestricted video, or input control is
   exposed.
8. Remote disclosure and privacy checks run before image content leaves the machine.
9. Tests cover denial, cancellation, missing portal, stale state, size limits,
   retention, and remote policy.
10. Linux acceptance covers Wayland, scaling, multiple displays, accessibility,
    restart cleanup, and x64 publish.

### 046 — documentation, dependency validation, and SBOM

Status: `Delivered`

Dependencies: 010, 014, 016, 024.

Delivered:

- official MCP C# SDK 2.x;
- stateless Streamable HTTP discovery;
- startup discovery without inference;
- fail-closed read-only agent tool exposure;
- MCP connection Settings.
- ADR 018 lookup, authority, version, privacy, cache, package, citation, retention,
  and SBOM rules;
- ordered exact-local, local-index, configured-MCP, and web research manager;
- bounded ranked versioned evidence with citation, freshness, confidence, conflicts,
  cache identity, offline behavior, and escalation history;
- deterministic project, central, lock, direct, transitive, and restored dependency
  evidence without Restore or model inference;
- exact candidate availability, framework/runtime assets, transitive ranges,
  prerelease, listing/deprecation, advisory, license, provenance, and integrity checks;
- reproducible CycloneDX 1.6 JSON, package/SBOM diff, preview-only agent operation,
  and explicit developer export;
- accepted core-library catalog and automatic dependency-version resolution;
- named developer and agent tools plus complete Settings source/cache/offline/status
  management;
- deterministic conflict, stale, mismatch, offline, MCP failure, web fallback,
  cancellation, deduplication, context, registry, lock, SBOM, and export tests.

Acceptance criteria:

1. An ADR defines lookup order, source authority, sufficiency, privacy, version
   matching, cache identity, package validation, SBOM ownership, citations, and
   retention.
2. One Business Logic manager queries exact local/package docs, local indexes,
   configured MCP sources, then web search only when required.
3. Results are small, ranked, cited, versioned, and requested on demand rather than
   included in every prompt.
4. A non-model service resolves declared, central, direct, transitive, and restored
   dependency versions without implicit restore.
5. Candidate validation checks exact package/version availability,
   framework/runtime compatibility, transitive dependencies, prerelease policy,
   listing/deprecation, advisories, license, provenance, and available integrity data.
6. A reproducible SBOM records the resolved graph and provenance. Package changes
   show dependency and SBOM diffs before mutation.
7. Version-matched documentation covers the accepted core library set.
8. Developer and agent lookup tools expose source, version, freshness, confidence,
   citation, and escalation reason.
9. Settings manages sources, indexes, refresh, cache, offline mode, retention, and
   failures.
10. Deterministic tests cover conflicts, stale assets, version mismatch, offline mode,
    MCP failure, web fallback, cancellation, deduplication, and context limits.

### 047 — model-accessible IDE tools

Status: `Partial`

Dependencies: 017, 018, 042, 043, 044, 046.

Delivered:

- Rider 2026.2 capability inventory and ADR 016;
- typed built-in module catalog;
- Settings → Agent tools status page;
- exact-file diagnostics, symbol information, definition, reference, and
  implementation tools for all roles;
- semantic rename preview/apply for the Implementer.

Remaining acceptance criteria:

1. Activate optional typed toolsets only for the next bounded role turn. A request
   does not invoke a tool or grant authority.
2. Persist safe optional-module exposure settings and project toolset use into run
   evidence.
3. Complete tree/glob/regex/ranged reads, open-document context, solution/project
   graph, dependency graph, project/changed-set diagnostics, and Git scope.
4. Add symbol search, call graph, type/override hierarchy, associated tests, paging,
   depth limits, and a deterministic changed-set quality result.
5. Add closed preview/fingerprint/apply operations for formatting, imports,
   namespaces, signature changes, extraction, moves, and safe delete.
6. Add typed asynchronous Build/Rebuild, test discovery/run/cancel, launch profile
   discovery, one-run overrides, structured output, and stop.
7. Add .NET debugger, database, profiler, notebook, and analyzer modules in separate
   authority-bounded slices.
8. Keep provider SDKs, Roslyn, debugger, database, and platform types inside their
   owning adapter boundaries.
9. Do not add an unrestricted shell or generic dynamic execute-by-name tool.
10. Exclude Unreal-specific behavior.

The detailed status matrix is [agent-ide-capabilities.md](../agent-ide-capabilities.md).

### 048 — Morgania editor evaluation and conditional migration

Status: `Planned`

Dependencies: 010, 012, 032, 043, 044.

Problem: the current AvaloniaEdit adapter requires custom code for editor sessions,
completion, signatures, diagnostics, navigation, and popups. Morgania may provide a
more coherent Avalonia and Roslyn editor foundation, but it also vendors Visual
Studio editor code and uses tightly coupled Roslyn editor components. Its
cross-platform claim does not prove Harness.NET's Linux, accessibility, lifecycle,
or publication requirements.

Acceptance criteria:

1. An evaluation records pinned upstream revisions, license, package and support
   status, dependency provenance, integrity, and SBOM impact.
2. An ADR amendment documents the choice before production adoption, including
   vendored code, MEF, Roslyn internals, version coupling, ownership, and rollback.
3. A Presentation-owned adapter slice retains the current Business Logic contracts,
   model tools, source identity, buffer versioning, and model-write validation.
4. The slice covers editing, dirty/save/conflict behavior, diagnostics, completion,
   signatures, quick info, navigation, rename, code actions, Dock integration, and
   restoration. AvaloniaEdit remains available during evaluation.
5. User and model operations use the same live buffer and reject stale results.
6. Wayland and X11 checks cover input, IME, clipboard, focus, popups, multi-caret,
   AT-SPI, Orca, scaling, displays, layout, and Linux x64 publish.
7. Measurements cover startup, load, typing, completion, diagnostics, cancellation,
   memory, disposal, and repeated source-context changes against the current editor.
8. Adoption requires a clear maintenance benefit and a passing complete desktop
   gate. Reject the migration for boundary leakage, failed accessibility or input,
   unacceptable resource cost, or recurring private Roslyn patch burden.
9. Removal of AvaloniaEdit is a separate reviewed cutover after the migrated editor
   passes acceptance and the rollback evidence is recorded.
