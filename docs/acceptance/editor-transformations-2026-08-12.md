# Editor transformations acceptance — 2026-08-12

Task 049 now has one shared closed transformation path for document, selection,
changed-span, paste, and supported on-type formatting, import organization,
unused-import cleanup, and missing-type import fixes.

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
- Implementer tools expose missing-import discovery plus separate preview and apply
  calls. Apply recomputes the
  preview, compares the exact fingerprint, checks the delegated path, writes one
  atomic batch, records durable evidence, and validates the persisted candidate with
  Roslyn.
- There is no generic Roslyn-action executor. The current closed set is
  `FormatDocument`, `FormatSelection`, `FormatChangedSpans`, `FormatPaste`,
  `FormatOnType`, `OrganizeImports`, `RemoveUnusedImports`, and `AddMissingImport`.

## Deterministic verification

Focused tests cover complete document formatting, selection confinement, syntax-tree
changed-span detection, exact paste/on-type ranges and triggers, automatic editor
wiring, settings persistence, import ordering, compiler-proven cleanup, comment
preservation, valid and invalid missing-import choices, Data Access to Business Logic
mapping, accessible editor discovery, atomic fingerprinted apply, delegated-path
rejection, evidence, and post-apply validation.

Verification result:

- repository build: passed with zero warnings and zero errors;
- full deterministic suite: 649 passed, zero failed;
- editor-intelligence verifier: passed, including 36 Roslyn adapter tests, 18 semantic
  boundary tests, 42 transformation-authority tests, 65 editor control tests, and its
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

## Remaining Task 049 work

The broader closed quick-fix, refactoring, and fix-all catalog; virtual source
navigation; inspection views;
keybindings/Vim; User Secrets; typed Run/Debug CodeLens targets; and the full editor
performance, IME, Orca, scaling, and restoration matrix remain open.

The current named AvaloniaEdit automation peer exposes the editor as a panel, but not
the AT-SPI Text interface. This slice proves accessible commands and the real initial
caret workflow; accessible arbitrary caret/text navigation remains part of that open
Orca matrix.
