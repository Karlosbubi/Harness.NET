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
projects. Harness then runs a no-restore solution build and the real deterministic test
suite after every C# edit, so a production task cannot close over an already failing
behavioral contract. Replacement source must also preserve the exact target namespace
and at least one existing target type; a compilable dependency class cannot be written
into the wrong file.

Acceptance does not trust the model-authored tests alone. After Harness reaches
`AwaitingAcceptance`, the script independently:

1. builds with warnings as errors and runs the generated test suite without restore;
2. rejects the original skipped placeholder, fewer than four generated test cases, or
   a generated suite without at least one assertion per test case;
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

The exercise found and closed additional false-positive completion paths beyond the
original compiler and transport failures: production edits were not behavior-tested,
an incorrect minimax implementation reached review, and a model could replace the test
file with a dependency class that compiled but contained no tests. Read-only staged
acceptance tests now validate GameState while its task is writable and activate an
exhaustive solver proof as soon as the original solver stub is replaced. Source-identity
validation rejects wrong-namespace and wrong-type replacements before mutation.

The accepted run is retained at
`artifacts/usability/ollama-tictactoe-20260809T090534Z`. It used Mistral Nemo for Lead,
Gemma 4 for Implementer, and Mistral for Reviewer, all through Ollama. Harness reached
`AwaitingAcceptance` after four completed tasks and one review cycle. The generated
solution built warning-free, all generated and read-only xUnit tests passed, the
independent validator explored 1,119 human branches without finding a forced win, and
the console accepted `q` and exited cleanly. The full exercise took about 21 minutes;
most of that time was local full-file correction inference, so targeted edit repair
remains the principal usability/performance opportunity even though the end-to-end
result is now accepted.
