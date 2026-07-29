# Settings ownership and delivery map

This inventory implements the first Task 040 slice under
[ADR 013](decisions/013-chat-first-desktop-workflow.md). Settings is the searchable
home for ordinary defaults; it is not an authorization surface. In particular, a
stored provider, credential, route, output limit, or budget default never authorizes
remote spending or another consequential operation.

| Category | Existing value or capability | Current owner | Settings state |
|---|---|---|---|
| General | Active workspace and registered workspaces | SQLite workspace store through `IWorkspaceService` | Planned; workspace switching remains in its focused surface |
| Editor | Buffer/editor behavior; future Roslyn features | Presentation for transient buffers; ADR 012 will add typed code-intelligence contracts | Planned |
| Appearance & accessibility | Preferred theme and installed user themes | SQLite appearance preference and XDG theme sources through `IAppearanceService` | Delivered; selection is persisted and applies immediately |
| Models & roles | Provider definitions, default role routes, and output maxima | Host XML supplies fallbacks; schema 19 stores typed application defaults through `IAgentDefaultsService`; per-goal overrides remain in the goal-model selection store | Delivered; model discovery and validated updates do not grant remote authority |
| Privacy & limits | Goal review cycles, remote cap, and per-run output maxima | Immutable goal/workflow Business Logic contracts and SQLite goal state | Partially delivered; draft goal cards progressively reveal typed review-cycle and explicit remote-cap overrides, while ordinary application defaults remain planned |
| Storage & recovery | SQLite private state, layout file, backup operation, and future restore | XDG application paths and typed operations/layout services | Planned |
| Advanced | OTLP endpoint, semantic-index module configuration, diagnostics | Host configuration and owning Business Logic/Data Access modules | Planned |

## Delivered shell behavior

- `Ctrl+,`, the application navigation, the header settings icon, and the command
  palette open the same Settings window.
- Search matches stable category names, summaries, and related terms.
- Categories that have no editable contract yet say **Planned** and render an honest
  unavailable explanation; they do not present controls that cannot persist.
- Theme maintenance moved out of the global header. The Appearance page reads and
  updates the typed `AppearanceSnapshot`/`IAppearanceService` boundary, including
  validation issues from user theme discovery.
- Models & roles projects all three effective role routes, distinguishes host
  fallbacks from saved defaults, discovers configured chat models on demand, validates
  provider/model membership in Business Logic, and persists a 1–8192 token maximum.
  Existing workflow limit prompts start from these defaults while remaining explicit
  progressive overrides during the chat-workflow migration.
- Layout maintenance remains available from the command palette, while current
  conversation model selection stays in the header because it directly affects the
  adjacent chat composer. Role defaults and goal-specific model overrides have not
  yet been conflated with that conversation control.

## Chat-first workflow progress

Existing plan, workflow, evidence, Restore, commit, and handoff records project as
immutable read-only conversation cards. With no selected goal, the composer creates a
private draft directly from the objective only after workspace trust, using three
review cycles and no remote budget or provider call. Existing goals appear as explicit
inline Continue choices. Plan generation, approval/change requests,
production continuation, cancellation, correlation-bound Restore decisions, and
exact-diff commit decisions are delivered with their bounded-call, one-use, and exact
fingerprint confirmations. Spend, other destructive operations, and budget-extension
remain explicit goal-bound actions for the next increment.

Draft goal cards expose a focused progressive settings surface for review cycles,
per-goal role/model routes, and an exact remote USD cap. Saving the cap is itself the
visible goal-bound authorization: the active workspace must be trusted, stale writes
fail, it is disabled by default, and neither the cap nor review cycles can change once
planning starts. Run-specific output ceilings remain explicit in the bounded-call
sheet rather than becoming ambient authority.
