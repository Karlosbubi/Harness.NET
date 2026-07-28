# ADR 008: Application-state backup and upgrade recovery

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 006](006-memory-observability-and-recovery.md)

## Context

Harness.NET persists private prompts, workflow evidence, approvals, costs, and vector
state in SQLite. Additive migrations protect compatibility but do not by themselves
provide a recovery point if an upgrade is interrupted, a database is corrupted, or a
user needs to move deliberate application state to a clean installation.

Secrets, logs, model blobs, goal worktrees, and user repositories have different
ownership and recovery boundaries and must not be silently copied into an export.

## Decision

Use a versioned, non-overwriting ZIP archive for deliberate Harness.NET application-
state backup and export. The archive contains:

- one consistent SQLite snapshot created through SQLite's backup API;
- a UTF-8 JSON manifest with format version, schema version, creation time, database
  byte count, and SHA-256;
- no Secret Service values, environment credentials, logs, caches, goal worktrees,
  or user-repository content.

Validate the snapshot with `PRAGMA integrity_check` before publishing the archive.
Write to a temporary sibling and atomically rename it to the user-selected destination;
never overwrite an existing archive. Restrict archive permissions to the current user
on Linux.

Before applying any pending embedded migration to an existing database, create the
same verified archive under the XDG data backup directory. Abort the upgrade if that
recovery point cannot be created. Clean-install initialization does not create an
empty backup.

Recovery is an offline operation: extract and hash-verify the database into a fresh
XDG data root while Harness.NET is stopped, then start the current binary so additive
migrations run normally. Release acceptance automates this recovery path. An online
restore command is intentionally excluded because replacing an active database would
weaken process and approval safety.

## Consequences

- Every real schema upgrade has a local recovery point before mutation.
- Deliberate exports are portable and independently verifiable but contain sensitive
  model and workflow content; Presentation must warn the user before creation.
- Repository branches/worktrees and provider credentials retain their existing owners
  and backup mechanisms.
- Backup creation requires additional disk space and can briefly delay startup before
  an upgrade.

## Alternatives considered

- Copying only the main database file was rejected because WAL state can make a raw
  copy inconsistent.
- Exporting logs, credentials, caches, or repositories was rejected because it would
  cross ownership and privacy boundaries.
- Replacing the live database from the running TUI was rejected because open
  connections and partial in-memory state make online restore unsafe.
