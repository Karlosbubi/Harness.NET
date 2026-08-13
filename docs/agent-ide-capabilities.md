# Model-accessible IDE capability map

This table tracks [ADR 016](decisions/016-model-accessible-ide-capabilities.md).
It was compared with the JetBrains Rider MCP Server 2026.2 catalog on
2026-08-10 and against the published
[Rider MCP tool documentation](https://www.jetbrains.com/help/rider/mcp-server.html).
Rider is an inventory reference. Harness.NET owns its schemas and authority rules.

Status meanings:

- **Delivered:** a role already receives the typed capability.
- **Internal:** Harness.NET has the behavior, but models cannot use it yet.
- **Partial:** a narrower typed behavior exists.
- **Planned:** no end-to-end model-accessible slice exists yet.
- **Excluded:** deliberately outside the product contract.

## Parity map

| Rider-inspired capability | Harness.NET target | Status | Required boundary |
|---|---|---|---|
| `read_file` | Bounded ranged/structural reads with line coordinates and baseline hash | Delivered | Trusted source context; original/worktree role scope |
| `list_directory_tree`, `search_file` | Bounded tree, glob and filename search respecting Git/exclusion policy | Delivered | No shell; confined normalized paths |
| `search_text`, `search_regex` | Literal and regex search with coordinates, masks and result limits | Delivered | Bounded tracked/project text |
| `search_symbol`, `skill_search` | Roslyn symbol search plus source-neutral capability search | Delivered | Exact semantic context; external symbols remain excluded |
| `get_all_open_file_paths`, `open_file_in_editor` | Read active/open document context and request developer-visible navigation | Delivered | Presentation performs navigation; no model desktop control |
| `create_new_file`, `apply_patch` | Exact-baseline create/patch in delegated goal areas with Roslyn preflight | Partial | Approved worktree, atomic mutation, evidence |
| `get_solution_projects`, `get_project_dependencies` | Evaluated solution/project graph, exact project/package references and resolved versions | Delivered | Declared/central/locked/direct/transitive/restored package evidence is separate and delivered |
| `get_project_problems`, `get_file_problems`, `lint_files` | Versioned file/project/changed-set diagnostics with stable identities and delta | Partial | Exact-file and deterministic changed-set checks plus post-transformation validation are delivered; broader lint delta remains Task 049 |
| `get_symbol_info` | Quick info, declaration, documentation, type and source/metadata destination | Delivered | Role-scoped source is loaded at the current exact file baseline |
| definition/reference/implementation navigation | Roslyn definition and bounded usage/implementation destinations | Delivered | Original workspace for Lead; approved goal worktree for Implementer/Reviewer. Generated output and locally decompiled metadata source, with explicit signature fallback, are eagerly resolved before the short-lived role session closes |
| semantic editor presentation | Visible-range classification, occurrences, folding, outline, breadcrumbs, and workspace symbols | Internal | Shared exact live buffer; developer UI delivered, model tools remain deliberately narrower |
| `analyze_calls`, class hierarchy tools | Incoming/outgoing call, type and override hierarchy | Delivered | Roslyn semantic identity and paging bounds |
| `findTests` | Discover tests associated with a symbol | Delivered | Deterministic Roslyn association; runnable test-case lifecycle remains Task 052 |
| `post_edit_quality_check` | One changed-set gate combining diagnostics, placeholders, tests/build evidence and unresolved findings | Delivered | Reuse evidence; never self-certify model output |
| `reformat_file` | Preview/apply repository code style and format changed files | Delivered | Closed document/selection/changed-span/paste/on-type formatting, import organization, and unused-import cleanup have exact baseline, fingerprint, file grant, atomic apply, evidence, and post-check |
| `find_missing_imports` | Discover exact namespace choices for an unresolved type | Delivered | Roslyn searches source, referenced projects, and metadata, inserts each candidate in memory, and returns it only when the type binds at the exact caret; `AddMissingImport` still requires preview/fingerprint/apply |
| `find_code_actions` | Discover closed compiler fixes and local/selection refactorings | Delivered | The pinned Roslyn catalog is explicitly allowlisted and every candidate is preflighted as a single current-document edit. Returned opaque ID and occurrence/document scope are required by `ApplyCodeAction` preview/apply; arbitrary, cross-document, project, and custom-host actions are rejected |
| `rename_refactoring` | Fingerprinted Roslyn rename preview/apply | Delivered | Approved worktree and delegated file areas |
| extract/introduce/loop/property/namespace and related local refactors | Closed preview/fingerprint/apply operations | Partial | Exact-caret and exact-selection local Roslyn refactorings are delivered, including extract method/local function and introduce variable. Cross-document move/signature/safe-delete operations remain excluded until they receive explicit multi-file contracts and authority; no arbitrary executor |
| `build_solution_start`, `build_solution_state` | Start/cancel/poll bounded build or rebuild with streaming structured problems | Partial | Workspace trust; no implicit restore |
| `get_run_configurations`, `execute_run_configuration` | Discover launch profiles, executable entry points and tests; run a typed target with bounded overrides | Planned | Explicit target/args/env/cwd; repository execution authority |
| `execute_terminal_command` | Closed structured command/run modules for justified gaps | Excluded as shell | Never accept an unrestricted shell command string |
| `get_repositories`, `git_status` | VCS roots, status, branch, HEAD and bounded diff | Partial | Repository-local Git adapters |
| Git integration mutations | Stage/commit through existing exact-diff fingerprint; future typed branch/remote actions | Partial | Separate explicit integration/network authority |
| debugger status/start/attach | Typed debug configurations, process attach and session lifecycle | Planned | Trust plus explicit launch/attach approval |
| breakpoints/logpoints and control | List/set/remove scoped breakpoints; pause/resume/step/run-to-line/stop | Planned | Session identities, stale-state checks, bounded waits |
| threads/stacks/frame/value inspection | Paged runtime inspection with depth/size limits | Planned | Suspended exact session/frame identity |
| debugger expression/variable mutation | Explicitly risk-classified evaluate/set operations | Planned | Consequential confirmation; evaluation may execute code |
| database connection/schema/object inspection | Named secret-backed connections and bounded metadata inspection | Planned | Settings + Secret Service; source/credential status |
| SQL execute/fetch/cancel and table preview | Bounded query sessions, pagination and cancellation | Planned | Server-enforced read-only principal or explicit mutation approval |
| create/edit/test database connection | Database connection Settings | Planned | Write-only secrets, validation and restart/runtime status |
| dotTrace snapshot/timeline/call-tree tools | Local .NET performance snapshot inspection and rendered evidence | Planned | Bounded local artifacts; capture/attach authority separate |
| Unity profiler overview/frame/analyze | Optional Unity/.NET profiling module | Planned | Module availability, bounded artifacts, no ambient context |
| memory dump and mixed/native attach | Post-mortem and advanced debugger modules | Planned | Sensitive artifact/process approval and retention |
| notebook execution | .NET Interactive notebook/cell discovery and bounded execution | Planned | Trusted repository execution; output/artifact limits |
| inspection-KTS/PSI generator tools | Typed Roslyn syntax, symbol, generated-source, and IL inspection | Partial | Closed exact-buffer inspection views are delivered to the editor, roles, and inbound MCP. Analyzer/code-fix authoring examples and fixture generation remain planned |
| engine/editor screenshots | XDG-portal approved visual evidence | Delivered | Portal consent, privacy, goal binding; no input control |
| `execute_tool` router | On-demand typed toolset activation for the next bounded role turn | Delivered | Catalog/role/policy validation; never dynamic invoke-by-name |
| `ue_*` and Unreal-specific asset/Blueprint/actor/viewport tools | No Harness.NET equivalent | Excluded | Explicit product exclusion |

## Always-present bootstrap toolset

Direct tools remain small and role-specific:

- inspect workspace/project readiness and current source context;
- bounded file read, text search, Git inspection and .NET graph inspection;
- bounded semantic repository retrieval and durable evidence listing where eligible;
- on-demand cited versioned documentation, dependency evidence, exact package
  validation, and deterministic package/SBOM previews;
- discover/request relevant IDE toolsets for the next bounded role turn.

Every role has direct exact-file diagnostics, symbol information, definition,
reference, and implementation lookup. Harness.NET loads the file and creates the
short-lived source session. Models do not supply session IDs, hashes, buffer versions,
or duplicate source text. Hierarchy and associated-test discovery use the on-demand
semantic-hierarchy module.

Existing mutation tools remain present only for an approved Implementer task. MCP
tools and future optional modules are on-demand unless the user explicitly chooses a
permitted direct exposure in Settings.

## On-demand toolsets

| Toolset | Representative operations | Default roles |
|---|---|---|
| Workspace exploration | tree/glob/regex/open-document context/dependency source | Lead, Implementer, Reviewer |
| Semantic analysis | diagnostics, symbol info/search, definitions/references/implementations, calls/types, test association | Lead, Implementer, Reviewer |
| Deterministic transformations | format, imports/namespaces, rename, signature, extract, move, safe delete | Implementer; Reviewer preview only |
| Build and test | asynchronous build state, test discovery/filter/run/cancel | Implementer, Reviewer verification |
| Run configurations | launch discovery, typed one-run overrides, process output/stop | Implementer with execution authority |
| Debugger | launch/attach, breakpoints, control, stack/value inspection/evaluation | Implementer with debug authority |
| Database | connection health, schema/object inspection, bounded queries | Role and connection policy dependent |
| Performance | snapshot metadata, timelines, call trees, memory dumps, Unity profiler | Implementer/Reviewer with artifact authority |
| Notebook/analyzer lab | .NET cells, analyzer API/examples, syntax model, fixture validation | Implementer with execution authority |
| Visual verification | request and inspect portal-approved captures | Lead/Implementer/Reviewer under Task 045 policy |
| Documentation and supply chain | versioned lookup, dependency inspection, candidate validation, SBOM/package preview | Lead/Implementer/Reviewer under offline and source policy |

## Delivery rule

Each slice includes catalog entry, role policy, typed implementation, deterministic
fake, integration tests, run status/evidence, and Settings management. A catalog row
may be unavailable. A raw adapter or command string is not a delivered capability.

Delivery ownership is intentionally split:

- Task 047 owns catalog activation, bounded exploration, semantic graphs, result
  identity, and changed-set quality.
- Task 059 exposes the same typed application capabilities through an unauthenticated,
  strictly loopback MCP server for dogfooding and isolated evaluation; it grants no new
  authority.
- Task 049 owns editor-facing formatting, code actions, refactorings, virtual source,
  and inspection views.
- Task 050 owns complete developer Git workflows. Its agent surface remains narrower.
- Task 052 owns Build/Test/Run/Debug lifecycle, project views, and Test Explorer.
- Tasks 053–056 own parallel sessions, review, ACP agents, inline assistance,
  customization, context inspection, and untrusted-content policy.
- Database, profiler, dump, notebook, and advanced analyzer rows remain future
  independent slices. Their presence in this map does not place them in Task 047.
