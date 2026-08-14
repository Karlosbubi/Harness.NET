# ADR 024: Developer Git workbench authority and state

- Status: Accepted
- Date: 2026-08-13
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 010](010-docked-desktop-workbench.md), [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 022](022-project-user-secrets.md)

## Context

Harness.NET shows repository status and a bounded diff, and it has a separate exact
approval flow for committing an accepted goal. Daily developer work still needs
staging, ordinary commits, branches, worktrees, stash, history, conflict resolution,
and remote synchronization. Treating those operations as strings or reusing goal
approval would blur source context, destructive authority, network access, and
credential ownership.

## Decision

### Ownership and state

Data Access owns LibGit2Sharp and the structured Git CLI adapter, repository/index
access, credential callbacks, process transport, and Git result mapping. Business
Logic owns the active source-context resolution, trust checks, operation contracts,
stale-state policy, confirmations, remote/destructive authority, and developer-facing
results. Presentation renders those contracts and never constructs a Git command.

One typed repository state supplies the Files, editor gutter, diff, review, Git
workbench, and eligible model inspection surfaces. It identifies the workspace and
optional approved goal worktree, branch or detached state, HEAD, index tree,
repository-state kind, upstream/divergence, staged and unstaged state per path, and a
complete state fingerprint. Display collections and patches are bounded and paged;
the fingerprint is computed over the complete relevant index and working state.

Every index or working-tree mutation carries the state fingerprint observed by the
developer. Data Access reopens the exact repository, recomputes the fingerprint, and
rejects stale requests before mutation. Multi-path and hunk operations are atomic
where Git supports an atomic index update; otherwise Harness stages a lock-backed
candidate index and publishes it only after every requested change validates. Results
return a new fingerprint and affected paths.

### Source contexts and goal commits

Inspection follows the active original workspace or approved goal worktree. Developer
staging and conflict editing may target either explicit context. Ordinary developer
commit, amend, branch switching, stash, destructive cleanup, and remote operations
target the trusted original workspace unless a later explicit goal-worktree action
states otherwise.

The accepted-goal commit flow remains unchanged and separate. Its durable approval
continues to bind goal, run, branch, expected HEAD, full diff hash, message, author,
and decision. A developer Git action never creates, satisfies, extends, or substitutes
for that approval. Any intervening index, worktree, HEAD, or branch change makes the
goal commit revalidation fail normally.

### Destructive and conflict operations

Discard, clean, reset-like actions, branch or tag deletion, stash drop, checkout over
changes, and conflict-result replacement use a preview/confirm/apply contract. The
preview records exact targets, fingerprint, data-loss class, recovery route, and
whether Git retains an object or reflog entry. Apply requires the preview identity and
unchanged fingerprint. Harness does not silently resolve conflicts, delete untracked
content, or claim recovery when none is available.

A three-way conflict document binds base, ours, theirs, result, index stages, path,
and fingerprint. Saving the result does not mark it resolved until the developer
explicitly stages that exact saved result. Compiler diagnostics remain advisory for
manual editing.

### Remote operations and credentials

Fetch, pull, and push are explicit developer network actions. A request names the
remote, refspecs, source and destination refs, expected local/remote observations,
fast-forward or force policy, and cancellation identity. Pull separates fetch from
the chosen merge or rebase integration and previews divergence before integration.
Force-with-lease is the strongest supported force mode; unconditional force is not a
default and requires a later explicit decision.

Credentials remain in configured Git helpers, SSH agents, or Secret Service-backed
typed credential adapters. Values never enter settings XML, SQLite, logs, prompts,
workflow evidence, backups, screenshots, command arguments, or error text. Remote
URLs are sanitized before persistence or display. Network enablement grants no goal
or agent authority.

### Process and model boundary

LibGit2Sharp is preferred for supported in-process operations. A structured Git CLI
adapter may cover required gaps only through closed executable and argument records,
sanitized environment, bounded output, cancellation, and process-tree cleanup. There
is no shell string or generic Git subcommand endpoint.

Developer Git operations are not model tools. A future model-visible operation needs
its own role, phase, source-context, exact-state, and authority policy under ADR 016.
Read-only inspection may remain model-visible with bounded results.

## Consequences

- All Git views and mutations can reject stale state consistently.
- Developer workflows cannot accidentally approve or commit an accepted agent goal.
- Destructive and remote actions require more UI steps because their exact target and
  consequences stay visible.
- Credentials and remote transport stay below Business Logic and outside durable
  application evidence.
- Complete Task 050 delivery requires deterministic repository fixtures, cancellation
  and failure tests, accessible desktop controls, large-history paging, and Linux x64
  publish verification.

## Alternatives considered

- Reusing goal commit approval was rejected because ordinary developer work has no
  accepted goal/run/diff decision.
- Passing Git command strings was rejected because it creates a shell-equivalent
  authority surface.
- Optimistic UI mutation without a full state fingerprint was rejected because index
  and working-tree changes can race external Git clients.
- Embedding credentials in remote URLs or process arguments was rejected because they
  leak through configuration, logs, process inspection, and screenshots.
