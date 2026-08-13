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

Status: `Delivered`

Acceptance: [Local-model regression corpus](../acceptance/local-model-regression-2026-08-12.md).

Dependencies: 045, 046, 047, 059.

Delivery: the Tic-Tac-Toe script now exercises the stateless MCP lifecycle as part of
a versioned regression corpus. It records invalid plans, tool choice, rewrite size,
recovery, and model-specific failures that ordinary deterministic tests do not expose.

Acceptance criteria:

1. Versioned scenarios cover planning, tool selection, implementation, correction,
   review, partial completion, and recovery without depending on paid inference.
2. The initial corpus includes the validated Tic-Tac-Toe solver task and smaller
   focused semantic-edit, multi-file, Build/Test, failure, and retry cases.
3. Each run records Harness revision, scenario, prompt, model/server identity,
   discovered capabilities, route settings, timing, resource use, tool trace, diff,
   evidence, and terminal outcome under ignored artifacts.
4. Deterministic validators judge repository state, compilation, tests, allowed
   paths, semantic-operation use, and required evidence. Model prose is not ground
   truth.
5. Metrics include plan validity, completion, partial completion, retry count, tool
   errors, rewrite size, compiler regressions, review findings, latency, and resource
   use.
6. Baseline comparison reports regressions and improvements without using one model's
   output as a golden patch.
7. Live Ollama execution is explicit and opt-in. Default test runs use deterministic
   fakes and spend nothing.
8. Cancellation, unavailable models, server restart, truncated output, malformed
   tool calls, and unsupported reasoning/tool combinations remain inspectable.
9. The suite can compare several configured local models within bounded concurrency
   and 16 GB VRAM constraints.
10. Documentation states how to reproduce, compare, retain, and clean artifacts.

### 059 — inbound MCP control and evaluation

Status: `Delivered`

Delivery order: immediately after 047 and before 038. The numeric ID is intentionally
later so Tasks 050–058 keep their published identities.

Dependencies: 015, 040, 045, 046, 047.

Problem: Harness exposes MCP tools to its own agents but cannot expose its application
state and typed actions to an external evaluation agent. Filesystem scripts and
screenshots can observe only fragments of the workflow, while a generic remote-control
API would bypass the product's authority and privacy model.

Acceptance criteria:

1. An ADR defines inbound MCP ownership, stateless transport, authentication,
   application-instance identity, normal and evaluation modes, tool policy, approval,
   privacy, persistence, audit, isolation, startup, and shutdown.
2. Data Access owns the official MCP SDK and Streamable HTTP transport. Business Logic
   owns tool eligibility, exact application/workspace/session/source identities,
   commands, authority, evidence, and result contracts. Presentation only reports
   status and adapts developer actions.
3. Settings ships in the first slice with enablement, mode, loopback endpoint,
   authentication status, token rotation, client/tool allowlists, per-tool approval,
   timeouts, limits, retention, health, active clients, disconnect, and reset.
4. Normal mode is disabled by default, binds only to loopback, requires an
   authenticated client, shows a persistent active-control indicator, and supports
   immediate revocation without restarting Harness.
5. Read-only tools inspect application/provider/MCP health, workspaces, active source
   context, open documents, Roslyn results, Git state, goals/runs/sessions, plans,
   evidence, costs, accessibility state, and layout with paging and freshness.
6. Typed action tools may open or focus a Harness document/panel, invoke an existing
   closed Business Logic command, start bounded Build/Test, manage a goal decision,
   or request Task 045 capture. Existing trust, approval, spend, baseline, privacy,
   and execution rules run unchanged.
7. Every tool declares read-only, mutation, execution, sensitive, destructive, and
   idempotency metadata accurately. Tool exposure is allowlisted, approval-controlled,
   bounded, attributed to a client, and recorded as inspectable evidence.
8. Isolated evaluation mode uses a temporary database/configuration root, disposable
   fixture repository and worktrees, fake or explicitly selected Ollama providers,
   no stored credentials, no normal repositories, and deterministic reset/snapshot
   operations.
9. Evaluation-only UI tools can inspect Harness-owned rendered frames and the AT-SPI
   tree and activate allowlisted Harness accessibility identities. They cannot
   control another process, arbitrary screen coordinates, global input, the desktop,
   or normal-mode screenshots without portal consent.
10. No tool accepts a shell string, arbitrary executable, raw SQL, generic tool name,
    unrestricted path, generic click/type target, credential value, or natural-language
    authority. MCP initialization instructions state these constraints concisely.
11. Stateless calls carry exact application instance, workspace, source context,
    session, document/baseline, operation, and continuation identities as applicable;
    stale or cross-instance calls fail without mutation.
12. Deterministic fake-client and live local-client tests cover discovery, schema,
    authentication, rotation, allowlists, approvals, stale identities, concurrency,
    cancellation, reconnect, malformed input, oversized output, reset, isolation,
    portal denial, accessibility, shutdown, restart, and Linux x64 publish.
13. A distinct loopback-only outbound `HarnessControl` connection can authenticate to
    a worker Harness instance and expose an exact allowlist of lifecycle tools to Lead.
    Ordinary MCP connections, Implementer, and Reviewer remain read-only. Settings
    owns the write-only token, client ID, allowlist, validation, status, and persistence.

Delivered in the Task 047/059 integration slice: accepted ADR 019; official SDK 2.x
stateless Streamable HTTP; loopback bearer authentication and one-time enrollment;
closed client/tool/approval policy; bounded audit and immediate revocation; typed
workspace, project, Git, goal/plan/workflow/cost/evidence, Roslyn, document/UI,
Build/Test, full asynchronous goal lifecycle, accepted-change/commit decision,
capture, and evaluation operations; exact instance/source
identities; volatile evaluation secrets; deterministic fixture snapshot/reset;
Harness-owned evaluation frames and closed accessibility actions; Settings ownership;
directed Harness-to-Harness Lead delegation; deterministic client/isolation tests; and
Linux publish verification.

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

### 047 — model-accessible semantic IDE foundation

Status: `Delivered`

Dependencies: 017, 018, 042, 043, 044, 046.

Delivered:

- Rider 2026.2 capability inventory and ADR 016;
- typed built-in module catalog;
- Settings → Agent tools status page;
- exact-file diagnostics, symbol information, definition, reference, and
  implementation tools for all roles;
- semantic rename preview/apply for the Implementer;
- one-turn on-demand modules with durable evidence and saved safe exposure;
- bounded tree/range/regex/project graph and open-buffer context;
- symbol search, call/type/override graphs, associated tests, and paging;
- exact result identity and deterministic changed-set quality.

Delivered acceptance criteria:

1. Activate optional typed toolsets only for the next bounded role turn. A request
   does not invoke a tool or grant authority.
2. Persist safe optional-module exposure settings and project toolset use into run
   evidence.
3. Complete tree/glob/regex/ranged reads, open-document context, solution/project
   graph, dependency graph, project/changed-set diagnostics, and Git scope.
4. Add symbol search, call graph, type/override hierarchy, associated tests, paging,
   depth limits, and a deterministic changed-set quality result.
5. Every result identifies workspace, source context, project, target framework,
   configuration, document version, freshness, truncation, and continuation where
   applicable.
6. Keep later Git, execution, debugger, database, profiler, notebook, and analyzer
   modules behind the same catalog and authority-class contracts without claiming
   those modules as part of this task.
7. Keep provider SDKs, Roslyn, Git, process, debugger, database, and platform types
   inside their owning adapter boundaries.
8. Do not add an unrestricted shell or generic dynamic execute-by-name tool.
9. Exclude Unreal-specific behavior.

The detailed status matrix is [agent-ide-capabilities.md](../agent-ide-capabilities.md).

### 048 — Morgania editor evaluation and conditional migration

Status: `Evaluated — migration rejected at the dependency gate`

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

### 049 — NetPad-level .NET editing and inspection

Status: `In progress — presentation, transformations, navigation, inspections, keybindings, and Vim input delivered`

Dependencies: 012, 043, 044, 047, 048.

References:

- NetPad `0c74746daf6f5402ad4d9a2cf3958131bdfc8011`;
- OmniSharp Roslyn `83fd615eafff33e297a9f59280d929cf09ec0d3c`.

Progress: semantic classification, occurrences, folding, outline, breadcrumbs,
workspace-symbol search, visible-buffer inlay hints, and lazy reference,
implementation, and associated-test CodeLens actions now share the exact live-buffer
Roslyn session and discard stale results. Viewport refresh avoids rebuilding structure,
and document occurrences avoid a solution-wide search. The maintained comparison is
[netpad-omnisharp-parity.md](../netpad-omnisharp-parity.md), with evidence in
[editor-inlays-codelens-2026-08-12.md](../acceptance/editor-inlays-codelens-2026-08-12.md).
Document/selection/changed-span formatting, guarded paste and supported on-type
formatting, import organization, compiler-proven unused-import cleanup, and missing-type
import fixes now use closed Roslyn previews. Missing-import
discovery returns only namespaces that bind the unresolved type at the exact caret.
The developer path is a guarded undoable live-buffer change; the Implementer path adds
an exact fingerprint, delegated-path check, atomic apply, durable evidence, and
post-apply validation. See
[editor-transformations-2026-08-12.md](../acceptance/editor-transformations-2026-08-12.md).

The same path now carries an explicit pinned Roslyn quick-fix/refactoring catalog.
Caret and exact-selection discovery returns only preflighted current-document edits;
safe providers also expose a bounded document-wide fix-all. Opaque action IDs and
typed scopes are required for preview/apply in the editor and model tools, and the
read-only catalog is available through opt-in inbound MCP.

File and workspace-symbol search cover file, type, and symbol navigation. Regions are
first-class outline entries without polluting lexical breadcrumbs. Definitions,
usages, and implementations resolve source-generator output and metadata signatures
through opaque handles bound to the exact live buffer. The desktop opens these as
labeled read-only C# documents and excludes them from repository and layout
persistence. Role and inbound MCP navigation results eagerly include successful
virtual documents before their short-lived Roslyn session closes. Full method-body
decompilation remains pending a maintained public dependency and supply-chain review.

The editor's Inspect menu now opens bounded read-only syntax-tree, symbol-detail,
generated-source, and Intermediate Language views. Roslyn builds each view from the
exact live buffer. Every result includes project version, target framework,
configuration, assembly, buffer version, and compilation identity. IL uses an in-
memory emit and metadata reader; it neither executes the project nor writes build
output. Roles and opt-in inbound MCP receive the same closed contract through
`inspect_code` and `harness_code_inspection`.

The workbench now uses one typed keybinding snapshot for shell and editor dispatch,
Settings, header hints, and command discovery. Settings provides whole-set validation,
conflict display, protected platform/input shortcuts, reset, and strict bounded
`harness-keybindings-v1` import/export. The same persisted settings select Standard or
Vim input. Vim operates on the existing live buffer, exposes Normal, Insert, Visual,
and Visual Line state, suspends modal exit during IME preedit, and passes unrelated
platform shortcuts through. Evidence is recorded in
[editor-keybindings-2026-08-13.md](../acceptance/editor-keybindings-2026-08-13.md) and
[editor-vim-mode-2026-08-13.md](../acceptance/editor-vim-mode-2026-08-13.md).

Problem: Harness.NET now has the core interactive Roslyn operations, semantic
presentation and adornment slices, formatting, closed actions, bounded generated and
metadata-signature navigation, and configurable keybindings. It still lacks explicit
cross-document refactoring contracts and full method-body decompilation. It also lacks
project User Secrets management, and typed execution targets for Run/Debug CodeLens.
OmniSharp implements many of the underlying Roslyn services, but adopting its server
would duplicate the current workspace and add process, download, version, recovery,
and transport costs.

Acceptance criteria:

1. A maintained parity matrix distinguishes delivered, missing, deliberately
   excluded, and Task 047 capabilities against the pinned NetPad and OmniSharp
   revisions.
2. Semantic classification, occurrence highlighting, folding, outline, breadcrumbs,
   and workspace symbol search update incrementally from the exact live buffer and
   discard stale results.
3. Settings-managed inlay hints and bounded CodeLens resolve lazily and cover
   references, implementations, tests, and valid run/debug actions.
4. Formatting, usings, code actions, refactorings, and fix-all use closed typed
   operations. Multi-file and model-requested changes use
   preview/fingerprint/apply, path authority, exact baselines, and post-checks.
5. Type, file, region, symbol, generated-source, and metadata/decompiled-source
   navigation is bounded. Virtual documents are labeled, read-only, and not written
   into the repository or normal document persistence.
6. Syntax-tree, symbol, generated-source, and IL views record the exact source,
   project, target, configuration, document version, and compilation identity.
7. Keybindings support validation, conflict display, reset, safe declarative
   import/export, command discovery, and optional Vim mode without breaking IME,
   accessibility, or platform shortcuts.
8. Project User Secrets use the standard .NET store and separate list, reveal, copy,
   add, change, and delete actions. Values are redacted from logs, evidence, backups,
   model context, search, and indexes. Values are masked by default, portal capture
   is blocked while a value is revealed, and no generic agent read is added.
9. Developer UI and typed model tools share implementation-neutral Business Logic
   contracts and one semantic buffer state. Agent transforms remain narrower and
   more auditable than manual editing.
10. In-process Roslyn remains the default. Reused source requires license,
    attribution, provenance, version, test, and SBOM review. OmniSharp types stay in
    Data Access and no runtime download, implicit Restore, or duplicate workspace is
    introduced.
11. An out-of-process OmniSharp adapter requires measured benefit and an ADR 012
    amendment. If approved, it includes pinned offline installation, integrity,
    lifecycle, readiness, crash recovery, restart, cancellation, and degraded-state
    behavior.
12. Deterministic and Linux desktop tests cover correctness, large solutions,
    latency, memory, cancellation, analyzer failure, source switches, keyboard use,
    IME, AT-SPI, Orca, scaling, Dock restoration, and Linux x64 publish.

NetPad's playground-only script, rich dump, spreadsheet export, and web-shell
features are outside this task. Task 052 covers the overlapping project, Run, Test,
and Debug needs. Database, profiler, and notebook modules remain later slices.

### 050 — complete Git workbench

Status: `Planned`

Dependencies: 023, 029, 035, 036, 039.

Problem: Git status, diff, exact commit approval, and branch handoff exist, but normal
developer work still requires another application for staging, history, stash,
branches, remote synchronization, and conflicts.

Acceptance criteria:

1. One Git state model supplies Files, editor gutters, diff, review, and Git tools
   for the active original or goal source context.
2. Stage and unstage support files, lines, and hunks with exact index/worktree
   baselines and stale-state rejection.
3. Discard, clean, branch deletion, and other destructive actions show exact targets,
   consequences, and available recovery before confirmation.
4. Developer commit and amend show the staged diff, branch, HEAD, identity, hooks
   policy, and result. Goal commit approval remains a separate exact flow.
5. Branch, tag, worktree, and stash views support create, switch/apply, rename where
   safe, and deletion with conflict and dirty-state handling.
6. History graph, file timeline, blame, commit details, and parent/child diffs remain
   responsive on large histories through paging and cancellation.
7. A three-way merge editor shows base, ours, theirs, result, unresolved regions,
   diagnostics, and exact save state without auto-resolving silently.
8. Fetch, pull, and push are explicit developer actions with remote/refspec,
   credential source, divergence, network, force policy, and result display. They do
   not become goal or agent authority.
9. Credentials stay behind Secret Service/configured Git boundaries and never enter
   logs, prompts, evidence, backups, or screenshots.
10. An ADR records remote and destructive Git ownership before implementation; tests
    cover stale index, conflicts, detached HEAD, submodules, worktrees, network
    failure, cancellation, restart, accessibility, and large repositories.

### 051 — developer terminal and structured tasks

Status: `Planned`

Dependencies: 015, 026, 029, 033, 037.

Problem: Run output correctly shows typed evidence, but a daily-use IDE also needs a
developer terminal and repeatable repository tasks. Giving agents the same terminal
would break the typed-authority model.

Acceptance criteria:

1. An ADR separates the developer PTY, structured tasks, typed agent operations, and
   durable Run output before process contracts change.
2. A dockable terminal supports input, output, resize, scrollback, copy/paste,
   selection, links, search, multiple sessions, cancellation, and process-tree stop.
3. Terminal creation shows workspace, source context, working directory, shell,
   environment profile, trust, and whether content is persisted.
4. Only trusted workspaces can start repository processes. Reopening the application
   restores terminal metadata and optional bounded scrollback, never a live process.
5. Structured tasks use typed executable, arguments, working directory, environment,
   dependency, presentation, cancellation, and problem-matcher contracts rather
   than a shell command string.
6. Discover supported tasks from existing repository conventions and private Harness
   settings without creating repository metadata.
7. Task UI supports discover, inspect, run, rerun, stop, recent results, and clear
   separation from Build/Test evidence.
8. Secrets and sensitive environment values are masked and excluded from logs,
   persistence, backups, model context, diagnostic bundles, and portal captures.
9. Agents cannot read, type into, or execute through the developer terminal. A future
   agent command must be a separate closed typed operation under ADR 016.
10. Linux acceptance covers common shells, Unicode, IME, resize, large output,
    process trees, cancellation, crash cleanup, AT-SPI, restart, and publish.

### 052 — .NET project, Run, Test, and Debug experience

Status: `Planned`

Dependencies: 018, 042, 047, 049, 051.

Problem: Harness can Build and Test through goal tools but lacks the project system,
execution controls, Test Explorer, and debugger needed for ordinary .NET work.

Acceptance criteria:

1. A semantic Solution view presents projects, target frameworks, configurations,
   references, packages, SDK/workload health, startup projects, and loading failures.
2. Launch discovery covers standard .NET and repository configuration without
   executing projects or restoring packages. One-run overrides are typed, validated,
   visible, and non-persistent by default.
3. Build/Rebuild, Run, Test, Debug, and Hot Reload are asynchronous identities with
   target, configuration, source context, state, structured output, cancellation,
   duration, and durable result.
4. Test Explorer supports discovery, hierarchy, search, traits, filters, run/debug
   selection, duration, failure history, source navigation, rerun, and cancellation.
5. Coverage import or collection records tool/version/provenance and maps exact
   results to editor and project views without implying uncovered code is defective.
6. The .NET debugger supports typed launch/attach, breakpoints, threads, stacks,
   scopes, variables, watches, stepping, stop, and source mapping behind an adapter.
7. Expression evaluation, value mutation, attach, dumps, external processes, and
   network listeners have separate risk classes and explicit developer decisions.
8. ASP.NET endpoint preview and Avalonia launch/capture bind to the exact run and may
   use Task 045 evidence; no generic browser or desktop control is introduced.
9. User and agent operations share Business Logic contracts, but agents receive only
   role-, phase-, trust-, target-, and authority-scoped commands with bounded output.
10. Tests cover multi-targeting, large solutions, discovery failure, cancellation,
    stale binaries, process leaks, hot-reload rejection, debug failures, restart,
    accessibility, and Linux x64 publish without implicit Restore.

### 053 — parallel local agent sessions and exact review

Status: `Planned`

Dependencies: 019, 021, 022, 023, 038, 040, 047.

Problem: the durable goal workflow is recoverable but behaves as one prominent run.
Modern agent work requires several inspectable, isolated tasks and a tighter
change-review loop.

Acceptance criteria:

1. An ADR defines session identity, concurrency, worktree ownership, model routes,
   budgets, approvals, checkpoints, attention, archive, and cleanup.
2. Each session owns its conversation, goal/run, source context, worktree, role
   routes, spend policy, permissions, evidence, checkpoints, and cancellation.
3. A session switcher and dashboard show running, paused, failed, blocked, complete,
   needs-direction, unread, model, branch, cost, elapsed time, and last safe boundary.
4. Search, archive, restore, pause/resume, follow-up, manual takeover, and explicit
   cleanup survive restart without replaying uncertain calls.
5. Concurrency is bounded by Settings and resource health; sessions do not share
   mutable Roslyn, process, approval, reservation, or worktree state accidentally.
6. Review supports next/previous change and accept/reject by line, hunk, file, or
   complete candidate with exact-baseline and stale-diff checks.
7. Review comments bind to source/diff coordinates and become explicit retry guidance
   or developer notes; they do not mutate a workspace by themselves.
8. Checkpoint restore and alternative-attempt fork preserve the prior history,
   evidence, cost, source identity, and comparison between candidates.
9. Completion and partial completion show integrated, rejected, unresolved, and
   unreviewed changes plus the exact next action.
10. Tests cover concurrent model/tool calls, cancellation, app crash, worktree
    collision, budget exhaustion, source switching, stale review, archive/restore,
    keyboard operation, accessibility, and bounded resource use.

### 054 — ACP external-agent interoperability

Status: `Planned`

Dependencies: 014, 015, 016, 040, 047, 053.

Problem: MCP exposes tools and information, not complete coding agents. Harness needs
an open agent boundary without replacing its own authority, evidence, and worktree
model with provider-specific integration.

Acceptance criteria:

1. An ADR defines ACP versioning, client ownership, transport, process/network
   authority, capabilities, attribution, context, permissions, persistence, and
   failure isolation.
2. Data Access owns protocol and transport types; Business Logic owns configured
   agent identity, eligibility, session policy, context, tools, and evidence.
3. Settings ships in the first slice with executable or endpoint, transport,
   arguments, environment references, working directory, enablement, trust, timeout,
   retention, health, and removal.
4. Startup or explicit refresh validates configuration and negotiates capabilities
   without inference, repository mutation, or implicit network disclosure.
5. External agents run inside Task 053 sessions and receive only the selected source
   context and approved Harness/MCP tools. They cannot impersonate Lead,
   Implementer, Reviewer, or built-in tools.
6. Messages, attachments, tool schemas, tool results, images, and context are bounded,
   attributed, persisted deliberately, and checked against remote-disclosure policy.
7. Permission requests map to existing typed authority and are denied when no exact
   Harness operation exists; protocol consent is not repository authority.
8. Owned processes have readiness, cancellation, timeout, crash, restart, log
   redaction, and process-tree cleanup. Remote endpoints show connection and privacy
   state.
9. Unsupported capability, protocol mismatch, malformed message, disconnect,
   reconnect, duplicate event, cancellation, and app restart produce recoverable
   states rather than silent fallback.
10. Deterministic fake-agent tests and opt-in local integrations cover negotiation,
    sessions, tools, permissions, streaming, failure, Settings, accessibility, and
    Linux publish. Exposing Harness as an ACP agent is out of scope.

### 055 — inline AI assistance and edit prediction

Status: `Planned`

Dependencies: 038, 049, 053.

Problem: chat handles deliberate goals but is too heavy for selection edits, quick
questions, and likely next changes during manual coding.

Acceptance criteria:

1. Selection edit, quick question, send-to-chat, and next-edit prediction are
   separate commands with clear scope and keyboard behavior.
2. Suggestions identify provider/model, local or remote route, source-context and
   document versions, affected range, state, and remote-disclosure decision.
3. Accept supports token, line, suggestion, or exact preview where meaningful;
   reject, dismiss, undo, and conflict handling remain immediate and deterministic.
4. Multi-file or semantic suggestions use preview/fingerprint/apply, delegated paths
   where applicable, exact baselines, atomic writes, and Roslyn post-validation.
5. Settings owns enablement, trigger policy, delay, feature-specific routes, local
   preference, remote disclosure, monetary mode, and status from the first slice.
6. Typing never waits for inference. Requests debounce, cancel on stale input or
   navigation, avoid duplicate calls, and expose offline/degraded state without
   interrupting manual editing.
7. Context is minimal and inspectable: selection, nearby syntax, diagnostics, and
   requested symbols rather than an ambient repository dump.
8. Local opt-in metrics cover time to first suggestion, completion, cancellation,
   acceptance, rejection, edit survival, and validation without recording source
   text as telemetry.
9. Deterministic tests cover stale responses, rapid typing, undo/redo, conflicting
   edits, remote denial, provider failure, accessibility, IME, and restart.
10. The local-model regression suite measures feature quality before any route becomes
    a shipped default.

### 056 — customization, context inspection, and agent safety

Status: `Planned`

Dependencies: 016, 024, 040, 046, 053, 054.

Problem: Harness has layered framework sources and role routes, but lacks reusable
procedures, explicit handoffs, call-by-call context visibility, and deterministic
lifecycle policy suitable for a personalized agent IDE.

Acceptance criteria:

1. An ADR defines skills, prompt procedures, role profiles, handoffs, typed policies,
   context provenance, untrusted-content treatment, exclusions, and profile storage.
2. Skills and procedures are versioned, discoverable, inspectable, enableable, and
   bounded; imported content shows source, integrity where available, permissions,
   and conflicts before activation.
3. Role profiles select instructions, eligible tools, model route, reasoning defaults,
   review policy, and handoffs without weakening locked framework or authority rules.
4. Lifecycle policies compose closed Harness operations such as format changed C#,
   run affected tests, require review for paths, or block an operation class. No
   arbitrary shell-hook string is accepted.
5. A pre-call context inspector shows provider/model, files, selections, rules,
   summaries, retrieval, documentation, MCP/ACP sources, tools, images, estimated
   disclosure, and exclusions.
6. Developers can remove items, request bounded compaction with provenance, or fork a
   session. The persisted call records the exact resulting context manifest.
7. Private global/workspace exclusions and compatible existing ignore files apply to
   search, index, model context, screenshots, diagnostics export, and attachments
   without creating Harness repository metadata.
8. Web, MCP, ACP, documentation, terminal, repository, and generated content remain
   attributed untrusted data and cannot authorize tools or override locked rules.
9. Local profiles support export, import, validation, diff, rollback, schema
   migration, and workspace association. Secrets and machine paths are omitted or
   represented by explicit unresolved references; cloud sync is out of scope.
10. Tests cover precedence, conflicts, malicious instructions, prompt injection,
    stale context, exclusion leaks, import corruption, policy denial, accessibility,
    and restart.

### 057 — release health, diagnostics, and updates

Status: `Planned`

Dependencies: 026, 033, 041, 053, 054.

Problem: the Linux publish is verified, but a daily-use application also needs
trusted updates, rollback, diagnosable failures, and explicit performance limits.

Acceptance criteria:

1. An ADR defines release signing, manifest ownership, update discovery, download,
   installation, schema compatibility, rollback, diagnostic bundles, telemetry, and
   retention.
2. Linux builds are reproducible within documented tolerances and publish signed
   artifacts, checksums, SBOM, version, source revision, and dependency provenance.
3. Update discovery shows channel, version, notes, provenance, size, compatibility,
   and restart/migration effect. Download and installation are separate explicit
   developer actions.
4. Integrity and signature checks occur before staging. Failed start or migration
   retains a verifiable rollback path for executable and application state.
5. A diagnostic bundle previews included files and redactions, excludes credentials
   and repository source by default, and requires an explicit destination.
6. Health views cover storage/migrations, worktrees, Roslyn, indexing, providers,
   MCP, ACP, processes, portals, logs, and stale or degraded dependencies.
7. Crash and recovery UI identifies affected sessions, last safe checkpoints,
   uncertain operations, retained artifacts, and available actions without automatic
   replay.
8. Measured budgets cover cold/warm startup, workspace load, typing, completion,
   diagnostics, navigation, memory, cancellation, shutdown, and idle resource use.
9. Telemetry is documented, optional, disabled by default, locally inspectable,
   revocable, and unable to include prompts, source, secrets, paths, or credentials.
10. Release tests cover offline mode, invalid signature, interrupted staging, disk
    exhaustion, incompatible schema, rollback, redaction, accessibility, and Linux
    x64 installation.

### 058 — replaceable development-environment adapters

Status: `Planned`

Dependencies: 050, 051, 052, 057.

Problem: Linux-first local work remains primary, but filesystem, process, SDK,
network, and port assumptions must not prevent later container, SSH, or platform
adapters.

Acceptance criteria:

1. An ADR defines environment identity, trust, transport, filesystem/process
   ownership, credentials, ports, data location, lifecycle, reconnect, and cleanup.
2. Business Logic contracts represent local and remote environment identity,
   workspace root, capabilities, health, trust, disclosure, connection, cancellation,
   and errors without SSH/container/platform types.
3. Data Access adapters own transport, remote filesystem, process, SDK/tool discovery,
   port forwarding, authentication, timeouts, and cleanup.
4. The first slice supports explicit developer open/reopen of a local container or
   SSH workspace, source browsing/editing, Roslyn/project health, Git, terminal,
   tasks, and typed Build/Test/Run where capabilities permit.
5. UI distinguishes local UI state from remote source, process, credential, port,
   model, and evidence locations at all times.
6. Disconnect, reconnect, host-key change, stale mount, remote process survival,
   partial capability, cancellation, and app restart have typed recoverable states.
7. Credentials use Secret Service or existing platform agents and stay out of
   settings, logs, prompts, backups, screenshots, and repositories.
8. Port forwarding is explicit, listed, revocable, bounded, and never inferred from
   model text or enabled silently.
9. Dev-container or Compose compatibility requires a separate format/provenance and
   execution decision. No cloud agent hosting, automatic repository upload, or
   Harness repository metadata is added.
10. Deterministic adapter tests plus opt-in local integration cover paths, latency,
    disconnects, cancellation, trust, privacy, accessibility, and Linux publish.
