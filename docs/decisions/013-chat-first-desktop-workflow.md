# ADR 013: Chat-first desktop workflow and Settings ownership

- Status: Accepted
- Date: 2026-07-29
- Amended: 2026-08-08
- Extends: [ADR 009](009-avalonia-presentation-toolkit.md), [ADR 010](010-docked-desktop-workbench.md)

## Context

The goal workflow was split across the workbench and many dialogs. Users had to
operate Lead, Implementer, Reviewer, routing, limits, approvals, and recovery as
separate controls. The primary interaction should be the goal conversation.

## Decision

### Scope

Prioritize this loop: open a repository, navigate and edit .NET code, chat with
agents, inspect and approve a plan, review validated changes and Git state, run
Build/Test, approve an exact commit, and understand the branch handoff.

Rider/Air, Cursor, and Zed are quality references for hierarchy, density, keyboard
access, latency, disclosure, and failure states. Do not copy their layout or branding.

### Conversation

Conversation is the default place to create or continue a goal. Render workflow state
as typed cards, not parsed model prose:

- plan and tasks;
- capability and remote-cost disclosure;
- run progress, cancellation, partial completion, and recovery;
- validation, Build/Test, review, diff, and evidence links;
- Restore and exact-commit requests;
- accepted branch and manual push/PR/merge handoff.

Detailed plans, diffs, Problems, Run output, and evidence remain documents or tools.

### Authority

Plan, Restore, remote spending, destructive operations, budget extension, and exact
commit retain typed durable decisions. Inline placement does not turn natural-language
agreement into approval. Show scope, consequence, fingerprint or cap, and evidence
before the action. Use a focused confirmation only when required by policy.

Denial, cancellation, expiry, stale state, and recovery remain on the originating
card.

### Settings

Use one searchable Settings window for:

- General;
- Editor;
- Appearance and accessibility;
- Model providers;
- MCP connections;
- Agent tools;
- Models and roles;
- Privacy and limits;
- Storage and recovery;
- Advanced diagnostics.

Move routine role routes and defaults out of goal creation. Goal-specific model,
privacy, review-cycle, and spending overrides remain available on demand. Settings
uses typed Business Logic commands and private storage; Presentation does not own the
behavior.

### Workbench

Keep the docked document workbench. Give Conversation enough default space for primary
use. A persistent header action, command, and shortcut restore and focus Conversation
after it is hidden or closed. Persist a user-moved Conversation pane without forcing
another document active on restart.

Keep maintenance controls in Settings or the command palette unless they affect the
current action. All important actions remain visible or keyboard discoverable.

### Platform ownership

Linux is the current product gate.

- Presentation owns native windows, pickers, clipboard, notifications, shortcuts,
  screen geometry, and accessibility.
- Data Access owns XDG paths, filesystem behavior, Secret Service, and process
  execution.
- Host selects platform implementations.

Do not create one unrestricted platform service or put OS checks in Business Logic.

### Stuck goals

A paused `NeedsDirection` card provides Retry for the exact failed role. Retry may
select a compatible model and optional guidance. Empty guidance means unchanged or
model-only retry. Persist the route and retry checkpoint before the call. Do not
replay the uncertain call or retry a stale/different role.

Every non-terminal goal provides confirmed Abort & start new. Abort records the
reason, ends active or paused runs, preserves tasks, evidence, history, and worktree,
removes the goal from continuation, clears selection, and focuses the composer. Abort
does not grant cleanup, Git, network, or model authority.

## Acceptance

Hands-on use decides UX quality. Automated checks cover keyboard completion of the
core loop, AT-SPI and Orca output, wide and compact layout, focus restoration, state
rendering, and common editor/chat latency.

## Consequences

- Conversation becomes the workflow entry point.
- Roles remain configuration and audit concepts.
- Consequential actions remain explicit.
- The workbench remains; its default emphasis changes.
- Platform dependencies remain replaceable without requiring non-Linux acceptance.

## Alternatives considered

- Restyling the dialogs does not fix navigation and context loss.
- Role-first navigation exposes orchestration rather than user goals.
- Approval inferred from chat is not a durable authority record.
- Copying another IDE conflicts with Harness.NET’s scope and design.
- Requiring all platforms now would slow the Linux product target.
