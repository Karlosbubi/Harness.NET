# Typed Test Explorer filters acceptance — 2026-08-29

This record covers the seventh Task 052 slice. It adds closed framework and lifecycle
filters to the existing compiler catalog/history join; it adds no execution authority,
process, persistence, or repository metadata.

## Delivered behavior

- The discovery request carries optional xUnit, NUnit, or MSTest identity as a closed
  enum across Presentation, Business Logic, and Data Access. Undefined values fail
  before compiler work.
- Roslyn applies framework selection from resolved test attributes before stable
  ordering, offset, result limit, truncation, and continuation. Paging therefore
  describes the selected framework rather than a client-side approximation.
- Existing bounded search continues to match project, fully qualified name, display
  name, trait name, and trait value inside the chosen framework.
- Test Explorer supplies accessible framework and lifecycle selectors. Lifecycle is
  evaluated only after the latest exact-context Test history is joined and has closed
  All, Not run, Running, Succeeded, Failed, Cancelled, and Interrupted choices.
- The live status states both the compiler-discovered count and the displayed count;
  empty filtered output is not reported as discovery failure.

## Verification

- Data Access coverage proves an NUnit filter returns only the compiler-resolved
  NUnit test while preserving the existing cross-framework discovery, query, and
  paging assertions.
- Business Logic coverage proves the typed framework maps into the Data Access request
  while exact confined results map back unchanged.
- Headless Avalonia coverage proves the xUnit request, accessible filter names, exact
  failed-history filtering, resulting hierarchy, and discovered/shown status.
- The final release gate passed repository metadata, 12 local-model regression tests,
  all 893 deterministic .NET tests (16 + 4 + 333 + 301 + 22 + 193 + 22 + 2), and the
  schema-33 Linux x64 publish/backup/recovery smoke.
- The production Avalonia AT-SPI workflow passed with the filter row, live discovery
  status, and hierarchy visible, then completed Build, goal-worktree editor, Roslyn
  quick-fix/save, search, and restart/layout recovery coverage.

## Remaining Task 052 work

Multi-selection and project/type runs, adapter-level case results, Test Debug,
coverage, typed one-run launch overrides, Hot Reload, and the debugger adapter remain
open.
