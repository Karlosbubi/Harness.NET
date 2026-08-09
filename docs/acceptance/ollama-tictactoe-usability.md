# Ollama Tic-Tac-Toe usability exercise

`eng/verify-ollama-tictactoe-usability.py` is a deliberately model-driven daily-use
exercise. Unlike the deterministic release verifier, it uses real tool-capable
Ollama models for Lead, Implementer, Reviewer, and bounded recovery and drives the production Avalonia
surface exclusively through Linux AT-SPI.

The script creates a persistent isolated diagnostic root containing:

- a restored and initially compiling .NET 10 solution with console, xUnit, and
  read-only acceptance projects, all configured to treat warnings as errors;
- private XDG configuration containing only an Ollama provider;
- the Harness.NET SQLite database, logs, and isolated goal worktree;
- phase timings and durable workflow/checkpoint state in `usability-report.json`;
- generated build/test output, console smoke output, and independent validation output.

Harness receives a bounded goal to replace the seed stubs with a playable human-X
versus computer-O game, immutable game engine, minimax solver, input validation, and
meaningful generated tests. The exercise crosses the plan-model selection surface,
approves the generated plan, and continues the real Implementer/Reviewer workflow.
If planning or implementation enters `NeedsDirection`, the script exercises the
searchable recovery model selector and supplies bounded corrective guidance. Activity
tracking includes durable tool calls as well as workflow checkpoints, so an active
compiler-correction loop is not misreported as a hung workflow.

Local exact-file generation returns fenced source text rather than embedding source in
a JSON string; this preserves C# escape sequences. Each accepted model-authored C# edit
must introduce no compiler warning or error, including errors in transitive dependent
projects. Harness then runs a no-restore solution build, and test-source edits must also
pass the real test suite before the Implementer can report completion.

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

## Evaluation on 2026-08-09

The exercise found and closed four false-positive completion paths: introduced
warnings were accepted, dependent projects were not recompiled, structured source was
damaged by JSON escaping, and generated tests were not executed before model review.
The normal deterministic Harness.NET suite remained green after those fixes.

The local-model usability result is still a failure, not an acceptance: Gemma 4,
Mistral Nemo, and Granite 3.3 required many compiler-rejected full-file proposals and
did not reliably finish the four-file project within a reasonable interactive session.
Raw fenced-source transport materially improved one run—`GameState` converged after
one correction and `MinimaxSolver` passed first try—but later runs still exhausted
bounded retries. The retained diagnostic roots under `artifacts/usability/` are the
evidence. These models should not be treated as dependable default Implementers until
proposal repair becomes more targeted than repeated full-file rewriting.
