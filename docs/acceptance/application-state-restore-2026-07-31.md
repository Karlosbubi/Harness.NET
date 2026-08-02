# Application-state restore acceptance — 2026-07-31

## User contract

Avalonia and the TUI accept an absolute Harness.NET backup ZIP in Operations. They
inspect before confirmation and show archive/database hashes, byte counts, schema,
creation time, and whether workbench layout state is present. Both explain that the
archive contains sensitive private state, excludes credentials and repositories, and
that changes made after staging will be replaced. Staging is explicit and the UI
reports that restart is required. The staged request is bound to the archive SHA-256
the user inspected, so replacing a valid archive at the same path requires a new
inspection and confirmation.

The running process never replaces its database. Host applies a pending restore
before database initialization on the next cold start. It revalidates the private
marker, staged SHA-256 values and sizes, schema compatibility, SQLite
`integrity_check`, and the layout envelope. Existing database/WAL/shared-memory and
layout files are captured in a private rollback directory. Publication uses temporary
siblings; a failure restores the captured files. Successful application retains the
newest three rollback directories and removes the pending request.

## Deterministic evidence

`SqliteApplicationRestoreTests` exercise a real schema-21 SQLite database and prove:

- v2 archive inspection, exact evidence, and optional layout validation;
- staging leaves newer live database and layout state untouched;
- only one restore can be pending;
- cold-start application restores archived data/layout and retains prior rollback;
- v1 archives restore into a fresh target and authoritatively remove absent layout;
- unknown/path-traversal entries and staged tampering fail closed; and
- a forced publication failure restores the pre-restore database.

Business Logic tests prove typed evidence/failure mapping. Avalonia store tests prove
the inspect-then-stage state transition and restart result; the TUI formatter test
proves exact evidence and the absent-layout consequence remain visible. All checks
are local, deterministic, provider-free, and incur no network or paid usage.
