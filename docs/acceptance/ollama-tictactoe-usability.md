# Ollama Tic-Tac-Toe usability exercise

`eng/verify-ollama-tictactoe-usability.py` is a deliberately model-driven daily-use
exercise. Unlike the deterministic release verifier, it uses one real tool-capable
Ollama model for Lead, Implementer, and Reviewer and drives the production Avalonia
surface exclusively through Linux AT-SPI.

The script creates a persistent isolated diagnostic root containing:

- a restored and initially compiling .NET 10 solution with console and xUnit projects;
- private XDG configuration containing only an Ollama provider;
- the Harness.NET SQLite database, logs, and isolated goal worktree;
- phase timings and durable workflow/checkpoint state in `usability-report.json`;
- generated build/test output, console smoke output, and independent validation output.

Harness receives a bounded goal to replace the seed stubs with a playable human-X
versus computer-O game, immutable game engine, minimax solver, input validation, and
meaningful generated tests. The exercise crosses the plan-model selection surface,
approves the generated plan, and continues the real Implementer/Reviewer workflow.
Because the current Avalonia model picker is exposed to AT-SPI as a generic panel,
the script records that accessibility defect and uses the only configured model's
default selection. If planning enters `NeedsDirection`, it exercises the recovery
dialog once with explicit JSON-only guidance before reporting a persistent failure.

Acceptance does not trust the model-authored tests alone. After Harness reaches
`AwaitingAcceptance`, the script independently:

1. builds with warnings as errors and runs the generated test suite without restore;
2. rejects the original failing placeholder or fewer than four generated test cases;
3. compiles a separate validator against the required public engine API;
4. checks win, draw, range, occupied-cell, turn, and immutability behavior;
5. enumerates every human branch reachable against the generated O solver and fails
   if the human can win or the solver selects an illegal move; and
6. launches the generated console app, sends `q`, and requires a clean bounded exit.

Run it in a graphical Linux session with a tool-capable model already installed:

```bash
./eng/verify-ollama-tictactoe-usability.py \
  --ollama-endpoint http://127.0.0.1:11434 \
  --model gemma4:latest
```

`HARNESS_OLLAMA_ENDPOINT` and `HARNESS_OLLAMA_MODEL` provide equivalent defaults.
No OpenRouter provider or remote budget is configured. The script performs real local
inference and can take substantial time; it is intentionally not part of the normal
deterministic test suite. Its artifact directory is retained on both success and
failure so usability stalls can be inspected rather than erased.
Failed reports include checkpoint summaries and recent typed-tool errors, making
model loops and deterministic code-intelligence rejections visible without querying
the SQLite database manually.
