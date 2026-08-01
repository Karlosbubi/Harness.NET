# Implementation Tasks

Tasks are dependency ordered, independently reviewable, and small enough to finish
with focused verification. A task moves to **Done** only when its acceptance check
passes and its documentation is current.

## Delivery discipline

- Decompose implementation tasks into bite-sized chunks with one coherent outcome
  and a focused verification step.
- Commit each completed, verified chunk promptly instead of accumulating unrelated
  work in the working tree.
- Use Conventional Commits descriptions in the form
  `<type>(optional-scope): concise description`, such as
  `feat(workspaces): persist explicit trust decisions`.
- Keep tests and task/documentation status changes in the same commit as the behavior
  they verify or describe.
- Do not mix unrelated cleanup or refactoring into a feature commit.

## Stage 1: Walking skeleton

| ID | Status | Task | Depends on | Done when |
|---|---|---|---|---|
| 001 | Done | Scaffold layer projects and boundary tests | - | The solution builds and reference-direction tests pass. |
| 002 | Done | Add the layer-boundary Roslyn analyzer | 001 | Invalid references and non-interface/record/enum contracts produce diagnostics. |
| 003 | Done | Add XDG configuration and Secret Service access | 001 | Paths resolve predictably and secrets never enter ordinary configuration. |
| 004 | Done | Initialize SQLite with Dapper and DbUp | 001, 003 | Startup creates and migrates a versioned database idempotently. |
| 005 | Done | Configure Serilog and OpenTelemetry | 003 | Redacted JSON logs work locally and OTLP remains opt-in. |
| 006 | Done | Build the initial adaptive Terminal.Gui shell | 001, 003 | The historical demonstration regions render and collapse. |
| 007 | Done | Add the Ollama chat/embedding connector | 001, 003, 005 | Model discovery, streaming chat, embeddings, cancellation, and failures map to records. |
| 008 | Done | Add the OpenRouter connector and cost accounting | 001, 003, 005 | Discovery, streaming, embeddings, routing policy, and cost caps are verified. |
| 009 | Done | Wrap Microsoft Agent Framework in agent roles | 001, 007 | Lead, implementer, and reviewer run behind Business Logic interfaces. |
| 010 | Done | Add tracked-text semantic indexing | 004, 007, 008 | Compatible index partitions rebuild and retrieve eligible repository chunks. |
| 011 | Done | Prove checkpoint recovery through the TUI | 004, 006, 009 | The historical demonstration run paused, resumed, and exposed expandable evidence; it is no longer composed in production. |
| 012 | Done | Publish the Linux x64 walking skeleton | 011 | A self-contained binary starts with correct XDG storage and graceful shutdown. |

## v1.0 usability backlog

The walking skeleton proves technology choices. The following slices are required
before Harness.NET is a usable v1.0 product. **Partial** means supporting code exists
but the end-user workflow is not complete.

| ID | Status | User capability | Depends on | Current gap | Done when |
|---|---|---|---|---|---|
| 013 | Done | Hold a durable local-model conversation | - | Successful live inference still depends on the configured server being reachable. | TUI instructions stream through Business Logic to Ollama, persist, reload after restart, and show actionable provider failures. |
| 014 | Done | Configure and verify model providers | - | - | Configuration validates endpoints, discovers capabilities, selects models per role, and reports health without exposing secrets. |
| 015 | Done | Register and trust a .NET workspace | - | - | A user can add a Git repository, select a solution/project, explicitly trust it, reopen it, and see dirty/base state. |
| 016 | Done | Load the user's engineering framework | - | - | Global, repository, and private framework layers load with precedence, locks, validation, and an inspectable effective view. |
| 017 | Done | Let agents inspect safely | - | - | Typed, path-confined read/search/status/diff/project/build-information tools run only in a trusted workspace and return bounded records. |
| 018 | Done | Let agents implement and verify | - | - | Approved runs can use typed edit/build/test tools with cancellation, output limits, correlation, and separate restore/network approval. |
| 019 | Done | Isolate work with Git | - | - | Each approved goal uses a validated branch/worktree, preserves dirty user state, and never merges/rebases automatically. |
| 020 | Done | Create goals and approve plans | - | - | Goals, caps, plans, revisions, approvals, and denials persist and every consequential transition is validated. |
| 021 | Done | Coordinate lead, implementer, and reviewer agents | - | - | Role prompts and tool scopes are wrapped behind Business Logic interfaces and a lead can delegate bounded tasks. |
| 022 | Done | Resume interrupted work safely | - | - | Runs checkpoint at safe boundaries, resume completed steps, and mark uncertain calls without automatic replay. |
| 023 | Done | Review evidence and accept results | - | - | Diff, tests, tool evidence, review findings, cycle caps, and explicit commit approval work end to end. |
| 024 | Done | Retrieve relevant repository context | - | - | Eligible Git-tracked text is chunked, partitioned by embedding configuration, rebuilt, searched, and filtered by policy. |
| 025 | Done | Use remote models under a cost cap | - | - | Remote use requires approval, streams through the provider boundary, and enforces estimated plus reconciled per-goal caps. |
| 026 | Done | Operate and distribute v1.0 reliably | - | - | A self-contained Linux x64 release passes clean-install, migration, outage, cancellation, recovery, and representative-repository acceptance tests. |
| 027 | Done | Deliver the default desktop as a complete product surface | 013-026 | - | The conversation-first shell and modal inspector are replaced by a rendered Dock workbench with safe source editing, adaptive minimum-size chrome, typed bounded output, and honest empty states. Repeatable production-host AT-SPI runs register and trust real repositories, create goals through both manual and Lead-generated plans, provision isolated worktrees, edit/build/test/review, approve an exact commit, switch real documents, restore after restart, and reject corrupt layout state. Production Orca generates contextual control speech without framework implementation names. No visible surface uses mock or filler UI. |
| 028 | Done | Validate the Dock dependency and package boundary | 027 | - | Stable Dock 12.0.0.2 Avalonia packages are pinned at the Presentation boundary. Real tool/document content is proven in the rendered visual tree, Fluent construction and 200% rendering pass, compact keyboard access and floating ownership are covered, Linux x64 lifecycle verification succeeds, and the production host passes repeatable AT-SPI action and isolated Orca speech-generation checkpoints without framework type-name announcements. |
| 029 | Done | Build the central document workbench | 028 | - | Workspace overview, bounded source, Git diff, current plan, and durable evidence documents use production state or honest empty states. Business Logic resolves one explicit original/approved-worktree context for source, search, Git, and diff; headless checks prove source/diff open, refresh, activation, close, identity retention, and cached switching across 18 documents from six representative projects. |
| 030 | Done | Deliver dockable production tool panels | 029 | - | Real workspace/search, source-control, goal context, conversation, and typed durable run-output controls occupy separate movable, hideable, floatable tool regions with save/reset actions and restart restoration. Build/Test/Restore output exposes real state, correlation, timing, exit, cancellation, truncation, stdout, and stderr or an honest empty/error/running state; no terminal or fabricated diagnostics are present. Product-wide keyboard, accessibility, and compact-layout acceptance remains Task 033. |
| 031 | Done | Persist and recover the desktop layout | 030 | - | A versioned, bounded, integrity-checked layout persists atomically in private XDG state; only known production panes survive, transient documents are omitted, duplicate/unknown state falls back safely, floating bounds are clamped, reset is immediate, and backup v2/offline recovery retain verified layout state without repository metadata. |
| 032 | Done | Add safe source editing semantics | 029 | - | Original-workspace and truncated files remain honestly read-only; an active approved goal opens its isolated worktree with an exact content baseline. Editable AvaloniaEdit tabs expose real dirty state, keyboard and visible save/reload/close actions, durable compare-and-swap saves, explicit reload/overwrite conflict recovery, and save/discard/cancel decisions for tab switches, closes, layout reset, workspace change, and application exit. |
| 033 | Done | Pass docked-workbench product acceptance | 030-032 | - | Real wide and minimum-size Linux empty states are recorded; rendered center-editor attachment, raw-input compact keyboard restoration, explicit application and Dock-chrome automation names, floating ownership, and 200% scaling pass. Repeatable production AT-SPI and isolated Orca workflows prove repository registration/trust, plan approval, editable-worktree documents, search, restart/layout recovery, contextual speech without framework names, and the complete Lead/Implementer/Reviewer exact-commit path. The latter injects a process restart and asserts real typed edit/build/test evidence, original-repository isolation, exact branch commit, and SQLite audit state. |

## Post-1.0 usability follow-through

| ID | Status | User capability | Depends on | Current gap | Done when |
|---|---|---|---|---|---|
| 034 | Done | Open a workspace through a familiar first-run journey | 027 | - | The primary shell and centered empty state open Avalonia's native single-folder picker, retain a manual-path fallback, scan the selected Git repository through the existing typed boundary, and present discovered solutions/projects before registration or trust. The workspace manager separates existing workspaces from adding one, and headless plus production AT-SPI checks cover the route. |
| 035 | Done | Make the workbench's daily surfaces feel like a professional IDE, not a prototype | 016, 026, 029, 032 | - | Framework rules/issues are scannable and filterable; Operations uses a native backup picker; Files is a bounded hierarchy with quick open; the header exposes command search; and Git diff has contrast-validated inline and side-by-side views. Source documents add theme-aware legible syntax colors, breadcrumb/branch/access chrome, compact save/reload/close actions, current-line and selection behavior, and caret/UTF-8/line-ending status without weakening exact-baseline saves. Real wide/compact approved-goal captures and production AT-SPI evidence are recorded in `docs/acceptance/source-editor-2026-07-29.md`. |
| 036 | Planned | Work across more than one trusted repository | 015 | Only one workspace can be active at a time; switching real projects requires re-registration/re-selection overhead. | A user can maintain multiple trusted workspaces and move between them without losing goal/context state, with clear active-workspace indication throughout Avalonia and the TUI. |
| 037 | Planned | Trust the app on real, messy repositories | 013-033 | Large-repository scale, dirty bases, mid-goal conflicts, index rebuilds under load, provider outages, budget exhaustion, and corrupted/interrupted state are exercised only through the single scripted representative-repo gate. | Large real repositories, dirty working trees, merge conflicts mid-goal, provider outages, budget exhaustion, and corrupted/interrupted state are exercised and demonstrably recoverable, not just the scripted gate scenario. |
| 038 | Deferred | See whether agent output can be trusted | 021 | No opt-in behavioral evaluation or regression data exists yet, so plan quality, tool-selection quality, and review quality have no measurable baseline over time or across model changes. Parked below Tasks 035-037 and 039-044 per 2026-07-29 prioritization. | Opt-in Ollama behavioral evaluation datasets exist and regressions in planning, tool selection, and review are detectable before they reach a user's repository. |
| 039 | Planned | Know what to do with an accepted goal branch | 023 | After exact-commit approval the goal branch sits in the repository with no in-app guidance; the user must already know to push, open a PR, or merge outside the app. | The app clearly surfaces the accepted branch's state and the deliberately manual next step (push/PR/merge) without automating merge or PR creation. |
| 040 | Done | Collaborate through chat instead of a chain of pop-ups | 035 | - | Conversation is the primary goal surface; typed inline cards expose plans, cost/capability decisions, progress, validation, evidence, Restore, commit, and handoff. Plan, spending, Restore, destructive, budget-extension, and exact-commit authority remain explicit typed actions. Ordinary role/model/output defaults move to one searchable Settings surface, goal overrides use progressive disclosure, and obsolete dialog paths are removed. Production wide/compact and AT-SPI acceptance is recorded in `docs/acceptance/chat-first-workflow-2026-07-29.md`. |
| 041 | Planned | Recover application state without the verifier script | 026 | Operations can only create a backup; restoring one into a fresh install is proven solely by `eng/verify-v1-release.sh` and is not available to a user in either Avalonia or the TUI. | A user can restore a private-state backup into a fresh or existing install from Avalonia/TUI Operations, with the same integrity verification the release gate already performs, gated by an explicit confirmation given the sensitivity of the archive. |
| 042 | In progress | Validate every edit, model or manual, before it is trusted | 017, 018, 032 | Trusted source tabs now have live compiler/analyzer diagnostics; model-authored candidate edits still need fail-closed preflight and post-apply verification. | An in-process Roslyn implementation behind implementation-neutral Data Access and Business Logic contracts loads only trusted source contexts without implicit restore. Versioned live buffers show syntax/compiler/analyzer diagnostics inline and in a Problems tool. Every model-authored candidate is compared with baseline diagnostics, is rejected before disk when it introduces a compiler Error, records warnings/findings as evidence, applies through an atomic baseline-protected boundary, and is verified again after apply. |
| 043 | Planned | Get semantic assistance while editing | 042 | The editor has no completion, quick info, signature help, definition/reference navigation, or semantic symbol awareness. | Warm, cancellable completion, quick info, signature help, go-to-definition, and find-references operate on the exact active Roslyn source context; stale responses are discarded, keyboard and pointer interactions are accessible, and detailed targets plus degraded states follow ADR 012. |
| 044 | Planned | Use deterministic Roslyn transformations for deterministic work | 042 | Humans and agents can only request textual edits, so a rename can miss references, alter unrelated text, or cross a delegated path grant. | Semantic rename resolves a symbol through Roslyn, previews every affected file and conflict with exact baselines and a fingerprint, enforces goal/task path grants, applies all files atomically or none, and records post-apply diagnostics and diff evidence. The editor and agent tool use the same typed operation; no model-authored text-search rename path exists. |

### Prepared delivery slices for Tasks 040 and 042-044

These slices are the implementation order. Each ends with its narrow tests, a build,
and synchronized documentation; a later slice must not be pulled into an earlier one
merely to complete the final visual design at once.

#### Task 040: chat-first workflow and Settings

1. **Settings foundation:** inventory current global, workspace, and goal values;
   expose typed read/update contracts for ordinary defaults; add a searchable Settings
   shell for General, Editor, Appearance and accessibility, Models and roles, Privacy
   and limits, Storage and recovery, and Advanced. Preserve goal-bound spending
   authorization as a separate decision rather than migrating it into a default.
   Complete: the category shell, search, persisted Appearance page, typed role/model/
   output defaults, remote-authority separation, and ownership inventory are delivered.
2. **Read-only workflow cards:** project existing plan, run, task, review, evidence,
   Restore, commit, and handoff records into immutable conversation
   card state. Render chronological loading, unavailable, stale, denied, failed,
   cancelled, recovered, and completed states without adding commands yet.
   The immutable projection and read-only Avalonia timeline are delivered for selected
   goals, plans, checkpoints, tasks, evidence, capability/Restore approvals, commit
   preview/approval, loading/unavailable/error states, and the full degraded-state
   vocabulary. Authority-bearing buttons remain in their existing surfaces for the
   next slice.
3. **Goal creation and continuation:** let the composer create or select a goal and
   continue its Lead/Implementer/Reviewer workflow with configured defaults. Put
   optional per-goal role routes, output ceilings, review cycles, privacy route, and
   remote cap behind one progressive-disclosure surface.
   The ordinary entry path is delivered: in a trusted active workspace the first
   composer submission creates and selects a private draft with three review cycles,
   no remote budget, and no provider call. Existing unselected goals render as
   explicit inline Continue choices. Role workflow progression and progressive
   per-goal overrides remain in this slice.
   Draft-only progressive overrides are now delivered from the goal card: review
   cycles and an exact goal-wide remote USD cap persist through a typed optimistic-
   concurrency boundary, and role/model routes open on demand. Remote spend stays
   disabled by default, requires workspace trust, and cannot be added after planning
   starts. Per-run output ceilings remain explicit on the run action.
4. **Inline authority actions:** bind explicit typed approve/deny/cancel commands to
   the matching plan, remote authorization, Restore, budget, destructive-operation,
   and exact-commit cards. Preserve any policy-required second confirmation as one
   focused sheet and prove stale fingerprints cannot execute.
   Plan generation, plan approval/change requests, production continuation, run
   cancellation, correlation-bound Restore decisions, and exact-diff commit decisions
   now project through typed actions on their matching cards. Bounded role-call
   disclosures, one-use Restore confirmation, exact fingerprint confirmation, and
   stale checks remain in force. Remote authorization, budget extension, and other
   destructive-operation cards are still pending.
5. **Retire modal orchestration:** remove superseded Goal dialog paths, move remaining
   detailed plan/diff/evidence views into documents or tools, resize the default Dock
   layout so conversation is usable, and record keyboard-only plus wide/compact
   hands-on acceptance. Refactor `GoalDialog` only along these delivered seams.
   Complete: routine Goal dialog entry points are removed; accepted runs load exact
   commit review from their card; semantic context remains a focused inspector; the
   normal and compact layouts keep chat usable; and production visual plus AT-SPI
   evidence is recorded in `docs/acceptance/chat-first-workflow-2026-07-29.md`.

#### Task 042: Roslyn workspace and validation

1. **Compatibility checkpoint:** pin one coherent Roslyn Workspaces/Features and
   MSBuild-locator package set; resolve the selected workspace's installed SDK before
   MSBuild types load; reconcile the existing construction-only Microsoft.Build use;
   and prove `.slnx`/`.sln`/`.csproj`, missing-SDK, Headless, and self-contained Linux
   publish behavior. Amend ADR 012 before choosing an out-of-process fallback.
   Complete: Roslyn 5.3 packages and MSBuild Locator 1.11.2 load the SDK selected by
   workspace-local `global.json`; the metadata inspector no longer loads MSBuild;
   synthetic and real entry points load without restore; missing SDKs degrade with an
   actionable code; and the single-file Linux release keeps Roslyn's build host
   external. Evidence is recorded in
   `docs/acceptance/roslyn-compatibility-2026-07-31.md`.
2. **Semantic contracts and deterministic fake:** add source-context, session,
   document-version, diagnostic, status, and validation records plus Data Access and
   Business Logic interfaces. Prove trust, path, cancellation, and stale-version
   policy without Roslyn or Avalonia.
   Complete: capability-oriented Data Access contracts contain no Roslyn/MSBuild
   types; presentation-neutral Business Logic contracts map source sessions,
   immutable buffer snapshots, diagnostics, and candidate validation. A deterministic
   engine fake proves trust gating, approved-worktree validation, confined paths,
   cancellation, session disposal, and stale in-flight result rejection.
3. **Roslyn/MSBuild implementation:** load the selected entry point for the trusted
   original workspace or approved goal worktree in Data Access; never restore; expose
   load progress and actionable SDK/assets/reference/analyzer failures; dispose or
   invalidate state on context changes. Update trust copy for project evaluation and
   analyzer/source-generator execution.
   Complete: the Host composes one bounded foreground Roslyn engine; it reports typed
   SDK/load/evaluation/ready progress, warms project compilations without restore,
   retains bounded workspace failures, synchronizes exact-baseline in-memory source,
   runs compiler and configured analyzer diagnostics, and safely drains in-flight work
   before context replacement or disposal. Real adapter tests prove diagnostics,
   baseline staleness, progress, context invalidation, and absence of implicit restore.
   Avalonia and TUI trust confirmations now name project evaluation plus configured
   analyzer/source-generator execution.
4. **Live diagnostics:** synchronize immutable editor buffers with debounce and
   cancellation; render version-matched diagnostics in AvaloniaEdit and a dockable
   Problems tool whose rows navigate to the exact document range. Record cold-load,
   warm-update, memory, and cancellation measurements.
   Complete: C# source tabs debounce immutable exact-baseline snapshots, cancel older
   work, discard stale identities, draw error/warning squiggles, and report code health
   in the editor footer. The seventh durable Dock tool provides accessible severity
   filters, bounded rows, Ctrl+Shift+M restoration, and exact source navigation while
   legacy six-tool layouts gain Problems during restore. Production wide/compact
   evidence and real-`Harness.slnx` measurements are recorded in
   `docs/acceptance/roslyn-live-diagnostics-2026-07-31.md`.
5. **Agent mutation preflight:** classify every candidate as compiler-validated or
   explicitly NotApplicable; validate applicable changes in memory, compare
   baseline/retained/resolved/introduced diagnostics, reject new compiler Errors,
   persist bounded warning/analyzer evidence, apply accepted edits with exact
   baselines, and verify the applied state before completing the tool result.

#### Task 043: interactive semantic assistance

1. Add warm cancellable completion with keyboard selection, commit characters,
   accessible item text, and stale-result rejection.
2. Add quick info and signature help with bounded documentation rendering and correct
   placement at the active versioned caret.
3. Add go-to-definition and find-references across the resolved source context,
   opening real documents and reporting generated/metadata/unavailable destinations
   honestly.
4. Run the representative small/large-workspace latency and memory checks from ADR
   012 and complete a hands-on editing pass.

#### Task 044: deterministic transformations

1. Extend the mutation boundary with an atomic multi-file compare-and-swap operation,
   including rollback, cancellation, normalized path grants, and durable evidence.
2. Add Roslyn rename resolution and a bounded preview containing symbol identity,
   conflicts, affected paths, baseline hashes, diagnostic delta, and a fingerprint.
3. Add fingerprinted apply and post-apply verification, then expose the same closed
   rename operation to the editor and the Implementer role.
4. Prove overloads, partial types, generated/uneditable references, linked files,
   conflicting names, stale buffers, out-of-grant paths, rollback, and large rename
   sets without invoking a model.

### v1.0 release gate

Tasks 013-033 are all **Done**, and a release candidate completes a representative
.NET repository change from workspace registration through explicit commit approval,
survives an injected interruption, and leaves both the user repository and private
Harness.NET state auditable. That gate defines version `1.0.0`.

Passing this gate proves the mechanics of the first complete workflow; it does not
by itself mean the application is productive for real day-to-day use. As of
2026-07-29 it is not yet: no edit is validated against a compiler workspace, the editor has no semantic
assistance or deterministic refactorings, a single goal still walks through up to 14
modal dialogs, only one workspace can be active at a time, and the workflow has only
been proven against the single scripted representative repository rather than large
or messy real ones. Tasks 035-037 and 039-044 track closing that gap; Task 038 is
deferred until those close. Tasks 040 and 042-044 are the current top priority:
matching a professional editing, Git, and agent-collaboration baseline before further
unique features.

`eng/verify-v1-release.sh` is the executable gate. Its deterministic suite covers
provider outages, budget failures, cancellation, interruption reconciliation, and
the representative trusted-repository workflow. Its package phase verifies clean
startup without an installed runtime, SIGTERM, portable backup, recovery into a
fresh XDG root, automatic pre-migration backup, schema 16-to-17 upgrade, and retained
audit content. The verifier never loads repository `.env` credentials or calls a
model provider.

`eng/verify-v1-desktop-release.sh` is the complete Linux desktop gate. It adds the
production AT-SPI/Orca workflow and the deterministic-loopback Avalonia
edit/build/test/review/exact-commit verifier. Neither graphical verifier invokes a
configured or paid provider.

## Task 001 acceptance

- Runtime projects are `Harness.DataAccess`, `Harness.BusinessLogic`,
  `Harness.Presentation.Terminal`, and `Harness.Host`.
- References point directly upward: Business Logic to Data Access, Presentation to
  Business Logic, and Host to all three solely as composition root.
- Central package management is enabled.
- Architecture tests assert the allowed runtime project-reference graph.
- `dotnet build` and `dotnet test` complete with zero warnings.

## Current verification

- The architecture analyzer has focused diagnostic tests and runs in every runtime
  project build.
- XDG resolution, Secret Service fallback, DbUp migrations, JSON-log redaction,
  responsive layout policy, and Ollama payload mapping have deterministic tests.
- The host and Terminal.Gui lifecycle have been smoke-tested against isolated XDG
  directories.
- On 2026-07-27, the production Ollama adapter passed live discovery and streaming
  against `gemma4:latest`, including completion/tool/thinking capabilities and token
  usage. The composed TUI path persisted `HARNESS_APP_OK` with 25 input and 7 output
  tokens.
- Task 013 has orchestration tests plus a real TUI-to-SQLite failure-path check. A
  submitted turn was persisted before the provider call, its structured connection
  failure was persisted, and the history reloaded through the same Business Logic
  service. Successful token streaming is now also covered by the opt-in live test.
- Task 014 now exposes provider health, discovered capabilities, refresh, and durable
  conversation model selection in the wide TUI. Typed XML defines named provider
  modules and validates main/reviewer/tool/embedding routing; all chat routes provide
  local role defaults and the embedding route is consumed by semantic indexing.
  Schema 14 persists explicit goal-specific lead, implementer, and reviewer choices.
  Goal catalog discovery distinguishes chat from embedding models, preserves named
  module attribution, reports per-provider failures without exposing secrets, and
  shows published remote input/output/request pricing. Agent execution resolves the
  selected goal/role route and carries strict privacy plus a required output-token
  ceiling into remote requests.
- Task 010 uses the pinned Microsoft SQLite vector connector solely inside Data
  Access. Deterministic tests prove Git-index eligibility and secret/generated/binary
  filtering, stable bounded chunks, provider/model/dimension/version partitioning,
  native cosine retrieval, atomic compatible replacement, and preservation of the
  ready generation when a rebuild is aborted. Business Logic enforces active trust,
  batches provider-neutral embeddings, validates vector shapes, accounts for remote
  usage, and exposes rebuild/search records without connector types.
- Task 024 exposes a no-inference compatible-index status operation and a goal-bound
  context service that maps every query to the goal workspace, 1-8 matches, and strict
  remote privacy. Lead, Implementer, and Reviewer tool scopes include a bounded
  `search_semantic_context` function; remote query embeddings are separately reserved,
  reconciled, and attributed to the goal. Avalonia and the Goals TUI show embedding
  access,
  provider/model/dimensions, current partition, and goal cost state before explicit
  rebuild or search actions. Rebuild requires a separate confirmation, and result
  views expose tracked/skipped files, truncation, chunks, input tokens, cost, source
  path, line range, distance, and full bounded context.
- On 2026-07-28, the OpenRouter embedding path returned a 1,536-dimensional vector
  from `openai/text-embedding-3-small` for one short input. The opt-in live test
  enforced a five-microdollar reservation ceiling before sending the request.
- Task 025 now requires an explicit remote provider/model choice for each goal role
  and a positive goal cap. The durable selection authorizes only that provider/model
  for the goal before plan approval, allowing a remote lead to propose a plan without
  granting mutation rights. Every role call is goal-scoped and output-capped;
  reservations and reconciled charges remain enforced atomically and visible in the
  goal cost report. Approved goals retain goal-scoped remote embedding support.
- Task 009 wraps Microsoft Agent Framework's `ChatClientAgent` behind semantic
  Business Logic contracts. Deterministic tests run lead, implementer, and reviewer
  prompts through separate configured provider/model routes and verify invalid
  requests, incomplete composition, and provider-failure mapping without exposing
  framework types to Presentation.
- Task 021 now maps Microsoft Agent Framework functions through provider-neutral
  semantic definitions, calls, and results into Ollama and OpenRouter. Deterministic
  tests prove a complete model-tool-model loop and closed role scopes: Lead is
  read-only against the trusted original workspace, Implementer gains only approved
  worktree edit/build/test capabilities, and Reviewer is read-only with durable
  evidence access. Restore, commit, package, and shell capabilities remain absent.
  OpenRouter reservations conservatively include tool schemas and tool traffic.
- Schema 17 persists 1-12 ordered Lead-authored tasks with semantic identifiers,
  sequence, title, objective, file-area boundary, acceptance criteria, state, and full
  report. Strict Lead JSON is rejected unless every task is bounded. The coordinator
  invokes Implementer once per pending task, checkpoints each call and report, resumes
  at the next pending task, reconciles a report durable before its checkpoint, and
  never replays an uncertain task call. Workflow snapshots, Avalonia, and the TUI
  expose every task and report. Before continuation, both adapters show pending task
  calls and the
  maximum remaining Reviewer/correction calls alongside routes, output caps, goal cap,
  reservation, spend, and remaining budget. Implementer agent requests require a
  semantic file-area grant, and the atomic edit tool rejects paths outside it.
- Schema 15 adds one active production run per goal with semantic states and ordered
  checkpoints. Avalonia and the TUI start Lead planning with an explicit output
  ceiling, pause at the durable plan, and continue approved work through Implementer
  and independent
  Reviewer ceilings while displaying the current cap, reservation, spend, and
  remaining budget. Deterministic recovery tests prove that an already-persisted plan
  is reconciled without another Lead call, a completed implementation resumes at the
  Reviewer boundary, and uncertain role calls become user-direction checkpoints
  without replay. Reviewer output is strict JSON with a closed accept/revise decision;
  malformed results cannot enter acceptance. Revision findings drive a bounded
  Implementer correction and independent re-review; the durable semantic cycle count
  stops the loop at the goal limit and requires user direction.
- Task 023 independently reviews diff and tool evidence before acceptance. Schema 16
  persists a separate exact commit request and closed Pending/Approved/Denied/Committed
  states. The request fingerprints goal, workflow run, branch, expected HEAD, complete
  diff SHA-256, message, and author. Avalonia and the TUI display the full fingerprint
  and diff, record it as Pending, then require a distinct approve/deny action. The Git
  adapter
  revalidates the exact worktree and fingerprint, commits only to the isolated branch,
  reconciles an interrupted successful commit, and never merges, rebases, cherry-picks,
  or performs network access.
- Task 011 persists semantic workflow runs and ordered checkpoints in schema 11.
  Deterministic store and orchestration tests prove plan-time pause, process-restart
  resume, recovery after interruption at an already persisted implementation
  boundary, independent review completion, and stale-transition rejection. The TUI
  Workflow menu starts or resumes the run and expands full checkpoint evidence in a
  scrollable view through presentation-neutral Business Logic contracts.
- Task 012 has a checked-in linux-x64 publish profile and repeatable lifecycle
  verifier. The compressed self-contained executable starts with `PATH` and
  `DOTNET_ROOT` pointing to nonexistent locations, loads the shipped XML defaults,
  creates its database and logs only under isolated XDG data/state roots, and exits
  with status zero after SIGTERM. Native libraries remain beside the executable so
  the runtime does not extract bundle content into a non-XDG home directory.
- The XML-selected `MainLlm` was verified through the composed TUI against Ollama;
  it persisted `HARNESS_XML_OK` with 34 input and 7 output tokens.
- Task 015 has deterministic Git inspection, SQLite registry, single-active-workspace
  selection, entry-point validation, and explicit trust-transition coverage. The TUI
  now provides a workspace-management modal and separate trust confirmation; narrow
  layouts reach the same commands through the top-level Workspace menu. Dashboard
  snapshots resolve active workspace context once per operation and retain a stable
  value throughout streaming.
- Task 016 has a provider-neutral rule resolver with deterministic precedence,
  provenance, locks, validation, and unresolved same-level conflict reporting.
  Bounded readers load global XDG Markdown and root repository `AGENTS.md` with
  privacy and provenance metadata. Workspace-private overlays persist in SQLite
  without repository metadata. Business Logic composes all three document layers,
  effective rules, and source failures into one snapshot. Named XML rules bind into
  immutable configuration and the resolver. Avalonia and the TUI render the effective
  snapshot in a scrollable view and edit the private workspace overlay with a
  supported multiline editor, without writing Harness.NET metadata to the repository.
- Task 017 has begun with a typed file-inspection boundary. Business Logic requires
  the requested workspace to be active and explicitly trusted. Data Access accepts
  only confined relative paths, rejects symbolic-link hops and non-UTF-8 files, and
  bounds returned content to 64 KiB with explicit truncation metadata. Tracked-text
  search enumerates the Git index, shares the confinement policy, skips oversized or
  non-text files, and returns bounded line records with truncation metadata. Git
  inspection returns branch and HEAD identity, bounded status records, and a
  combined index/worktree diff capped at 128 KiB. Non-evaluating .NET metadata
  inspection uses the MSBuild solution parser plus bounded XML/JSON readers for
  projects, target frameworks, SDK/language settings, references, and `global.json`.
- Task 020 has durable draft goals tied to the active workspace. Goal creation
  validates title/objective bounds, review-cycle limits, and optional remote-model
  budgets before schema-versioned SQLite persistence. Plan proposals increment
  revisions and wait for a decision. Approval requires active workspace trust;
  denial requires a reason. Goal, plan, and decision transitions persist atomically
  and reject stale or duplicate decisions. Avalonia and the TUI create and inspect goals,
  proposes plans, confirms worktree-granting approval, records denial reasons, and
  displays local-only authorization or fully attributed remote-cost totals. Goal
  identifiers, plan identifiers and revisions, review caps, money, states, decision,
  approval, and worktree values cross Presentation through semantic records/enums.
- Task 019 has a structured Git worktree adapter with canonical goal identifiers,
  deterministic Harness-owned branch/path names, bounded diagnostics, cancellation,
  and idempotent retry. Tests verify that worktrees start at the recorded base commit
  while dirty state in the user's original worktree remains unchanged. Approval
  provisions the worktree before atomically persisting the approval and active grant;
  provisioning failure leaves the plan pending and grants no mutation capability.
- Task 018 has an approved mutation boundary for file creation and replacement.
  Business Logic requires an approved goal, active persisted worktree grant, active
  workspace, current trust, and correlation identifier. Data Access independently
  confines paths, rejects symbolic links and stale SHA-256 expectations, caps UTF-8
  content at 1 MiB, and atomically replaces the destination. The same authorization
  boundary now exposes typed Build, Test, and separately approved Restore operations
  against the registered entry point in the goal worktree. The process adapter
  disables implicit restore for Build and Test, confines the entry point, cancels
  the process tree, and returns exit, duration, bounded output, and truncation
  evidence. Schema version 9 persists every approved
  tool request before execution and completes it with full correlated result evidence.
  Duplicate correlations cannot replay a tool, incomplete calls remain identifiable
  for recovery, and a presentation-neutral Business Logic service exposes the audit
  trail. Tool operations, kinds, states, identifiers, and correlations use semantic
  enums and single-value records under ADR 007. Schema version 10 adds durable
  Restore approval requests and decisions. Approval is scoped to one goal,
  correlation, capability, and registered entry point; denied, missing, mismatched,
  or replayed requests cannot start the process. Build and Test continue to force
  `--no-restore`, while only an explicitly approved Restore may resolve dependencies.
  Avalonia and the TUI manage the durable exact request and its separate decision;
  neither surface turns that approval into a global network grant.
- Task 026 adds a typed Operations boundary and Avalonia/TUI/CLI application-state export.
  Backups use a consistent SQLite snapshot, verify integrity, publish atomically
  without overwrite, restrict Linux permissions, and report semantic path, hash,
  byte-count, schema-version, and failure values. Schema upgrades create and verify
  the same recovery point before mutation. The release verifier proves a clean
  self-contained install, SIGTERM cancellation, schema migration, portable recovery,
  retained audit data, and a real isolated repository workflow through exact commit
  approval, while deterministic provider/cost tests exercise outages and fail-closed
  spending without using configured credentials.
- Task 031 persists a closed, versioned Dock-layout description atomically under
  private XDG state. Headless tests prove moved, hidden, floated, and resized panel
  restoration; transient-document omission; unknown/duplicate graph rejection;
  invalid-proportion normalization; off-screen bound clamping; and immediate reset.
  Reset and live graph replacement explicitly release durable application controls
  from retired Dock presenters, and a rendered-tree regression proves the overview,
  workspace, conversation, and goal-context content remains visible afterward.
  Backup v2 tests and the Linux x64 lifecycle verifier prove optional layout hash
  evidence and offline recovery without adding repository metadata.
- Task 032 adds a semantic Business Logic document boundary over the approved-goal
  worktree and the existing durable mutation service. Deterministic tests prove
  read-only fallback, trust and approval enforcement, exact UTF-8 baselines,
  truncation safety, compare-and-swap conflicts, and current-version evidence. The
  headless Dock workbench proves editable/dirty state, conflict overwrite,
  cancel/save/discard tab activation, close, reset, and exit decisions; the editor
  also exposes visible save/reload/close and keyboard save/close commands. The
  representative repository acceptance test performs the real read and atomic save
  in the isolated Git worktree.
- Task 029 routes source, tracked-text search, Git state, and diff through one semantic
  Business Logic workspace context. An approved selected goal resolves all four to
  its active worktree; otherwise the UI labels and reads the trusted original
  workspace without implying edit authority. Headless Dock tests prove source and
  diff open/refresh/activate/close behavior, retain source editor instances during
  tab switching, and exercise 18 documents across six representative projects. The
  complete open-and-1,800-switch scenario took 278 ms on the 2026-07-29 verification
  run and is guarded by conservative 10-second open and 5-second switch ceilings.
- Task 030 adds a separate durable Run output Dock tool backed by a typed Business
  Logic projection over the existing tool-evidence audit. It lists only real
  Build/Test/Restore executions and exposes state, correlation, timestamps, entry
  point, exit code, cancellation, duration, bounded stdout/stderr, and truncation;
  corrupt or mismatched result JSON is rejected before Presentation. Headless tests
  cover populated and honest empty states, and the representative repository test
  proves Restore, Build, and Test results round-trip through SQLite into the same
  projection. The required pane is part of layout format 2 and older format-1 graphs
  fall back visibly to the complete safe default.
- Task 028 now uses Dock's Avalonia content model rather than treating MVVM `Context`
  as rendered content. A regression test requires the real AvaloniaEdit source editor
  to appear in the window visual tree. Deterministic checks cover compact keyboard
  restoration, explicit automation names, floating-window ownership, and 200%
  rendering; the recorded wide/compact review is linked from ADR 010.
- Task 035 replaces manual relative-path entry in Files with a bounded Git-tracked
  tree over the same typed original-workspace or approved-worktree context used by
  source, search, and Git. The tree filters locally, refreshes without accepting stale
  context results, opens files through the existing document boundary, and retains
  content search below the tree. Conversation cards now bind their panel/accent
  backgrounds dynamically so an effective light/dark theme change cannot combine an
  old card background with new foreground colors.
- Task 035 replaces the raw unified-diff text dump with a decorated diff viewer. A
  display-only parser classifies file, hunk, metadata, context, added, and removed
  lines, tracks old/new line numbers from each hunk header, and pairs replacements
  into aligned side-by-side rows that leave the shorter side honestly empty. The
  inline mode reviews changes in place with dual gutters and +/- markers; the
  comparison mode evaluates Git state across two columns. Rows carry semantic
  classes so the already contrast-validated `DiffAdd*`/`DiffRemove*` theme tokens —
  previously defined through every layer but consumed by no view — supply the
  colour, and an effective theme change repaints them. `eng/capture-diff-viewer.py`
  records the evidence under `docs/acceptance` from the real production host over a
  real repository working tree.
- Task 035 adds a command palette over the shell's real commands, reachable both
  from a visible header command bar and with Ctrl+Shift+P, because a chord-only
  entry point is undiscoverable and unreachable for some users. Matching is a
  case-insensitive subsequence over "Category: Title", and a command that needs an
  active or trusted workspace stays listed with the reason it cannot run rather
  than disappearing. The bar states its own shortcut.
- Task 035 adds Ctrl+P quick open over the same bounded, context-resolved
  Git-tracked catalog the Files panel uses, loaded on demand. A file is offered as
  a command that opens it, so ranking, keyboard handling, and styling are shared
  with the palette; matching runs over the whole repository-relative path. Without
  a trusted workspace the status line says so instead of opening an empty picker.
- Task 035 replaces the framework text dump with a scannable inspector. A validity
  headline and counts sit above a filter and a locked-only toggle; rules and issues
  render as individual rows with lock chips and provenance, and guidance-document
  bodies stay collapsed because their content is what made the old view unreadable.
  Filtering observes the text property rather than the changed event so a
  programmatic filter applies too.
- Task 035 replaces hand-typed backup destinations with the platform save dialog,
  matching the workspace folder picker from Task 034. Manual entry remains for
  desktops without a picker, and choosing an existing archive reports the
  no-overwrite constraint immediately instead of at creation time.
- Task 035 restructures the header bar from loose labelled fields into an IDE
  headerbar: an application mark, a title block whose subtitle reports the real
  active workspace and branch instead of a static tagline, and bordered clusters
  that group each label with the control it names. Accessibility names are
  unchanged and the production AT-SPI workflow still passes.
- Task 035 completes the source/editor pass with a focused `SourceEditorSurface`
  seam instead of extending the Dock host further. It presents the real relative
  path, source context, access state, actions, caret, selection, encoding, and line
  endings around the existing exact-baseline editor. C# lexical colours are remapped
  through semantic Harness theme tokens and refreshed with effective theme changes.
  `eng/capture-source-editor.py` records real approved-goal wide and compact states;
  the review and limitations are documented in
  `docs/acceptance/source-editor-2026-07-29.md`.
