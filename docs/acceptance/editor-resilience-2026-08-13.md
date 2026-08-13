# Editor resilience acceptance — 2026-08-13

This record covers the final Task 049 Linux resilience matrix. It does not add a
debugger. Metadata decompilation was delivered separately afterward.

## Behavior

- A project analyzer failure produces a bounded `analyzer_failed` issue and a
  `Degraded` result. Compiler diagnostics from the active file remain visible.
  Roslyn's process-level `AD0001` and `AD0002` details are not shown as file
  diagnostics.
- Diagnostics cancellation interrupts an analyzer that is already running. The
  operation gate is released and later work can continue.
- Replacing the foreground Roslyn context disposes the prior workspace. Eight
  consecutive replacements leave every prior session stale and the current session
  usable.
- Visual-only Avalonia and Dock peers receive an invisible automation name when they
  load. Named controls keep their semantic name and role. Orca no longer speaks
  `Grid`, `Border`, presenter, or visual-layer implementation names.

## Measurements

The performance fixture loaded the current Harness.NET solution: 1,019 tracked files,
15 project files, and 855 C# files. A run on the supported Linux development machine
reported:

| Measurement | Result | Required bound |
|---|---:|---:|
| Cold solution load | 7,808 ms | under 60 s |
| Warm diagnostics update | 2,275 ms | under 15 s |
| Retained foreground-session memory | 118.9 MiB | under 1 GiB |
| Cancelled diagnostics return | 1.1 ms | under 1 s |
| Warm completion p95, 20 calls | 34.7 ms | under 200 ms |
| Warm definition navigation | 23.9 ms | under 2 s |

The test asserts the bounds. The figures are evidence for this repository and
machine, not universal product guarantees.

## Complete Linux gate

`./eng/verify-editor-intelligence.py --complete-linux` passed in 325.3 seconds. It
performed no model inference and no paid-provider call. The gate included:

- a zero-warning solution build;
- 51 Roslyn adapter, 22 semantic-boundary, 43 mutation-authority, 10 editor-settings
  policy, 3 editor-settings storage, 5 developer-run storage, 3 developer-run policy,
  77 editor/Vim control, 7 Project User Secrets storage, 14 secrets/capture policy,
  1 secrets-dialog, and 2 theme tests;
- the real Harness.NET large-workspace latency and memory test;
- analyzer failure, in-flight cancellation, stale result, and repeated context tests;
- keyboard-only completion, quick info, definition, panel restoration, and compact
  layout tests;
- IME-preedit suspension and platform-shortcut pass-through;
- 200% scaling and moved, hidden, floating, corrupt, and restarted Dock layouts;
- the production AT-SPI workflow with strict Orca speech inspection; and
- self-contained Linux x64 publication and startup verification.

The complete deterministic solution regression passed 718 tests: 6 analyzer, 1
architecture, 275 Business Logic, 245 Data Access, 22 Host, 145 Avalonia
Presentation, 22 terminal Presentation, and 2 Avalonia UI tests.

## Data boundary

Fixtures contain synthetic source only. The analyzer error exposed to callers is a
fixed message without exception text, paths, environment values, credentials, or
source content. Temporary test results and publish output are not repository files.

## Subsequent closure

- The closed catalog now includes bounded Add Parameter and Replace Property/Method
  cross-document actions plus semantic rename. Additional providers require explicit
  policy admission; there is no generic Roslyn action executor.
- Metadata method-body decompilation and its pinned dependency, license, integrity,
  and SBOM review are recorded in
  [editor-decompilation-2026-08-13.md](editor-decompilation-2026-08-13.md).
- Debug CodeLens belongs to Task 052 and remains hidden until a real debugger adapter
  exists.
