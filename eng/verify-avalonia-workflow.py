#!/usr/bin/env python3
"""Exercise the complete production Avalonia goal workflow without real inference.

The production host talks through its real Ollama provider boundary to a deterministic
loopback server owned by this verifier. The server emits bounded Lead, Implementer,
and Reviewer responses plus real typed tool calls; it never contacts a configured or
paid provider. The UI is driven exclusively through Linux AT-SPI.
"""

from __future__ import annotations

import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import runpy
import sqlite3
import subprocess
import sys
import tempfile
import threading
import time
from typing import Any, Callable


UPDATED_PROGRAM = 'Console.WriteLine("verified by Harness.NET");\n'
ORIGINAL_PROGRAM = 'Console.WriteLine("before");\n'
ORIGINAL_SHA256 = hashlib.sha256(ORIGINAL_PROGRAM.encode()).hexdigest()


class DeterministicOllamaServer(ThreadingHTTPServer):
    def __init__(self) -> None:
        super().__init__(("127.0.0.1", 0), DeterministicOllamaHandler)
        self.events: list[str] = []
        self.failure: str | None = None

    @property
    def endpoint(self) -> str:
        host, port = self.server_address
        return f"http://{host}:{port}/"


class DeterministicOllamaHandler(BaseHTTPRequestHandler):
    server: DeterministicOllamaServer

    def do_GET(self) -> None:
        if self.path != "/api/tags":
            self.send_error(404)
            return

        self.respond({
            "models": [{
                "name": "harness-v1-deterministic",
                "model": "harness-v1-deterministic",
                "details": {"family": "acceptance", "parameter_size": "0B"},
                "capabilities": ["completion", "tools"],
            }],
        })

    def do_POST(self) -> None:
        if self.path != "/api/chat":
            self.send_error(404)
            return

        try:
            request = json.loads(self.read_request_body())
            self.respond_chat(request)
        except Exception as error:  # pragma: no cover - reported to the parent verifier
            self.server.failure = f"{type(error).__name__}: {error}"
            self.respond({"error": self.server.failure}, status=500)

    def read_request_body(self) -> bytes:
        if self.headers.get("Transfer-Encoding", "").lower() != "chunked":
            length = int(self.headers.get("Content-Length", "0"))
            return self.rfile.read(length)

        chunks: list[bytes] = []
        while True:
            size_line = self.rfile.readline().strip()
            size = int(size_line.split(b";", 1)[0], 16)
            if size == 0:
                while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                    pass
                break
            chunks.append(self.rfile.read(size))
            if self.rfile.read(2) != b"\r\n":
                raise ValueError("invalid HTTP chunk terminator")
        return b"".join(chunks)

    def respond_chat(self, request: dict[str, Any]) -> None:
        messages = request.get("messages", [])
        instructions = "\n".join(
            message.get("content", "")
            for message in messages
            if message.get("role") == "system"
        )
        tool_results = sum(message.get("role") == "tool" for message in messages)
        if "lead agent" in instructions:
            if tool_results == 0:
                self.tool_call("lead", "inspect_dotnet", {})
            else:
                self.content("lead", {
                    "plan": "Inspect the project, update Program.cs, build, test, and review the exact diff.",
                    "tasks": [{
                        "title": "Update the representative greeting",
                        "objective": "Replace the greeting and verify the isolated project.",
                        "fileAreas": ["Program.cs"],
                        "acceptanceCriteria": [
                            "Program.cs contains the approved greeting.",
                            "The project builds and tests without restore.",
                        ],
                    }],
                })
            return

        if "implementer agent" in instructions:
            if tool_results == 0:
                self.tool_call("implementer", "apply_file_edit", {
                    "correlationId": "v1-edit-program",
                    "relativePath": "Program.cs",
                    "expectedSha256": ORIGINAL_SHA256,
                    "content": UPDATED_PROGRAM,
                })
            elif tool_results == 1:
                self.tool_call("implementer", "dotnet_build", {
                    "correlationId": "v1-build",
                })
            elif tool_results == 2:
                self.tool_call("implementer", "dotnet_test", {
                    "correlationId": "v1-test",
                })
            else:
                self.content(
                    "implementer",
                    "Updated Program.cs through the typed edit boundary; the durable build "
                    "and test tool calls both succeeded.",
                )
            return

        if "reviewer agent" in instructions:
            if tool_results == 0:
                self.tool_call("reviewer", "inspect_git", {})
            elif tool_results == 1:
                self.tool_call("reviewer", "list_tool_evidence", {})
            else:
                self.content("reviewer", {
                    "decision": "accept",
                    "summary": "The exact diff is bounded and durable edit, build, and test evidence succeeded.",
                })
            return

        roles = [message.get("role") for message in messages]
        raise AssertionError(
            f"unrecognized agent role prompt; message roles={roles}; "
            f"system={instructions[:500]!r}"
        )

    def tool_call(self, role: str, name: str, arguments: dict[str, Any]) -> None:
        self.server.events.append(f"{role}:{name}")
        self.respond({
            "message": {
                "content": "",
                "tool_calls": [{
                    "function": {"name": name, "arguments": arguments},
                }],
            },
            "done": True,
            "done_reason": "tool_calls",
            "prompt_eval_count": 1,
            "eval_count": 1,
        }, media_type="application/x-ndjson")

    def content(self, role: str, value: str | dict[str, Any]) -> None:
        self.server.events.append(f"{role}:complete")
        content = value if isinstance(value, str) else json.dumps(value, separators=(",", ":"))
        self.respond({
            "message": {"content": content},
            "done": True,
            "done_reason": "stop",
            "prompt_eval_count": 1,
            "eval_count": 1,
        }, media_type="application/x-ndjson")

    def respond(
        self,
        value: dict[str, Any],
        status: int = 200,
        media_type: str = "application/json",
    ) -> None:
        payload = (json.dumps(value, separators=(",", ":")) + "\n").encode()
        self.send_response(status)
        self.send_header("Content-Type", media_type)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, format: str, *args: object) -> None:
        return


def run(
    command: list[str],
    cwd: Path,
    quiet: bool = False,
    environment: dict[str, str] | None = None,
) -> None:
    output = subprocess.DEVNULL if quiet else None
    subprocess.run(
        command,
        cwd=cwd,
        env=environment,
        check=True,
        stdout=output,
        stderr=output,
    )


def wait_until(predicate: Callable[[], bool], message: str, timeout: float = 90) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.2)
    raise AssertionError(message)


def wait_for_text(
    application: Any,
    name: str,
    expected: str,
    timeout: float = 90,
) -> None:
    wait_until(
        lambda: expected in read_accessible_text(application, name),
        f"control {name!r} did not contain {expected!r}",
        timeout,
    )


def read_accessible_text(application: Any, name: str) -> str:
    node = application.find(name)
    return application.text(name, node.role)


def wait_for_name_containing(
    application: Any,
    text: str,
    role: str,
    timeout: float = 90,
) -> None:
    wait_until(
        lambda: any(
            text in node.name and node.role == role
            for node in application.nodes()
        ),
        f"AT-SPI did not expose {role} containing {text!r}",
        timeout,
    )


def create_repository(root: Path, repository_root: Path) -> Path:
    repository = root / "repository"
    run([
        "dotnet", "new", "console", "--framework", "net10.0",
        "--name", "Representative", "--output", str(repository), "--no-restore",
    ], repository_root, quiet=True)
    (repository / "Program.cs").write_text(ORIGINAL_PROGRAM, encoding="utf-8")
    run(["git", "init", "-q"], repository)
    run(["git", "config", "user.name", "Harness Workflow Acceptance"], repository)
    run(["git", "config", "user.email", "workflow@invalid.example"], repository)
    build_environment = os.environ.copy()
    build_environment.update(build_environment_values(root))
    run(
        ["dotnet", "restore", "Representative.csproj"],
        repository,
        quiet=True,
        environment=build_environment,
    )
    run(["git", "add", "."], repository)
    run(["git", "commit", "-qm", "Initial representative repository"], repository)
    run(["git", "branch", "-M", "main"], repository)
    return repository


def build_environment_values(root: Path) -> dict[str, str]:
    return {
        "BaseIntermediateOutputPath": str(root / "build/obj") + os.sep,
        "BaseOutputPath": str(root / "build/bin") + os.sep,
    }


def write_configuration(root: Path, endpoint: str) -> None:
    config = root / "config/harness.net/harness.xml"
    config.parent.mkdir(parents=True, exist_ok=True)
    config.write_text(f"""<?xml version="1.0" encoding="utf-8" ?>
<Harness>
  <Providers>
    <Ollama>
      <Kind>Ollama</Kind>
      <Endpoint>{endpoint}</Endpoint>
      <ChatModel>harness-v1-deterministic</ChatModel>
      <EmbeddingModel>unused</EmbeddingModel>
      <EmbeddingDimensions>1</EmbeddingDimensions>
      <ConnectTimeoutSeconds>2</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>30</RequestTimeoutSeconds>
    </Ollama>
  </Providers>
</Harness>
""", encoding="utf-8")


def create_goal_and_plan(application: Any, title: str) -> None:
    application.invoke("Workspace", "page tab")
    application.invoke("Goals and plans")
    application.wait_for_name("Goals and plans", "frame")
    application.invoke("New goal")
    application.wait_for_name("New goal", "frame")
    application.set_text("Goal title", title)
    application.set_text(
        "Goal objective",
        "Change the representative greeting, build, test, independently review, and commit it.",
    )
    application.set_text("Review-cycle limit", "2")
    application.invoke("Create goal")
    application.wait_for_name_containing(f"{title} — Draft", "list item")
    application.invoke("Start planning…")
    application.wait_for_name("Start Lead planning", "frame")
    application.set_text("Lead maximum output tokens", "1024")
    application.invoke("Run with these limits")
    wait_for_name_containing(
        application,
        f"{title} — AwaitingPlanApproval", "list item"
    )
    application.invoke("Run & evidence", "page tab")
    wait_for_text(
        application,
        "Production workflow details",
        "State: AwaitingPlanApproval",
    )


def reopen_and_approve_plan(application: Any, title: str) -> None:
    application.invoke("Workspace", "page tab")
    application.invoke("Goals and plans")
    application.wait_for_name("Goals and plans", "frame")
    wait_for_name_containing(
        application,
        f"{title} — AwaitingPlanApproval", "list item"
    )
    application.invoke_containing(f"{title} — AwaitingPlanApproval", "list item")
    application.invoke("Approve plan…")
    application.wait_for_name("Approve plan and capabilities", "frame")
    application.invoke("Approve and create worktree")
    wait_for_name_containing(application, f"{title} — Approved", "list item")


def run_implementation_and_review(application: Any) -> None:
    application.invoke("Continue run…")
    application.wait_for_name("Continue production run", "frame")
    application.set_text("Implementer maximum output tokens", "1024")
    application.set_text("Reviewer maximum output tokens", "1024")
    application.invoke("Run with these limits")
    application.invoke("Run & evidence", "page tab")
    wait_for_text(
        application,
        "Production workflow details",
        "State: AwaitingAcceptance",
        timeout=120,
    )
    wait_for_text(
        application,
        "Production workflow details",
        "Independent reviewer accepted review cycle 1",
    )


def approve_exact_commit(application: Any) -> None:
    application.invoke("Exact commit…")
    application.wait_for_name("Exact commit approval", "frame")
    wait_for_text(
        application,
        "Exact commit fingerprint",
        "State: unrequested exact preview",
    )
    application.set_text("Commit message", "Update representative greeting")
    application.set_text("Commit author name", "Harness Workflow Acceptance")
    application.set_text("Commit author email", "workflow@invalid.example")
    application.invoke("Record pending request")
    wait_for_text(
        application,
        "Exact commit fingerprint",
        "State: Pending",
    )
    application.invoke("Approve exact diff…")
    application.wait_for_name("Approve exact commit", "frame")
    application.invoke("Approve and commit")
    wait_for_text(
        application,
        "Exact commit fingerprint",
        "State: Committed",
    )


def verify_durable_outcome(root: Path, repository: Path, provider: DeterministicOllamaServer) -> None:
    worktree_lines = subprocess.check_output(
        ["git", "-C", str(repository), "worktree", "list", "--porcelain"], text=True
    ).splitlines()
    worktrees = [
        Path(line.removeprefix("worktree "))
        for line in worktree_lines
        if line.startswith("worktree ")
    ]
    goal_worktrees = [path for path in worktrees if path.resolve() != repository.resolve()]
    if len(goal_worktrees) != 1:
        raise AssertionError(f"expected one isolated goal worktree, found {goal_worktrees}")
    worktree = goal_worktrees[0]
    if (repository / "Program.cs").read_text(encoding="utf-8") != ORIGINAL_PROGRAM:
        raise AssertionError("the original user repository was mutated")
    if (worktree / "Program.cs").read_text(encoding="utf-8") != UPDATED_PROGRAM:
        raise AssertionError("the approved worktree does not contain the verified edit")
    if subprocess.check_output(
        ["git", "-C", str(repository), "rev-list", "--count", "HEAD"], text=True
    ).strip() != "1":
        raise AssertionError("the original branch commit history changed")
    if subprocess.check_output(
        ["git", "-C", str(worktree), "rev-list", "--count", "HEAD"], text=True
    ).strip() != "2":
        raise AssertionError("the isolated goal branch does not contain the approved commit")
    if subprocess.check_output(
        ["git", "-C", str(worktree), "status", "--porcelain"], text=True
    ).strip():
        raise AssertionError("the committed goal worktree is not clean")
    message = subprocess.check_output(
        ["git", "-C", str(worktree), "log", "-1", "--format=%B"], text=True
    )
    if "Update representative greeting" not in message or "Harness-Diff-SHA256:" not in message:
        raise AssertionError("the goal commit does not retain its exact-diff fingerprint")

    expected_events = [
        "lead:inspect_dotnet",
        "lead:complete",
        "implementer:apply_file_edit",
        "implementer:dotnet_build",
        "implementer:dotnet_test",
        "implementer:complete",
        "reviewer:inspect_git",
        "reviewer:list_tool_evidence",
        "reviewer:complete",
    ]
    if provider.failure is not None:
        raise AssertionError(f"deterministic provider failed: {provider.failure}")
    if provider.events != expected_events:
        raise AssertionError(f"unexpected provider/tool sequence: {provider.events}")

    database = root / "data/harness.net/harness.db"
    with sqlite3.connect(database) as connection:
        run_state = connection.execute(
            "SELECT state FROM goal_workflow_runs ORDER BY created_at DESC LIMIT 1"
        ).fetchone()
        checkpoint_kinds = [row[0] for row in connection.execute(
            "SELECT kind FROM goal_workflow_checkpoints ORDER BY sequence"
        )]
        tool_evidence = list(connection.execute(
            "SELECT correlation_id, tool_name, state FROM tool_calls ORDER BY started_at"
        ))
        commit_state = connection.execute(
            "SELECT state, commit_sha, changed_file_count FROM goal_commit_approvals"
        ).fetchone()
    if run_state != ("Completed",):
        raise AssertionError(f"workflow did not complete durably: {run_state}")
    if checkpoint_kinds != [
        "Started", "LeadCallStarted", "PlanProposed", "PlanApproved",
        "ImplementerCallStarted", "ImplementationProduced", "ReviewerCallStarted",
        "ReviewCompleted", "Accepted",
    ]:
        raise AssertionError(f"unexpected durable workflow checkpoints: {checkpoint_kinds}")
    if tool_evidence != [
        ("v1-edit-program", "FileEdit", "Succeeded"),
        ("v1-build", "Build", "Succeeded"),
        ("v1-test", "Test", "Succeeded"),
    ]:
        raise AssertionError(f"unexpected durable tool evidence: {tool_evidence}")
    if commit_state is None or commit_state[0] != "Committed" or not commit_state[1] or commit_state[2] != 1:
        raise AssertionError(f"exact commit approval was not durably completed: {commit_state}")


def main() -> int:
    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("the Avalonia workflow verifier requires a graphical Linux session")

    repository_root = Path(__file__).resolve().parent.parent
    support = runpy.run_path(str(repository_root / "eng/verify-avalonia-atspi.py"))
    dbus = support["dbus"]
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    run([
        "dotnet", "build", "src/Harness.Host/Harness.Host.csproj", "--no-restore",
        "--nologo", "--verbosity", "quiet",
    ], repository_root)

    session_bus = dbus.SessionBus()
    status_object = session_bus.get_object("org.a11y.Bus", "/org/a11y/bus")
    status_properties = dbus.Interface(status_object, support["PROPERTIES"])
    original_enabled = bool(status_properties.Get("org.a11y.Status", "IsEnabled"))
    original_screen_reader = bool(
        status_properties.Get("org.a11y.Status", "ScreenReaderEnabled")
    )
    status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(True))
    status_properties.Set(
        "org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(True)
    )
    accessibility_address = str(dbus.Interface(status_object, "org.a11y.Bus").GetAddress())
    accessibility_bus = dbus.bus.BusConnection(accessibility_address)

    provider = DeterministicOllamaServer()
    provider_thread = threading.Thread(target=provider.serve_forever, daemon=True)
    provider_thread.start()
    process: subprocess.Popen[bytes] | None = None
    application: Any | None = None
    try:
        with tempfile.TemporaryDirectory(prefix="harness-workflow-") as temporary:
            root = Path(temporary)
            repository = create_repository(root, repository_root)
            write_configuration(root, provider.endpoint)
            environment = os.environ.copy()
            environment.update({
                "XDG_CONFIG_HOME": str(root / "config"),
                "XDG_DATA_HOME": str(root / "data"),
                "XDG_STATE_HOME": str(root / "state"),
                "XDG_CACHE_HOME": str(root / "cache"),
            })
            environment.update(build_environment_values(root))

            process, application = support["launch"](
                executable, environment, accessibility_bus
            )
            support["verify_initial_accessibility"](application)
            support["register_workspace"](application, repository)
            title = "Production workflow acceptance"
            create_goal_and_plan(application, title)
            if provider.events != ["lead:inspect_dotnet", "lead:complete"]:
                raise AssertionError(f"lead planning did not use the expected route: {provider.events}")

            # Inject a real process interruption at the durable plan-approval boundary.
            support["stop"](process)
            process = None
            with sqlite3.connect(root / "data/harness.net/harness.db") as connection:
                interrupted_state = connection.execute(
                    "SELECT state FROM goal_workflow_runs ORDER BY created_at DESC LIMIT 1"
                ).fetchone()
            if interrupted_state != ("AwaitingPlanApproval",):
                raise AssertionError(f"planning boundary was not durable: {interrupted_state}")

            process, application = support["launch"](
                executable, environment, accessibility_bus
            )
            reopen_and_approve_plan(application, title)
            run_implementation_and_review(application)
            approve_exact_commit(application)
            support["stop"](process)
            process = None
            verify_durable_outcome(root, repository, provider)
    except BaseException:
        print(f"Deterministic provider events: {provider.events}", file=sys.stderr)
        print(f"Deterministic provider failure: {provider.failure}", file=sys.stderr)
        if application is not None:
            for name in ("Goal operation status", "Production workflow details"):
                try:
                    print(f"{name}:\n{read_accessible_text(application, name)}", file=sys.stderr)
                except Exception as error:
                    print(f"Could not read {name}: {error}", file=sys.stderr)
            try:
                choices = [
                    node.name for node in application.nodes() if node.role == "list item"
                ]
                print(f"Visible list items: {choices}", file=sys.stderr)
            except Exception as error:
                print(f"Could not inspect list items: {error}", file=sys.stderr)
        raise
    finally:
        if process is not None:
            support["stop"](process)
        provider.shutdown()
        provider.server_close()
        provider_thread.join(timeout=5)
        status_properties.Set(
            "org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(original_screen_reader)
        )
        status_properties.Set(
            "org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled)
        )

    print("Avalonia production edit/build/test/review/exact-commit workflow passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
