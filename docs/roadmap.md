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
- Task 050: complete local and remote developer Git workbench.

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

### Delivered: Task 047 — model-accessible semantic IDE foundation

Use the maintained [capability map](agent-ide-capabilities.md). Rider is a breadth
reference only. Unreal-specific tools are excluded.

Delivered scope:

1. Finish on-demand toolset activation and persist optional exposure settings.
2. Complete workspace tree, file/regex search, open-document context, project graph,
   dependency, diagnostics, and Git scopes.
3. Add symbol search, call and type hierarchy, associated-test discovery, paging,
   and a deterministic changed-set quality result.
4. Make every result identify its workspace, source context, project, target,
   configuration, document version, truncation, and freshness.
5. Keep the catalog, role policy, evidence, and adapter boundaries usable by later
   Git, execution, debugger, database, profiler, and notebook slices without
   pretending those capabilities are delivered here.

Do not add an unrestricted shell, a generic execute-by-name tool, or an unbounded
tool catalog in every prompt.

### Delivered: Task 059 — inbound MCP control and evaluation

Expose Harness.NET itself as a local Model Context Protocol server over stateless
Streamable HTTP. The server adapts existing Business Logic commands and evidence; it
does not reimplement application behavior or make MCP an authority boundary. Codex
and other configured clients can inspect and exercise Harness directly while every
operation retains its source context, trust, approval, baseline, privacy, and audit
rules.

Provide two explicit modes. Normal dogfooding mode exposes bounded application,
workspace, document, Roslyn, Git, goal, session, evidence, Build/Test, accessibility,
and consented visual-verification operations. Isolated evaluation mode starts with a
temporary database, disposable fixture repository, fake or explicitly selected local
providers, no stored credentials, and resettable state; it may add Harness-owned UI
snapshots and accessibility-ID actions that cannot address other applications or the
normal developer environment.

The server is disabled by default, strictly loopback-only, intentionally unauthenticated,
visibly active, revocable, and fully audited. Client IDs provide attribution and
allowlisting, not identity proof. Tools declare read, mutation, execution, sensitive, and
destructive behavior and remain individually allowlisted and approval-controlled.
Do not expose generic click/type, shell, SQL, arbitrary command dispatch, desktop
control, silent screenshot capture, or a route around typed Harness authority. Record
an ADR for inbound MCP ownership, loopback boundary, modes, tool policy, application
instance identity, test isolation, privacy, and shutdown before implementation.

### Delivered quality gate: Task 038 — local-model workflow regression

The opt-in Tic-Tac-Toe exercise is now part of a versioned local-model regression
suite. It measures planning, tool selection,
semantic-operation use, edit size, validation, retry behavior, review findings,
completion, latency, and resource use. Keep live inference opt-in and local-only by
default; deterministic fakes continue to cover ordinary test runs.

Drive the application through Task 059 where a scenario requires real Harness
interaction. Store prompts, scenario versions, model and server identity, capability
discovery, timings, tool traces, diffs, Build/Test evidence, failures, and partial
results under ignored artifacts. Compare runs without declaring model prose to be
ground truth.
Use deterministic validators for repository state, compilation, tests, policy, and
expected semantic operations. The gate must catch the large-rewrite and invalid-plan
regressions already observed in hands-on use. See the
[acceptance record](acceptance/local-model-regression-2026-08-12.md).

### Evaluated: Task 048 — Morgania editor evaluation and conditional migration

Evaluate Morgania as the Avalonia editor platform before adding more
Presentation-specific behavior to the current AvaloniaEdit integration. Morgania is
the editor used by current RoslynPad and combines an Avalonia rendering and input
layer with Visual Studio editor APIs and Roslyn editor services. This is a gated
evaluation, not approval to replace AvaloniaEdit.

The evaluation must:

1. Pin the inspected Morgania and RoslynPad revisions. Verify license, public package
   availability, release and support policy, transitive dependencies, integrity,
   provenance, and SBOM changes. Do not depend on an unversioned branch.
2. Document Morgania's vendored Visual Studio editor code, recompiled or internal
   Roslyn dependencies, MEF composition, nullable settings, warning suppressions,
   and any private-access mechanism. Measure the work required for a Roslyn or
   Avalonia upgrade instead of treating upstream compatibility as stable.
3. Keep Morgania, Avalonia, Visual Studio editor, MEF, and Roslyn implementation
   types inside their existing adapter boundaries. Preserve the Business Logic code
   intelligence contracts, typed model tools, validation policy, and shared semantic
   source state. Record an ADR amendment before adopting the new editor platform.
4. Introduce an editor adapter seam and a representative vertical slice without
   deleting the AvaloniaEdit path. The slice must cover open, edit, dirty state,
   save, exact-baseline conflict handling, diagnostics, completion, signature help,
   quick info, definition, usages, implementations, rename, code actions, and
   restoration inside Dock.
5. Prove that the user editor and model tools observe the same current buffer,
   source-context identity, baseline, and version. Stale semantic results must still
   be rejected, manual edits must remain permissive, and model writes must retain
   their existing validation and authority checks.
6. Run Linux acceptance on Wayland and X11 for keyboard commands, IME and dead keys,
   clipboard, pointer selection, multi-caret behavior, focus, popups, AT-SPI, Orca,
   100% and 200% scaling, multiple displays, Dock movement, layout restoration, and
   Linux x64 self-contained publish. Upstream cross-platform builds do not satisfy
   this gate.
7. Measure cold startup, first and warm completion, typing and diagnostic latency,
   large-solution load, cancellation, memory, disposal, and repeated workspace or
   goal switches. Compare the results with the current editor and the targets in
   ADR 012.
8. Compare total maintenance cost. Adoption should remove or substantially simplify
   the custom completion, signature, popup, diagnostic-rendering, navigation, and
   editor-session code rather than leave two permanent editor stacks.

Adopt Morgania only if the slice passes the Linux, accessibility, performance,
dependency, lifecycle, and architecture gates. Keep AvaloniaEdit if Morgania leaks
implementation types across layers, cannot meet accessibility or input requirements,
adds unacceptable startup or memory cost, or requires routine large private Roslyn
patches. Retain a working rollback path until the migrated editor passes the complete
desktop release gate; remove the old stack only in a separate, reviewed cutover.

The 2026-08-12 evaluation rejected the inspected RoslynPad 22.1 implementation at
the package, provenance, version-coupling, maintenance, and upstream-smoke gates.
Harness.NET retained AvaloniaEdit behind a new Presentation-owned adapter and kept
the existing Roslyn contracts and shared live buffer. See
[ADR 020](decisions/020-editor-platform-boundary-and-morgania-evaluation.md) and the
[evaluation record](acceptance/morgania-editor-evaluation-2026-08-12.md). Reconsider
the migration only when a pinned, supported, publicly verifiable Morgania release is
available; that is a new evaluation, not unfinished work on the rejected revision.

### Completed: Task 049 — NetPad-level .NET editing and inspection

Close the remaining user-facing code-intelligence gaps identified against
[NetPad at `0c74746`](https://github.com/tareqimbasher/NetPad/commit/0c74746daf6f5402ad4d9a2cf3958131bdfc8011)
and use
[OmniSharp Roslyn at `83fd615`](https://github.com/OmniSharp/omnisharp-roslyn/commit/83fd615eafff33e297a9f59280d929cf09ec0d3c)
as a service and test reference. Harness.NET already provides completion, quick info,
signature help, diagnostics, definition, usages, implementations, semantic rename,
full repository context, Git workflows, deterministic model-write validation, and
typed agent access. Do not rebuild those features around OmniSharp endpoints.

Add the missing capabilities in this order:

1. Roslyn semantic classification with incremental visible-range updates and theme
   tokens; document occurrence highlighting; contextual folding; document outline,
   breadcrumbs, and workspace symbol search.
2. Configurable parameter and type inlay hints, plus bounded CodeLens for references,
   implementations, tests, and available run/debug actions. Resolve expensive detail
   lazily and cancel work for stale or invisible documents.
3. Format document, selection, changed spans, paste, and supported on-type triggers;
   organize/fix usings; code actions, quick fixes, refactorings, and fix-all. Every
   multi-file or model-requested mutation uses preview/fingerprint/apply, exact
   baselines, affected-path authority, and post-apply diagnostics. Do not expose a
   raw or arbitrary code-action executor.
4. Navigate to type, file, region, symbol, metadata/decompiled source, and generated
   source. Generated and metadata documents are clearly labeled, bounded, read-only,
   and excluded from ordinary persistence and repository mutation.
5. Add developer and agent views for syntax trees, symbol details, generated source,
   and IL. Tie every result to the exact project, target framework, configuration,
   document version, and build or compilation identity; never present stale output as
   current.
6. Add a configurable editor command and keybinding layer with conflict detection,
   discoverability, reset, import/export of safe declarative bindings, and an
   optional Vim mode. Keep text input, IME, accessibility, and platform shortcuts
   correct when a mode is active.
7. Add a separate project User Secrets slice using the standard .NET store. Listing,
   revealing, copying, adding, changing, and deleting secrets are distinct developer
   actions. Secret values stay out of logs, evidence, backups, model context, search,
   and indexes. Values are masked by default, and portal capture is unavailable while
   a value is revealed. Agents receive no generic secret-read tool.
8. Expose deterministic read results and closed transformation previews through the
   same Business Logic contracts used by the developer UI. This is where Harness.NET
   must exceed NetPad: the user and models share one live semantic state, while model
   authority remains narrower than user editing authority.

Progress through 2026-08-13: items 1 and 2 are delivered through the shared exact-buffer
Roslyn session. Viewport-only refresh no longer rebuilds document structure, and
occurrence lookup is confined to the active document. Typed SQLite-backed Editor
settings control visible-buffer parameter and inferred-type hints plus bounded lazy
reference, implementation, associated-test, and project-entry-point Run CodeLens
actions. Run uses a typed project/framework/declaration/source target and direct
no-shell `dotnet` execution. Debug remains hidden until Task 052 provides a debugger.
See the
maintained [NetPad and OmniSharp parity matrix](netpad-omnisharp-parity.md). Item 3 now
also has closed document/selection/changed-span formatting, guarded paste and on-type
formatting, import organization, compiler-proven unused-import cleanup, and missing-type
import fixes. Changed spans come from Roslyn syntax-tree differences against the exact
persisted solution. Paste and supported `;`, `}`, and new-line triggers carry exact
ranges and typed triggers; settings control both automatic paths. The editor uses one
guarded undoable buffer replacement and never saves automatically. Missing-import
choices are exact namespaces that Roslyn proves bind the unresolved type at the caret.
The Quick fix path now also composes an explicit allowlist from the pinned Roslyn
feature assemblies: 20 compiler/style fix providers and 25 local refactoring
providers, including exact-selection extract-method and introduce-variable
operations. Discovery preflights every choice and omits added or removed documents,
project/reference changes, and custom host operations. Add Parameter and Replace
Property/Method are explicitly admitted cross-document providers. Discovery labels
their physical affected-file count; preview includes every baseline and edit; apply
enforces every model path grant, writes one atomic batch, and validates the complete
persisted set. Other providers remain document-confined. Occurrence and safe
document-wide scopes use opaque action IDs through the same preview/fingerprint/apply
path. The editor, role tools, and opt-in
`harness_code_actions` MCP tool share the typed read result; no arbitrary Roslyn
action is callable.
Models use the same closed preview/fingerprint/apply path with delegated paths, atomic
persistence, durable evidence, and post-apply diagnostics. Item 4 now includes file
and workspace-symbol search, region outline navigation, and exact-buffer generated-
source and metadata-signature virtual documents. Opaque handles are session-local and stale-safe; the
desktop labels them read-only and layout capture drops them. Role and inbound MCP
navigation results eagerly include resolved virtual text before closing their Roslyn session.
Metadata navigation now uses the pinned MIT-licensed `ICSharpCode.Decompiler`
`10.1.1.8388` package to reconstruct a selected member from an exact local
implementation assembly. Reference-only or unavailable bodies use an explicit
signature fallback. ADR 012 and the decompilation acceptance record contain the
license, provenance, integrity, SBOM, authority, test, and rollback review.
Item 5 is delivered through one closed exact-buffer inspection contract covering
syntax tree, semantic symbol details, generated-source inventory, and locally emitted
IL. The developer menu, role tool, and opt-in inbound MCP use the same bounded read-
only result and exact project/version/TFM/configuration/assembly/compilation identity.
IL emission is in memory and never executes project code. Item 6 now has one typed,
SQLite-backed workbench/editor command layer. Settings validates the complete binding
set, shows conflicts, resets defaults, and imports or exports only bounded
`harness-keybindings-v1` JSON. The active snapshot also drives shell/editor dispatch,
header hints, and command-palette labels. Item 6 is now complete: the same persistent
settings select optional Vim input over the live buffer, with explicit modal state,
counted core motions/operators, clipboard synchronization, IME-preedit suspension,
and platform-shortcut pass-through. Item 7 is delivered through a developer-only
Project User Secrets service and masked dialog. The Data Access adapter accepts the
standard nested or flattened string JSON shape and writes the standard flattened
shape atomically. It requires a literal, unconditional project `UserSecretsId`; it
never evaluates MSBuild or initializes the project. List results contain keys only.
Reveal and portal capture hold mutually exclusive leases, values never enter shared
presentation state, and no role or MCP secret-read tool exists.
The complete Linux resilience matrix now covers measured large-solution latency and
memory, in-flight cancellation, analyzer failure, repeated foreground-context
replacement, keyboard-only use, IME, strict Orca speech, 200% scaling, Dock
restoration, and self-contained publication. Deterministic evidence is recorded in
[editor-transformations-2026-08-12.md](acceptance/editor-transformations-2026-08-12.md);
virtual-navigation evidence is in
[editor-virtual-navigation-2026-08-12.md](acceptance/editor-virtual-navigation-2026-08-12.md),
inspection evidence is in
[editor-code-inspection-2026-08-12.md](acceptance/editor-code-inspection-2026-08-12.md),
keybinding evidence is in
[editor-keybindings-2026-08-13.md](acceptance/editor-keybindings-2026-08-13.md),
Vim evidence is in
[editor-vim-mode-2026-08-13.md](acceptance/editor-vim-mode-2026-08-13.md),
Project User Secrets evidence is in
[project-user-secrets-2026-08-13.md](acceptance/project-user-secrets-2026-08-13.md),
[editor-resilience-2026-08-13.md](acceptance/editor-resilience-2026-08-13.md) records
the Linux resilience matrix,
and prior visual evidence is in
[editor-inlays-codelens-2026-08-12.md](acceptance/editor-inlays-codelens-2026-08-12.md).

Keep direct in-process Roslyn as the default implementation. Source may be adapted
from NetPad or OmniSharp only after license, attribution, version, provenance, tests,
and SBOM review. Do not download or execute a language server at runtime, add implicit
Restore or network access, run duplicate Roslyn workspaces, or expose OmniSharp wire
models across Data Access. An out-of-process OmniSharp adapter is allowed only after
measurements show a concrete isolation, compatibility, or feature advantage and an
ADR amends ADR 012. It must then have typed lifecycle, readiness, crash recovery,
restart, version pinning, integrity, offline installation, and degraded-state UI.

Acceptance compares feature behavior, typing latency, first and warm results, memory,
cancellation, stale-result rejection, analyzer failures, large solutions, and repeated
source-context switches. The complete Linux desktop gate covers keyboard-only use,
IME, AT-SPI, Orca, scaling, Dock restoration, and the chosen Task 048 editor. NetPad's
script runner, rich object dumping, spreadsheet export, and web shell are not IDE
parity requirements. Task 052 covers the relevant project, Run, Test, and Debug
workflows without changing the product into a LINQPad clone. Database, profiler, and
notebook modules remain later independent slices.

### Completed: Task 050 — complete Git workbench

Turn the current status and diff support into the daily Git workflow. Add staging and
unstaging by file, line, and hunk; safe discard; commit and amend; branch and worktree
management; stash; history graph; file timeline; blame; and three-way conflict
resolution. Fetch, pull, and push are explicit developer actions with exact target,
credential, network, divergence, and result display. They never become ambient goal
authority or automatic agent behavior.

Reuse the active source context and exact baselines across Files, editor, diff, Git,
and review. Every destructive operation shows affected paths and recovery options.
Record an ADR before adding remote integration or conflict-write contracts. Preserve
the existing exact goal-commit approval and post-commit handoff.

ADR 024 and the first workbench slices are delivered: one active-context Git snapshot now
separates index and working-tree state, carries an exact stale-state fingerprint,
keeps untracked contents out of diffs, and supports file-level stage and unstage from
the Git tool. Exact hunk and changed-line stage/unstage are also delivered through
opaque recomputed patch-unit identities and a closed stdin-only Git adapter. The
Git tool also previews and explicitly confirms exact tracked-file discard and
untracked-file deletion. It rejects dirty editor buffers and stale state, preserves
the index, and does not follow symbolic links. The remaining workbench capabilities
follow on the same contracts. Developer commit and amend are also delivered as a
separate original-workspace flow: compose, exact staged preview, then confirmation.
The preview displays identity, branch/HEAD, paths, message, and whether configured
hooks run; stale or truncated state is rejected. This cannot satisfy or replace a
goal commit approval.

Local branch management is delivered on that same exact state. Reference fingerprints
detect external branch changes. The Git panel lists local branches and supports create,
safe switch, and rename. Active-context changes close or resolve editor buffers and
refresh persisted workspace identity. Branch deletion has an exact tip/merge/force
preview and explicit recovery warning. Local tags are also delivered: the Git panel
lists peeled targets, creates lightweight or annotated tags at the exact displayed
HEAD, and deletes only after an exact name/target preview and acknowledgement.
Reference changes invalidate every displayed tag action. Developer worktree management
is delivered on a separate complete set fingerprint: inspect, create from an existing
or new branch, enter through the normal workspace trust flow, and exact confirmed
removal. Goal-managed and registered worktrees are protected, and dirty removal needs
an explicit force choice. Local stash create, exact apply-with-retention, and exact
confirmed deletion are also delivered. Applying a conflict keeps the stash and shows
the resulting conflict state; including untracked files during creation is explicit.
The Git panel now separates Changes, Branches, Tags, Worktrees, Stashes, and History
into accessible tabs. History supplies a paged topological graph across reachable
refs, an optional rename-following file timeline, exact commit details and bounded
parent/child patches, and paged blame. Inspection follows the active original or
approved-goal source context and runs off the UI thread with cancellation. The
Conflicts tab now supplies bounded read-only base, ours, and theirs panes plus an
editable result, unresolved marker regions, and isolated Roslyn diagnostics. Exact
fingerprint/hash save and a separate stage action prevent silent resolution, while
unsaved-result prompts prevent refresh, switch, or exit loss. The Remotes tab completes
the task with explicit fetch, reviewed merge/rebase integration of fetched tracking
refs, and explicit push. Typed previews bind the remote, source, destination, local and
remote-tracking observations, divergence, credential source, and recovery limits.
Push defaults to fast-forward and permits only exact force-with-lease as its force
policy. URLs and errors are credential-safe, process output is not retained, operations
are cancellable, and remote authority is never exposed to goals or agents.

### Planned: Task 051 — developer terminal and structured tasks

Add a developer-operated PTY terminal and task runner as separate workbench tools.
Run output remains typed durable evidence; it does not become the terminal. The
terminal is available only in trusted workspaces, has explicit working directory and
environment display, supports cancellation and process-tree cleanup, and restores
no live process after restart. Secrets are redacted from persisted history and
diagnostic bundles.

Discover declarative tasks from existing repository conventions and private Harness
settings. A task records an executable, arguments, working directory, environment,
trust requirement, presentation, and cancellation policy; it never stores a shell
string as an agent capability. Agents continue to use closed typed operations rather
than the developer terminal.

### In progress: Task 052 — .NET project, Run, Test, and Debug experience

Add a semantic Solution view for projects, target frameworks, configurations,
dependencies, SDK/workload health, startup projects, and launch profiles. Add Test
Explorer discovery, hierarchy, filtering, run/debug, duration and failure history,
and coverage navigation. Add typed asynchronous Build/Rebuild, Run, Test, Debug, Hot
Reload, structured output, cancellation, and process lifecycle.

Delivered first slice: a Roslyn-proven project entry point can be run from CodeLens
in a trusted original workspace or approved goal worktree. Harness revalidates the
project, target framework, source path, and saved source hash, starts `dotnet`
without a shell, Restore, or launch profile, supports process-tree cancellation,
persists bounded lifecycle metadata, and keeps stdout/stderr process-local. Solution,
Build/Rebuild, Test Explorer, launch profiles, Hot Reload, and Debug remain planned.

Use the existing trust and Restore boundaries. Selecting a launch profile does not
authorize execution; debug attach, expression evaluation, mutation, dumps, network
listeners, and external processes remain separately classified. ASP.NET endpoint
preview and Avalonia launch/capture may consume the same run identity and portal
evidence. User and agent surfaces share contracts, while agent authority stays
narrower.

### Planned: Task 053 — parallel local agent sessions and exact review

Build several independent chat/goal sessions on the existing durable workflow and
isolated worktrees. Each session has its own source context, model routes, spend
policy, permissions, history, checkpoints, and attention state. Add running, paused,
failed, blocked, complete, and needs-direction views; search and archive; explicit
pause/resume; follow-up; manual takeover; and restart recovery. Do not add unattended
background authority.

Add exact changed-file review with accept/reject by line, hunk, and file; comments
bound to diff baselines; checkpoint restore; and alternate-attempt forks. Acceptance
updates a candidate result or retry guidance, not the original workspace implicitly.
Concurrent sessions must not share mutable Roslyn, process, budget, approval, or
worktree state accidentally.

### Planned: Task 054 — ACP external-agent interoperability

Implement Harness.NET as an Agent Client Protocol client so configured external
agents can run inside Harness chat, context, permissions, worktrees, evidence, and
review. MCP remains the tool and information integration boundary; ACP is the agent
integration boundary. External agents cannot impersonate native roles or bypass
typed authority.

Ship Settings ownership from the first slice: executable or endpoint, transport,
arguments, environment references, working directory, capabilities, health,
enablement, trust, timeout, retention, and removal. Validate handshake and capability
negotiation without inference. Bound messages and attachments, preserve attribution,
redact secrets, expose degraded states, and stop owned processes cleanly. Consider
exposing Harness as an ACP agent only in a later decision.

### Planned: Task 055 — inline AI assistance and edit prediction

Add selection-scoped edit, quick question, send-to-chat, and next-edit suggestions
after the editor foundation is stable. Suggestions may span nearby lines but must
show their scope, route, source version, and remote-disclosure state. Support accept
by token, line, suggestion, or exact preview; reject and dismiss remain local actions.

Use separate Settings-managed routes for inline explanation, editing, and prediction,
with local models preferred and remote spend accounted normally. Cancel stale work,
avoid inference while typing when disabled or offline, and apply multi-file or model
edits only through exact-baseline Roslyn validation. Collect opt-in latency and
acceptance metrics locally without storing source text as telemetry.

### Planned: Task 056 — customization, context inspection, and agent safety

Add reusable skills, prompt procedures, role profiles, and explicit handoffs. Add
typed lifecycle policies such as format changed C# files, run affected tests, require
review for selected paths, or block a class of operation. Policies select closed
Harness operations; they do not execute arbitrary shell hooks.

Before a model call, show a context inspector for files, selections, rules,
documentation, retrieval results, toolsets, images, provider, estimated disclosure,
and exclusions. Let the developer remove context, compact with provenance, or fork a
session. Add private global/workspace exclusions and honor compatible existing ignore
files without creating Harness metadata in repositories. Mark web, MCP,
documentation, terminal, and repository content as untrusted data and keep it from
silently granting authority. Add local profile export, import, validation, diff, and
rollback; defer cloud sync.

### Planned: Task 057 — release health, diagnostics, and updates

Make the installed application diagnosable and recoverable. Produce signed,
reproducible Linux artifacts and manifests; verify update provenance and integrity;
show release notes and migrations; and require an explicit developer action to
install. Retain a rollback path across executable and schema changes.

Add a redacted diagnostic bundle, crash/session recovery report, and health views for
Roslyn, indexing, providers, MCP, ACP, worktrees, processes, storage, and portals.
Define measurable budgets for startup, typing, completion, diagnostics, workspace
load, memory, cancellation, and shutdown. Telemetry remains optional, local-first,
documented, and disabled by default.

### Planned: Task 058 — replaceable development-environment adapters

Add local container and SSH-backed development environments only after the local IDE
workflow is complete. Keep UI and Business Logic contracts platform-neutral while
Data Access owns transport, filesystem, process, port, credential, and lifecycle
adapters. A remote environment has explicit host identity, workspace root, SDK/tool
health, trust, connection state, reconnection, cancellation, and data-disclosure
status.

Start with developer-invoked open/reopen and typed tasks. Do not add cloud agent
hosting, automatic repository upload, hidden port forwarding, or unattended remote
execution. Dev-container or Compose compatibility requires a separate format,
security, provenance, and execution decision; Harness.NET still adds no private
metadata directory to user repositories.

### Planned: Task 060 — workbench composition refactor

Decompose the accreted Avalonia Presentation classes — `WorkbenchDockHost`,
`AvaloniaPresentationStore`, `SettingsWindow`, and `GoalDialog` — into per-tool and
per-section composition units under the guardrails of
[ADR 025](decisions/025-workbench-composition-and-refactor-guardrails.md), and fix
the measured developer-experience gaps: SDK pin portability, hosted pull-request
verification, and ignored local agent credentials. Structure changes only; behavior,
layers, contracts, and toolkits are unchanged, proven by layout, AT-SPI, keybinding,
and acceptance evidence per slice.

The [refactor baseline](refactor-baseline.md) records the 2026-08-24 measurements,
target structure, ordered slices 060.0–060.8, and delegation protocol. Slice 060.0
(SDK pin, ignore rules, continuous verification) should land immediately; extraction
slices are sequential and coordinate with in-progress Task 052 before touching run
output and document surfaces. Landing this before Tasks 053 and 055 keeps their new
UI from growing files this task is shrinking.

## Ongoing work

- Repeat hands-on Avalonia usability checks after changes to workspace, editor, goals,
  evidence, and recovery.
- Keep the accepted workbench, AT-SPI, Orca, and deterministic workflow checks passing.
- Keep Linux-specific code behind focused Presentation or Data Access interfaces.
- Add another platform or gRPC adapter only for a concrete workflow.

## Deferred

- Distributed workers and message brokers.
- Multi-user accounts and shared authorization.
- Web UI.
- Plugin marketplace.
- Unrestricted agent shells.
- Automatic agent merge, rebase, push, or pull-request creation.
- Database, profiler, notebook, dump, and advanced analyzer modules until Tasks
  050–053 complete the daily workflow.
- Unattended background operation.

Stage 3 ends when the application supports real development across repositories and
restarts without the gaps above. Passing another scripted scenario is not sufficient.
