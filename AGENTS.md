# Agent Guidelines

## Before changing the repository

1. Read `README.md`, `docs/framework.md`, and the relevant accepted decision records.
2. Treat the accepted framework and decision records as settled architecture.
3. Record any proposed exception or architectural change before implementing it.

## Working agreement

- Keep changes narrowly scoped and keep documentation synchronized with behavior.
- Prefer idiomatic .NET, immutable data, explicit boundaries, and structured APIs.
- Default to semantic types in new and changed code: enums for closed sets and
  immutable single-value records for identifiers, paths, hashes, units, limits,
  validated values, and otherwise ambiguous primitives. Compose those values into
  immutable record contracts. Keep a primitive only when it has no distinct domain
  meaning, and make that exception evident at the boundary.
- Prefer `DataAccess -> BusinessLogic -> Presentation` layering where sensible.
  Only interfaces, records, and enums may cross those boundaries, and data/contracts
  flow upward except where dependency injection requires reverse-boundary composition.
- Deliver new behavior as end-to-end feature slices.
- When behavior is configurable, deliver its typed Settings ownership, management UI,
  validation, persistence, status, and documentation in the same slice; raw
  configuration keys alone are not complete.
- Keep presentation modular and free of business logic. The first adapter is a TUI;
  supported future surfaces are Avalonia applications and APIs such as gRPC, not web UI.
- Enable nullable analysis and keep compiler warnings at zero.
- Verify every code change with the narrowest relevant test and at least a build.
- Keep tests fiscally conservative: use deterministic fakes by default, never treat
  a configured provider key as authorization to spend, and require explicit user
  approval for the smallest practical bounded live or paid-provider check.
- Use typed workspace tools. Do not introduce an unrestricted agent shell.
- Keep provider SDK types inside Data Access and Microsoft Agent Framework types
  behind Business Logic interfaces, records, and enums.
- Use `Microsoft.Extensions.Logging.ILogger` at DI boundaries; Serilog is the
  configured implementation.
- Never commit credentials, machine-specific paths, model blobs, or conversation
  content that has not been deliberately persisted by the user.
- Update or add a decision record when a choice constrains future architecture.
- Finish completed repository work by committing it, pushing its feature branch, and
  opening a pull request. Never develop or push changes directly on the default branch;
  create a dedicated branch first so `main` can remain protected. Open pull requests as
  drafts unless the user explicitly requests ready-for-review publication.

Harness.NET must not add custom metadata directories to user repositories. Prefer
existing `AGENTS.md` and documentation; keep private state in Harness.NET storage.
