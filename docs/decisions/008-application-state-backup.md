# ADR 008: Application-state backup and upgrade recovery

- Status: Accepted
- Date: 2026-07-28
- Amends: [ADR 006](006-memory-observability-and-recovery.md)

## Context

SQLite contains private prompts, evidence, approvals, costs, and vector state.
Migrations do not provide a recovery copy for an interrupted upgrade, corruption, or
transfer to a clean installation.

Secrets, logs, model blobs, goal worktrees, and user repositories have different
ownership and recovery boundaries and must not be silently copied into an export.

## Decision

Use a versioned, non-overwriting ZIP archive for deliberate Harness.NET application-
state backup and export. Format `harness-backup-v2` contains:

- one consistent SQLite snapshot created through SQLite's backup API;
- a UTF-8 JSON manifest with format version, schema version, creation time, database
  byte count, and SHA-256;
- the bounded, integrity-checked private workbench-layout envelope when one exists,
  with its archive entry, byte count, and SHA-256 recorded in the manifest;
- no Secret Service values, environment credentials, logs, caches, goal worktrees,
  or user-repository content.

Version-1 archives containing only SQLite and the manifest remain recoverable through
the documented offline process. A corrupt saved layout prevents a version-2 archive
from being published rather than silently producing incomplete recovery state.

Validate the snapshot with `PRAGMA integrity_check` before publishing the archive.
Write to a temporary sibling and atomically rename it to the user-selected destination;
never overwrite an existing archive. Restrict archive permissions to the current user
on Linux.

Before applying any pending embedded migration to an existing database, create the
same verified archive under the XDG data backup directory. Abort the upgrade if that
recovery point cannot be created. Clean-install initialization does not create an
empty backup.

Manual recovery is offline: extract and hash-verify the database into a fresh
XDG data root and the optional layout into a fresh XDG state root while Harness.NET
is stopped, then start the current binary so additive migrations run normally and
Presentation independently validates machine-specific layout state. Release
acceptance automates this recovery path. An online
restore command is intentionally excluded because replacing an active database would
weaken process and approval safety.

### Staged in-app recovery amendment (2026-07-31)

Avalonia and the TUI may stage recovery, but the running application never replaces
its live database. Operations verifies a v1/v2 archive, shows schema, creation time,
hashes, and sensitivity, then requires confirmation. Data Access extracts verified
content to a bounded private staging directory and atomically records one request.
The request includes the confirmed archive SHA-256. Staging fails if the file changes.

On the next start, before SQLite initialization, the Host runs the Data Access restore
bootstrapper. It
revalidates the marker, staged hashes, schema, layout envelope, and SQLite integrity;
copies any existing database, WAL, shared-memory file, and layout into a private
rollback directory; publishes the staged
database and optional layout through temporary siblings; and restores the rollback if
publication fails. Only then may normal initialization and additive migrations run.
Successful publication clears the marker but retains the bounded rollback directory
for manual recovery. A missing optional layout explicitly removes the live layout so
the restored archive is authoritative.

Staging refuses unknown entries, path traversal, unsupported formats, hash/size/schema
mismatches, corrupt SQLite, oversized content, an existing pending request, and a
source archive inside Harness.NET restore staging. Presentation tells the user
that work performed after staging will be replaced and that a restart is required.
Online replacement remains prohibited.

## Consequences

- Every real schema upgrade has a local recovery point before mutation.
- Deliberate recovery retains a valid desktop layout without treating monitor bounds
  or Dock structure as trusted input.
- Deliberate exports are portable and independently verifiable but contain sensitive
  model and workflow content; Presentation must warn the user before creation.
- Repository branches/worktrees and provider credentials retain their existing owners
  and backup mechanisms.
- Backup creation requires additional disk space and can briefly delay startup before
  an upgrade.
- Restore initiation is available in-app while restore publication remains an offline
  startup boundary. Existing installs retain a private pre-restore rollback copy.

## Alternatives considered

- Copying only the main database file was rejected because WAL state can make a raw
  copy inconsistent.
- Exporting logs, credentials, caches, or repositories was rejected because it would
  cross ownership and privacy boundaries.
- Replacing the live database from the running TUI was rejected because open
  connections and partial in-memory state make online restore unsafe.
