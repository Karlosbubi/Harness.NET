# Cobertura coverage navigation acceptance — 2026-08-29

This record covers the eleventh Task 052 slice. It adds developer-operated coverage
inspection to the existing Roslyn Test Explorer lifecycle without adding a collector,
implicit test execution, Restore, shell, or model execution authority.

## Delivered behavior

- The Coverage subview imports only the workspace-relative Cobertura XML path entered
  by the developer. Harness does not scan for or silently select reports.
- Data Access confines both the report and every mapped existing C#, F#, or Visual
  Basic source file to the exact trusted original workspace or approved goal worktree.
  Symbolic reports/sources and outside-root, non-XML, missing, or oversized input are
  rejected.
- XML DTD processing and external resolution are disabled. A report is limited to 8
  MiB, 500 reported source files, and 100,000 distinct line records; duplicate lines
  retain the largest hit count and truncation is explicit.
- Schema 37 durably records the source-context identity and description, relative
  report path, SHA-256, closed Cobertura format, bounded producer/version, generation
  and import timestamps, unmapped/truncated state, and relative source-line hit counts.
  Only the latest ten imports per exact source context are retained.
  It does not store raw XML, source content, machine paths, stacks, test output, or
  failure payloads.
- The accessible tree summarizes covered/instrumented lines per source file and offers
  exact editor navigation for up to 2,000 uncovered lines. Copy states that uncovered
  evidence is not itself a defect.

## Verification

- Data Access tests cover relative and exact in-workspace absolute source mapping,
  duplicate hit reconciliation, safe provenance, outside-root rejection, DTD rejection,
  schema-37 migration, exact original/goal context lookup, line round-trip, and cascade
  ownership, and ten-import retention.
- Business Logic tests prove exact context resolution, typed mapping, durable import,
  latest-context retrieval, fixed import time, and rejection before context resolution
  for invalid bounded input.
- Headless Avalonia coverage proves accessible report/import/tree names, provenance and
  per-file summaries, exact uncovered-line selection, and workspace request mapping.
- The release gate passed repository metadata, 12 local-model regression tests, all
  919 non-live deterministic .NET tests (16 + 4 + 342 + 316 + 22 + 195 + 22 + 2),
  and the schema-37 Linux x64 publish/backup/downgrade/upgrade/recovery smoke.
- The production AT-SPI verifier was extended to import the representative report and
  assert its accessible source action. This shell has no attached graphical
  Linux session, so that production process check could not be rerun here; the new UI
  behavior is covered by the headless Avalonia test and no production AT-SPI pass is
  claimed for this slice.

## Remaining Task 052 work

Test Debug, typed one-run launch overrides, Hot Reload, and the debugger adapter remain
open.
