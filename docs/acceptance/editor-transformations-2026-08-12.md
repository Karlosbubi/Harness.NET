# Editor transformations acceptance — 2026-08-12

Task 049 now has one shared closed transformation path for document formatting,
selection formatting, and import organization.

## Behavior

- Data Access runs Roslyn against the exact session, path, persisted baseline, live
  text, buffer version, and optional selection. It returns complete original and
  candidate text, replacement count, diagnostic delta, and a SHA-256 fingerprint. It
  does not write.
- Business Logic rejects malformed, stale, unbounded, or conflicting adapter results.
- The editor exposes a Transform menu, command-palette entries, and shortcuts. It
  applies the candidate to the live buffer as one undoable replacement, preserves the
  caret where possible, and leaves saving under developer control.
- Implementer tools expose separate preview and apply calls. Apply recomputes the
  preview, compares the exact fingerprint, checks the delegated path, writes one
  atomic batch, records durable evidence, and validates the persisted candidate with
  Roslyn.
- There is no generic Roslyn-action executor. The current closed set is
  `FormatDocument`, `FormatSelection`, and `OrganizeImports`.

## Deterministic verification

Focused tests cover complete document formatting, selection confinement, import
ordering, invalid range rejection, Data Access to Business Logic mapping, atomic
fingerprinted apply, delegated-path rejection, evidence, and post-apply validation.

Verification result:

- repository build: passed with zero warnings and zero errors;
- full deterministic suite: 634 passed, zero failed;
- editor-intelligence verifier: passed, including 27 Roslyn adapter tests, 15 semantic
  boundary tests, 41 transformation-authority tests, 62 editor control tests, and its
  settings and theme checks;
- the production source-editor capture completed and was inspected at 1920×1240. The
  Transform action is visible beside the semantic navigation actions without clipping;
- production Avalonia AT-SPI workbench verification: passed;
- Linux x64 self-contained publish: passed;
- repository formatting verification: passed.

## Remaining Task 049 work

Changed-span, paste, and on-type formatting; unused and missing import fixes; quick
fixes, refactorings, and fix-all; virtual source navigation; inspection views;
keybindings/Vim; User Secrets; typed Run/Debug CodeLens targets; and the full editor
performance, IME, Orca, scaling, and restoration matrix remain open.
