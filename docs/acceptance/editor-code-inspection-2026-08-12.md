# Editor code inspection acceptance — 2026-08-12

## Scope

This record covers Task 049 item 5: exact-context syntax-tree, symbol-detail,
generated-source, and Intermediate Language inspection for developers and models.

## Delivered behavior

- The source editor exposes one Inspect menu with four closed choices: Syntax tree,
  Symbol details, Generated source, and IL.
- Data Access builds every result from the current Roslyn session and exact live
  buffer. Results carry path, buffer version, project version, target framework,
  configuration, assembly identity, and a SHA-256 compilation identity.
- Syntax-tree inspection starts at the enclosing member or accessor and emits bounded
  node/token kinds, spans, token text, and missing-token state.
- Symbol inspection reports fully qualified display, kind, metadata name,
  accessibility, static/implicit state, containing symbol, assembly, type, locations,
  attributes, and bounded documentation XML.
- Generated-source inspection reads at most 20 source-generator documents and 2 MiB
  of text from Roslyn's public generator APIs. It does not create files.
- IL inspection emits the exact compilation to memory, finds the selected method in
  ECMA-335 metadata, and decodes bounded opcodes and operands. It does not execute the
  project, restore packages, or write an assembly.
- The desktop opens each result as a labeled read-only transient document. Layout
  capture excludes its ID and content.
- Lead, Implementer, and Reviewer receive the read-only `inspect_code` tool. The
  default inbound MCP catalog exposes the same contract as
  `harness_code_inspection`; Settings owns its exact allowlist from the first slice.

## Bounds and failure states

Syntax output stops after 4,000 items or 2 MiB. Generated output stops after 20
documents or 2 MiB. IL stops after 64 KiB per method, 16 same-name/arity candidates,
or 2 MiB. A missing symbol, non-method IL caret, compilation error, missing generated
output, cancellation, newer buffer, or metadata mismatch returns a typed non-ready
state. The UI does not present failed output as current.

## Deterministic evidence

- Data Access tests cover syntax, parameter symbol detail, locally emitted method IL,
  generated output, exact origin identity, and no generated file write.
- Business Logic tests cover kind/text/origin mapping and the goal-scoped model result
  before the short-lived Roslyn session closes.
- Agent policy tests prove the read-only tool exists for all roles without widening
  mutation authority.
- Avalonia headless tests prove the result is read-only and excluded from saved Dock
  layout.
- MCP settings tests prove the known/default allowlist contains the new closed tool.
- `dotnet build Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`
  succeeds with zero warnings.
- `./eng/verify-editor-intelligence.py` passes 47 Roslyn adapter, 21 semantic-
  boundary, 42 transformation-authority, 68 editor-control, settings-storage,
  settings-policy, and theme-contract tests.
- `dotnet test Harness.slnx --no-build --no-restore
  -p:UseSharedCompilation=false -m:1` passes all 669 deterministic tests: 6 analyzer,
  1 architecture, 255 Business Logic, 227 Data Access, 22 Host, 134 Avalonia
  presentation, 22 terminal presentation, and 2 Avalonia UI tests.
- `./eng/verify-avalonia-atspi.py` passes against the production Avalonia editor.
- `./eng/verify-linux-x64-publish.sh` publishes and starts the Linux x64 artifact.

## Remaining Task 049 work

- configurable keybindings, conflict management, declarative import/export, and
  optional Vim mode;
- project User Secrets with capture and model-context interlocks;
- a real debugger adapter for Debug CodeLens in Task 052;
- validated full metadata method-body decompilation;
- completion of the broader latency, memory, cancellation, analyzer-failure, IME,
  accessibility, scaling, restoration, and Linux publication matrix.
