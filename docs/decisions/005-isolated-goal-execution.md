# ADR 005: Isolated and approved goal execution

- Status: Accepted
- Date: 2026-07-26

## Context

Agents need enough autonomy to complete useful .NET work while protecting the user's
current worktree, limiting capabilities, and preserving explicit human authority.

## Decision

Require a trusted Git-backed .NET workspace. The lead proposes a plan before any
mutation. Approval creates a dedicated goal branch and worktree and grants typed,
repository-local inspection, edit, build, and test capabilities.

Network use, restore/package changes, destructive actions, budget extensions, and
commits remain approval-gated. No unrestricted shell is exposed. LibGit2Sharp handles
supported Git operations; a structured Git CLI adapter handles worktrees and other
required gaps.

The implementer produces changes and evidence, then an independent reviewer approves
or returns findings. The loop pauses at the goal's configured review-cycle limit.
Accepted work is committed to the goal branch only after approval and is never merged
automatically. Commit approval is a separate durable decision over the exact goal,
workflow run, isolated branch, expected HEAD, complete diff SHA-256, commit message,
and author identity. The commit adapter revalidates that fingerprint immediately
before writing the commit and can reconcile the same commit after interruption.

## Consequences

- User changes in the original worktree remain isolated.
- Builds and tests execute repository code only after one-time workspace trust.
- Tool APIs must canonicalize paths, enforce worktree scope, and accept cancellation.
- Worktree and uncertain-operation recovery become required lifecycle behavior.

## Alternatives considered

- Direct current-worktree edits were rejected because they weaken isolation.
- An allowlisted or unrestricted shell was rejected because typed capabilities are
  easier to understand and enforce.
- Automatic merging was rejected to keep integration under user control.
