#!/usr/bin/env python3
"""Drive Harness.NET through a non-trivial, local-Ollama-only usability exercise.

The script creates an isolated .NET repository, asks Harness.NET through its real
Avalonia UI to implement a console Tic-Tac-Toe game and minimax opponent, then runs
both the generated test suite and an independent exhaustive solver validator. It
never configures or authorizes a remote model provider.
"""

from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import html
import json
import os
from pathlib import Path
import runpy
import shlex
import sqlite3
import subprocess
import sys
import time
from typing import Any, Iterator
from urllib.error import URLError
from urllib.request import Request, urlopen


GOAL_TITLE = "Build validated Tic-Tac-Toe with an unbeatable computer"
GOAL_OBJECTIVE = """Build a polished .NET 10 console Tic-Tac-Toe application where a human plays X against a computer playing O.

Requirements:
- Keep the existing solution and project layout. Edit only the existing files under src/TicTacToe and tests/TicTacToe.Tests; do not create, rename, or split projects or source files.
- Keep the engine in namespace TicTacToe.Core with public enum Mark { Empty, X, O }.
- GameState must be immutable and expose a public parameterless constructor, CurrentPlayer, Winner, IsDraw, IReadOnlyList<int> LegalMoves, a zero-based indexer, and GameState Play(int cell).
- MinimaxSolver must expose int ChooseMove(GameState state, Mark computerMark), choose only legal moves, and play optimally so the human cannot force a win.
- The console UI must render the board, accept cells 1-9, reject malformed/occupied moves without crashing, show the result, and allow q to exit immediately.
- Replace the placeholder tests with meaningful deterministic xUnit coverage for wins, draws, invalid moves, legal solver choices, and exhaustive human move sequences proving the O solver never loses.
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
    public Mark CurrentPlayer => Mark.X;
    public Mark Winner => Mark.Empty;
    public bool IsDraw => false;
    public IReadOnlyList<int> LegalMoves => Enumerable.Range(0, 9).ToArray();
    public Mark this[int cell] => cell is >= 0 and < 9
        ? Mark.Empty
        : throw new ArgumentOutOfRangeException(nameof(cell));

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
    [Fact]
    public void Complete_the_engine_and_replace_this_placeholder() =>
        Assert.Fail("Harness.NET must replace this placeholder with deterministic tests.");
}
"""

AGENT_GUIDANCE = """# Generated repository instructions

- Target .NET 10 with nullable analysis and warnings as errors.
- Do not add or update NuGet packages; the repository is restored before Harness starts.
- Keep game rules independent from console input/output.
- Keep the existing two-project layout and edit only existing source and test files;
  model-authored compiler inputs require an exact pre-existing file baseline.
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
        help="tool-capable Ollama model used for Lead, Implementer, and Reviewer",
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


def create_repository(root: Path) -> Path:
    repository = root / "repository"
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
        "dotnet", "add", "tests/TicTacToe.Tests/TicTacToe.Tests.csproj", "reference",
        "src/TicTacToe/TicTacToe.csproj",
    ], repository, environment=environment)
    run([
        "dotnet", "sln", "TicTacToe.slnx", "add", "src/TicTacToe/TicTacToe.csproj",
        "tests/TicTacToe.Tests/TicTacToe.Tests.csproj",
    ], repository, environment=environment)

    write(repository / "AGENTS.md", AGENT_GUIDANCE)
    write(repository / "Directory.Build.props", """<Project>
  <PropertyGroup>
    <NuGetAudit>false</NuGetAudit>
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


def write_configuration(root: Path, endpoint: str, model: str) -> None:
    config = root / "config/harness.net/harness.xml"
    write(config, f"""<?xml version="1.0" encoding="utf-8" ?>
<Harness>
  <Providers>
    <Ollama>
      <Kind>Ollama</Kind>
      <Endpoint>{html.escape(endpoint.rstrip('/') + '/')}</Endpoint>
      <ChatModel>{html.escape(model)}</ChatModel>
      <EmbeddingModel>{html.escape(model)}</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>1800</RequestTimeoutSeconds>
    </Ollama>
  </Providers>
  <Routing>
    <MainLlm>Ollama</MainLlm>
    <Reviewer>Ollama</Reviewer>
    <ToolLlm>Ollama</ToolLlm>
    <Embedding>Ollama</Embedding>
  </Routing>
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
    if state == "NeedsDirection":
        previous = latest_workflow_status(database)
        application.invoke("Retry Lead")
        application.wait_for_name("Retry Lead with changes", "frame")
        application.set_text(
            "Guidance for Lead retry",
            "Return the requested JSON object directly. Do not wrap it in Markdown or code fences.",
        )
        application.invoke("Retry Lead")
        report.setdefault("usability_observations", []).append(
            "The first Lead response used a Markdown fence and entered NeedsDirection; "
            "the exercise retried through the recovery UI with corrective guidance."
        )
        state = wait_for_workflow_update(database, previous, min(timeout, 60))
        if state == "Running":
            state = wait_for_state(
                database, {"AwaitingPlanApproval", "NeedsDirection"}, timeout
            )
    if state != "AwaitingPlanApproval":
        raise RuntimeError(
            "Lead planning still needs user direction after one guided retry; "
            "inspect Harness logs and evidence"
        )
    application.wait_for_name("Approve plan", "push button")


def approve_and_run(application: Any, database: Path, timeout: int) -> None:
    application.invoke("Approve plan")
    application.wait_for_name("Approve plan and capabilities", "frame")
    application.invoke("Approve and create worktree")
    application.wait_for_name("Continue run", "push button")
    application.invoke("Continue run")
    state = wait_for_state(database, {"AwaitingAcceptance", "NeedsDirection"}, timeout)
    if state != "AwaitingAcceptance":
        raise RuntimeError(
            "Implementation/review paused for user direction; inspect Harness logs and evidence"
        )


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

    test_sources = "\n".join(
        path.read_text(encoding="utf-8")
        for path in (worktree / "tests").rglob("*.cs")
    )
    generated_test_cases = test_sources.count("[Fact]") + test_sources.count("[Theory]")
    if "Assert.Fail" in test_sources or generated_test_cases < 4:
        raise RuntimeError(
            "generated validation did not replace the placeholder with at least four test cases"
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
        "model": args.model,
        "result": "running",
    }
    report_path = root / "usability-report.json"
    support = runpy.run_path(str(repository_root / "eng/verify-avalonia-atspi.py"))
    dbus = support["dbus"]
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    database = root / "data/harness.net/harness.db"
    process: subprocess.Popen[str] | None = None
    process_log: Any | None = None
    application: Any | None = None

    session_bus = dbus.SessionBus()
    status_object = session_bus.get_object("org.a11y.Bus", "/org/a11y/bus")
    status_properties = dbus.Interface(status_object, support["PROPERTIES"])
    original_enabled = bool(status_properties.Get("org.a11y.Status", "IsEnabled"))
    original_screen_reader = bool(
        status_properties.Get("org.a11y.Status", "ScreenReaderEnabled")
    )
    status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(True))
    status_properties.Set("org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(True))
    accessibility_address = str(dbus.Interface(status_object, "org.a11y.Bus").GetAddress())
    accessibility_bus = dbus.bus.BusConnection(accessibility_address)

    try:
        with measured(report, "ollama_preflight"):
            verify_ollama(args.ollama_endpoint, args.model)
        if not args.skip_host_build:
            with measured(report, "host_build"):
                run([
                    "dotnet", "build", "src/Harness.Host/Harness.Host.csproj", "--no-restore",
                    "--nologo", "--verbosity", "minimal",
                ], repository_root)
        with measured(report, "seed_repository"):
            repository = create_repository(root)
            write_configuration(root, args.ollama_endpoint, args.model)

        environment = os.environ.copy()
        environment.update({
            "XDG_CONFIG_HOME": str(root / "config"),
            "XDG_DATA_HOME": str(root / "data"),
            "XDG_STATE_HOME": str(root / "state"),
            "XDG_CACHE_HOME": str(root / "cache"),
        })
        environment.update(build_environment_values(root))
        process_log = (root / "harness-process.log").open("w", encoding="utf-8")
        process = subprocess.Popen(
            [str(executable), "--ui=avalonia"],
            env=environment,
            text=True,
            stdout=process_log,
            stderr=subprocess.STDOUT,
        )
        application = support["wait_for_application"](accessibility_bus, process.pid)

        with measured(report, "workspace_registration"):
            register_workspace(application, repository)
        with measured(report, "lead_plan_generation"):
            create_goal_and_generate_plan(
                application, database, report, args.timeout_seconds
            )
        with measured(report, "implementation_and_review"):
            approve_and_run(application, database, args.timeout_seconds)
        with measured(report, "independent_validation"):
            report["validation"] = validate_generated_project(root, repository)

        report["result"] = "passed"
        print(f"Ollama Tic-Tac-Toe usability exercise passed: {root}")
        return 0
    except BaseException as error:
        report["result"] = "failed"
        report["error"] = f"{type(error).__name__}: {error}"
        report["diagnostics"] = diagnostic_state(application, database)
        print(f"Usability exercise failed; diagnostics preserved at {root}", file=sys.stderr)
        raise
    finally:
        report["finished_at"] = datetime.now(timezone.utc).isoformat()
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        if process is not None:
            support["stop"](process)
        if process_log is not None:
            process_log.close()
        status_properties.Set(
            "org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(original_screen_reader)
        )
        status_properties.Set(
            "org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled)
        )


if __name__ == "__main__":
    raise SystemExit(main())
