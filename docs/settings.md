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
| Models & roles | Provider definitions and default role routes | Host XML at startup; per-goal overrides in the SQLite goal-model selection store through `IGoalModelService` | Planned; requires a typed ordinary-default persistence contract rather than editing host configuration from Presentation |
| Privacy & limits | Goal review cycles, remote cap, and per-run output maxima | Immutable goal/workflow Business Logic contracts and SQLite goal state | Planned; ordinary defaults may move here, but goal-bound spend approval remains separate |
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
- Layout maintenance remains available from the command palette, while current
  conversation model selection stays in the header because it directly affects the
  adjacent chat composer. Role defaults and goal-specific model overrides have not
  yet been conflated with that conversation control.

## Next Settings foundation slice

Introduce a typed, persisted application-default contract for agent-role routes and
output maxima at the Business Logic boundary, backed by private Data Access storage.
Project effective defaults separately from progressive, per-goal overrides. The
operation must not copy remote-cap authority into defaults or treat configured
credentials as consent.
