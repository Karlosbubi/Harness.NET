# Model-accessible IDE capability map

This is the implementation map for [ADR 016](decisions/016-model-accessible-ide-capabilities.md).
It was checked against the live JetBrains Rider MCP Server 2026.2 catalog on
2026-08-10 and against the published
[Rider MCP tool documentation](https://www.jetbrains.com/help/rider/mcp-server.html).
Rider is a breadth and UX reference; Harness.NET owns its contracts and authority.

Status meanings:

- **Delivered:** a role already receives the typed capability.
- **Internal:** the IDE or Business Logic has the behavior, but models do not yet
  receive an equivalent typed tool.
- **Partial:** a narrower typed behavior exists.
- **Planned:** no end-to-end model-accessible slice exists yet.
- **Excluded:** deliberately outside the product contract.

## Parity map

| Rider-inspired capability | Harness.NET target | Status | Required boundary |
|---|---|---|---|
| `read_file` | Bounded ranged/structural reads with line coordinates, baseline hash and dependency-source support | Partial | Trusted source context; original/worktree role scope |
| `list_directory_tree`, `search_file` | Bounded tree, glob and filename search respecting Git/exclusion policy | Internal/Planned | No shell; confined normalized paths |
| `search_text`, `search_regex` | Literal and regex search with coordinates, masks and result limits | Partial | Bounded tracked/project text |
| `search_symbol`, `skill_search` | Roslyn symbol search plus source-neutral capability search | Internal/Planned | Exact semantic context; external symbols opt-in |
| `get_all_open_file_paths`, `open_file_in_editor` | Read active/open document context and request developer-visible navigation | Internal/Planned | Presentation performs navigation; no model desktop control |
| `create_new_file`, `apply_patch` | Exact-baseline create/patch in delegated goal areas with Roslyn preflight | Partial | Approved worktree, atomic mutation, evidence |
| `get_solution_projects`, `get_project_dependencies` | Evaluated solution/project graph, exact project/package references and resolved versions | Partial | Trusted evaluation; no implicit restore |
| `get_project_problems`, `get_file_problems`, `lint_files` | Versioned file/project/changed-set diagnostics with stable identities and delta | Partial | Models now receive exact-file Roslyn diagnostics; project/changed-set and lint scopes remain |
| `get_symbol_info` | Quick info, declaration, documentation, type and source/metadata destination | Delivered | Role-scoped source is loaded at the current exact file baseline |
| definition/reference navigation | Roslyn definition and bounded reference destinations | Delivered | Original workspace for Lead; approved goal worktree for Implementer/Reviewer |
| `analyze_calls`, class hierarchy tools | Incoming/outgoing call, type, implementation and override hierarchy | Planned | Roslyn semantic identity, paging and depth bounds |
| `findTests` | Discover tests associated with symbol/project and exact runnable test cases | Planned | Deterministic test adapters; no model guess |
| `post_edit_quality_check` | One changed-set gate combining diagnostics, formatting, tests/build evidence and unresolved findings | Partial | Reuse evidence; never self-certify model output |
| `reformat_file` | Preview/apply repository code style and format changed files | Planned | Deterministic formatter, exact baseline, post-check |
| `rename_refactoring` | Fingerprinted Roslyn rename preview/apply | Delivered | Approved worktree and delegated file areas |
| signature/extract/move/namespace/safe-delete refactors | Closed preview/fingerprint/apply operations for each transformation | Planned | No arbitrary code-action executor |
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
| create/edit/test database connection | First-class database Settings management | Planned | Write-only secrets, validation and restart/runtime status |
| dotTrace snapshot/timeline/call-tree tools | Local .NET performance snapshot inspection and rendered evidence | Planned | Bounded local artifacts; capture/attach authority separate |
| Unity profiler overview/frame/analyze | Optional Unity/.NET profiling module | Planned | Module availability, bounded artifacts, no ambient context |
| memory dump and mixed/native attach | Post-mortem and advanced debugger modules | Planned | Sensitive artifact/process approval and retention |
| notebook execution | .NET Interactive notebook/cell discovery and bounded execution | Planned | Trusted repository execution; output/artifact limits |
| inspection-KTS/PSI generator tools | Typed Roslyn analyzer/code-fix authoring examples, syntax trees and validation fixtures | Planned | Local docs plus deterministic compile/test harness |
| engine/editor screenshots | XDG-portal approved visual evidence from Task 045 | Planned | Portal consent, privacy, goal binding; no input control |
| `execute_tool` router | On-demand typed toolset activation for the next bounded role turn | Planned | Catalog/role/policy validation; never dynamic invoke-by-name |
| `ue_*` and Unreal-specific asset/Blueprint/actor/viewport tools | No Harness.NET equivalent | Excluded | Explicit product exclusion |

## Always-present bootstrap toolset

Keep this set small and role-adjusted:

- inspect workspace/project readiness and current source context;
- bounded file read, text search, Git inspection and .NET graph inspection;
- bounded semantic repository retrieval and durable evidence listing where eligible;
- discover/request relevant IDE toolsets for the next bounded role turn.

The first shared Roslyn slice keeps exact-file diagnostics, symbol information,
definition, and reference lookup direct for every role. Harness.NET loads the current
file and creates the short-lived exact-context session itself; models do not provide
session IDs, baseline hashes, buffer versions, or duplicated source text. Broader
hierarchy and test-discovery schemas remain on-demand work.

Existing mutation tools remain present only for an approved Implementer task. MCP
tools and future optional modules are on-demand unless the user explicitly chooses a
permitted direct exposure in Settings.

## On-demand toolsets

| Toolset | Representative operations | Default roles |
|---|---|---|
| Workspace exploration | tree/glob/regex/open-document context/dependency source | Lead, Implementer, Reviewer |
| Semantic analysis | diagnostics, symbol info/search, definitions/references, calls/types, test association | Lead, Implementer, Reviewer |
| Deterministic transformations | format, imports/namespaces, rename, signature, extract, move, safe delete | Implementer; Reviewer preview only |
| Build and test | asynchronous build state, test discovery/filter/run/cancel | Implementer, Reviewer verification |
| Run configurations | launch discovery, typed one-run overrides, process output/stop | Implementer with execution authority |
| Debugger | launch/attach, breakpoints, control, stack/value inspection/evaluation | Implementer with debug authority |
| Database | connection health, schema/object inspection, bounded queries | Role and connection policy dependent |
| Performance | snapshot metadata, timelines, call trees, memory dumps, Unity profiler | Implementer/Reviewer with artifact authority |
| Notebook/analyzer lab | .NET cells, analyzer API/examples, syntax model, fixture validation | Implementer with execution authority |
| Visual verification | request and inspect portal-approved captures | Implementer/Reviewer under Task 045 policy |

## Delivery rule

Every slice adds its catalog entry, role policy, typed implementation, deterministic
fake, focused integration tests, evidence/status projection and **Settings → Agent
tools** management at the same time. A catalog row may say unavailable, but it may not
pretend a raw adapter or command string is a delivered IDE capability.
