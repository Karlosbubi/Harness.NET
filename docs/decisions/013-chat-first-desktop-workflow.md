# ADR 013: Chat-first desktop workflow and settings ownership

- Status: Accepted
- Date: 2026-07-29
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md), [ADR 010](010-docked-desktop-workbench.md)

## Context

Harness.NET's complete goal workflow is currently distributed across the docked
workbench and as many as fourteen modal dialogs. Goal creation, role routing, output
limits, semantic context, approvals, evidence, and commit handling are technically
available, but their separation makes the user operate the orchestration machinery
instead of collaborating naturally with the agents.

The intended primary interaction is conversation. The user should be able to state
an outcome, clarify it, inspect what the agents propose or did, and exercise authority
at consequential boundaries without navigating a role-control console. Lead,
Implementer, and Reviewer remain important Business Logic concepts, but they are
implementation and audit detail rather than primary navigation.

Harness.NET also needs the interaction quality of a professional Linux editor while
retaining its own visual language. JetBrains, Cursor, and Zed are quality references,
not templates to copy and not a request for feature parity.

## Decision

### Product quality boundary

Target professional quality in the core Harness.NET loop: open a repository, navigate
and edit .NET code, converse with agents, inspect and approve a plan, review validated
changes and Git state, run Build/Test, approve an exact commit, and understand the
manual handoff. Do not delay that loop to reproduce the full breadth of a general IDE.

Use the existing Harness mockup, semantic theme tokens, and honest product state as
the design source. Borrow interaction principles—clear hierarchy, compact density,
keyboard reachability, fast feedback, progressive disclosure, and polished empty and
failure states—without copying another product's layout or branding.

### Conversation as the workflow surface

Conversation becomes the primary goal surface and the default place to start or
continue work. A submitted goal is a conversational turn that creates or selects the
durable goal context. Ordinary clarification, progress, role summaries, and completion
handoff appear in chronological context.

Structured workflow events render as typed inline cards adjacent to the conversation:

- proposed-plan summary with expandable tasks and acceptance criteria;
- capability and remote-cost disclosure;
- current run progress, cancellation, and recovery state;
- validation, Build/Test, reviewer, and diff summaries with document links;
- Restore and exact-commit requests;
- accepted branch and manual push/PR/merge handoff.

Cards project existing Business Logic records and commands; Presentation does not
parse conversational prose to infer workflow state. Detailed plans, diffs, Problems,
run output, and evidence continue to open as first-class documents or tools. Chat is
the narrative and action entry point, not the only place information may exist.

### Human authority without modal churn

Plan, Restore, remote spending, destructive operations, budget extensions, and exact
commit retain their existing durable authorization boundaries. Each requires an
explicit action on the matching typed request. Moving the action inline does not turn
it into conversational consent.

An approval card first shows the consequence, scope, relevant fingerprint or cap,
and links to full evidence. When a second confirmation is required by an accepted
policy—especially exact commit—the confirmation may use a focused sheet or dialog,
but informational and configuration dialogs must not be chained around it. Denial,
cancellation, expiry, stale state, and recovery render on the original card.

### Settings and per-goal overrides

Add one searchable Settings surface with stable categories:

- General and workspace behavior;
- Editor and code intelligence;
- Appearance and accessibility;
- Models and agent-role defaults;
- Privacy, remote routing, and default limits;
- Storage, backup, and recovery;
- Advanced diagnostics and experimental modules.

Lead, Implementer, and Reviewer model routes and default
review-cycle limits move out of the routine goal journey. The shell may show concise
effective-route and cost status, but it does not require users to operate role
selectors for every goal.

Goal-specific overrides remain available through progressive disclosure when a user
needs a different model, privacy route, or remote cap. ADR 014 replaces the
former local-only default: the saved spend-mode preference now authorizes new goals.
The exact
goal-bound authorization remains visible and explicit.

Settings are typed application configuration and private state according to their
existing ownership. Presentation does not implement settings behavior locally merely
because it renders the form.

### Workbench information architecture

Keep the ADR 010 docked document workbench. The center remains the editor/diff/plan
document area. Files/search, Git, Problems, goal/evidence, conversation, and run output
remain movable tools, but default placement gives conversation enough continuous space
to be the primary agent interaction instead of a shallow log strip.
Because Conversation is the primary interaction surface, closing or hiding its dock
must never strand the user: a persistent header action, command-palette entry, and
keyboard shortcut restore the tool and focus its composer.
When the user moves Conversation into a more prominent dock, layout restoration keeps
that exact placement and honors it as the active pane instead of forcing the workspace
overview to the foreground.

The default shell emphasizes workspace identity, command search, active goal/run
state, and the next meaningful action. Provider, theme, role, and layout maintenance
controls move to Settings or the command palette unless they are immediately relevant.
Every important action remains keyboard reachable and discoverable without memorizing
a shortcut.

`GoalDialog` is decomposed while its workflows move into focused cards, documents,
settings pages, and small confirmation surfaces. Shared typed view models reduce
immutable Presentation state; they do not move business decisions out of Business
Logic. Refactoring follows the delivered slices rather than preceding them as an
unbounded rewrite.

### Platform capability ownership

Linux remains the product and acceptance target for the foreseeable future. Platform
dependencies are replaceable at the layer that owns them:

- Presentation capabilities own native windows, folder/save pickers, clipboard,
  notifications, desktop shortcuts, screen geometry, and accessibility integration.
- Data Access capabilities own XDG/path storage, filesystem behavior, Secret Service,
  process execution, and other operating-system services below Business Logic.
- Host selects the Linux implementations through composition.

Do not collect unrelated Linux behavior in one generic platform service, scatter
operating-system checks through feature code, or move UI capabilities into Data
Access. A future platform supplies focused replacements without changing Business
Logic contracts. Portability is preserved through these seams, but only Linux must
pass the current product gate.

### Evaluation

The author's real daily use is the final UX acceptance. Automated checks protect the
known qualities after that judgment: keyboard-only completion of the core loop,
AT-SPI/Orca semantics, wide and compact rendered screenshots, focus restoration,
honest loading/error/recovery states, and recorded latency for common editor and chat
actions. Screenshot assertions do not substitute for hands-on review.

### Stuck-goal recovery amendment (2026-08-08)

A paused `NeedsDirection` run must not reduce recovery to an unexplained Continue
action. Its inline run card offers **Retry** for the exact failed role.
The focused recovery sheet includes a capability-qualified replacement model and
optional bounded user guidance. Empty guidance deliberately means
an unchanged or model-only retry. The chosen goal-role route and retry checkpoint are
persisted before the new call; remote replacement models retain the
existing spend-policy and explicit-confirmation gates. Retry never replays the uncertain
call and cannot target a different or stale role.

Every non-terminal goal also exposes **Abort & start new goal** both before selection
and in its goal timeline. Abort is a confirmed typed command, records the bounded user
reason, terminally closes any active or paused production run without deleting its
tasks, evidence, or worktree, and marks the goal unavailable for continuation.
Presentation then clears that goal context and focuses the ordinary composer. Abort
grants no Git, network, model, or cleanup authority; isolated worktree disposal
remains a separate future lifecycle decision.

## Consequences

- Goal orchestration becomes legible as a conversation with structured evidence rather
  than a sequence of forms.
- Role configurability remains powerful but leaves the everyday interaction path.
- Consequential approvals stay explicit and auditable even when rendered inline.
- The existing dock workbench is retained, but its default proportions and tool
  emphasis will change.
- Linux integration remains first-class without making Business Logic Linux-specific.

## Alternatives considered

- Keeping the modal workflow and only restyling it was rejected because navigation,
  context loss, and repeated configuration are interaction problems rather than color
  or spacing problems.
- Making roles the primary user-facing workflow was rejected because users express
  outcomes and review evidence; orchestration roles are mostly defaults and audit
  detail.
- Hiding approvals inside natural-language chat was rejected because conversational
  agreement is not a durable capability decision.
- Copying one reference IDE was rejected because Harness.NET has a different product
  purpose and an established visual direction.
- Requiring cross-platform acceptance now was rejected because it would dilute the
  Linux-first quality target.
