# ADR 022: Project User Secrets management

- Status: Accepted
- Date: 2026-08-13
- Extends: [ADR 004](004-framework-and-storage.md), [ADR 008](008-application-state-backup.md), [ADR 017](017-portal-visual-verification.md)

## Context

Developers need to inspect and change development secrets without leaving the editor.
These values are not repository content, Harness.NET application settings, provider
credentials, or agent context. A generic file editor or shell command would make the
authority too broad. Keeping a revealed value on a shared presentation model would
also make accidental logging, indexing, evidence capture, or model disclosure likely.

The .NET Secret Manager associates a project with an unconditional literal
`UserSecretsId` and stores development-only values in the standard per-user
`secrets.json` location. The store is not encrypted and is outside the repository.
Its path and flattened JSON representation are implementation details, so the adapter
needs compatibility tests and a replaceable platform boundary.

## Decision

Add a developer-only Project User Secrets service. It accepts only an active, trusted
workspace and a project returned by Harness.NET's bounded project inspection. A
project is manageable only when its project file contains one unambiguous,
unconditional, literal `UserSecretsId`. Harness.NET does not evaluate repository
MSBuild targets, synthesize an identifier, or mutate a project to initialize one.

Data Access owns project metadata parsing, standard user-profile path resolution,
bounded JSON parsing, and atomic file replacement. The first path resolver supports
Linux and Windows and is replaceable without changing Business Logic. Reads accept
nested string objects and flattened keys. Writes use the flattened representation
produced by `dotnet user-secrets` after mutation. Unsupported values, duplicate
flattened keys, symlinks, traversal, oversized input, and concurrent replacement fail
closed. Linux directories and files are restricted to the current user.

Business Logic exposes distinct list, reveal, copy, add, change, and delete operations.
List results contain keys and project status only. Secret-bearing semantic values
redact their string representation. Reveal returns a disposable disclosure lease;
Presentation keeps the value in the dialog control only and disposes the lease on
hide, selection change, or close. Copy obtains one transient value for the desktop
clipboard. Values never enter the application state store.

A singleton privacy guard makes visual capture and on-screen disclosure mutually
exclusive. A capture holds a capture lease from before portal invocation until the
result is stored. Reveal fails while a capture is active, and capture fails with a
typed policy result while a value is revealed. This closes the race where either
operation starts while the other is in progress.

Project secret values are excluded from logs, evidence, application backup, search,
semantic indexes, model prompts, agent tools, and inbound MCP. No generic agent read
operation is added. The store remains outside Harness.NET backup ownership.

## Consequences

- Developers can manage standard .NET development secrets from a masked, explicit UI.
- Agent and developer code-intelligence surfaces do not gain secret-read authority.
- Projects with imported, conditional, or computed identifiers show a precise
  unsupported status and remain unchanged.
- A future .NET storage-layout change requires a Data Access compatibility update,
  not a Business Logic or Presentation redesign.
- Secret Manager is a development convenience, not a production secret vault.

## Alternatives considered

- Calling `dotnet user-secrets` was rejected because project discovery evaluates
  MSBuild and a typed editor action must not execute repository build logic.
- Using Secret Service was rejected because applications using User Secrets would not
  read those values.
- Editing `secrets.json` as a normal document was rejected because it would expose
  values to search, indexing, persistence, capture, and agent context.
- Automatically inserting `UserSecretsId` was rejected because it is a repository
  mutation and belongs in the normal preview and approval path.
