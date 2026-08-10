# Settings ownership and delivery map

This inventory implements the first Task 040 slice under
[ADR 013](decisions/013-chat-first-desktop-workflow.md). Settings is the searchable
home for ordinary defaults. ADR 014 makes the persisted remote-spend mode an
intentional authorization default for newly created goals; credentials and model
routes alone still grant no authority.

| Category | Existing value or capability | Current owner | Settings state |
|---|---|---|---|
| General | Active workspace and registered workspaces | SQLite workspace store through `IWorkspaceService` | Planned; workspace switching remains in its focused surface |
| Editor | Buffer/editor behavior; future Roslyn features | Presentation for transient buffers; ADR 012 will add typed code-intelligence contracts | Planned |
| Appearance & accessibility | Preferred theme and installed user themes | SQLite appearance preference and XDG theme sources through `IAppearanceService` | Delivered; selection is persisted and applies immediately |
| Model providers | Ollama and OpenRouter catalogs, endpoints, model/embedding defaults, dimensions, timeouts, secret references, access class, pricing readiness, and discovery health | Typed Business Logic service over a private XDG XML override and Linux Secret Service; active provider objects remain host-composed | Delivered; validated configuration edits require restart, API keys are write-only, and catalog discovery performs no inference or authorization |
| MCP connections | Named stateless Streamable HTTP endpoints, enablement, timeouts, protocol/tool discovery, and fail-closed agent eligibility | Official MCP SDK 2.x isolated in Data Access; Business Logic owns read-only policy; private XDG XML owns configuration | Delivered; add/edit/enable/remove and refresh are first-class Settings actions, discovery performs no inference, and active-process changes require restart |
| Agent tools | Built-in and external capability catalog, module health, role eligibility, direct/on-demand exposure and authority class | Business Logic catalog/policy over typed Data Access modules; goal approvals remain separate | Planned with Task 047; the first catalog slice must add the searchable page rather than relying on raw configuration |
| Models & roles | Capability-qualified default role routes and output maxima | Business Logic owns the role-capability matrix; host XML supplies fallbacks; schema 19 stores typed application defaults through `IAgentDefaultsService`; per-goal overrides remain in the goal-model selection store | Delivered; each picker contains only models that fully support its role, and invalid saved defaults are reported |
| Privacy & limits | Default and per-goal remote-spend mode, optional aggregate cap, review cycles, and per-run output maxima | Typed Business Logic preference and goal contracts over private SQLite state | Delivered for unlimited/capped/local-only spend defaults and draft-goal overrides; other ordinary workflow defaults remain planned |
| Storage & recovery | SQLite private state, layout file, verified backup, and staged next-start restore | XDG application paths and typed operations/layout services | Available |
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
- Interactive startup discovers the configured chat catalogs without inference so
  provider status and qualified role choices are populated immediately. The Model
  providers page treats Ollama and OpenRouter as peers. It edits endpoint, default
  chat/embedding models, dimensions, timeouts, and secret references in the private
  XDG override while preserving unrelated settings. OpenRouter API keys are masked,
  write-only, and sent directly to Secret Service; snapshots never contain them.
  Configuration changes clearly require restart, while explicit refresh replaces the
  cached catalog and can validate a credential replaced against the active reference.
- Interactive startup also discovers enabled MCP endpoints through the stateless
  `2026-07-28` flow. The MCP connections page manages named endpoints, timeouts, and
  enablement; reports protocol, eligible/rejected tools, and failures; and exposes no
  arbitrary invocation control. Only explicitly read-only, non-destructive tools are
  namespaced into agent schemas. Catalog, description, and schema bounds keep a server
  from expanding ambient agent context without limit; rejected entries are reflected
  in the page counts.
- Models & roles projects all three effective role routes, distinguishes host
  fallbacks from saved defaults, filters every picker through the Business Logic role
  capability policy, reports unavailable or incompatible saved defaults, and persists
  a 1–10,000,000 token maximum. Providers and individual models may enforce a lower
  runtime ceiling; Harness.NET preserves the configured upper bound and reports a
  provider rejection instead of silently reducing it.
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
review cycles and the persisted remote-spend default. The shipped default is Unlimited;
creation performs no provider call. Existing goals appear as explicit
inline Continue choices. Plan generation selects a compatible Lead model and defaults
to the effective configured Lead route. Approval/change requests,
production continuation, cancellation, correlation-bound Restore decisions, and
exact-diff commit decisions are delivered with their bounded-call, one-use, and exact
fingerprint confirmations. Spend, other destructive operations, and budget-extension
remain explicit goal-bound actions for the next increment.

Draft goal cards expose a focused progressive settings surface for review cycles,
per-goal role/model routes, and prominent Unlimited, Capped, and Local-only spend
choices. The active workspace must be trusted, stale writes fail, and neither the
spend policy nor review cycles can change once planning starts. Monetary policy remains
explicit in the bounded-call sheet rather than becoming ambient authority.

Task 040 production acceptance, including wide/compact captures and the Linux AT-SPI
journey, is recorded in
[`acceptance/chat-first-workflow-2026-07-29.md`](acceptance/chat-first-workflow-2026-07-29.md).
