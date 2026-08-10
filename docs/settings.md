# Settings

Settings owns ordinary defaults. Goal-specific authority remains on the goal.

| Page | State | Owner |
|---|---|---|
| General | Planned; workspace switching remains in Workspace UI. | `IWorkspaceService` and SQLite. |
| Editor | Planned; transient editor behavior remains in Presentation. | Presentation and code-intelligence services. |
| Appearance & accessibility | Delivered. | `IAppearanceService`, SQLite preference, XDG theme files. |
| Model providers | Delivered. | Typed Business Logic service, private XDG XML, Secret Service. |
| MCP connections | Delivered. | MCP Data Access adapter, Business Logic policy, private XDG XML. |
| Agent tools | Partial. Catalog status is delivered; optional exposure persistence and activation remain. | Business Logic catalog and policy. |
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

## Agent tools

The delivered status page shows built-in and external source, module health, eligible
roles, direct/on-demand state, authority class, operations, unavailable reason, and
configured MCP source count.

Optional module enablement, safe exposure preferences, on-demand activation, and run
evidence remain Task 047 work. Settings never grants goal authority.

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
