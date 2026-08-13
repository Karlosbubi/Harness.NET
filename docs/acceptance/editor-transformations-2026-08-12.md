# Editor transformations acceptance — 2026-08-12

Task 049 now has one shared closed transformation path for document, selection,
changed-span, paste, and supported on-type formatting, import organization,
unused-import cleanup, missing-type import fixes, and a closed local and bounded
cross-document quick-fix/refactoring catalog.

## Behavior

- Data Access runs Roslyn against the exact session, path, persisted baseline, live
  text, buffer version, optional exact range, and typed trigger. It returns complete
  original and candidate text, replacement count, diagnostic delta, and a SHA-256
  fingerprint. It does not write.
- Business Logic rejects malformed, stale, unbounded, or conflicting adapter results.
- Changed-span formatting compares the current and persisted Roslyn syntax trees. It
  formats affected lines without treating an independently supplied live buffer as a
  whole-document replacement.
- Paste formatting carries the exact inserted range. On-type formatting accepts only
  `;`, `}`, and new-line triggers. Both are settings-managed, cancellation-aware, and
  leave the file unsaved.
- Unused imports come from Roslyn's `CS8019`/`IDE0005` diagnostics. A directive with
  attached comments or directives is kept rather than risking trivia loss.
- Missing-import discovery starts at one unresolved type at the exact caret, searches
  source, project references, and metadata, inserts each candidate namespace in
  memory, and returns it only when Roslyn binds the type to that namespace.
- The editor exposes a Transform menu, a Quick fix action, command-palette entries,
  and shortcuts. It
  applies the candidate to the live buffer as one undoable replacement, preserves the
  caret where possible, and leaves saving under developer control.
- Add Parameter and Replace Property/Method are explicitly admitted cross-document
  providers. Discovery reports physical affected-file count and whether the active
  document changes. Preview returns every confined persisted path, baseline, original
  and candidate text, replacement count, diagnostics, and one fingerprint. Added,
  removed, generated, external, structural, inconsistent-linked, over-100-file, and
  over-10-MiB results fail closed.
- A developer cross-document choice uses the approved goal mutation boundary rather
  than replacing only the active buffer. Apply re-resolves the action and fingerprint,
  writes all files as one exact-baseline batch, validates the complete persisted set,
  and refreshes every affected open editor. Model apply also checks every affected
  path against its delegated file areas. An affected open editor with unsaved text
  blocks the developer action, and incomplete batch confirmation fails before
  post-validation.
- Implementer tools expose missing-import discovery plus separate preview and apply
  calls. Apply recomputes the
  preview, compares the exact fingerprint, checks the delegated path, writes one
  atomic batch, records durable evidence, and validates the persisted candidate with
  Roslyn.
- There is no generic Roslyn-action executor. The current closed set is
  `FormatDocument`, `FormatSelection`, `FormatChangedSpans`, `FormatPaste`,
  `FormatOnType`, `OrganizeImports`, `RemoveUnusedImports`, `AddMissingImport`, and
  `ApplyCodeAction`. Code-action discovery composes only explicitly allowlisted pinned
  Roslyn providers. It preflights each choice as either a confined document change or
  one of the explicitly admitted cross-document providers, while omitting custom host
  operations, project/reference changes, and added files. Exact selections enable
  extract-method and introduce-variable refactorings. Safe providers expose a bounded
  document scope that repeats the exact action identity until no matching diagnostic
  remains.
- The editor merges imports, compiler fixes, local refactorings, and labeled
  “Fix all in document” choices behind Quick fix / `Ctrl+.`. An applied choice is one
  undoable unsaved buffer replacement. Lead, Implementer, and Reviewer can discover
  actions; only Implementer can preview/apply in its delegated worktree. The opt-in
  inbound `harness_code_actions` MCP tool exposes the same read-only original-context
  result.

## Deterministic verification

Focused tests cover complete document formatting, selection confinement, syntax-tree
changed-span detection, exact paste/on-type ranges and triggers, automatic editor
wiring, settings persistence, import ordering, compiler-proven cleanup, comment
preservation, valid and invalid missing-import choices, Data Access to Business Logic
mapping, accessible editor discovery, atomic fingerprinted apply, delegated-path
rejection, evidence, and post-apply validation.
They also cover provider composition, interface implementation, stale or unknown
action IDs, bounded document fix-all, auto-property and exact-selection refactoring,
cross-document Add Parameter and Replace Property/Method previews, complete edit-set
mapping, atomic batch apply, all-path grant rejection, Data Access/Business Logic
identity mapping, role and model schemas, accessible editor discovery, and single-step
undo.

Verification result:

- repository build: passed with zero warnings and zero errors;
- full deterministic suite: 659 passed, zero failed;
- editor-intelligence verifier: passed, including 43 Roslyn adapter tests, 19 semantic
  boundary tests, 42 transformation-authority tests, 66 editor control tests, and its
  settings and theme checks;
- the production source-editor capture completed and was inspected at 1920×1240. The
  Quick fix and Transform actions are visible beside the semantic navigation actions
  without overlap;
- the standard 1200×900 Settings capture was inspected. The formatting switches were
  moved above CodeLens so both automatic controls and their safety note are visible
  without scrolling;
- production Avalonia AT-SPI workbench verification: passed. The gate opened an
  unresolved `StringBuilder` in a real approved worktree, selected the proven
  `System.Text` action, ran the real changed-code formatter, saved through the document
  boundary, and verified the exact persisted import;
- Linux x64 self-contained publish: passed;
- repository formatting verification: passed.

The 2026-08-13 cross-document extension passed a zero-warning solution build and the
complete deterministic editor verifier: 52 Roslyn adapter, 23 semantic-boundary, 45
transformation-authority, and 78 editor/Vim control tests, plus the existing settings,
Run, Project User Secrets, capture-policy, and theme suites. The focused real-Roslyn
fixtures prove one declaration-only Add Parameter action and one two-file Replace
Property/Method action without writing during preview. Business Logic tests prove one
batch for the complete edit set, post-validation of every persisted file, and rejection
when any model path is outside its grant. The Avalonia test proves a multi-file choice
routes through atomic goal mutation instead of replacing only the active buffer.
The full deterministic solution regression passed 725 tests: 6 analyzer, 1
architecture, 279 Business Logic, 246 Data Access, 22 Host, 147 Avalonia Presentation,
22 terminal Presentation, and 2 Avalonia UI tests.

## Subsequent closure

Full method-body decompilation was delivered afterward. A real Debug CodeLens adapter is
Task 052. Later provider additions remain explicit policy changes rather than a generic
action executor.

The complete keyboard, IME, AT-SPI, strict Orca, scaling, restoration, and Linux
publication matrix is recorded in
[editor-resilience-2026-08-13.md](editor-resilience-2026-08-13.md).
