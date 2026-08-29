# Test Explorer multi-selection acceptance — 2026-08-29

This record covers the ninth Task 052 slice. It adds arbitrary exact-test selection
without adding raw filter syntax, a shell, implicit Restore, process fan-out, or model
execution authority.

## Delivered behavior

- Every exact test row has an accessible selection checkbox and the Test Explorer
  header exposes Run selected with a live count.
- A selection contains 2–24 distinct compiler-discovered tests from one project.
  Cross-project and over-limit choices are rejected visibly before execution.
- Business Logic sorts exact names ordinally, derives a stable SHA-256 identity from
  project and members, re-resolves the trusted inspected project, and rejects forged,
  duplicate, unsafe, oversized, or undefined selections.
- Data Access constructs the VSTest equality-OR expression internally and starts one
  direct `dotnet test <project> --no-restore --filter <internal-expression>` process.
  Presentation supplies semantic members, never filter syntax.
- The operation reuses Run output, cancellation, bounded process-local streams, and
  durable lifecycle metadata. Schema 35 stores the ordered member array as validated
  JSON only for Selection rows and preserves schema-34 rows unchanged.

## Verification

- Runner coverage proves two exact members produce one process and one deterministic
  filter argument; existing unsafe-name, confinement, and cancellation tests remain.
- Business Logic coverage proves sorting, one runner call, scope/member persistence,
  reconstruction, and deterministic identity validation.
- SQLite coverage proves typed member round-trip and schema constraints.
- Headless Avalonia coverage proves the selection count/action, exact request members,
  one execution start, Run output activation, and accessibility.

- The final release gate passed repository metadata, 12 local-model regression tests,
  all 904 deterministic .NET tests (16 + 4 + 337 + 308 + 22 + 193 + 22 + 2), and the
  schema-35 Linux x64 publish/backup/recovery smoke. One unrelated fake-executable
  process-start test failed transiently on the first full run, passed immediately in
  isolation, and the complete gate then passed cleanly.
- The production Avalonia AT-SPI workflow passed Test Explorer discovery with the new
  selection control present and then completed Build, goal-worktree editor, Roslyn
  quick-fix/save, search, layout restart, and corrupt-layout recovery against schema
  35. The two-member request is covered headlessly because the production fixture
  intentionally contains only one adapter-free source test.

## Remaining Task 052 work

Adapter-level case results, Test Debug, coverage, typed one-run launch overrides, Hot
Reload, and the debugger adapter remain open.
