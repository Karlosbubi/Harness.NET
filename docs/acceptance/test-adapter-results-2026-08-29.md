# Adapter-level Test results acceptance — 2026-08-29

This record covers the tenth Task 052 slice. It adds bounded per-case outcomes to the
existing exact/type/project/selection lifecycle without adding a test adapter,
Restore, shell, raw result persistence, or model execution authority.

## Delivered behavior

- Every developer Test command adds the standard `trx` logger and a unique results
  directory below Harness.NET's private cache. Nothing is written into the repository.
- Data Access accepts files no larger than 4 MiB, disables DTD processing and external
  resolution, reads at most 2,000 cases, maps adapter outcomes to the closed
  Passed/Failed/Skipped/Other set, and removes the private directory immediately.
- Business Logic carries typed fully qualified name, display name, outcome, duration,
  and an explicit truncation flag while the process is local.
- Schema 36 durably records only fully qualified name, outcome, duration, and bounded
  ordering. Adapter display names, raw XML, stdout/stderr, stacks, messages, and failure
  payloads are not persisted; after restart the safe name becomes the display label.
- Run output shows aggregate counts and individual case outcomes. Exact Test Explorer
  history includes passed/failed/skipped counts and labels incomplete capture.

## Verification

- Parser tests cover names resolved from TRX definitions, passed and skipped outcome
  mapping, duration, and the absence of output/message fields.
- Runner tests prove private logger arguments, parsed failed-case evidence, directory
  cleanup, exact/type/project/selection filters, and cancellation.
- Business Logic tests prove Data Access outcomes map to process-local presentation and
  safe durable records, then reconstruct through the execution list boundary.
- SQLite tests prove ordered outcome/duration round-trip, truncation, foreign-key
  ownership, and schema-35-to-36 migration through the current initializer.
- Headless Avalonia tests prove Run output and Test Explorer summaries.

- The final release gate passed repository metadata, 12 local-model regression tests,
  all 909 deterministic .NET tests (16 + 4 + 338 + 311 + 22 + 194 + 22 + 2), and the
  schema-36 Linux x64 publish/backup/recovery smoke.
- The production Avalonia AT-SPI workflow passed Test Explorer discovery and then the
  complete Build, goal-worktree editor, Roslyn quick-fix/save, search, layout restart,
  and corrupt-layout recovery path against the schema-36 host. The fixture deliberately
  has no restored adapter, so typed parsing and projection use deterministic fake TRX.

## Remaining Task 052 work

Test Debug, coverage, typed one-run launch overrides, Hot Reload, and the debugger
adapter remain open.
