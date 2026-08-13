# Editor inlay hints and CodeLens acceptance — 2026-08-12

Task 049's second editor slice adds exact-buffer inlay hints and lazy CodeLens without
adding another language service or workspace.

## Delivered behavior

- Settings → Editor persists independent switches for parameter-name hints,
  inferred-type hints, reference lenses, implementation lenses, and associated-test
  lenses in private SQLite state. Defaults are enabled and changes apply to open C#
  editors.
- Roslyn produces parameter hints for non-obvious positional arguments and inferred
  types for `var`, implicit lambda parameters, and `foreach var` in the visible live
  buffer. Every result retains source context, session, path, baseline, and buffer
  version.
- Visible declarations expose bounded reference, applicable implementation, and
  associated-test actions. The solution queries run only after the developer selects
  a lens. A later slice added typed Run for the Roslyn-proven project entry point;
  Debug remains hidden until a real debugger adapter exists.
- Viewport-only classification requests no longer rebuild document folding and
  outline data. Document occurrence lookup now confines Roslyn reference search to
  the active document.

## Deterministic evidence

- `dotnet build Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1` — clean,
  zero warnings.
- `dotnet test Harness.slnx --no-build --no-restore -p:UseSharedCompilation=false
  -m:1` — 626 tests passed.
- `./eng/verify-editor-intelligence.py --no-build` — 23 Roslyn adapter, 14 semantic
  boundary, 1 settings-policy, 1 settings-storage, 62 production editor-control, and
  2 theme-contract tests passed.
- `./eng/verify-avalonia-atspi.py` — production AT-SPI verification passed.
- `./eng/verify-linux-x64-publish.sh` — self-contained publish, schema 27 startup,
  backup, recovery migration, and state preservation passed.

## Visual dogfood

The production host was launched with isolated XDG state through the existing
Settings and source-editor capture drivers. The Editor page was searchable by
`inlay`, exposed all five accessible switches and the save action, and fit at
980×700. A temporary trusted .NET project showed semantic coloring, `value:` on a
literal `Console.WriteLine` argument, declaration-level reference/implementation/test
lenses, and the normal exact-buffer health state in the real Dock workbench.

The already-loaded `harness_live` MCP connector still returned HTTP 401 after the
live process credential changed. This is the known cached-authorization restart issue
recorded in the Morgania evaluation; visual and deterministic dogfood continued
without weakening the loopback, allowlist, or typed-authority boundaries.

## Remaining Task 049 work

Formatting, usings, code actions and refactorings; generated and metadata virtual
documents; compiler inspection views; configurable keybindings and optional Vim;
User Secrets; a real debugger adapter; and the broader large
solution, latency, memory, IME, Orca, and scaling matrix remain open.
