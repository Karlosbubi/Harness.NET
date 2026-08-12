# Editor transformations acceptance — 2026-08-12

Task 049 now has one shared closed transformation path for document formatting,
selection formatting, import organization, unused-import cleanup, and missing-type
import fixes.

## Behavior

- Data Access runs Roslyn against the exact session, path, persisted baseline, live
  text, buffer version, and optional selection. It returns complete original and
  candidate text, replacement count, diagnostic delta, and a SHA-256 fingerprint. It
  does not write.
- Business Logic rejects malformed, stale, unbounded, or conflicting adapter results.
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
  `FormatDocument`, `FormatSelection`, `OrganizeImports`, `RemoveUnusedImports`, and
  `AddMissingImport`.

## Deterministic verification

Focused tests cover complete document formatting, selection confinement, import
ordering, compiler-proven cleanup, comment preservation, valid and invalid
missing-import choices, Data Access to Business Logic mapping, accessible editor
discovery, atomic fingerprinted apply, delegated-path rejection, evidence, and
post-apply validation.

Verification result:

- repository build: passed with zero warnings and zero errors;
- full deterministic suite: 642 passed, zero failed;
- editor-intelligence verifier: passed, including 31 Roslyn adapter tests, 17 semantic
  boundary tests, 42 transformation-authority tests, 64 editor control tests, and its
  settings and theme checks;
- the production source-editor capture completed and was inspected at 1920×1240. The
  Quick fix and Transform actions are visible beside the semantic navigation actions
  without overlap;
- production Avalonia AT-SPI workbench verification: passed. The gate opened an
  unresolved `StringBuilder` in a real approved worktree, selected the proven
  `System.Text` action, saved through the document boundary, and verified the exact
  persisted import;
- Linux x64 self-contained publish: passed;
- repository formatting verification: passed.

## Remaining Task 049 work

Changed-span, paste, and on-type formatting; the broader closed quick-fix,
refactoring, and fix-all catalog; virtual source navigation; inspection views;
keybindings/Vim; User Secrets; typed Run/Debug CodeLens targets; and the full editor
performance, IME, Orca, scaling, and restoration matrix remain open.

The current named AvaloniaEdit automation peer exposes the editor as a panel, but not
the AT-SPI Text interface. This slice proves accessible commands and the real initial
caret workflow; accessible arbitrary caret/text navigation remains part of that open
Orca matrix.
