# Settings

Settings owns ordinary defaults. Goal-specific authority remains on the goal.

| Page | State | Owner |
|---|---|---|
| General | Planned; workspace switching remains in Workspace UI. | `IWorkspaceService` and SQLite. |
| Editor | Delivered for inlay hints, CodeLens visibility, and automatic C# formatting. More editor defaults remain planned. | Typed Business Logic service, SQLite preference, Roslyn and Presentation adapters. |
| Appearance & accessibility | Delivered. | `IAppearanceService`, SQLite preference, XDG theme files. |
| Model providers | Delivered. | Typed Business Logic service, private XDG XML, Secret Service. |
| MCP connections | Delivered. | MCP Data Access adapter, Business Logic policy, private XDG XML. |
| Harness control | Delivered. | Inbound MCP Data Access transport, Business Logic policy, private XDG XML/Secret Service, volatile evaluation secrets. |
| Documentation & dependencies | Delivered. | Research and dependency Business Logic policy, private XDG XML/cache, package/documentation adapters. |
| Agent tools | Delivered for Task 047. | Business Logic catalog/policy/evidence and private XDG XML. |
| Visual verification | Delivered. | Typed Business Logic policy, SQLite preference, XDG portal and private state adapters. |
| Models & roles | Delivered. | `IAgentDefaultsService`, role-capability policy, SQLite defaults, goal route store. |
| Privacy & limits | Delivered for default and goal Unlimited/Capped/LocalOnly mode and review cycles. | Business Logic preferences and goal records. |
| Storage & recovery | Delivered. | XDG paths, SQLite, backup, layout, and restore services. |
| Advanced | Planned. | Host configuration and owning adapters. |

Pages without an editable contract show `Planned`; they do not display controls that
cannot save.

## Entry points

`Ctrl+,`, the header button, application navigation, and command palette open the same
window. Search matches page names, summaries, and related terms.

## Appearance

The page lists built-in and valid user themes, reports theme-file failures, and
updates the persisted semantic theme through `IAppearanceService`. Changes apply
immediately.

## Editor

The page persists separate switches for parameter-name hints, inferred-type hints,
reference, implementation, and associated-test CodeLens actions, format-on-paste,
and supported format-on-type triggers. Defaults are on.
Changes apply to open trusted C# editors without restarting Harness.NET.

Roslyn computes hints only for the exact visible live buffer. Results carry the
session, path, baseline, and buffer version and are discarded when stale. CodeLens
discovery is bounded to visible declarations. Reference, implementation, and test
queries run only when the developer selects the corresponding lens. Run and Debug
lenses remain absent unless a typed execution service reports a valid target; Settings
cannot create execution authority.

Use the Transform menu or command palette to format the document, a selection, or only
syntax changed since the persisted baseline. `Ctrl+Alt+L` formats the document,
`Ctrl+Alt+F` formats a selection, and `Ctrl+Alt+O` sorts and groups imports. When enabled,
paste formatting is confined to the pasted lines and on-type formatting runs after
`;`, `}`, or a new line. Each request carries an exact range and typed trigger, is
cancelled when the buffer changes, enters the editor as one undoable change, and
remains unsaved until the developer saves it.

## Model providers

Startup discovers configured catalogs without inference. The page shows provider
availability, configured defaults, compatible model count, pricing readiness, and
discovery failures.

It edits endpoint, chat/embedding models, embedding dimensions, timeouts, and secret
references through typed commands. Changes go to the private XDG override and require
restart. Catalog refresh may validate the active endpoint or a replaced credential.

OpenRouter API keys are masked and write-only. They go directly to Secret Service and
never appear in XML, SQLite, logs, or Settings snapshots.

## MCP connections

Startup discovers enabled endpoints without inference. The page adds, edits, enables,
disables, removes, and refreshes named connections. It shows protocol, eligible and
rejected tool counts, failures, and restart state.

Only tools that explicitly declare read-only and non-destructive behavior reach agent
schemas. The page does not provide arbitrary tool invocation. Catalog, description,
schema, and result limits prevent an endpoint from adding unbounded model context.

`HarnessControl` is a separate loopback-only connection kind for a directed
controller→worker Harness.NET topology. The page owns its stable client ID, write-only
bearer token, Secret Service status, and exact `harness_` tool allowlist. Discovery
requires the worker to identify itself as `Harness.NET`. Only Lead receives eligible
control tools; Implementer and Reviewer retain ordinary read-only MCP tools. The
connection grants no plan, repository, spending, or commit approval, and cyclic
delegation is unsupported until durable depth and cycle tracking exist. Selecting the
kind for a new connection prefills the current inspection and goal-lifecycle tool set;
remove anything the controller should not receive before saving.

## Agent tools

The delivered status page shows built-in and external source, module health, eligible
roles, direct/on-demand state, authority class, operations, unavailable reason, and
configured MCP source count.

Safe optional modules can be exposed directly as a saved preference. Otherwise an
agent discovers and requests an on-demand module for one next bounded role turn. The
grant is recorded as goal evidence and expires after that turn. Settings changes
prompt composition only; they grant no repository, execution, spend, or disclosure
authority.

## Harness control

This page owns the inbound MCP server from its first slice: enablement, Normal or
IsolatedEvaluation mode, loopback endpoint, client and tool allowlists, explicit
approval holds, request/result limits, audit retention, health, active clients,
disconnect, token rotation, and isolated-fixture reset.

The server is disabled by default. “Rotate and copy token once” creates a new random
token, revokes existing clients, and puts the new value on the clipboard only for that
explicit action. Existing tokens cannot be displayed. Configure the client with:

- Streamable HTTP endpoint shown on the page;
- `Authorization: Bearer <copied-token>`;
- a stable `X-Harness-Client` value permitted by the client allowlist.

Tools listed under “Require explicit approval” are absent from discovery. Removing a
tool from that hold and applying Settings is the explicit approval. Build, Test,
capture, plan decisions, UI activation, and evaluation reset continue through their
normal typed policy and identity checks.

The closed goal lifecycle catalog includes draft creation and settings, compatible
model discovery and per-role selection, planning, retry, resume, cancellation, abort,
plan decisions, budget extension, accepted-change preview, commit approval, and commit
decision. Each operation remains separately allowlisted. Planning, retry, and resume
return a background operation ID immediately; clients poll `harness_goals` and may
cancel only that exact operation. Commit approval still targets one accepted run,
branch HEAD, and complete diff fingerprint and never merges the goal branch.

`harness_goals`, `harness_evidence`, `harness_workflow_evidence`, and
`harness_goal_models` require a bounded result count and return continuation tokens.
Goal inspection can target one exact goal. Tool/build evidence and workflow prompt,
recovery, and model evidence are separate so clients request only the context needed.
Model discovery can filter by provider, role, and text before paging, so a full remote
catalog is available without injecting it all into one agent response.

IsolatedEvaluation also requires process startup with
`--mcp-evaluation-root /tmp/<dedicated-directory>`. The process uses separate XDG-like
paths, a separate SQLite database, volatile secrets, and a deterministic disposable
fixture. Harness-owned PNG frames and accessibility actions are available only there.

## Documentation and dependencies

The page configures the fixed lookup chain: exact restored package or SDK docs,
configured local indexes, named closed read-only MCP documentation tools, and HTTPS
web search. Web search runs only when earlier evidence is insufficient. Offline mode
blocks live MCP, web, and package-registry requests. Each result shows source, version,
freshness, confidence, citation, rank, and lookup escalation.

Index roots, MCP `connection/tool` routes, web endpoints, NuGet v3 service indexes,
refresh mode, cache age, retention, result count, and context size persist in private
XDG configuration. The page reports cache size and the last cache failure and can
apply retention immediately.

The same page provides explicit developer operations to inspect existing project,
central, lock, and restored dependency evidence; validate one exact package version;
preview dependency and CycloneDX SBOM changes; preview the current deterministic SBOM;
and export it to an absolute JSON path. Inspection never restores or changes packages.
Preview never exports. Export occurs only from the export control and refuses an
existing destination unless overwrite is checked.

## Visual verification

This page reports Screenshot portal availability and target support. It configures
capture enablement, the 1–16 MiB frame limit, 1–90 day retention, 1–100 captures per
goal, and remote-model disclosure. Remote disclosure is off by default.

For the selected goal, the page requests one interactive frame, lists retained
evidence, shows the exact stored bytes available to agents, and deletes a selected
frame. Every request goes through desktop consent. Harness.NET exposes no background
capture, video, generic desktop API, or input control. Captures use private XDG state
and are excluded from application backups and user repositories.

## Models and roles

The page shows effective Lead, Implementer, and Reviewer routes, including whether a
route comes from shipped configuration or a saved default. Every picker searches the
full discovered catalog and then shows only models that meet that role’s capability
requirements. Invalid saved routes remain visible as errors.

There are no user output-token limits. Token counts are usage evidence. Monetary
spending policy is configured under Privacy & limits and on draft goals.

The header conversation model is separate from role defaults because it controls the
adjacent general chat composer.

## Privacy and limits

The saved remote-spend mode applies to new goals:

- Unlimited: no Harness aggregate monetary ceiling;
- Capped: explicit aggregate USD cap;
- Local only: no remote model calls.

Draft goals may override spend mode, cap, review cycles, and role routes before
planning. The workspace must be trusted. Stale writes fail. These fields become fixed
when planning starts.

Credentials and model routes do not authorize spending by themselves. The goal’s
stored mode is the authority record.

## Chat workflow

Conversation cards render goal, plan, run, task, evidence, Restore, commit, recovery,
and branch handoff records. The composer creates a draft in a trusted workspace
without a provider call. Existing goals appear as Continue choices.

Typed card actions cover plan generation, approval/change, continuation,
cancellation, stuck-role retry, abort, Restore decisions, exact commit decisions, and
recovery. Required confirmations retain call-count, authority, cost, one-use, and
fingerprint checks.

Acceptance evidence is in
[chat-first-workflow-2026-07-29.md](acceptance/chat-first-workflow-2026-07-29.md).
