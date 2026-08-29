# Compiler-backed Test Explorer discovery acceptance — 2026-08-29

This record covers the fourth Task 052 slice. It adds deterministic source-test
discovery and navigation, not test execution, Debug, Restore, or an agent process
capability.

## Delivered behavior

- Data Access walks the exact active Roslyn solution and classifies methods through
  compiler-resolved attribute symbols. It recognizes xUnit `Fact`/`Theory`, NUnit
  `Test`/`TestCase`/`TestCaseSource`/`Theory`, and MSTest
  `TestMethod`/`DataTestMethod`, including custom derived test attributes.
- Each result has a stable semantic test identity, confined project and document
  paths, framework, fully qualified and display names, exact zero-based source range,
  parameterized flag, and at most 32 bounded traits. xUnit traits, NUnit properties
  and categories, and MSTest properties and categories are supported on methods and
  containing types.
- Query matching covers project, fully qualified name, display name, trait name, and
  trait value. Requests accept at most 2,000 rows, use deterministic ordering and
  bounded continuation, and stop the source catalog at 10,000 tests.
- Business Logic requires the exact active context/session, rejects malformed or
  outside-root results, maps no Roslyn type across the boundary, and fails stale or
  cancelled discovery explicitly.
- The Workspace tool has an accessible Tests tab with refresh and search controls. It
  renders project, containing type, and test nodes; labels parameterized tests and
  traits; reports empty/degraded/bounded states; and opens the exact source range in
  the existing editor.
- Discovery starts no process, invokes no shell or test adapter, performs no Restore,
  loads no test assembly, and does not infer tests from paths or source text.

## Verification

- Data Access coverage compiles local xUnit, NUnit, and MSTest attribute definitions
  and proves derived attributes, display names, traits, parameterization, exact source
  mapping, deterministic identifiers, search, and paging.
- Business Logic coverage proves exact-session mapping and removal of mismatched or
  unconfined results.
- Headless Avalonia coverage proves exact session/entry-point and search requests,
  project/type/test hierarchy, accessibility names, status, and exact test-navigation
  handoff.
- The final release gate passed repository metadata, 12 local-model regression tests,
  all 886 deterministic .NET tests (16 + 4 + 329 + 298 + 22 + 193 + 22 + 2), and the
  schema-32 Linux x64 publish/backup/recovery smoke. The recovery fixture now uses
  Python's standard-library SQLite driver rather than an extra system CLI.
- The production Avalonia AT-SPI workflow passed against a real restored fixture. It
  opened the accessible Tests tab, observed the live Roslyn status, discovered the
  representative xUnit source test, and then completed Build, goal-worktree editor,
  Roslyn quick-fix/save, search, and restart/layout recovery coverage.

## Remaining Task 052 work

Test selection and typed execution/debug, duration and failure history, rerun,
coverage, typed one-run launch overrides, Hot Reload, and the debugger adapter remain
open.
