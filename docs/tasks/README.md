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
| 049 | NetPad-level .NET editing and inspection. | [Parity matrix](../netpad-omnisharp-parity.md), [metadata decompilation](../acceptance/editor-decompilation-2026-08-13.md), and the linked editor acceptance records below. |

## Detailed task records

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

1. An ADR defines inbound MCP ownership, stateless transport, loopback boundary,
   application-instance identity, normal and evaluation modes, tool policy, approval,
   privacy, persistence, audit, isolation, startup, and shutdown.
2. Data Access owns the official MCP SDK and Streamable HTTP transport. Business Logic
   owns tool eligibility, exact application/workspace/session/source identities,
   commands, authority, evidence, and result contracts. Presentation only reports
   status and adapts developer actions.
3. Settings ships in the first slice with enablement, mode, loopback endpoint,
   client/tool allowlists, per-tool approval,
   timeouts, limits, retention, health, active clients, disconnect, and reset.
4. Normal mode is disabled by default, binds only to loopback, records an optional
   client ID for attribution and requires it when an allowlist is configured, shows a persistent active-control indicator, and supports
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
    loopback confinement, client allowlists, approvals, stale identities, concurrency,
    cancellation, reconnect, malformed input, oversized output, reset, isolation,
    portal denial, accessibility, shutdown, restart, and Linux x64 publish.
13. A distinct loopback-only outbound `HarnessControl` connection can connect to
    a worker Harness instance and expose an exact allowlist of lifecycle tools to Lead.
    Ordinary MCP connections, Implementer, and Reviewer remain read-only. Settings
    owns the client ID, allowlist, validation, status, and persistence.

Delivered in the Task 047/059 integration slice: accepted ADR 019; official SDK 2.x
stateless Streamable HTTP; strict loopback confinement without inbound authentication;
closed client/tool/approval policy; bounded audit and immediate revocation; typed
workspace, project, Git, goal/plan/workflow/cost/evidence, Roslyn, document/UI,
Build/Test, full asynchronous goal lifecycle, accepted-change/commit decision,
capture, and evaluation operations; exact instance/source
identities; deterministic fixture snapshot/reset;
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

Status: `Completed`

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

Explicitly admitted cross-document providers now report affected-file count and
whether the active document changes during discovery. Add Parameter and Replace
Property/Method previews carry every physical source edit and persisted baseline in
one fingerprint. Human and model apply re-resolves the action, rejects generated,
external, structural, oversized, or inconsistent changes, enforces every delegated
path for models, writes one atomic batch, and validates the complete persisted set.

File and workspace-symbol search cover file, type, and symbol navigation. Regions are
first-class outline entries without polluting lexical breadcrumbs. Definitions,
usages, and implementations resolve source-generator output and metadata source
through opaque handles bound to the exact live buffer. The desktop opens these as
labeled read-only C# documents and excludes them from repository and layout
persistence. `ICSharpCode.Decompiler` reconstructs a selected metadata member from an
exact local implementation assembly; reference-only or unavailable bodies retain an
explicit signature fallback. Role and inbound MCP navigation results eagerly include
successful virtual documents before their short-lived Roslyn session closes. See
[editor-decompilation-2026-08-13.md](../acceptance/editor-decompilation-2026-08-13.md).

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

Project User Secrets now use a separate developer-only Business Logic service and the
standard per-user .NET store. The command palette and trusted-workspace overview open
a masked dialog with distinct list, reveal, copy, add, change, and delete actions.
Only projects with one literal unconditional `UserSecretsId` are accepted; Harness.NET
does not run MSBuild or mutate a project to initialize it. Values never enter shared
presentation state or model surfaces. A singleton disclosure guard makes portal
capture and reveal mutually exclusive. Evidence is recorded in
[project-user-secrets-2026-08-13.md](../acceptance/project-user-secrets-2026-08-13.md)
and [ADR 022](../decisions/022-project-user-secrets.md).

Roslyn now attaches a typed project, target framework, declaration, source path,
baseline, and buffer version only to the real project entry point. Run revalidates
that target in the trusted source context and starts `dotnet` directly with no shell,
implicit Restore, or launch profile. The Run Output tool supports lifecycle display
and cancellation. Lifecycle metadata survives restart; potentially sensitive stdout
and stderr do not. The same bounded actions are accessible in the editor toolbar.
Debug remains absent until a debugger adapter exists. See
[ADR 023](../decisions/023-typed-developer-dotnet-execution.md) and
[editor-run-codelens-2026-08-13.md](../acceptance/editor-run-codelens-2026-08-13.md).

The complete Linux editor gate now covers the current Harness.NET solution, measured
cold and warm latency, retained memory, in-flight cancellation, analyzer failure,
eight consecutive Roslyn context replacements, keyboard-only use, IME, AT-SPI, strict
Orca speech, 200% scaling, Dock restoration, and self-contained Linux x64 publish.
See [editor-resilience-2026-08-13.md](../acceptance/editor-resilience-2026-08-13.md).

Result: Harness.NET has the core interactive Roslyn operations, semantic presentation
and adornment slices, formatting, closed local and cross-document actions, bounded
generated and decompiled metadata navigation, configurable keybindings, and the full
Task 049 Linux resilience evidence. Debug CodeLens remains correctly hidden because
the debugger belongs to Task 052.
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

Status: `Completed`

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

Delivered index and file-cleanup slices: ADR 024 fixes ownership and authority before mutation work.
The active source context now exposes one staged/unstaged/conflict state with a
complete fingerprint and separate bounded diffs. Developers can stage or unstage an
exact file, hunk, or changed line from the Git tool. Hunk and line identities are
opaque and recomputed below Presentation; callers cannot provide patch text or Git
arguments. Harness rejects stale fingerprints, refreshes the view, and never includes
untracked file content in inspection output. Developers can preview and explicitly
confirm an exact tracked-file discard or untracked-file deletion. These actions are
limited to the original workspace, reject dirty editor buffers and stale Git state,
preserve the index when discarding, and never follow a selected symbolic link.

Developer commit and amend are now separate from goal approval. They target only the
original workspace, require an exact untruncated staged-diff preview and unchanged Git
fingerprint, show branch or detached state, HEAD, configured author identity, staged
paths, message, and hook policy, then require a second confirmation. Git hooks run by
default; bypass is an explicit compose-time choice. Unborn branches and detached HEAD
are supported. References are delivered; history inspection follows below. Merge
editing and remotes remain open.

Local branch management is also delivered in the Git tool. The shared fingerprint
now covers every Git reference as well as HEAD, index, operation state, and worktree.
Developers can list, create, switch, and rename local branches. Switching or renaming
the current branch resolves dirty documents first and refreshes the registered
workspace so all user and model source contexts use the new branch. Deletion previews
the exact name and tip SHA, distinguishes merged from forced unmerged deletion, states
reflog/object-retention limits, and requires explicit acknowledgement. Developers can
also list tags, create a lightweight or annotated tag at the exact displayed HEAD,
and delete a tag through an exact name and peeled-target preview. Annotated creation
requires a bounded message and configured Git identity. Tag mutation rejects stale
reference state and in-progress Git operations. Developer worktrees are now listed in
the tabbed Git workbench with their exact path, branch or detached state, HEAD, dirty,
conflict, lock, goal-management, and workspace-registration state. Developers can
create one from an existing branch or a new branch at HEAD, then open it through the
normal workspace inspection and trust flow. Removal revalidates both repository and
worktree-set fingerprints, blocks original, locked, registered, and Harness-managed
goal worktrees, and requires an exact destructive preview; dirty removal also requires
an explicit force choice. Local stashes are now listed with exact commit and base
identity. Developers can create a stash with an explicit untracked-file choice and
apply it while retaining the stash. Conflicts stay visible and the stash remains
available. Deletion is a separate exact commit-bound preview with an explicit recovery
warning and acknowledgement. A paged topological history graph now covers commits
reachable from repository refs, with an optional rename-following file timeline,
exact commit metadata, bounded diffs from each parent to the selected child, and paged
line blame. Heavy inspection runs away from the UI thread, observes cancellation, and
carries the active original or approved-goal source context. Three-way text conflict
editing is now delivered as a separate Conflicts tab: exact index stages supply
read-only base, ours, and theirs panes; only the result is editable; common unresolved
regions and bounded Roslyn diagnostics remain visible. Saving is bound to the complete
Git fingerprint and exact result hash, writes through the confined atomic editor, and
does not resolve the index. Staging the exact saved result is a separate action.
Unsaved result edits join the normal save/discard/cancel flow and automatic refresh
cannot replace them. Explicit remote synchronization is delivered in a separate
Remotes tab. Fetch names one remote and exact branch mapping and changes only the
remote-tracking reference. Pull remains deliberately split: after fetch, the developer
reviews exact local/tracking commits and ahead/behind state, then chooses fast-forward
merge or rebase integration. Push names its source and destination and defaults to a
non-forced update; force-with-lease is the only force policy and binds the displayed
remote-tracking commit. Every operation has an exact preview and second confirmation,
targets only the original workspace, supports cancellation and process-tree cleanup,
uses configured Git helpers or SSH agents, sanitizes displayed URLs, and discards
remote process output. See the
[remote synchronization acceptance record](../acceptance/git-workbench-remotes-2026-08-18.md).

### 051 — developer terminal and structured tasks

Status: `In progress — durable developer PTY lifecycle delivered; structured tasks open`

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

Progress: ADR 029 now separates the trusted developer-only PTY, structured task
contracts, typed agent operations, and durable Run evidence. It fixes layer ownership,
transient-content privacy, restart behavior, process-tree cleanup, and the prohibition
on exposing terminal authority to agents. The first terminal slice adds a dockable
multi-session Avalonia terminal over a real PTY with input/output, Unicode, resize,
bounded scrollback, selection, copy/paste, search, transient detected links, exact
source/trust/environment/persistence metadata, and complete owned process-tree Stop.
Real Linux adapter evidence verifies resize, Unicode, and background-child cleanup.
Terminal services remain absent from the agent catalog and factory. Schema 40 records
at most 20 safe lifecycle entries per workspace/goal context, never terminal content,
environment values, executable paths, or detected links. Startup cutoff reconciliation
marks unfinished records interrupted exactly once; the terminal pane restores those
records with an explicit expired-content notice and never resurrects a process. The
remaining work is accessibility/publish acceptance and the separate structured-task
slice.

### 052 — .NET project, Run, Test, and Debug experience

Status: `In progress — durable project and exact Linux Test Debug delivered`

Dependencies: 018, 042, 047, 049, 051.

Problem: Harness can Build and Test through goal tools but lacks the project system,
execution controls, Test Explorer, and debugger needed for ordinary .NET work.

Progress: the first developer execution slice provides typed project-entry-point Run,
process-tree cancellation, transient bounded output, and durable lifecycle metadata.
The second slice adds a source-context-aware static Solution tree for the entry point,
SDK policy, selected-SDK/workload-manifest health, projects, target frameworks,
declared/conventional configurations, typed project kind and startup candidates,
declared references/packages, language and nullable metadata, with typed project-file
navigation and no Restore, MSBuild evaluation, or repository execution.
Per-project loading failures remain visible without hiding healthy projects.
Bounded launch-profile discovery exposes profile kinds and safe metadata while
withholding argument text, environment values, and executable paths. The third slice
adds per-project Build/Rebuild, startup-project command-palette actions, inspected
configuration validation, confined no-Restore process execution, cancellation,
transient bounded streams, and durable/restart-safe operation metadata. The fourth
slice adds exact-session Roslyn discovery for xUnit, NUnit, and MSTest, including
derived attributes, traits, parameterization, bounded search and paging, a
project/type/test hierarchy, and exact source navigation. The fifth slice adds a
closed per-test Run action with an exact fully-qualified-name filter, no Restore or
shell, process-tree cancellation, transient bounded output, and durable test identity,
exit, duration, failure, and restart history. Multi-test selection, project/type
runs, per-case adapter results, test Debug, coverage, typed one-run overrides, Hot
Reload, and Debug remain open. The sixth slice projects the newest exact-test history
back into Test Explorer with state, duration, exit code, Rerun, and in-place Stop.
The seventh adds typed xUnit/NUnit/MSTest filtering before Roslyn paging and exact
not-run/running/succeeded/failed/cancelled/interrupted lifecycle filtering after the
history join. Name and trait search remains bounded and compiler-session scoped. The
eighth adds deterministic project and containing-type selections with the same
Run/Rerun/Stop/history lifecycle; a closed Exact/Type/Project selector starts one
confined no-Restore process per action and schema 34 persists its scope. Multi-test
selection is delivered by the ninth slice: accessible leaf checkboxes select 2–24
exact tests from one project, Business Logic derives a stable sorted identity, and
Data Access constructs one bounded VSTest OR filter for one process. Schema 35 stores
the exact members. The tenth slice collects standard TRX only in a private ephemeral
directory, parses at most 2,000 typed case outcomes, deletes raw files, keeps adapter
display text process-local, and persists safe case identity/outcome/duration in schema
36. The eleventh slice adds explicit, exact-context Cobertura import and navigation.
Reports and mapped source files are workspace-confined and bounded; schema 37 keeps
safe provenance and line hits, while the accessible Coverage tree opens exact
uncovered lines and clearly labels them as evidence rather than defects. The twelfth
slice adds an accessible one-run confirmation with an exact inspected profile, confined relative
working directory, and bounded argument/environment lists. Values flow directly to
`dotnet` without a shell and remain process-local; safe names and counts are visible,
but no override is persisted.
The thirteenth slice adds a distinct cancellable Hot Reload lifecycle through confined,
non-interactive `dotnet watch`; schema 38 preserves its identity and restart
reconciliation. The fourteenth slice accepts and implements the debugger supply-chain
boundary: Settings explicitly installs, verifies, repairs, or removes pinned
NetCoreDbg 3.2.0-1092 in application-private storage. The release archive, retained MIT
license, exact payload names and sizes, and every SHA-256 digest are fixed in code;
Harness never searches PATH, accepts a custom executable, or starts a TCP adapter.
The fifteenth slice completes exact project-entry Debug. It reuses the Roslyn-proven
target and Run revalidation lifecycle, then starts the verified adapter over bounded
private stdio. The Debug workspace exposes verified source breakpoints, threads,
stacks, scopes, expandable variables, bounded output, source navigation, pause,
continue, stepping, stop, and deterministic adapter/debuggee cleanup. Test Debug
remains open; arbitrary attach, expression evaluation, and value mutation remain
excluded by ADR 028.
The sixteenth slice adds exact Linux Test Debug. A short-lived Roslyn session
revalidates the selected test identity and source line, then Harness starts one exact
no-Restore test operation with host debugging enabled. It discovers only that owned
operation's waiting managed testhost descendant, rechecks ancestry and command
identity immediately before attach, and never accepts a PID from presentation,
configuration, or a model. Test Debug reuses the debugger workspace and deterministic
process-tree cleanup.
The seventeenth slice makes both Debug forms durable without persisting inspection
content. Schema 39 records the exact project-entry or test identity, source context,
state, timestamps, exit, duration, and bounded safe failure. A one-shot application
lifecycle reconciliation marks only pre-start running rows interrupted; it cannot
reclassify a live operation created later by another service. Debug output,
breakpoints, threads, stacks, scopes, variables, and one-run override values remain
process-local. See the
[durable Debug acceptance record](../acceptance/developer-debug-durability-2026-08-29.md).

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
6. The .NET debugger supports typed launch and owned-test attach, breakpoints, threads,
   stacks, scopes, variables, stepping, stop, and source mapping behind an adapter.
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

### 060 — workbench composition refactor

Status: `Complete` — PR #2 merged slice 060.0 and the shrink-only budget
infrastructure. Slices 060.1–060.3 extracted the Files/Search, Git-changes,
branches/tags, and worktrees/stashes units with focused tests. Slice 060.4 extracted
remotes, history/blame, and the conflict editor with its Roslyn lifecycle. Slice 060.5
extracted run output, Problems, document sessions, Roslyn lifecycle/interactions,
rename, transformations, navigation, inspection, CodeLens, layout lifecycle,
cross-tool focus/accessibility, and overview composition. The 795-line host has left
the burn-down allowlist. Slice 060.6 has extracted bounded feature command and test
partials; both the production and test stores have left the allowlist. Slice 060.7
has extracted bounded settings sections and goal workflow dialogs with matching
focused tests; both production windows have left the allowlist. Slice 060.8 has
completed the Avalonia UX audit, with bounded shell/test units, a complete typed
palette for 46 core tool actions, and shared live tool status. The remaining
production services and test fixtures are split into bounded partials, leaving the
shared global size-budget allowlist empty and completing the task.

Dependencies: 049, 050. Coordinates with in-progress 052.

Problem: The runtime layers are healthy, but the Avalonia Presentation layer has
accreted: `WorkbenchDockHost` (6,389 lines, 85 fields) owns every dock tool and
appears in half of recent commits, with `AvaloniaPresentationStore`, `SettingsWindow`,
and `GoalDialog` close behind. Every Stage 3 task grows these same files. Developer
experience has verified gaps: a stale SDK pin that fails clean machines, no
continuous verification, and unignored local agent credentials.

Groundwork: [ADR 025](../decisions/025-workbench-composition-and-refactor-guardrails.md)
fixes the guardrails; [the refactor baseline](../refactor-baseline.md) records the
2026-08-24 measurements, target structure, ordered slices 060.0–060.8, delegation
protocol, and risks.

Acceptance criteria:

1. ADR 025 is accepted before any slice merges, and every slice observes its
   scope limits: structure changes only, no behavior, layer, contract, or
   toolkit changes.
2. `global.json` builds on any supported major-version SDK; the 52
   environment-dependent Roslyn test failures measured in the baseline are gone.
3. Every pull request runs restore, build with warnings as errors, and the full
   deterministic test suite on hosted Linux x64; live and paid tests stay excluded.
4. Local agent working directories (`.codex/` and equivalents) are Git-ignored;
   no credential-bearing file in the working tree is one `git add -A` from history.
5. Each workbench tool, settings section, and goal-dialog aspect is one
   composition unit in one file at or under 800 lines, receiving shared services
   through a typed context rather than constructor spread.
6. `WorkbenchDockHost` retains only dock arrangement, layout persistence, and
   cross-tool navigation; the size-budget architecture test passes with an empty
   burn-down allowlist before the task closes.
7. A layout saved before each slice restores identically after it; automation
   identifiers, AT-SPI structure, keybindings, command palette, and Vim behavior
   are preserved and re-verified with the existing `eng/` scripts.
8. Test classes map one-to-one to composition units; `PresentationControlTests`
   and `AvaloniaPresentationStoreTests` are decomposed alongside their sources
   with no reduction in executed test count.
9. Every tool action is reachable through the command palette and appears in the
   keybindings catalog; tool panels share uniform chrome and empty, busy, and
   error presentation.
10. Each slice lands as its own reviewed pull request with full-suite results,
    the acceptance evidence named in its exit criteria, and a before/after
    line-count table; slices touching Task 052 surfaces are sequenced behind it.

### 061 — architecture enforcement and composition seams

Status: `Complete`

Dependencies: none (independent of 060; shares only the ADR 025 size-budget test
infrastructure). May run in parallel with 060 — it touches Analyzers, Host, and
architecture tests, none of 060's presentation surface.

Problem: The 2026-08-24 architecture review found the layer rules holding with zero
exceptions, but three load-bearing conventions are unenforced: the Business Logic
translation boundary (27 deliberate mirror-record families; nothing prevents a Data
Access type leaking into a Business Logic public signature), the value-contracts-only
cross-feature coupling shape, and the 537-line linear composition root that every
feature slice edits.

Groundwork: [ADR 026](../decisions/026-translation-boundary-and-architecture-enforcement.md)
fixes the rules; [architecture.md](../architecture.md) records the measurements and
the enforcement matrix this task turns green.

Implementation evidence (2026-08-24): `HARNESS003` is enforced at error severity with
positive and negative analyzer coverage; the architecture suite pins 36 existing
cross-feature service edges; Host registrations are split across five internal
modules, `Program.cs` is 179 lines, and the Host parity test compares all 138 service
descriptors by module count and normalized service-type/key/lifetime fingerprint to
the reviewed pre-split baseline at commit `16f3085`.

Acceptance criteria:

1. ADR 026 is accepted before implementation; no runtime behavior changes in any
   slice.
2. `HARNESS003` reports a public Business Logic symbol whose signature reachably
   mentions a `Harness.DataAccess` type as a compile error, covering methods,
   properties, constructor parameters, generic arguments, tuple elements, and
   base interfaces.
3. `Harness.Analyzers.Tests` proves `HARNESS003` on positive and negative cases,
   including generic and nested-generic signatures and allowed BCL and
   Microsoft.Extensions types.
4. The six Business Logic contract files importing Data Access namespaces are
   audited under the rule; any violation is fixed by introducing the missing
   mirror type, never by weakening the rule.
5. An architecture test asserts the cross-feature service-reference inventory
   inside Business Logic, seeded from the measured current state; extending the
   inventory is a one-line reviewed change with a stated reason.
6. Host gains internal per-feature registration modules; `Program.cs` keeps
   ordering, configuration, run mode, and shutdown, at or under 200 lines,
   enforced by the ADR 025 size-budget test.
7. Registration parity is proven against the reviewed pre-split baseline: the
   modular service collection preserves all 138 registrations by module count and
   normalized service-type/key/lifetime fingerprint captured at commit `16f3085`.
8. `architecture.md`'s enforcement matrix is updated in the same slice as each
   mechanism lands; no rule flips to Enforced before its mechanism merges.
9. The full deterministic suite passes; analyzer changes introduce no new
   warnings anywhere in the solution.
10. Each slice lands as its own reviewed draft pull request per AGENTS.md, with
    before/after evidence for the composition-root split (line counts, module
    list, parity test result).

### 062 — contributor verification and repository governance

Status: `Complete`

Dependencies: 060.0, 061.

Problem: Contributor entry points, acceptance-evidence ownership, test tiers,
verification scripts, documentation navigation, and dependency review were implicit.
Ignored run output was described like durable evidence, while the sole production
preview dependency had no recorded exit condition.

Decision: [ADR 027](../decisions/027-contributor-verification-and-dependency-governance.md)
fixes the machine-local evidence boundary, test taxonomy, dependency cadence, notices
consistency, and SqliteVec exit condition.

Completion evidence (2026-08-25): contributor, documentation, acceptance, dependency,
and verification maps are versioned; CI checks local links, ADR statuses, acceptance
labels, notice versions, and preview exit records; every xUnit assembly declares a
Fast or Adapter tier and live tests declare Live; CI runs 319 Fast tests first and
then all 823 non-live deterministic tests with serialized Avalonia ownership.
The first read-only NuGet.org review reported no known vulnerable packages; available
updates were deferred to dedicated compatibility PRs, with the next review due by
2026-09-25.

Acceptance criteria:

1. `CONTRIBUTING.md` points to, but does not duplicate, the working agreement and
   branch → verification → commit → push → draft-PR flow.
2. `docs/README.md`, `docs/acceptance/README.md`, and `eng/README.md` provide cold-start
   maps with evidence ownership and prerequisites.
3. Ignored `artifacts/` output is labeled machine-local; only deliberately redacted,
   bounded files below `docs/acceptance/` count as durable repository evidence.
4. CI rejects missing local Markdown targets, ADR status drift, ambiguous acceptance
   artifact labels, notice/version drift, and unrecorded preview dependencies.
5. Tests expose `Tier=Fast`, `Tier=Adapter`, and explicit `Tier=Live` selection; hosted
   verification fails fast on Fast and excludes Live from the complete deterministic
   gate.
6. Avalonia.Headless tests cannot race through solution-level or test-collection
   parallel teardown.
7. A documented monthly dependency review is human-controlled and records evidence;
   no dependency-update bot is introduced.
8. The exact `Microsoft.SemanticKernel.Connectors.SqliteVec` preview pin has a stable
   release or reviewed replacement exit condition and dedicated-update evidence.

### 063 — transient workbench event surface

Status: `Complete`

Dependencies: Task 060 slice 060.1. Deliver before 053 attention states.

Problem: Long operations complete or fail only inside their originating panel, so a
developer focused in the editor can miss goal, Git, indexing, execution, or backup
state changes.

Acceptance criteria:

1. Business Logic exposes a bounded immutable `WorkbenchEvent` contract with typed
   severity, source, message, timestamp, and optional closed navigation target.
2. Presentation owns a session-only bounded queue; events never enter user
   repositories, prompts, logs, backups, or telemetry by implication.
3. Avalonia renders non-modal, keyboard-dismissible notifications that never steal
   focus and announce through AT-SPI exactly once.
4. Repeated events coalesce deterministically; overflow, expiry, navigation, and
   dismissal are tested without timers that make the suite flaky.
5. Goal and later Task 053 attention states consume the same surface rather than
   introducing a second notification channel.

### 064 — running remote-spend visibility

Status: `Planned`

Dependencies: 014, 040.

Problem: Remote reservations, reconciliation, and caps are enforced but are not
glanceable while a goal is running.

Acceptance criteria:

1. Existing cost reports feed immutable per-goal display contracts; no accounting is
   duplicated in Presentation.
2. Active workflow and running-goal surfaces show reconciled and reserved spend plus
   remaining cap for Capped goals, with provider/model detail on explicit inspection.
3. Micro-USD stays below the UI boundary and rendering uses USD consistently.
4. Unlimited, Capped, LocalOnly, unknown pricing, released reservation, overage, and
   completed-run states have deterministic tests and accessible labels.
5. Visibility introduces no new spend authority, provider call, telemetry, or
   persistence.

### 065 — command-palette fuzzy ranking and recency

Status: `Planned`

Dependencies: Task 060 slice 060.1; coordinate final coverage with 060.8.

Problem: Substring and word-prefix scoring misses common abbreviated/subsequence
queries, and frequently used commands have no session-aware ranking advantage.

Acceptance criteria:

1. Deterministic subsequence scoring includes word-boundary, adjacency, prefix, and
   exact-match bonuses with stable tie-breaking.
2. Queries such as `git wt` and `gwt` find and rank `Git: Worktrees` predictably.
3. A bounded private recency model affects ranking only after textual relevance and
   never stores repository content or telemetry.
4. Corrupt recency state fails closed, and Settings/documentation disclose ownership
   if recency persists beyond the session.
5. Unit tests cover ranking, normalization, ties, recency bounds, reset, and the full
   command catalog without weakening Task 060.8 coverage.

### 066 — live keyboard reference overlay

Status: `Planned`

Dependencies: 049 and Task 060 slice 060.1.

Problem: Typed keybindings are discoverable in Settings but not available as a quick,
searchable workbench reference.

Acceptance criteria:

1. A read-only searchable overlay renders the live validated binding snapshot grouped
   by category, including intentionally unbound commands.
2. Its default invocation is added through ADR 021's closed catalog and complete
   conflict validation, not hard-coded control handling.
3. Vim mode notes reflect current state without replacing the standard catalog.
4. Opening, searching, navigation, dismissal, focus return, screen sizing, and AT-SPI
   structure have deterministic and graphical acceptance coverage.
5. The overlay introduces no new persisted state or executable command strings.

### 067 — Vim search and named registers

Status: `Planned`

Dependencies: 049.

Problem: Vim mode lacks its primary search motions and named registers; macros would
multiply the binding/state matrix before those foundations are proven.

Acceptance criteria:

1. Deliver `/`, `?`, `n`, and `N` incremental search first, including counts,
   wrap/no-match state, cancellation, visual selection, and operator composition.
2. Named registers `a`–`z` follow in a separate slice; the `+` register maps only to
   the existing clipboard boundary.
3. Search and registers preserve IME composition, undo, dirty state, Roslyn document
   identity, configured command precedence, and accessible mode/status text.
4. Controller tests cover motion/operator matrices and Unicode without coupling to
   Avalonia framework types.
5. Macros (`q`, `@`) remain deferred until a separate accepted decision defines
   recording scope, recursion/bounds, authority, and accessibility.

### 068 — explicit split editor groups

Status: `Planned`

Dependencies: Task 060 slice 060.5.

Problem: Dock dragging can place documents side by side, but there are no discoverable
split/move commands or explicit persisted editor-group semantics.

Acceptance criteria:

1. Typed commands split right/down and move the current document between named editor
   groups over the existing Dock model.
2. ADR 011 layout persistence round-trips groups without changing existing dock or
   document identifiers and migrates any necessary codec state explicitly.
3. Dirty buffers, diagnostics, active-document state, navigation, close prompts, and
   document switcher ownership remain singular across moves.
4. Commands appear in the palette and keybinding catalog with conflict validation.
5. Headless layout, keyboard/focus, compact-screen, AT-SPI, and save-before/
   restore-after acceptance evidence cover both split directions.

### 069 — bounded per-role reasoning policy

Status: `Complete`

Dependencies: 003, 038, 040.

Problem: Thinking-capable local models inherit provider-default reasoning on every
ordinary role call. On modest hardware, a single typed inspection step can therefore
spend minutes generating hidden reasoning before the next tool call, with no
developer-visible way to choose responsiveness over depth.

Decision: [ADR 003](../decisions/003-agent-and-provider-architecture.md) owns a
portable two-state role policy. Fresh routes retain provider behavior because models
can bind planning and native tool protocols to reasoning mode. Settings persists an
explicit opt-out per Lead, Implementer, and Reviewer when measured latency warrants
the quality tradeoff. Full role profiles remain in Task 056.

Acceptance criteria:

1. Business Logic owns immutable role-default and route contracts carrying either
   `Disabled` or `ProviderDefault`; Data Access maps that policy to provider requests.
2. Fresh local and remote routes keep provider behavior, existing saved routes migrate
   without a silent behavior change, and an explicit per-role opt-out requests no
   optional reasoning.
3. Settings → Models & roles exposes, validates, persists, and reports the effective
   policy beside each role model.
4. The deterministic structured local-file proposal continues to force reasoning off
   independently of the ordinary role policy.
5. Migration, routing, provider-request mapping, Settings state, build, deterministic
   tests, and a bounded live Ollama comparison are recorded in the
   [acceptance record](../acceptance/local-role-reasoning-policy-2026-08-28.md).

### 070 — local typed-tool workflow liveness

Status: `Complete`

Dependencies: 003, 018, 023, 038, 069.

Problem: A live local-model workflow can mutate successfully yet remain running after
an empty handoff, skip final Build/Test, accept a text-only review, mistake a dotted
directory for an exact file, or keep the regression driver polling an already failed
inbound operation.

Acceptance criteria:

1. A successful Implementer tool sequence remains actionable when the model emits no
   final text; empty role output without durable tool work is an explicit failure.
2. Exact-file bootstrap falls back to bounded typed tools when the candidate path is
   a missing file, including dotted directory names.
3. A text-only Reviewer response receives one in-session correction that requires
   typed diff and evidence inspection before deciding.
4. After all delegated tasks and after each review correction, the workflow runs typed
   Build then Test exactly once before review; one concrete failure receives one
   bounded Implementer repair and revalidation.
5. Correction completion requires new mutation or verification evidence, persistent
   validation failure is bounded and retryable, and long diagnostics never invalidate
   a workflow checkpoint.
6. The live regression driver stops immediately on failed or cancelled inbound
   operations and reports the durable error.
7. Deterministic tests, a warning-free build, and bounded Ollama dogfood evidence are
   recorded in the [acceptance record](../acceptance/local-tool-loop-liveness-2026-08-28.md).

### 071 — live agent activity status

Status: `Complete`

Dependencies: 040, 063, and Task 060's relevant Conversation/header composition
slice.

Problem: Active model calls are intentionally quiet, but a long local inference can
look indistinguishable from a stalled workflow. Users and dogfood testers need calm,
truthful reassurance that work is still active and an on-demand way to inspect its
latest observable progress.

Acceptance criteria:

1. A compact, persistent status affordance appears while a goal operation is active
   and shows the current typed phase or role, elapsed time, and age of the latest
   observable update without stealing focus.
2. Expanding it shows a bounded activity timeline sourced only from real workflow
   checkpoints, provider stream state, and typed tool calls/results, with navigation
   to the originating goal and durable evidence where available.
3. The quiet default does not stream hidden reasoning, prompts, credentials, raw
   provider payloads, or token-by-token noise. It distinguishes waiting for inference,
   executing a typed tool, validating, retrying, and requiring direction.
4. Progress indicators never fabricate percentage completion. A lack of observable
   updates is reported as an increasing age rather than an animated claim that work
   is advancing, and existing cancellation/recovery controls remain reachable.
5. Multiple operations coalesce into a truthful summary with deterministic selection
   of details; completed and failed activity hands off to Task 063 notifications
   instead of creating a second event system.
6. Reduced-motion, keyboard, compact-layout, screen-reader, fake-clock, stalled-call,
   recovery, and bounded live-local-model acceptance coverage prove the widget remains
   informative without becoming distracting.

Implementation evidence (2026-08-28): the header status pill reports the active
workflow phase/role, elapsed time, and age of the latest observable update. Its flyout
shows bounded durable checkpoints, typed evidence, and sanitized session-only provider
and tool lifecycles; it retains no prompt, response, reasoning, argument, or result
payload. Concurrent operations coalesce deterministically, review corrections are
identified as retries, and the existing cancellation plus goal/evidence navigation
remain reachable. Completed and failed workflows hand off to Task 063 events. Fixed-
clock, stalled-call, recovery, compact rendered-frame, focus, accessibility, lifecycle,
and bounded live Ollama coverage are recorded in the acceptance record.
See the [slice acceptance record](../acceptance/live-agent-activity-status-2026-08-28.md).

### 072 — deterministic compiler repair before model retry

Status: `Complete`

Dependencies: 012, 038, 042, 047, and 070.

Problem: On modest local hardware, asking an Implementer model to repeat an otherwise
valid C# edit merely to add one unambiguous namespace costs tens of seconds or minutes.
Harness already owns a compiler-proven missing-import transformation, but model-authored
file edits currently reject the candidate before that deterministic capability can help.

Acceptance criteria:

1. A model-authored C# candidate rejected solely by at most four introduced `CS0246`
   or `CS0103` diagnostics receives a bounded in-memory Roslyn repair attempt before
   any model retry.
2. Each repair requires exactly one compiler-proven namespace and the existing closed
   `AddMissingImport` preview; ambiguity, unsupported files, multi-file output, or
   additional diagnostics fail closed without changing the candidate on disk.
3. The repaired candidate passes the existing warning-free candidate validation,
   exact-baseline atomic write, and persisted-result validation boundaries.
4. Typed evidence names applied deterministic repairs without storing hidden reasoning
   or creating a generic AST/code-action executor.
5. Deterministic tests prove unique, ambiguous, still-invalid, bounded, stale, and
   ordinary valid/rejected candidate behavior, plus a warning-free build and a
   measured comparison against local inference latency.

Implementation evidence (2026-08-28): model-authored C# file edits now run the closed
repair stage inside their existing Roslyn session. A unique missing import updates only
the in-memory candidate, is fully revalidated, crosses the same exact-baseline atomic
write and persisted validation boundaries, and appears in typed tool evidence. Every
ambiguous, stale, over-bound, or still-invalid case retains the original rejection.
See the [acceptance record](../acceptance/deterministic-compiler-repair-2026-08-28.md).

### 073 — bounded Ollama context and GPU recovery

Status: `Complete`

Dependencies: 003, 038, 069, 070.

Problem: Every Ollama agent request imposed a hidden 32,768-token context. Alternating
two near-VRAM-capacity models under that floor repeatedly evicted and reloaded the
GPU, exhausted host headroom, and triggered an AMD PSP runtime-resume failure that
left Ollama on CPU fallback.

Acceptance criteria:

1. Each Ollama provider owns a typed maximum-agent-context setting with shipped and
   test-safe defaults, range validation, XDG persistence, restart status, Settings UI,
   and configuration documentation.
2. The adapter sizes short requests down, caps larger requests by the configured
   maximum and the model-advertised context, and has deterministic boundary coverage.
3. Host recovery evidence distinguishes the unchanged LXC passthrough configuration
   from the runtime-power failure and records the bounded server policy and a real
   full-VRAM-offload smoke.
4. The repository stores no machine path, credential, model blob, or conversation
   content. See the updated
   [local workflow acceptance record](../acceptance/local-tool-loop-liveness-2026-08-28.md).
