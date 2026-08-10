# Deterministic Roslyn rename acceptance — 2026-07-31

## Accepted behavior

- Rename starts from an approved goal worktree, exact repository-relative document,
  persisted SHA-256 baseline, versioned buffer, caret, and validated C# identifier.
  Roslyn resolves symbol identity and references; neither Presentation nor a model
  constructs replacements through text search.
- A preview includes the repository-relative symbol identity, all affected physical
  paths, original and candidate text, replacement counts, exact baselines, diagnostic
  delta, conflicts, and a stable SHA-256 fingerprint. The preview is bounded to 100
  files and 10 MiB of complete original/new diff evidence.
- Metadata, generated/missing, unwritable, outside-context, inconsistent linked-file,
  semantic-name-conflict, oversized, cancelled, and stale results fail closed. A ready
  preview has no conflicts and always has a fingerprint.
- Apply reloads the trusted goal context and recomputes the exact preview. A changed
  symbol, baseline, buffer, diagnostic result, path set, text, or fingerprint prevents
  all writes. Model-originated applies additionally require every affected normalized
  path to fall within an Implementer file-area grant.
- The file boundary validates and stages every replacement, rechecks every baseline,
  and then commits under one worktree gate. A mid-commit failure or cancellation runs
  reverse-order rollback; the operation never reports a partial batch as successful.
- A successful batch is checked again against the persisted Roslyn solution. The
  preview, full bounded diff, file hashes, rollback/cancellation state, and post-apply
  diagnostics are durable schema-20 `Rename` tool evidence.
- F2 in an editable source tab opens a compact identifier prompt followed by an
  accessible affected-files preview. Applying refreshes every affected open editor.
  Implementer agents receive separate `preview_symbol_rename` and
  `apply_symbol_rename` tools backed by the same service and mutation boundary.

## Deterministic checks

- Real Roslyn project tests cover a two-file partial type, one selected overload and
  only its bound call sites, same-name semantic conflict, metadata symbols, an
  unwritable reference, 25 affected files, and one physical source linked into two
  projects. Preview never mutates disk.
- Business Logic tests prove stale in-flight preview rejection, changed-fingerprint
  rejection, normalized out-of-grant denial, one atomic apply, post-apply validation,
  and durable success evidence without invoking a model.
- Data Access tests inject a failure during the second file commit and observe both
  originals restored with no temporary artifacts. They also prove stale-baseline
  all-or-none behavior and cancellation rollback.
- A headless UI test opens an editable source tab, previews through
  the shared human operation, applies the accepted fingerprint, refreshes the editor
  with the returned content/hash, and leaves the document clean.
- SQLite initialization, upgrade backup, and tool evidence tests exercise schema 20
  and persist the new closed `Rename` evidence kind.

No model provider, restore, remote language server, network operation, or paid check
is used by this acceptance slice.
