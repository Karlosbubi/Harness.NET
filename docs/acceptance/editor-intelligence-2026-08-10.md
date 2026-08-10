# Editor intelligence acceptance — 2026-08-10

## Accepted behavior

- Every non-truncated C# source tab exposes visible IntelliSense, symbol information,
  definition, usage, and implementation actions. Keyboard access remains available
  through Ctrl+Space, Ctrl+K, F12, Shift+F12 or Alt+F7, and Ctrl+F12 or Ctrl+Alt+B.
- Roslyn resolves implementations from the exact active source context, including
  interface implementations and overrides of virtual methods. A single destination
  navigates immediately; multiple destinations remain an explicit bounded choice.
- The same implementation operation is available to Lead, Implementer, and Reviewer
  through their role-scoped source contexts. Models do not construct sessions or pass
  unverified source snapshots.
- Syntax rendering distinguishes comments, strings, types, numbers, methods,
  preprocessors, punctuation, and keywords through semantic theme tokens.
- Unsupported, unavailable, metadata-only, cancelled, degraded, and stale results
  remain explicit; no text-search result is presented as semantic navigation.

## Deterministic verification

Run the focused, inference-free gate from the repository root:

```text
./eng/verify-editor-intelligence.py
```

Use `--no-build` after an existing build, or `--atspi` to additionally launch the
production Linux accessibility verifier. The gate covers the real Roslyn adapter,
Business Logic freshness boundary, headless editor controls, and theme contract.

The 2026-08-10 focused run passed 21 Roslyn adapter tests, 13 semantic-boundary tests,
56 editor-control tests, and 2 theme-contract tests. It performs no provider call,
model inference, restore, or paid operation.

The `--atspi` run also passed against the production Avalonia application and found
the five semantic actions by their developer-facing accessible names in a real source
document.
