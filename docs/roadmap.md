# Roadmap

The task ledger is the source of truth for implementation status. A task is complete
only when its code, tests, documentation, and required acceptance evidence are in the
same commit.

Configurable features must include typed configuration, Settings UI, validation,
persistence, runtime status, and documentation. An adapter plus a configuration key
is not a finished feature.

## Completed stages

### Stage 0: architecture

Completed:

- product scope and first repository workflow;
- layer, presentation, hosting, persistence, provider, Git, retrieval, logging, and
  testing decisions;
- initial Ollama server and model verification.

### Stage 1: application skeleton

Completed:

- enforced project layers and composition root;
- XDG storage, Secret Service, SQLite migrations, logs, OTLP, and cancellation;
- Avalonia and Terminal.Gui hosts;
- Ollama and OpenRouter chat, embeddings, discovery, streaming, privacy, and cost
  accounting;
- durable conversations and provider failures;
- workspace registration, entry-point inspection, and trust;
- framework resolution and private overlays;
- isolated goal branches and worktrees;
- typed file, Git, .NET, Build/Test, Restore, and semantic retrieval tools;
- checkpointed Lead, Implementer, and Reviewer workflow;
- self-contained Linux x64 publish.

### Stage 2: complete repository workflow

Completed:

1. Register and trust a Git-backed .NET workspace.
2. Select a solution or project.
3. Create a goal and generate a plan.
4. Approve the plan and create an isolated worktree.
5. Run scoped implementation and validation.
6. Run independent review and bounded correction cycles.
7. Inspect exact evidence and approve a branch commit.
8. Recover from interruption at durable workflow boundaries.

The deterministic release gate verifies this path. It does not prove that Harness.NET
is a complete daily-use IDE.

## Stage 3: daily-use IDE work

Stage 3 is active. Rider/Air, Cursor, and Zed are quality references for editing and
Git workflows, not designs to copy. Harness.NET remains focused on .NET integration,
typed agent tools, and personal configuration.

### Completed usability work

- Task 034: native workspace opening and first-run flow.
- Task 035: file tree, command palette, source editor, Git diff, layout, and visual
  cleanup.
- Task 036: multiple trusted workspaces.
- Task 037: recovery on dirty, large, interrupted, and degraded repositories.
- Task 039: clear post-commit branch handoff.
- Task 040: chat-first goals, plans, decisions, progress, evidence, and Settings.
- Task 041: in-app backup inspection and staged restore.
- Task 042: Roslyn workspace, live diagnostics, and pre-write validation.
- Task 043: completion, quick info, signature help, definitions, usages, and
  implementations.
- Task 044: deterministic Roslyn rename for users and agents.
- Task 046: cited versioned documentation lookup, deterministic dependency and
  candidate evidence, package/SBOM previews, and explicit CycloneDX export.
- Task 047 foundation: model-accessible diagnostics, symbol information, definitions,
  references, implementations, and the Agent tools catalog page.
- Task 045: consented single-frame XDG portal capture, private goal evidence,
  developer preview, agent request/inspect tools, and remote-disclosure policy.

### Completed: Task 045 — controlled visual verification

Add Linux screenshot capture through XDG Desktop Portal.

Requirements:

1. Record an ADR for ownership, consent, privacy, retention, image limits, and
   platform boundaries.
2. Define platform-neutral capture contracts.
3. Implement the Linux Screenshot portal adapter for single-frame capture. Do not
   add ScreenCast, PipeWire, video, or input control.
4. Represent consent, cancellation, denial, portal absence, monitor selection, and
   scaling as typed outcomes.
5. Store bounded captures as goal-scoped evidence with workspace, goal, time,
   initiating action, and application/window identity.
6. Show the same capture and context to the developer and the model.
7. Give models typed request/inspect operations. Do not provide generic desktop
   capture, background surveillance, video by default, or input control.
8. Apply remote disclosure and privacy policy before sending an image to a remote
   model.
9. Verify Wayland behavior, portal denial, 100% and 200% scaling, multiple displays,
   restart cleanup, accessibility, and Linux x64 publish.

Visual evidence supplements deterministic UI tests, AT-SPI, Build/Test, and human
review. It does not replace them.

### Completed: Task 046 — documentation, dependencies, and SBOM

Delivered behavior:

1. Record an ADR for lookup order, authority, version matching, privacy, cache
   identity, package validation, SBOM ownership, citations, and retention.
2. Add one lookup manager with this order:
   exact local/package documentation; local indexed documentation; configured MCP
   sources; web search when earlier sources are insufficient.
3. Keep documentation out of routine prompts. Return a small cited result set only
   when requested.
4. Resolve declared, central, direct, transitive, and restored package versions
   without a model and without implicit restore.
5. Validate candidate packages against configured sources for exact version,
   framework/runtime compatibility, dependency graph, listing/deprecation state,
   advisories, license, provenance, and available integrity data.
6. Generate a reproducible SBOM from the resolved graph. Show package and SBOM diffs
   before mutation. Export only on explicit request.
7. Index version-matched documentation for .NET, Avalonia, Rx.NET, Serilog, Microsoft
   Agent Framework, Roslyn, Dock, Dapper, SQLite, xUnit, and accepted dependencies.
8. Add Documentation/Research UI, agent tools, Settings, offline behavior, and
   deterministic tests.

Unknown or conflicting facts must remain unknown or conflicting. A model may explain
the evidence but may not replace it.

### Next: Task 047 — remaining model-accessible IDE tools

Use the maintained [capability map](agent-ide-capabilities.md). Rider is a breadth
reference only. Unreal-specific tools are excluded.

Implementation order:

1. Finish on-demand toolset activation and persist optional exposure settings.
2. Complete workspace tree, file/regex search, open-document context, project graph,
   dependency, diagnostics, and Git scopes.
3. Add symbol search, call and type hierarchy, associated-test discovery, paging,
   and a deterministic changed-set quality result.
4. Add closed preview/fingerprint/apply operations for formatting, imports,
   namespaces, signature changes, extraction, moves, and safe delete.
5. Add typed asynchronous build, test discovery/run/cancel, launch profiles, process
   output, and stop.
6. Add .NET debugging in separate authority-bounded slices.
7. Add database inspection and query support with Settings and Secret Service.
8. Add optional profiling, notebook, analyzer, and advanced diagnostic modules.

Do not add an unrestricted shell, a generic execute-by-name tool, or an unbounded
tool catalog in every prompt.

## Ongoing work

- Repeat hands-on Avalonia usability checks after changes to workspace, editor, goals,
  evidence, and recovery.
- Keep the accepted workbench, AT-SPI, Orca, and deterministic workflow checks passing.
- Keep Linux-specific code behind focused Presentation or Data Access interfaces.
- Add another platform or gRPC adapter only for a concrete workflow.

## Deferred

- Task 038: opt-in local-model quality regression datasets.
- Distributed workers and message brokers.
- Multi-user accounts and shared authorization.
- Web UI.
- Plugin marketplace.
- Unrestricted agent shells.
- Automatic merge, rebase, push, or pull-request creation.
- Unattended background operation.

Stage 3 ends when the application supports real development across repositories and
restarts without the gaps above. Passing another scripted scenario is not sufficient.
