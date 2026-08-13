# Editor virtual navigation acceptance — 2026-08-12

## Scope

This record covers the original Task 049 navigation to regions, source-generator
output, and metadata signatures. File search and workspace-symbol search already cover files,
types, and source symbols.

## Delivered behavior

- `#region` directives appear in the document outline and folding model. They do not
  appear in the namespace/type/member breadcrumb chain.
- Definition, usage, and implementation results distinguish repository source,
  generated source, and metadata.
- A generated or metadata destination carries an opaque handle bound to the active
  Roslyn session, source path, buffer version, and exact source-text hash.
- Generated documents are read from Roslyn's public source-generator APIs. The exact
  generated span is selected.
- Metadata documents contain locally generated public/protected signatures. They are
  explicitly labeled as signatures and do not claim to decompile method bodies.
- The desktop opens virtual C# documents as labeled read-only tabs. Their header shows
  project version, target framework, configuration, assembly, and compilation
  identity.
- Virtual content is bounded to 2 MiB, cached only in the current session, excluded
  from layout persistence, and never written to the repository.
- Lead, Implementer, Reviewer, and inbound MCP navigation use the same Business Logic
  contract. Successful virtual documents are resolved before the short-lived Roslyn
  session closes, so model results do not contain dead handles.
- Changed buffers invalidate existing handles.

## Architecture and safety

The implementation uses public Roslyn APIs only. Roslyn's internal metadata-as-source
service is not called. Full method-body decompilation was delivered later after a
maintained public dependency passed license, provenance, package, SBOM, publication,
and behavior review. This is recorded in ADR 012.

No Roslyn type crosses Data Access. Business Logic exposes immutable semantic
contracts. Presentation owns the read-only editor and Dock document. Layout capture
already drops all transient source and virtual documents.

## Deterministic evidence

- Data Access: generated output is exact, metadata signatures carry exact origin,
  and stale buffers reject handles.
- Business Logic: IDs, origin values, read-only state, text, and ranges map without
  leaking Roslyn types; goal navigation resolves virtual text before session close.
- Avalonia headless: F12 opens a labeled read-only metadata tab, displays compilation
  identity, and layout persistence contains neither its ID nor content.
- `./eng/verify-editor-intelligence.py` builds with zero warnings and exercises the
  complete Roslyn adapter, semantic boundary, and editor-control suites.
- `dotnet test Harness.slnx --no-build --no-restore
  -p:UseSharedCompilation=false -m:1` passes all 664 deterministic tests: 6 analyzer,
  1 architecture, 253 Business Logic, 225 Data Access, 22 Host, 133 Avalonia
  presentation, 22 terminal presentation, and 2 Avalonia UI tests.

## Subsequent closure

- validated method-body decompilation is recorded in
  [editor-decompilation-2026-08-13.md](editor-decompilation-2026-08-13.md);
- exact-context syntax-tree, symbol-detail, and IL inspection views;
- configurable keybindings and optional Vim behavior;
- project User Secrets;
- the broader performance, cancellation, analyzer-failure, IME, accessibility,
  scaling, restoration, and Linux publication matrix in Task 049 criterion 12.
