# Ollama Tic-Tac-Toe usability test

`eng/verify-ollama-tictactoe-usability.py` drives an isolated Harness.NET instance
through its authenticated stateless MCP surface and uses real Ollama models for
planning, implementation, review, and recovery. It is not part of the deterministic
release gate.

## Test repository

The script creates an isolated persistent directory containing:

- a compiling .NET 10 console app, xUnit project, and read-only acceptance project;
- warnings-as-errors configuration;
- private XDG configuration with Ollama only;
- Harness.NET SQLite state, logs, and goal worktree;
- workflow/checkpoint metrics in `usability-report.json`;
- Build/Test, console smoke, and independent validator output.

The goal is a human-X versus computer-O game with an immutable engine, input
validation, minimax solver, and generated tests.

## Workflow checks

The script:

1. starts an ephemeral loopback-only MCP server with an owner-only bearer token;
2. selects models and generates a plan;
3. approves the plan;
4. runs the real Implementer and Reviewer workflow;
5. uses explicit retry guidance if a role reaches `NeedsDirection`, then explicitly
   resumes after a successful retried task;
6. records MCP calls, checkpoints, routes, recovery, edit attempts, and tool calls.

Exact-file generation accepts fenced C# source. Later corrections use one to four
exact `SEARCH`/`REPLACE` blocks against the last candidate. Prose is rejected before
compiler validation. Correction prompts retain the full goal and load C# files cited
by compiler or test evidence through typed reads.

Every accepted C# edit must add no compiler warning or error, including in dependent
projects. Harness.NET then runs a no-restore solution build and deterministic tests.
Replacement source must keep the target namespace and at least one existing target
type.

The structured local-file proposal path disables reasoning because it expects a small
machine-readable edit. Normal tool calls retain provider-default reasoning.

## Independent validation

After `AwaitingAcceptance`, the script:

1. builds with warnings as errors and runs generated tests without restore;
2. rejects a skipped placeholder, fewer than four tests, or tests without assertions;
3. compiles a separate validator against the required engine API;
4. checks wins, draws, range, occupied cells, turns, and immutability;
5. enumerates every reachable human branch and rejects a solver that permits a human
   win or returns an illegal move;
6. starts the console app, sends `q`, and requires a bounded clean exit.

## Run

```bash
./eng/verify-ollama-tictactoe-usability.py \
  --ollama-endpoint http://127.0.0.1:11434 \
  --model ornith:9b
```

`HARNESS_OLLAMA_ENDPOINT` and `HARNESS_OLLAMA_MODEL` provide defaults. The script uses
no OpenRouter provider. It retains artifacts on success and failure. Real local
inference may take several minutes.

The versioned wrapper is `eng/verify-local-model-regression.py`. It adds corpus
metadata, model comparison, deterministic validators, bounded partial-result capture,
and cleanup. See
[the Task 038 acceptance record](local-model-regression-2026-08-12.md).

## Accepted run: 2026-08-09

Artifact: `artifacts/usability/ollama-tictactoe-20260809T090534Z`

Models:

- Lead: Mistral Nemo;
- Implementer: Gemma 4;
- Reviewer: Mistral.

Result:

- four tasks completed;
- one review cycle;
- warning-free build;
- generated and read-only xUnit tests passed;
- independent validator checked 1,119 human branches without a forced human win;
- console quit test passed;
- elapsed time: about 21 minutes.

Most time was spent on local full-file correction inference.

## Failed run: 2026-08-10

Artifact: `artifacts/usability/ollama-tictactoe-20260810T121124Z`

This run found and fixed test-harness issues: correction prompts had dropped the goal,
stack traces referenced source not supplied to correction, and prose could reach the
source parser. The script now preserves the goal, reads cited C# files, rejects prose,
limits correction attempts, and records aggregate metrics.

Observed local models:

- Qwen 3 14B: fast with reasoning disabled; produced duplicate declarations.
- Ornith 9B: did not return a bounded proposal in this run.
- Gemma 4 and Mistral Nemo: repeatedly derived turn state from one board cell.
- Codestral: rejected before inference because it did not declare the required tools.
- Qwen2.5-Coder 14B: best result in the run, but did not meet the draw contract.

The run stopped after about 9 minutes 39 seconds. It recorded seven GameState edit
attempts, five accepted edits, two rejected edits, five successful builds, five failed
test runs, and four recovery boundaries. Three acceptance tests passed. The draw test
failed because `Winner` threw instead of returning `Mark.Empty`. The generated test
project still contained its skipped placeholder. This is a failed result.
