# Local-model regression corpus

Task 038 is delivered.

## Scope

The versioned corpus is in `eng/local-model-regression/scenarios/v1`. It contains the
live Tic-Tac-Toe workflow plus deterministic scenarios for semantic edits, multi-file
Build/Test work, retry, partial completion, cancellation, model unavailability,
server restart, truncated output, malformed tool calls, and unsupported combinations
of reasoning and tools.

Ordinary runs use deterministic fixtures. Live inference requires both `--live` and
an explicit Ollama model. The runner never configures OpenRouter or another paid
provider. Live model comparisons are sequential, reject model files larger than
16 GiB before inference, and stop if observed Ollama VRAM use exceeds 16 GiB.

Each run records the Harness revision, scenario and prompt, route and model identity,
discovered capabilities, MCP and agent tool traces, elapsed time, peak resources,
bounded diff, validation evidence, metrics, and terminal outcome. Failed and
interrupted live runs retain partial diffs and evidence. Repository state, allowed
paths, Build/Test results, semantic-tool use, rewrite size, and evidence are checked
independently of model prose.

## Verification

The deterministic acceptance run on 2026-08-12 passed all ten fixture scenarios:

```text
python3 eng/test_local_model_regression.py
10 tests passed

./eng/verify-local-model-regression.py
10 scenarios passed; liveInference=false; paidInference=false; boundedConcurrency=1
```

An unchanged report compared with itself classified all ten scenarios as
`unchanged`. Comparison uses metric deltas and pass/fail state, not generated patch
text.

The repository gate also passed:

```text
dotnet build Harness.slnx --no-restore
0 warnings, 0 errors

dotnet test Harness.slnx --no-build --no-restore -m:1
616 passed, 0 failed
```

## Live dogfood result

A local-only run used Mistral Nemo for Lead and Reviewer and Qwen2.5-Coder 14B for
Implementer. The Lead produced a valid four-task plan. Qwen made targeted edits to
`GameState.cs`; every intermediate edit compiled. Four test runs exposed a missing
terminal-state guard. Explicit retry guidance then produced a fifth edit and a
passing test run. Observed Qwen VRAM use was 15,126,117,743 bytes with a 32,768-token
loaded context.

The run also found a control-flow defect in the test driver: a successful explicit
Implementer retry leaves the Harness workflow at a durable `Running` boundary and
requires an explicit resume before the next task. The driver now recognizes that
boundary, waits until the retry operation is idle, and calls `harness_resume_goal`.
Deterministic regression tests cover the exact typed MCP call sequence and the brief
active-operation race.

A follow-up run crossed that boundary and completed both `GameState` and
`MinimaxSolver`. It then reached `Program.cs`, where Qwen repeated candidates missing
the `TicTacToe.Core` import and nullable `Console.ReadLine` handling. Roslyn rejected
all of them before mutation. Recovery guidance now names those two invariants and
forbids replaying an unchanged rejected candidate. This remains a model-quality
partial baseline, not a passing Tic-Tac-Toe result.

Earlier dogfood attempts remain useful negative baselines: Ornith returned prose or
stalled without tools, Mistral emitted empty or invalid structured edits, and Gemma
replaced most of the regression module while falsely reporting completion. None of
those isolated worktrees were merged.

## Reproduce and compare

```bash
./eng/verify-local-model-regression.py

./eng/verify-local-model-regression.py \
  --live \
  --scenario tictactoe \
  --ollama-endpoint http://127.0.0.1:11434 \
  --model mistral-nemo:latest \
  --implementer-model qwen2.5-coder:14b \
  --reviewer-model mistral-nemo:latest

./eng/verify-local-model-regression.py \
  --baseline artifacts/local-model-regression/BASELINE/report.json
```

Outputs are ignored under `artifacts/local-model-regression/<run>`. Keep a run by
copying or renaming that directory. Delete one run safely with:

```bash
./eng/verify-local-model-regression.py \
  --clean artifacts/local-model-regression/RUN
```

The cleanup option rejects the artifact root and paths outside it.
