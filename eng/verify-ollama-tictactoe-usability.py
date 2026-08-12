#!/usr/bin/env python3
"""Drive Harness.NET through a non-trivial, local-Ollama-only usability exercise.

The script creates an isolated .NET repository, drives Harness.NET through its
authenticated stateless MCP control surface, and independently validates the result.
It never configures or authorizes a remote model provider.
"""

from __future__ import annotations

import argparse
import base64
from contextlib import contextmanager
from datetime import datetime, timezone
import html
import json
import os
from pathlib import Path
import secrets
import shlex
import sqlite3
import socket
import subprocess
import sys
import tempfile
import time
from typing import Any, Iterator
from urllib.error import URLError
from urllib.request import Request, urlopen

from local_model_regression import (
    LiveGoalDriver,
    MAX_TRACE_ITEMS,
    SCHEMA_VERSION,
    StatelessMcpClient,
    bounded_text,
    collect_ollama_identity,
    derive_metrics,
    semantic_validation_summary,
    sha256_text,
)


GOAL_TITLE = "Build validated Tic-Tac-Toe with an unbeatable computer"
GOAL_OBJECTIVE = """Build a polished .NET 10 console Tic-Tac-Toe application where a human plays X against a computer playing O.

Requirements:
- Keep the existing solution and three-project layout, including the read-only deterministic acceptance tests. The complete mutation allow-list is exactly src/TicTacToe/GameState.cs, src/TicTacToe/MinimaxSolver.cs, src/TicTacToe/Program.cs, and tests/TicTacToe.Tests/UnitTest1.cs. Use those exact existing paths as plan file areas. Do not create, rename, or split projects or source files, and never edit tests/TicTacToe.Acceptance.
- Keep the engine in namespace TicTacToe.Core with public enum Mark { Empty, X, O }.
- GameState must be immutable and expose a public parameterless constructor, CurrentPlayer, Winner, IsDraw, IReadOnlyList<int> LegalMoves, a zero-based indexer, and GameState Play(int cell). Store all nine marks in private state. The public constructor creates an empty X-to-move board. Play validates range, occupancy, and terminal state; clones the board; writes CurrentPlayer; evaluates the eight winning lines and full-board draw; and returns a new state with the next player. The indexer and LegalMoves must reflect the stored board.
- MinimaxSolver must expose int ChooseMove(GameState state, Mark computerMark), choose only legal moves, and play optimally so the human cannot force a win. Recursively score terminal states from computerMark's perspective, maximize on the computer turn, minimize on its opponent's turn, and use a deterministic legal tie-break.
- The console UI must render the board, accept cells 1-9, reject malformed/occupied moves without crashing, show the result, and allow q to exit immediately. Console.ReadLine() is nullable: receive every result in string? and treat null/EOF like q.
- Replace the placeholder tests with concise deterministic xUnit coverage for wins, draws, invalid moves, and representative legal solver choices. Every Fact or Theory must execute at least one meaningful Assert; do not emit empty, comment-only, placeholder, or vacuous tests. A known reachable full-board draw is the move sequence 0,1,2,4,3,5,7,6,8; use it rather than guessing draw data. The independent acceptance validator performs the exhaustive human move traversal, so do not duplicate that traversal in the generated test file.
- Do not add packages, restore dependencies, weaken warnings, or edit AGENTS.md. Build and test the complete solution without restore and inspect the exact Git diff before reporting completion.
"""

GAME_STATE_STUB = """namespace TicTacToe.Core;

public enum Mark
{
    Empty,
    X,
    O,
}

public sealed class GameState
{
    private readonly Mark[] board;

    public GameState() : this(new Mark[9])
    {
    }

    private GameState(Mark[] board)
    {
        this.board = board;
    }

    public Mark CurrentPlayer => throw new NotImplementedException();
    public Mark Winner => throw new NotImplementedException();
    public bool IsDraw => throw new NotImplementedException();
    public IReadOnlyList<int> LegalMoves => throw new NotImplementedException();
    public Mark this[int cell] => throw new NotImplementedException();

    public GameState Play(int cell) => throw new NotImplementedException();
}
"""

SOLVER_STUB = """namespace TicTacToe.Core;

public sealed class MinimaxSolver
{
    public int ChooseMove(GameState state, Mark computerMark) =>
        throw new NotImplementedException();
}
"""

PROGRAM_STUB = """Console.WriteLine("Tic-Tac-Toe implementation required. Press q to exit.");
if (Console.ReadLine()?.Trim().Equals("q", StringComparison.OrdinalIgnoreCase) is true)
{
    return;
}
"""

TEST_STUB = """namespace TicTacToe.Tests;

public sealed class ImplementationTests
{
    [Fact(Skip = "HARNESS_GENERATION_PLACEHOLDER")]
    public void Complete_the_engine_and_replace_this_placeholder()
    {
    }
}
"""

AGENT_GUIDANCE = """# Generated repository instructions

- Target .NET 10 with nullable analysis and warnings as errors.
- Do not add or update NuGet packages; the repository is restored before Harness starts.
- Keep game rules independent from console input/output.
- Keep the existing three-project layout and edit only the four explicitly authorized files;
  model-authored compiler inputs require an exact pre-existing file baseline.
- The only editable files are src/TicTacToe/GameState.cs,
  src/TicTacToe/MinimaxSolver.cs, src/TicTacToe/Program.cs, and
  tests/TicTacToe.Tests/UnitTest1.cs. Plans and tool calls must use these exact paths.
- tests/TicTacToe.Acceptance is a read-only deterministic contract and exhaustive
  validator. Inspect it when needed, but never edit it.
- Preserve the public TicTacToe.Core API described in the active goal because an
  independent external validator compiles against it.
- Preserve Directory.Build.props; it keeps restored intermediates available to the
  isolated Git worktree without adding generated files to the repository.
- Use immutable game states and deterministic tests.
- Build and test the solution with --no-restore before claiming completion.
"""

VALIDATOR_PROGRAM = r"""using TicTacToe.Core;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static GameState Play(params int[] moves)
{
    GameState state = new();
    foreach (int move in moves)
    {
        state = state.Play(move);
    }
    return state;
}

GameState xWin = Play(0, 3, 1, 4, 2);
Require(xWin.Winner == Mark.X, "top-row X win was not detected");
Require(!xWin.IsDraw, "winning state was reported as a draw");

GameState draw = Play(0, 1, 2, 4, 3, 5, 7, 6, 8);
Require(draw.Winner == Mark.Empty && draw.IsDraw, "full drawn board was not detected");

GameState initial = new();
Require(initial.CurrentPlayer == Mark.X, "X must play first");
Require(initial.LegalMoves.Count == 9, "new board must expose nine legal moves");
Require(Enumerable.Range(0, 9).All(cell => initial[cell] == Mark.Empty),
    "new board contains a non-empty cell");

try
{
    _ = initial.Play(9);
    throw new InvalidOperationException("out-of-range move was accepted");
}
catch (ArgumentOutOfRangeException)
{
}

try
{
    _ = initial.Play(0).Play(0);
    throw new InvalidOperationException("occupied move was accepted");
}
catch (InvalidOperationException error) when (error.Message != "occupied move was accepted")
{
}

MinimaxSolver solver = new();
int exploredHumanBranches = 0;

void ExploreHumanTurns(GameState state)
{
    if (state.Winner != Mark.Empty || state.IsDraw)
    {
        Require(state.Winner != Mark.X, "human found a forced win against the solver");
        return;
    }

    Require(state.CurrentPlayer == Mark.X, "exhaustive traversal expected the human turn");
    foreach (int humanMove in state.LegalMoves.ToArray())
    {
        exploredHumanBranches++;
        GameState afterHuman = state.Play(humanMove);
        if (afterHuman.Winner != Mark.Empty || afterHuman.IsDraw)
        {
            Require(afterHuman.Winner != Mark.X, "human found a forced win against the solver");
            continue;
        }

        int[] before = afterHuman.LegalMoves.ToArray();
        int computerMove = solver.ChooseMove(afterHuman, Mark.O);
        Require(before.Contains(computerMove), "solver selected an illegal move");
        Require(before.SequenceEqual(afterHuman.LegalMoves), "solver mutated the supplied state");
        ExploreHumanTurns(afterHuman.Play(computerMove));
    }
}

ExploreHumanTurns(new GameState());
Require(exploredHumanBranches >= 100, "exhaustive traversal covered too few human branches");
Console.WriteLine($"Independent validation passed; explored {exploredHumanBranches} human branches.");
"""

ACCEPTANCE_TESTS = r"""using TicTacToe.Core;

namespace TicTacToe.Acceptance;

public sealed class GameStateContractTests
{
    [Fact]
    public void Alternating_moves_preserve_turns_and_detect_a_win()
    {
        GameState state = new();
        Assert.Equal(Mark.X, state.CurrentPlayer);

        state = state.Play(0);
        Assert.Equal(Mark.O, state.CurrentPlayer);
        state = state.Play(3);
        Assert.Equal(Mark.X, state.CurrentPlayer);
        state = state.Play(1);
        Assert.Equal(Mark.O, state.CurrentPlayer);
        state = state.Play(4);
        Assert.Equal(Mark.X, state.CurrentPlayer);
        state = state.Play(2);

        Assert.Equal(Mark.X, state.Winner);
        Assert.False(state.IsDraw);
        Assert.Throws<InvalidOperationException>(() => state.Play(5));
    }

    [Fact]
    public void Full_non_winning_board_is_a_draw()
    {
        GameState state = new();
        foreach (int move in new[] { 0, 1, 2, 4, 3, 5, 7, 6, 8 })
        {
            state = state.Play(move);
        }

        Assert.Equal(Mark.Empty, state.Winner);
        Assert.True(state.IsDraw);
        Assert.Empty(state.LegalMoves);
    }

    [Fact]
    public void Moves_validate_range_and_occupancy_without_mutating_the_source()
    {
        GameState initial = new();
        GameState next = initial.Play(4);

        Assert.Equal(Mark.Empty, initial[4]);
        Assert.Equal(Mark.X, next[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => initial.Play(9));
        Assert.Throws<InvalidOperationException>(() => next.Play(4));
    }

    [Fact]
    public void Computer_solver_cannot_lose_against_any_human_line()
    {
        MinimaxSolver solver = new();
        try
        {
            _ = solver.ChooseMove(new GameState().Play(0), Mark.O);
        }
        catch (NotImplementedException)
        {
            // The solver's original repository stub is expected while the earlier GameState
            // task is validated. Once the solver task replaces it, the proof below must run.
            return;
        }

        int exploredHumanBranches = 0;

        void Explore(GameState state, List<int> moves)
        {
            if (state.Winner != Mark.Empty || state.IsDraw)
            {
                Assert.True(state.Winner != Mark.X,
                    $"Human forced a win through moves: {string.Join(",", moves)}");
                return;
            }

            Assert.Equal(Mark.X, state.CurrentPlayer);
            foreach (int humanMove in state.LegalMoves.ToArray())
            {
                exploredHumanBranches++;
                GameState afterHuman = state.Play(humanMove);
                List<int> afterHumanMoves = [.. moves, humanMove];
                if (afterHuman.Winner != Mark.Empty || afterHuman.IsDraw)
                {
                    Assert.True(afterHuman.Winner != Mark.X,
                        $"Human forced a win through moves: {string.Join(",", afterHumanMoves)}");
                    continue;
                }

                int[] legal = afterHuman.LegalMoves.ToArray();
                int computerMove = solver.ChooseMove(afterHuman, Mark.O);
                Assert.Contains(computerMove, legal);
                Assert.Equal(legal, afterHuman.LegalMoves);
                Explore(afterHuman.Play(computerMove), [.. afterHumanMoves, computerMove]);
            }
        }

        Explore(new GameState(), []);
        Assert.True(exploredHumanBranches >= 100);
    }
}
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ollama-endpoint",
        default=os.environ.get("HARNESS_OLLAMA_ENDPOINT", "http://127.0.0.1:11434"),
        help="Ollama base endpoint (default: HARNESS_OLLAMA_ENDPOINT or localhost)",
    )
    parser.add_argument(
        "--model",
        default=os.environ.get("HARNESS_OLLAMA_MODEL", "gemma4:latest"),
        help="tool-capable Ollama model used for Lead and as the other role fallback",
    )
    parser.add_argument(
        "--implementer-model",
        help="tool-capable Ollama model used for Implementer (default: --model)",
    )
    parser.add_argument(
        "--reviewer-model",
        help="tool-capable Ollama model used for Reviewer (default: --model)",
    )
    parser.add_argument(
        "--recovery-implementer-model",
        action="append",
        default=[],
        help="additional tool-capable Ollama model selectable for Implementer recovery; may be repeated",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        help="persistent diagnostic directory (default: artifacts/usability/timestamp)",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=1800,
        help="maximum wait for each model-driven workflow boundary",
    )
    parser.add_argument("--skip-host-build", action="store_true")
    return parser.parse_args()


def run(
    command: list[str],
    cwd: Path,
    *,
    environment: dict[str, str] | None = None,
    capture: bool = False,
    timeout: int | None = None,
) -> str:
    result = subprocess.run(
        command,
        cwd=cwd,
        env=environment,
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=timeout,
    )
    output = result.stdout or ""
    if result.returncode != 0:
        raise RuntimeError(
            f"Command failed ({result.returncode}): {shlex.join(command)}\n{output.rstrip()}"
        )
    return output if capture else ""


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def ollama_json(endpoint: str, path: str, payload: dict[str, str] | None = None) -> Any:
    body = None if payload is None else json.dumps(payload).encode()
    request = Request(
        endpoint.rstrip("/") + path,
        data=body,
        headers={"Content-Type": "application/json"} if body is not None else {},
        method="POST" if body is not None else "GET",
    )
    try:
        with urlopen(request, timeout=20) as response:
            return json.load(response)
    except URLError as error:
        raise RuntimeError(f"Ollama is unavailable at {endpoint}: {error}") from error


def verify_ollama(endpoint: str, model: str) -> None:
    catalog = ollama_json(endpoint, "/api/tags")
    names = {
        value
        for item in catalog.get("models", [])
        for value in (item.get("name"), item.get("model"))
        if value
    }
    if model not in names:
        raise RuntimeError(
            f"Ollama model {model!r} is not installed; available models: {sorted(names)}"
        )
    details = ollama_json(endpoint, "/api/show", {"model": model})
    capabilities = set(details.get("capabilities", []))
    if "tools" not in capabilities:
        raise RuntimeError(
            f"Ollama model {model!r} does not advertise tool support: {sorted(capabilities)}"
        )


def build_environment_values(root: Path) -> dict[str, str]:
    return {
        "HarnessUsabilityBuildRoot": str(root / "build") + os.sep,
    }


def create_repository(root: Path, repository: Path | None = None) -> Path:
    repository = repository or root / "repository"
    repository.mkdir(parents=True)
    environment = os.environ.copy()
    environment.update(build_environment_values(root))
    run(["dotnet", "new", "sln", "--format", "slnx", "--name", "TicTacToe"], repository,
        environment=environment)
    run([
        "dotnet", "new", "console", "--framework", "net10.0", "--name", "TicTacToe",
        "--output", "src/TicTacToe", "--no-restore",
    ], repository, environment=environment)
    run([
        "dotnet", "new", "xunit", "--framework", "net10.0", "--name", "TicTacToe.Tests",
        "--output", "tests/TicTacToe.Tests", "--no-restore",
    ], repository, environment=environment)
    run([
        "dotnet", "new", "xunit", "--framework", "net10.0",
        "--name", "TicTacToe.Acceptance",
        "--output", "tests/TicTacToe.Acceptance", "--no-restore",
    ], repository, environment=environment)
    run([
        "dotnet", "add", "tests/TicTacToe.Tests/TicTacToe.Tests.csproj", "reference",
        "src/TicTacToe/TicTacToe.csproj",
    ], repository, environment=environment)
    run([
        "dotnet", "add", "tests/TicTacToe.Acceptance/TicTacToe.Acceptance.csproj",
        "reference", "src/TicTacToe/TicTacToe.csproj",
    ], repository, environment=environment)
    run([
        "dotnet", "sln", "TicTacToe.slnx", "add", "src/TicTacToe/TicTacToe.csproj",
        "tests/TicTacToe.Tests/TicTacToe.Tests.csproj",
        "tests/TicTacToe.Acceptance/TicTacToe.Acceptance.csproj",
    ], repository, environment=environment)

    write(repository / "AGENTS.md", AGENT_GUIDANCE)
    write(repository / "Directory.Build.props", """<Project>
  <PropertyGroup>
    <NuGetAudit>false</NuGetAudit>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <PropertyGroup Condition="'$(HarnessUsabilityBuildRoot)' != ''">
    <BaseIntermediateOutputPath>$(HarnessUsabilityBuildRoot)obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
    <BaseOutputPath>$(HarnessUsabilityBuildRoot)bin/$(MSBuildProjectName)/</BaseOutputPath>
  </PropertyGroup>
</Project>
""")
    write(repository / "Directory.Packages.props", """<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
""")
    write(repository / "src/TicTacToe/Program.cs", PROGRAM_STUB)
    write(repository / "src/TicTacToe/GameState.cs", GAME_STATE_STUB)
    write(repository / "src/TicTacToe/MinimaxSolver.cs", SOLVER_STUB)
    write(repository / "tests/TicTacToe.Tests/UnitTest1.cs", TEST_STUB)
    write(repository / "tests/TicTacToe.Acceptance/UnitTest1.cs", ACCEPTANCE_TESTS)
    run(["dotnet", "restore", "TicTacToe.slnx", "-m:1"], repository,
        environment=environment)
    run([
        "dotnet", "build", "TicTacToe.slnx", "--no-restore", "-warnaserror", "-m:1",
    ], repository, environment=environment)
    run(["git", "init", "-q"], repository)
    run(["git", "config", "user.name", "Harness Usability Exercise"], repository)
    run(["git", "config", "user.email", "usability@invalid.example"], repository)
    run(["git", "config", "commit.gpgsign", "false"], repository)
    run(["git", "add", "."], repository)
    run(["git", "commit", "-qm", "Seed Tic-Tac-Toe usability project"], repository)
    run(["git", "branch", "-M", "main"], repository)
    return repository


def write_configuration(
    root: Path,
    endpoint: str,
    lead_model: str,
    implementer_model: str,
    reviewer_model: str,
    recovery_implementer_models: list[str],
    mcp_endpoint: str | None = None,
) -> None:
    config = root / (
        "config/harness.xml" if mcp_endpoint is not None
        else "config/harness.net/harness.xml"
    )
    recovery_providers = "\n".join(
        f"""    <RecoveryImplementer{index}>
      <Kind>Ollama</Kind>
      <Endpoint>{html.escape(endpoint.rstrip('/') + '/')}</Endpoint>
      <ChatModel>{html.escape(model)}</ChatModel>
      <EmbeddingModel>{html.escape(model)}</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>1800</RequestTimeoutSeconds>
    </RecoveryImplementer{index}>"""
        for index, model in enumerate(recovery_implementer_models, start=1)
    )
    inbound = "" if mcp_endpoint is None else f"""
  <InboundMcp>
    <Enabled>true</Enabled>
    <Mode>IsolatedEvaluation</Mode>
    <Endpoint>{html.escape(mcp_endpoint)}</Endpoint>
    <RequestTimeoutSeconds>300</RequestTimeoutSeconds>
    <ResultLimit>5000</ResultLimit>
    <AuditRetention>10000</AuditRetention>
    <AllowedClients><Client>local-regression</Client></AllowedClients>
    <AllowedTools>
      <Tool>harness_application</Tool>
      <Tool>harness_workspace</Tool>
      <Tool>harness_goals</Tool>
      <Tool>harness_evidence</Tool>
      <Tool>harness_create_goal</Tool>
      <Tool>harness_goal_models</Tool>
      <Tool>harness_select_goal_model</Tool>
      <Tool>harness_start_planning</Tool>
      <Tool>harness_resume_goal</Tool>
      <Tool>harness_retry_goal</Tool>
      <Tool>harness_cancel_goal_operation</Tool>
      <Tool>harness_decide_plan</Tool>
      <Tool>harness_commit_preview</Tool>
      <Tool>harness_build</Tool>
      <Tool>harness_test</Tool>
      <Tool>harness_audit</Tool>
      <Tool>harness_evaluation_snapshot</Tool>
    </AllowedTools>
    <ApprovalRequiredTools />
  </InboundMcp>"""
    write(config, f"""<?xml version="1.0" encoding="utf-8" ?>
<Harness>
  <Providers>
    <LeadOllama>
      <Kind>Ollama</Kind>
      <Endpoint>{html.escape(endpoint.rstrip('/') + '/')}</Endpoint>
      <ChatModel>{html.escape(lead_model)}</ChatModel>
      <EmbeddingModel>{html.escape(lead_model)}</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>1800</RequestTimeoutSeconds>
    </LeadOllama>
    <ImplementerOllama>
      <Kind>Ollama</Kind>
      <Endpoint>{html.escape(endpoint.rstrip('/') + '/')}</Endpoint>
      <ChatModel>{html.escape(implementer_model)}</ChatModel>
      <EmbeddingModel>{html.escape(implementer_model)}</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>1800</RequestTimeoutSeconds>
    </ImplementerOllama>
    <ReviewerOllama>
      <Kind>Ollama</Kind>
      <Endpoint>{html.escape(endpoint.rstrip('/') + '/')}</Endpoint>
      <ChatModel>{html.escape(reviewer_model)}</ChatModel>
      <EmbeddingModel>{html.escape(reviewer_model)}</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>1800</RequestTimeoutSeconds>
    </ReviewerOllama>
{recovery_providers}
  </Providers>
  <Routing>
    <MainLlm>LeadOllama</MainLlm>
    <Reviewer>ReviewerOllama</Reviewer>
    <ToolLlm>ImplementerOllama</ToolLlm>
    <Embedding>LeadOllama</Embedding>
  </Routing>
{inbound}
</Harness>
""")


def latest_workflow_state(database: Path) -> str | None:
    if not database.is_file():
        return None
    with sqlite3.connect(database) as connection:
        row = connection.execute(
            "SELECT state FROM goal_workflow_runs ORDER BY updated_at DESC LIMIT 1"
        ).fetchone()
    return None if row is None else str(row[0])


def latest_workflow_status(database: Path) -> tuple[str, str] | None:
    if not database.is_file():
        return None
    with sqlite3.connect(database) as connection:
        row = connection.execute(
            "SELECT state, updated_at FROM goal_workflow_runs "
            "ORDER BY updated_at DESC LIMIT 1"
        ).fetchone()
    return None if row is None else (str(row[0]), str(row[1]))


def workflow_checkpoint_count(database: Path) -> int:
    if not database.is_file():
        return 0
    with sqlite3.connect(database) as connection:
        row = connection.execute(
            "SELECT COUNT(*) FROM goal_workflow_checkpoints"
        ).fetchone()
    return 0 if row is None else int(row[0])


def workflow_activity_count(database: Path) -> int:
    if not database.is_file():
        return 0
    with sqlite3.connect(database) as connection:
        checkpoints = connection.execute(
            "SELECT COUNT(*) FROM goal_workflow_checkpoints"
        ).fetchone()
        tool_calls = connection.execute("SELECT COUNT(*) FROM tool_calls").fetchone()
    return int(checkpoints[0]) + int(tool_calls[0])


def wait_for_checkpoint_count(database: Path, minimum: int, timeout: int) -> str:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if workflow_checkpoint_count(database) >= minimum:
            return latest_workflow_state(database) or ""
        time.sleep(0.5)
    raise TimeoutError(
        f"workflow did not persist checkpoint {minimum} in {timeout}s; "
        f"latest={latest_workflow_state(database)}"
    )


def wait_for_workflow_update(
    database: Path, previous: tuple[str, str] | None, timeout: int
) -> str:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        current = latest_workflow_status(database)
        if current is not None and current != previous:
            return current[0]
        time.sleep(0.5)
    raise TimeoutError("workflow did not persist a state update after retry")


def wait_for_state(database: Path, expected: set[str], timeout: int) -> str:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        state = latest_workflow_state(database)
        if state in expected:
            return state
        time.sleep(0.5)
    raise TimeoutError(
        f"workflow did not reach {sorted(expected)} in {timeout}s; latest={latest_workflow_state(database)}"
    )


def wait_for_state_with_progress(
    database: Path, expected: set[str], inactivity_timeout: int
) -> str:
    deadline = time.monotonic() + inactivity_timeout
    activity_count = workflow_activity_count(database)
    while time.monotonic() < deadline:
        state = latest_workflow_state(database)
        if state in expected:
            return state
        current_count = workflow_activity_count(database)
        if current_count > activity_count:
            activity_count = current_count
            deadline = time.monotonic() + inactivity_timeout
        time.sleep(0.5)
    raise TimeoutError(
        "workflow produced no checkpoint or tool-call progress for "
        f"{inactivity_timeout}s while waiting for {sorted(expected)}; "
        f"latest={latest_workflow_state(database)}"
    )


def register_workspace(application: Any, repository: Path) -> None:
    application.invoke("Workspace", "page tab")
    application.invoke("Manage workspaces")
    application.wait_for_name("Manage workspaces", "frame")
    application.set_text("Repository path", str(repository))
    application.invoke("Inspect")
    application.wait_for_name("TicTacToe.slnx", "list item")
    application.invoke("Register")
    application.wait_for_name_containing("untrusted", "list item")
    application.invoke("Trust…")
    application.wait_for_name("Trust workspace", "frame")
    application.invoke("Trust workspace")
    application.wait_for_name_containing("trusted", "list item")
    application.invoke("Close")
    application.wait_for_name_containing("Trust: Trusted", "label")


def create_goal_and_generate_plan(
    application: Any,
    database: Path,
    report: dict[str, Any],
    timeout: int,
) -> None:
    application.invoke("Conversation", "page tab")
    application.set_text("Goal or message composer", f"{GOAL_TITLE}\n\n{GOAL_OBJECTIVE}")
    application.invoke("Submit composer")
    application.wait_for_name_containing(f"Goal: {GOAL_TITLE}", "panel")
    application.invoke("Generate plan")
    application.wait_for_name("Generate goal plan", "frame")

    # The isolated configuration contains one eligible model, selected by default.
    # Avalonia currently exposes this AutoCompleteBox as a panel rather than an
    # editable AT-SPI control, so keyboard-driven selection cannot be automated.
    report.setdefault("usability_observations", []).append(
        "The plan model search is exposed to AT-SPI as a panel, not an editable control; "
        "the exercise used the configured Ollama default."
    )
    application.invoke("Generate plan")
    state = wait_for_state(database, {"AwaitingPlanApproval", "NeedsDirection"}, timeout)
    lead_recoveries = 0
    while state == "NeedsDirection" and lead_recoveries < 6:
        previous = latest_workflow_status(database)
        application.invoke("Retry Lead")
        application.wait_for_name("Retry Lead with changes", "frame")
        application.set_text(
            "Guidance for Lead retry",
            "Return the requested JSON object directly. Every task must have a non-empty "
            "title, objective, fileAreas array, and acceptanceCriteria array. Use only the "
            "four exact existing paths authorized by the goal. Do not wrap the object in "
            "Markdown or code fences. Do not create standalone inspect, analyze, discover, "
            "assessment, planning, or documentation tasks; inspection belongs inside each "
            "implementation task.",
        )
        application.invoke("Retry Lead")
        lead_recoveries += 1
        report.setdefault("usability_observations", []).append(
            "A Lead response was not usable delegation JSON and entered "
            "NeedsDirection; the exercise retried through the recovery UI with "
            "corrective guidance."
        )
        state = wait_for_workflow_update(database, previous, min(timeout, 60))
        if state == "Running":
            state = wait_for_state(
                database, {"AwaitingPlanApproval", "NeedsDirection"}, timeout
            )
    if state != "AwaitingPlanApproval":
        raise RuntimeError(
            "Lead planning still needs user direction after six guided retries; "
            "inspect Harness logs and evidence"
        )
    application.wait_for_name("Approve plan", "push button")


def approve_and_run(
    application: Any,
    database: Path,
    report: dict[str, Any],
    timeout: int,
    recovery_implementer_models: list[str],
) -> None:
    application.invoke("Approve plan")
    application.wait_for_name("Approve plan and capabilities", "frame")
    application.invoke("Approve and create worktree")
    application.wait_for_name("Continue run", "push button")
    application.invoke("Continue run")
    recoveries = 0
    while True:
        state = wait_for_state_with_progress(
            database, {"AwaitingAcceptance", "NeedsDirection"}, timeout
        )
        if state == "AwaitingAcceptance":
            return
        if recoveries >= 8:
            raise RuntimeError(
                "Implementation/review still needs direction after eight bounded recoveries"
            )

        nodes = application.nodes()
        retry_names = {
            node.name for node in nodes
            if node.role == "push button" and node.name.startswith("Retry ")
        }
        retry_name = next(
            (name for name in ("Retry Implementer", "Retry Reviewer")
             if name in retry_names),
            None,
        )
        if retry_name is None:
            raise RuntimeError(
                "Workflow needs direction but exposes no Implementer or Reviewer retry action"
            )

        role = retry_name.removeprefix("Retry ")
        before = workflow_checkpoint_count(database)
        application.invoke(retry_name)
        application.wait_for_name(f"Retry {role} with changes", "frame")
        if role == "Implementer" and recovery_implementer_models:
            recovery_model = recovery_implementer_models[
                recoveries % len(recovery_implementer_models)
            ]
            application.invoke("Show all models")
            time.sleep(0.5)
            matches = [
                node for node in application.nodes()
                if node.role == "list item" and recovery_model in node.name
            ]
            if matches:
                application.invoke_node(matches[-1])
                report.setdefault("usability_observations", []).append(
                    f"Selected replacement Implementer model {recovery_model} through the recovery UI."
                )
            else:
                application.invoke("Show all models")
                report.setdefault("usability_observations", []).append(
                    f"Recovery model {recovery_model} was correctly absent from the "
                    "Implementer-compatible selector; retained the current route."
                )
        guidance = (
            "Use the typed tools now. Preserve every passing method and repair only the first "
            "relevant user-code stack frame or Roslyn diagnostic range. Read the exact existing "
            "target path first and pass its sha256 as expectedSha256. Before consuming GameState "
            "or MinimaxSolver, use get_symbol_info and find_symbol_definition to verify the exact "
            "public signature and accessibility; never invent a constructor or helper. Correct a "
            "rejected request with a new correlation id, run the relevant build/test, and do not "
            "return a prose-only status."
            if role == "Implementer"
            else
            "Inspect Git diff and durable tool evidence, then return only the required "
            "structured reviewer decision."
        )
        application.set_text(f"Guidance for {role} retry", guidance)
        application.invoke(retry_name)
        recoveries += 1
        report.setdefault("usability_observations", []).append(
            f"The workflow paused for {role}; the exercise used the explicit retry UI "
            "with bounded corrective guidance."
        )
        state = wait_for_checkpoint_count(database, before + 2, min(timeout, 600))
        if state == "Running":
            application.wait_for_name("Continue run", "push button")
            application.invoke("Continue run")


def goal_worktree(repository: Path) -> Path:
    lines = run(
        ["git", "-C", str(repository), "worktree", "list", "--porcelain"],
        repository,
        capture=True,
    ).splitlines()
    worktrees = [
        Path(line.removeprefix("worktree "))
        for line in lines
        if line.startswith("worktree ")
    ]
    candidates = [path for path in worktrees if path.resolve() != repository.resolve()]
    if len(candidates) != 1:
        raise RuntimeError(f"expected one isolated goal worktree, found {candidates}")
    return candidates[0]


def validate_generated_project(root: Path, repository: Path) -> dict[str, Any]:
    worktree = goal_worktree(repository)
    environment = os.environ.copy()
    environment.update(build_environment_values(root))
    build_output = run(
        ["dotnet", "build", "TicTacToe.slnx", "--no-restore", "-warnaserror", "-m:1"],
        worktree,
        environment=environment,
        capture=True,
        timeout=300,
    )
    test_output = run(
        ["dotnet", "test", "TicTacToe.slnx", "--no-build", "--no-restore", "-m:1"],
        worktree,
        environment=environment,
        capture=True,
        timeout=300,
    )

    generated_test_source = (
        worktree / "tests/TicTacToe.Tests/UnitTest1.cs"
    ).read_text(encoding="utf-8")
    generated_test_cases = (
        generated_test_source.count("[Fact") + generated_test_source.count("[Theory")
    )
    generated_assertions = generated_test_source.count("Assert.")
    if (
        "HARNESS_GENERATION_PLACEHOLDER" in generated_test_source
        or generated_test_cases < 4
        or generated_assertions < generated_test_cases
    ):
        raise RuntimeError(
            "generated validation requires at least four non-vacuous test cases with an assertion per case"
        )

    validator = root / "independent-validator"
    project_reference = html.escape(str(worktree / "src/TicTacToe/TicTacToe.csproj"))
    write(validator / "TicTacToe.Validation.csproj", f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{project_reference}" />
  </ItemGroup>
</Project>
""")
    write(validator / "Program.cs", VALIDATOR_PROGRAM)
    run(["dotnet", "restore", "TicTacToe.Validation.csproj"], validator,
        environment=environment, capture=True, timeout=180)
    validator_output = run(
        ["dotnet", "run", "--project", "TicTacToe.Validation.csproj", "--no-restore"],
        validator,
        environment=environment,
        capture=True,
        timeout=300,
    )

    app = subprocess.run(
        ["dotnet", "run", "--project", "src/TicTacToe/TicTacToe.csproj", "--no-build", "--no-restore"],
        cwd=worktree,
        env=environment,
        input="q\n",
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=30,
        check=True,
    )
    if "tic-tac-toe" not in app.stdout.lower():
        raise RuntimeError("console smoke test did not identify the generated game")
    if run(["git", "status", "--porcelain"], repository, capture=True).strip():
        raise RuntimeError("Harness mutated the original user repository")

    write(root / "validation/build.log", build_output)
    write(root / "validation/test.log", test_output)
    write(root / "validation/independent-validator.log", validator_output)
    write(root / "validation/console-smoke.log", app.stdout)
    return {
        "worktree": str(worktree),
        "generated_test_cases": generated_test_cases,
        "independent_validator": validator_output.strip(),
        "console_smoke": app.stdout.strip(),
    }


@contextmanager
def measured(report: dict[str, Any], name: str) -> Iterator[None]:
    started = time.monotonic()
    try:
        yield
    finally:
        report.setdefault("phases", {})[name] = round(time.monotonic() - started, 3)


def diagnostic_state(application: Any | None, database: Path) -> dict[str, Any]:
    result: dict[str, Any] = {"workflow_state": latest_workflow_state(database)}
    if application is not None:
        result["visible_controls"] = [
            {"role": node.role, "name": node.name}
            for node in application.nodes()
            if node.name
        ]
    if database.is_file():
        with sqlite3.connect(database) as connection:
            result["checkpoints"] = [
                {"kind": row[0], "actor": row[1], "summary": row[2]}
                for row in connection.execute(
                    "SELECT kind, actor, summary FROM goal_workflow_checkpoints "
                    "ORDER BY created_at, sequence"
                )
            ]
            result["tool_calls"] = [
                {
                    "tool": row[0],
                    "state": row[1],
                    "path": row[2],
                    "error_code": row[3],
                    "error": row[4],
                }
                for row in connection.execute(
                    "SELECT tool_name, state, json_extract(request_json, '$.path'), "
                    "json_extract(result_json, '$.errorCode'), "
                    "json_extract(result_json, '$.error') FROM tool_calls "
                    "ORDER BY started_at DESC LIMIT 20"
                )
            ]
    return result


def workflow_metrics(database: Path) -> dict[str, Any]:
    if not database.is_file():
        return {}
    with sqlite3.connect(database) as connection:
        file_edits = [
            {
                "path": row[0],
                "attempts": row[1],
                "succeeded": row[2],
                "failed": row[3],
            }
            for row in connection.execute(
                "SELECT json_extract(request_json, '$.path'), COUNT(*), "
                "SUM(CASE WHEN state = 'Succeeded' THEN 1 ELSE 0 END), "
                "SUM(CASE WHEN state = 'Failed' THEN 1 ELSE 0 END) "
                "FROM tool_calls WHERE tool_name = 'FileEdit' "
                "GROUP BY json_extract(request_json, '$.path') ORDER BY MIN(started_at)"
            )
        ]
        tool_states = [
            {"tool": row[0], "state": row[1], "count": row[2]}
            for row in connection.execute(
                "SELECT tool_name, state, COUNT(*) FROM tool_calls "
                "GROUP BY tool_name, state ORDER BY tool_name, state"
            )
        ]
        role_routes = [
            {"role": row[0], "provider": row[1], "model": row[2]}
            for row in connection.execute(
                "SELECT role, provider, model FROM goal_model_selections ORDER BY role"
            )
        ]
        recovery_count = connection.execute(
            "SELECT COUNT(*) FROM goal_workflow_checkpoints "
            "WHERE kind = 'UserDirectionRequired'"
        ).fetchone()[0]
    return {
        "file_edits": file_edits,
        "tool_states": tool_states,
        "role_routes": role_routes,
        "recovery_boundaries": recovery_count,
    }


def available_loopback_endpoint() -> str:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return f"http://127.0.0.1:{listener.getsockname()[1]}/mcp"


def process_rss_bytes(process: subprocess.Popen[str]) -> int:
    try:
        status = Path(f"/proc/{process.pid}/status").read_text(encoding="utf-8")
        line = next(value for value in status.splitlines() if value.startswith("VmRSS:"))
        return int(line.split()[1]) * 1024
    except (FileNotFoundError, StopIteration, ValueError):
        return 0


def wait_for_mcp(
    client: StatelessMcpClient,
    process: subprocess.Popen[str],
    timeout_seconds: int,
) -> dict[str, Any]:
    deadline = time.monotonic() + min(timeout_seconds, 60)
    last_error: BaseException | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"Harness exited before MCP startup ({process.returncode})")
        try:
            return client.call("harness_application", {})
        except BaseException as error:
            last_error = error
            time.sleep(0.25)
    raise TimeoutError(f"Harness MCP did not start: {last_error}")


def internal_tool_trace(database: Path) -> list[dict[str, Any]]:
    if not database.is_file():
        return []
    with sqlite3.connect(database) as connection:
        rows = connection.execute(
            "SELECT tool_name, state, request_json, result_json, started_at, completed_at "
            "FROM tool_calls ORDER BY started_at LIMIT ?",
            (MAX_TRACE_ITEMS,),
        ).fetchall()
    trace: list[dict[str, Any]] = []
    for tool, state, request_json, result_json, started_at, completed_at in rows:
        request = json.loads(request_json)
        result = json.loads(result_json) if result_json else {}
        path = request.get("relativePath") or request.get("path")
        semantic_validation, introduced_errors = semantic_validation_summary(result)
        trace.append({
            "kind": "tool",
            "tool": tool,
            "state": state,
            "path": path,
            "mutation": tool in {"FileEdit", "ApplySymbolRename"},
            "changedLines": int(result.get("changedLines", 0) or 0),
            "semanticValidation": semantic_validation,
            "introducedCompilerErrors": introduced_errors,
            "error": result.get("error"),
            "errorCode": result.get("errorCode"),
            "startedAt": started_at,
            "completedAt": completed_at,
        })
        if semantic_validation:
            trace.append({
                "kind": "tool",
                "tool": "RoslynEditValidation",
                "state": "Succeeded",
                "path": path,
                "mutation": False,
                "changedLines": 0,
                "introducedCompilerErrors": introduced_errors,
                "startedAt": started_at,
                "completedAt": completed_at,
            })
    return trace


def changed_lines(worktree: Path) -> int:
    output = run(["git", "diff", "--numstat"], worktree, capture=True)
    total = 0
    for line in output.splitlines():
        added, removed, _ = line.split("\t", 2)
        if added.isdigit():
            total += int(added)
        if removed.isdigit():
            total += int(removed)
    return total


def drive_goal_with_mcp(
    client: StatelessMcpClient,
    instance_id: str,
    workspace_id: str,
    report: dict[str, Any],
    timeout_seconds: int,
    models: dict[str, Any],
    sample_resources: Any,
) -> tuple[str, dict[str, Any]]:
    driver = LiveGoalDriver(
        client, instance_id, workspace_id, timeout_seconds, sample_resources)
    goal_id = driver.create_goal(GOAL_TITLE, GOAL_OBJECTIVE)
    report["goal_id"] = goal_id
    for role, provider, model in (
        ("Lead", "LeadOllama", models["lead"]),
        ("Implementer", "ImplementerOllama", models["implementer"]),
        ("Reviewer", "ReviewerOllama", models["reviewer"]),
    ):
        driver.select_model(goal_id, role, provider, model)

    goal = driver.start_planning(goal_id)
    lead_retries = 0
    while int(goal["workflow"]["state"]) == LiveGoalDriver.WORKFLOW_NEEDS_DIRECTION:
        if lead_retries >= 6:
            raise RuntimeError("Lead did not produce a valid bounded plan after six retries")
        lead_retries += 1
        goal = driver.retry(
            goal_id,
            "Lead",
            "Return only the required JSON object. Use the exact four editable paths from "
            "the goal as file areas. Every task needs non-empty title, objective, "
            "fileAreas, and acceptanceCriteria. Fold inspection and validation into "
            "implementation tasks.",
        )
    driver.approve_plan(goal, "Local regression plan is bounded to the fixture allow-list.")
    goal = driver.resume(goal_id)

    role_names = {1: "Implementer", 2: "Reviewer"}
    recoveries = 0
    while int(goal["workflow"]["state"]) == LiveGoalDriver.WORKFLOW_NEEDS_DIRECTION:
        if recoveries >= 8:
            raise RuntimeError("Implementation/review did not recover after eight retries")
        retry_role = role_names.get(goal["workflow"].get("retryRole"))
        if retry_role is None:
            raise RuntimeError("Harness requested direction without an actionable retry role")
        guidance = (
            "Use typed tools and make a small targeted edit. Read the exact target before "
            "editing, preserve passing code, use Roslyn symbol tools before shared API "
            "changes, and run Build and Test before reporting. Preserve the exact namespace "
            "and target declarations from the current file. For GameState.Play, reject a "
            "move when Winner is non-empty or IsDraw is true before checking and cloning "
            "the requested cell. Program.cs must import TicTacToe.Core and receive every "
            "Console.ReadLine result as string?. Repair only the first failing diagnostic "
            "or test assertion; do not repeat a rejected candidate unchanged."
            if retry_role == "Implementer" else
            "Inspect the exact diff and durable evidence, then return the required "
            "structured review decision."
        )
        recoveries += 1
        goal = driver.retry(goal_id, retry_role, guidance)
        if int(goal["workflow"]["state"]) == LiveGoalDriver.WORKFLOW_RUNNING:
            goal = driver.resume(goal_id)
    report["lead_retries"] = lead_retries
    report["workflow_recoveries"] = recoveries
    return goal_id, goal


def main() -> int:
    args = parse_args()
    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("this Avalonia usability exercise requires a graphical Linux session")

    repository_root = Path(__file__).resolve().parent.parent
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    root = (args.output_root or
            repository_root / "artifacts/usability" / f"ollama-tictactoe-{timestamp}").resolve()
    if root.exists():
        raise SystemExit(f"output directory already exists: {root}")
    root.mkdir(parents=True)
    report: dict[str, Any] = {
        "started_at": datetime.now(timezone.utc).isoformat(),
        "ollama_endpoint": args.ollama_endpoint,
        "models": {
            "lead": args.model,
            "implementer": args.implementer_model or args.model,
            "reviewer": args.reviewer_model or args.model,
            "recovery_implementers": args.recovery_implementer_model,
        },
        "result": "running",
    }
    report_path = root / "usability-report.json"
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    evaluation_root = Path(tempfile.mkdtemp(prefix="harness-tictactoe-mcp-"))
    database = evaluation_root / "data/harness.db"
    repository = evaluation_root / "data/evaluation-fixture"
    mcp_endpoint = available_loopback_endpoint()
    token = base64.b64encode(secrets.token_bytes(48)).decode("ascii")
    token_file = evaluation_root / "mcp-token"
    token_file.write_text(token, encoding="utf-8")
    token_file.chmod(0o600)
    report["evaluation_root"] = str(evaluation_root)
    report["mcp_endpoint"] = mcp_endpoint
    process: subprocess.Popen[str] | None = None
    process_log: Any | None = None
    client = StatelessMcpClient(mcp_endpoint, token)
    peak_host_rss = 0
    peak_model_vram = 0
    selected_models: list[str] = []
    goal_id: str | None = None
    started_monotonic = time.monotonic()

    def sample_resources() -> None:
        nonlocal peak_host_rss, peak_model_vram
        if process is not None:
            peak_host_rss = max(peak_host_rss, process_rss_bytes(process))
        request = Request(
            f"{args.ollama_endpoint.rstrip('/')}/api/ps",
            headers={"Accept": "application/json"},
        )
        with urlopen(request, timeout=5) as response:
            payload = json.load(response)
        loaded_vram = [
            int(model.get("size_vram", 0) or 0)
            for model in payload.get("models", [])
        ]
        peak_model_vram = max([peak_model_vram, *loaded_vram])
        if peak_model_vram > 16 * 1024**3:
            raise RuntimeError(
                f"Ollama runtime VRAM {peak_model_vram} exceeds the 16 GiB limit")

    try:
        with measured(report, "ollama_preflight"):
            selected_models = [
                report["models"]["lead"],
                report["models"]["implementer"],
                report["models"]["reviewer"],
                *report["models"]["recovery_implementers"],
            ]
            for model in dict.fromkeys(selected_models):
                verify_ollama(args.ollama_endpoint, model)
                identity = collect_ollama_identity(args.ollama_endpoint, model)
                if int(identity.get("modelSizeBytes", 0) or 0) > 16 * 1024**3:
                    raise RuntimeError(
                        f"Ollama model {model!r} exceeds the 16 GB regression limit")
        if not args.skip_host_build:
            with measured(report, "host_build"):
                run([
                    "dotnet", "build", "src/Harness.Host/Harness.Host.csproj", "--no-restore",
                    "--nologo", "--verbosity", "minimal",
                ], repository_root)
        with measured(report, "seed_repository"):
            create_repository(root, repository)
            write_configuration(
                evaluation_root,
                args.ollama_endpoint,
                report["models"]["lead"],
                report["models"]["implementer"],
                report["models"]["reviewer"],
                report["models"]["recovery_implementers"],
                mcp_endpoint,
            )

        environment = os.environ.copy()
        environment.update(build_environment_values(root))
        process_log = (root / "harness-process.log").open("w", encoding="utf-8")
        process = subprocess.Popen(
            [
                str(executable), "--ui=avalonia",
                "--mcp-evaluation-root", str(evaluation_root),
                "--mcp-evaluation-token-file", str(token_file),
            ],
            env=environment,
            text=True,
            stdout=process_log,
            stderr=subprocess.STDOUT,
        )
        with measured(report, "mcp_startup"):
            application = wait_for_mcp(client, process, args.timeout_seconds)
            workspace = client.call("harness_workspace", {})
        with measured(report, "mcp_goal_workflow"):
            goal_id, goal = drive_goal_with_mcp(
                client,
                application["instanceId"],
                workspace["workspace"]["id"],
                report,
                args.timeout_seconds,
                report["models"],
                sample_resources,
            )
        workflow_state = int(goal["workflow"]["state"])
        if workflow_state not in {
            LiveGoalDriver.WORKFLOW_AWAITING_ACCEPTANCE,
            LiveGoalDriver.WORKFLOW_COMPLETED,
        }:
            raise RuntimeError(f"workflow stopped in non-complete state {workflow_state}")
        with measured(report, "independent_validation"):
            report["validation"] = validate_generated_project(root, repository)

        worktree = Path(report["validation"]["worktree"])
        diff = run(["git", "diff", "--no-ext-diff"], worktree, capture=True)
        trace = internal_tool_trace(database)
        tools = {item["tool"] for item in trace if item["state"] == "Succeeded"}
        required = {"Build", "Test"}
        missing = sorted(required - tools)
        if missing:
            raise RuntimeError(f"workflow omitted required typed tools: {missing}")
        if not any(item.get("semanticValidation") for item in trace):
            raise RuntimeError("workflow omitted durable Roslyn edit-validation evidence")
        rewrite_lines = changed_lines(worktree)
        if rewrite_lines > 500:
            raise RuntimeError(f"workflow rewrite size {rewrite_lines} exceeds 500 lines")

        evidence = client.call("harness_evidence", {
            "goalId": goal_id, "maximumResults": 500, "continuation": None,
        })
        model_identities = [
            collect_ollama_identity(args.ollama_endpoint, model)
            for model in dict.fromkeys(selected_models)
        ]
        events: list[dict[str, Any]] = [
            {"kind": "plan", "valid": goal.get("plan") is not None},
            *trace,
            *({"kind": "retry"} for _ in range(
                int(report["lead_retries"]) + int(report["workflow_recoveries"]))),
            {"kind": "compiler", "introduced": sum(
                int(item.get("introducedCompilerErrors", 0)) for item in trace)},
            {"kind": "review", "findings": 0},
            {"kind": "terminal", "outcome": "completed"},
        ]
        elapsed_ms = round(sum(report.get("phases", {}).values()) * 1000)
        metrics = derive_metrics(events, elapsed_ms, peak_host_rss)
        metrics["rewriteLines"] = rewrite_lines
        report["regression_run"] = {
            "schemaVersion": SCHEMA_VERSION,
            "harnessRevision": run(
                ["git", "rev-parse", "HEAD"], repository_root, capture=True).strip(),
            "scenario": {
                "id": "tictactoe",
                "version": 1,
                "prompt": GOAL_OBJECTIVE,
                "promptSha256": sha256_text(GOAL_OBJECTIVE),
            },
            "modelServer": model_identities,
            "discoveredCapabilities": sorted({
                capability
                for identity in model_identities
                for capability in identity.get("capabilities", [])
            }),
            "routes": workflow_metrics(database).get("role_routes", []),
            "resource": {
                "peakHostRssBytes": peak_host_rss,
                "peakModelVramBytes": peak_model_vram,
            },
            "controlTrace": client.trace[:MAX_TRACE_ITEMS],
            "toolTrace": trace,
            "diff": bounded_text(diff),
            "evidence": [
                *evidence.get("evidence", {}).get("items", []),
                {"evidenceKind": "build", "path": "validation/build.log"},
                {"evidenceKind": "test", "path": "validation/test.log"},
                {"evidenceKind": "diff", "sha256": sha256_text(diff)},
                {"evidenceKind": "independent-validator",
                 "path": "validation/independent-validator.log"},
            ],
            "terminalOutcome": "completed",
            "metrics": metrics,
            "passed": True,
        }

        report["result"] = "passed"
        print(f"Ollama Tic-Tac-Toe usability exercise passed: {root}")
        return 0
    except BaseException as error:
        report["result"] = "failed"
        report["error"] = f"{type(error).__name__}: {error}"
        report["diagnostics"] = diagnostic_state(None, database)
        trace = internal_tool_trace(database)
        successful_tools = {
            item["tool"] for item in trace if item.get("state") == "Succeeded"
        }
        partial = bool(successful_tools.intersection({"FileEdit", "CreateFile"}))
        diff = ""
        rewrite_lines = 0
        try:
            worktree = goal_worktree(repository)
            diff = run(["git", "diff", "--no-ext-diff"], worktree, capture=True)
            rewrite_lines = changed_lines(worktree)
        except (OSError, RuntimeError, subprocess.CalledProcessError):
            pass
        identities: list[dict[str, Any]] = []
        for model in dict.fromkeys(selected_models):
            try:
                identities.append(collect_ollama_identity(args.ollama_endpoint, model))
            except (OSError, RuntimeError, URLError):
                identities.append({"provider": "Ollama", "model": model,
                                   "available": False})
        events: list[dict[str, Any]] = [
            {"kind": "plan", "valid": any(
                item.get("tool") == "Build" for item in trace)},
            *trace,
            *({"kind": "retry"} for _ in range(
                int(report.get("lead_retries", 0)) +
                int(report.get("workflow_recoveries", 0)))),
            {"kind": "compiler", "introduced": sum(
                int(item.get("introducedCompilerErrors", 0)) for item in trace)},
            {"kind": "terminal", "outcome": "partial" if partial else "failed"},
        ]
        elapsed_ms = round((time.monotonic() - started_monotonic) * 1000)
        metrics = derive_metrics(events, elapsed_ms, peak_host_rss)
        metrics["rewriteLines"] = rewrite_lines
        report["regression_run"] = {
            "schemaVersion": SCHEMA_VERSION,
            "harnessRevision": run(
                ["git", "rev-parse", "HEAD"], repository_root, capture=True).strip(),
            "scenario": {
                "id": "tictactoe",
                "version": 1,
                "prompt": GOAL_OBJECTIVE,
                "promptSha256": sha256_text(GOAL_OBJECTIVE),
            },
            "modelServer": identities,
            "discoveredCapabilities": sorted({
                capability
                for identity in identities
                for capability in identity.get("capabilities", [])
            }),
            "routes": workflow_metrics(database).get("role_routes", []),
            "resource": {
                "peakHostRssBytes": peak_host_rss,
                "peakModelVramBytes": peak_model_vram,
            },
            "controlTrace": client.trace[:MAX_TRACE_ITEMS],
            "toolTrace": trace,
            "diff": bounded_text(diff),
            "evidence": [
                {"evidenceKind": "diff", "sha256": sha256_text(diff)},
                {"evidenceKind": "failure", "detail": report["error"]},
            ],
            "terminalOutcome": "partial" if partial else "failed",
            "metrics": metrics,
            "passed": False,
            "validationFailures": [report["error"]],
        }
        print(f"Usability exercise failed; diagnostics preserved at {root}", file=sys.stderr)
        raise
    finally:
        report["finished_at"] = datetime.now(timezone.utc).isoformat()
        report["workflow_metrics"] = workflow_metrics(database)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        if process is not None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        if process_log is not None:
            process_log.close()


if __name__ == "__main__":
    raise SystemExit(main())
