#!/usr/bin/env python3
"""Versioned local-model regression contracts, validation, and MCP transport."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import time
from typing import Any, Iterable
from urllib.request import Request, urlopen


SCHEMA_VERSION = "harness-local-model-regression-v1"
MAX_TRACE_ITEMS = 500
MAX_TEXT_CHARACTERS = 64_000
TERMINAL_OUTCOMES = {
    "completed", "partial", "failed", "cancelled", "unavailable", "restarted",
    "truncated", "malformed_tool_call", "unsupported_capability",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def bounded_text(value: str, maximum: int = MAX_TEXT_CHARACTERS) -> str:
    return value if len(value) <= maximum else value[:maximum] + "\n[truncated]"


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class Scenario:
    scenario_id: str
    version: int
    kind: str
    prompt: str
    expected_outcome: str
    allowed_paths: tuple[str, ...]
    required_tools: tuple[str, ...]
    semantic_tools: tuple[str, ...]
    required_evidence: tuple[str, ...]
    maximum_rewrite_lines: int
    fixture_events: tuple[dict[str, Any], ...]

    @staticmethod
    def load(path: Path) -> "Scenario":
        value = json.loads(path.read_text(encoding="utf-8"))
        required = {
            "schemaVersion", "id", "version", "kind", "prompt", "expectedOutcome",
            "validation", "fixtureEvents",
        }
        if set(value) != required or value["schemaVersion"] != SCHEMA_VERSION:
            raise ValueError(f"invalid scenario contract: {path}")
        validation = value["validation"]
        expected_validation = {
            "allowedPaths", "requiredTools", "semanticTools", "requiredEvidence",
            "maximumRewriteLines",
        }
        if set(validation) != expected_validation:
            raise ValueError(f"invalid validation contract: {path}")
        if value["kind"] not in {"fixture", "live"}:
            raise ValueError(f"unsupported scenario kind: {value['kind']}")
        if value["expectedOutcome"] not in TERMINAL_OUTCOMES:
            raise ValueError(f"unsupported terminal outcome: {value['expectedOutcome']}")
        scenario_id = str(value["id"])
        if not scenario_id or any(character not in "abcdefghijklmnopqrstuvwxyz0123456789-" for character in scenario_id):
            raise ValueError(f"invalid scenario id: {scenario_id!r}")
        maximum_rewrite_lines = int(validation["maximumRewriteLines"])
        if maximum_rewrite_lines < 0:
            raise ValueError("maximumRewriteLines cannot be negative")
        return Scenario(
            scenario_id,
            int(value["version"]),
            str(value["kind"]),
            str(value["prompt"]),
            str(value["expectedOutcome"]),
            tuple(map(str, validation["allowedPaths"])),
            tuple(map(str, validation["requiredTools"])),
            tuple(map(str, validation["semanticTools"])),
            tuple(map(str, validation["requiredEvidence"])),
            maximum_rewrite_lines,
            tuple(value["fixtureEvents"]),
        )


def load_corpus(root: Path) -> list[Scenario]:
    scenarios = [Scenario.load(path) for path in sorted(root.glob("*.json"))]
    identities = [(item.scenario_id, item.version) for item in scenarios]
    if not scenarios or len(identities) != len(set(identities)):
        raise ValueError("scenario corpus must be non-empty with unique id/version pairs")
    return scenarios


def git_revision(repository: Path) -> str:
    return subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=repository, check=True, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    ).stdout.strip()


class StatelessMcpClient:
    """One initialize request and one call request per operation; no session is retained."""

    def __init__(self, endpoint: str, client_id: str = "local-regression"):
        self.endpoint = endpoint
        self.client_id = client_id
        self.trace: list[dict[str, Any]] = []

    def call(self, tool: str, arguments: dict[str, Any]) -> Any:
        started = time.monotonic()
        trace = {
            "tool": tool,
            "startedAt": utc_now(),
            "arguments": arguments,
            "succeeded": False,
            "error": None,
        }
        try:
            self._post({
                "jsonrpc": "2.0", "id": 1, "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18", "capabilities": {},
                    "clientInfo": {
                        "name": "Harness.NET local regression", "version": "1",
                    },
                },
            })
            response = self._post({
                "jsonrpc": "2.0", "id": 2, "method": "tools/call",
                "params": {"name": tool, "arguments": arguments},
            })
            if "error" in response:
                raise RuntimeError(f"MCP {tool} failed: {response['error']}")
            result = response.get("result", {})
            if result.get("isError"):
                detail = next(
                    (item.get("text") for item in result.get("content", [])
                     if item.get("type") == "text"), None,
                )
                raise RuntimeError(f"MCP {tool} returned an error: {detail}")
            text = next(
                (item.get("text") for item in result.get("content", [])
                 if item.get("type") == "text"), None,
            )
            trace["succeeded"] = True
            return None if text is None else json.loads(text)
        except BaseException as error:
            trace["error"] = f"{type(error).__name__}: {error}"
            raise
        finally:
            trace["elapsedMs"] = max(1, round((time.monotonic() - started) * 1000))
            self.trace.append(trace)

    def _post(self, payload: dict[str, Any]) -> dict[str, Any]:
        request = Request(
            self.endpoint,
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json, text/event-stream",
                "X-Harness-Client": self.client_id,
            },
        )
        with urlopen(request, timeout=300) as response:
            if "text/event-stream" not in response.headers.get("Content-Type", ""):
                return json.load(response)
            for raw_line in response:
                line = raw_line.decode("utf-8").strip()
                if line.startswith("data:"):
                    candidate = json.loads(line[5:].strip())
                    if candidate.get("id") == payload["id"]:
                        return candidate
        raise RuntimeError("MCP response ended before the matching result")


class LiveGoalDriver:
    """Bounded typed lifecycle driver for one isolated Harness.NET goal."""

    WORKFLOW_RUNNING = 0
    WORKFLOW_AWAITING_PLAN = 1
    WORKFLOW_AWAITING_ACCEPTANCE = 2
    WORKFLOW_NEEDS_DIRECTION = 3
    WORKFLOW_PARTIAL = 4
    WORKFLOW_COMPLETED = 5
    WORKFLOW_ABORTED = 6
    OPERATION_RUNNING = 0
    OPERATION_COMPLETED = 1
    OPERATION_CANCELLED = 2
    OPERATION_FAILED = 3

    def __init__(
        self,
        client: StatelessMcpClient,
        instance_id: str,
        workspace_id: str,
        timeout_seconds: int,
        sample_resources: Any | None = None,
    ):
        self.client = client
        self.instance_id = instance_id
        self.workspace_id = workspace_id
        self.timeout_seconds = timeout_seconds
        self.sample_resources = sample_resources

    def create_goal(self, title: str, objective: str, review_cycles: int = 2) -> str:
        value = self.client.call("harness_create_goal", {
            "expectedInstanceId": self.instance_id,
            "workspaceId": self.workspace_id,
            "title": title,
            "objective": objective,
            "reviewCycleLimit": review_cycles,
            "remoteBudgetMicrousd": None,
        })
        return str(value["result"]["goal"]["id"]["value"])

    def select_model(self, goal_id: str, role: str, provider: str, model: str) -> None:
        self.client.call("harness_select_goal_model", {
            "expectedInstanceId": self.instance_id,
            "goalId": goal_id,
            "role": role,
            "provider": provider,
            "model": model,
        })

    def start_planning(self, goal_id: str) -> dict[str, Any]:
        self.client.call("harness_start_planning", {
            "expectedInstanceId": self.instance_id,
            "goalId": goal_id,
        })
        return self.wait_for(goal_id, {
            self.WORKFLOW_AWAITING_PLAN, self.WORKFLOW_NEEDS_DIRECTION,
        })

    def retry(self, goal_id: str, role: str, guidance: str | None) -> dict[str, Any]:
        self.client.call("harness_retry_goal", {
            "expectedInstanceId": self.instance_id,
            "goalId": goal_id,
            "role": role,
            "guidance": guidance,
        })
        terminal = (
            {self.WORKFLOW_AWAITING_PLAN, self.WORKFLOW_NEEDS_DIRECTION}
            if role == "Lead" else
            {
                self.WORKFLOW_RUNNING,
                self.WORKFLOW_AWAITING_ACCEPTANCE,
                self.WORKFLOW_NEEDS_DIRECTION,
                self.WORKFLOW_PARTIAL,
                self.WORKFLOW_COMPLETED,
            }
        )
        return self.wait_for(goal_id, terminal)

    def approve_plan(self, goal: dict[str, Any], reason: str) -> None:
        self.client.call("harness_decide_plan", {
            "expectedInstanceId": self.instance_id,
            "goalId": goal["goal"]["id"]["value"],
            "planId": goal["plan"]["id"]["value"],
            "decision": "Approve",
            "reason": reason,
        })

    def resume(self, goal_id: str) -> dict[str, Any]:
        self.client.call("harness_resume_goal", {
            "expectedInstanceId": self.instance_id,
            "goalId": goal_id,
        })
        return self.wait_for(goal_id, {
            self.WORKFLOW_AWAITING_ACCEPTANCE,
            self.WORKFLOW_NEEDS_DIRECTION,
            self.WORKFLOW_PARTIAL,
            self.WORKFLOW_COMPLETED,
        })

    def wait_for(self, goal_id: str, states: set[int]) -> dict[str, Any]:
        deadline = time.monotonic() + self.timeout_seconds
        last_progress: tuple[Any, ...] | None = None
        progress_deadline = deadline
        while time.monotonic() < deadline:
            if self.sample_resources is not None:
                self.sample_resources()
            value = self.client.call("harness_goals", {
                "goalId": goal_id,
                "maximumResults": 1,
                "continuation": None,
            })
            matches = value.get("goals", [])
            if len(matches) != 1:
                raise RuntimeError(f"Harness returned {len(matches)} matches for exact goal")
            goal = matches[0]
            workflow = goal.get("workflow")
            operation = goal.get("inboundOperation")
            workflow_state = None if workflow is None else int(workflow["state"])
            operation_state = (
                None if operation is None else int(operation.get("state", 0))
            )
            if operation_state == self.OPERATION_FAILED:
                detail = operation.get("error") or "no failure detail was returned"
                raise RuntimeError(
                    f"Harness inbound goal operation failed: {detail}"
                )
            if operation_state == self.OPERATION_CANCELLED:
                raise RuntimeError("Harness inbound goal operation was cancelled")
            operation_running = (
                operation_state == self.OPERATION_RUNNING
            )
            if workflow_state in states and not (
                workflow_state == self.WORKFLOW_RUNNING and operation_running
            ):
                return goal
            progress = (
                None if workflow is None else workflow.get("state"),
                0 if workflow is None else len(workflow.get("activities", [])),
                None if operation is None else operation.get("state"),
            )
            if progress != last_progress:
                last_progress = progress
                progress_deadline = min(deadline, time.monotonic() + self.timeout_seconds)
            if time.monotonic() >= progress_deadline:
                break
            time.sleep(0.5)
        raise TimeoutError(f"Harness goal {goal_id} did not reach {sorted(states)}")


def collect_ollama_identity(endpoint: str, model: str) -> dict[str, Any]:
    def request(path: str, payload: dict[str, str] | None = None) -> dict[str, Any]:
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        call = Request(
            endpoint.rstrip("/") + path,
            data=body,
            headers={"Content-Type": "application/json"} if body else {},
            method="POST" if body else "GET",
        )
        with urlopen(call, timeout=30) as response:
            return json.load(response)

    tags = request("/api/tags")
    found = next(
        (item for item in tags.get("models", []) if model in {item.get("name"), item.get("model")}),
        None,
    )
    if found is None:
        return {"model": model, "available": False}
    show = request("/api/show", {"model": model})
    running = request("/api/ps")
    loaded = next(
        (item for item in running.get("models", []) if model in {item.get("name"), item.get("model")}),
        None,
    )
    model_info = show.get("model_info", {})
    declared_context = next(
        (value for key, value in model_info.items()
         if key == "context_length" or key.endswith(".context_length")),
        None,
    )
    return {
        "provider": "Ollama",
        "model": model,
        "available": True,
        "digest": found.get("digest"),
        "modelSizeBytes": found.get("size"),
        "parameterSize": found.get("details", {}).get("parameter_size"),
        "quantization": found.get("details", {}).get("quantization_level"),
        "capabilities": sorted(show.get("capabilities", [])),
        "declaredContextLength": declared_context,
        "effectiveContextLength": None if loaded is None else loaded.get("context_length"),
        "loadedVramBytes": None if loaded is None else loaded.get("size_vram"),
    }


def derive_metrics(events: Iterable[dict[str, Any]], elapsed_ms: int, peak_rss: int) -> dict[str, Any]:
    values = list(events)
    tool_events = [item for item in values if item.get("kind") == "tool"]
    return {
        "planValid": any(item.get("kind") == "plan" and item.get("valid") is True for item in values),
        "completed": any(item.get("kind") == "terminal" and item.get("outcome") == "completed" for item in values),
        "partialCompletion": any(
            item.get("kind") == "checkpoint" and item.get("partial") is True or
            item.get("kind") == "terminal" and item.get("outcome") == "partial"
            for item in values),
        "retryCount": sum(item.get("kind") == "retry" for item in values),
        "toolErrors": sum(item.get("error") is not None for item in tool_events),
        "rewriteLines": sum(int(item.get("changedLines", 0)) for item in tool_events if item.get("mutation")),
        "compilerRegressions": sum(item.get("kind") == "compiler" and item.get("introduced", 0) > 0 for item in values),
        "reviewFindings": sum(int(item.get("findings", 0)) for item in values if item.get("kind") == "review"),
        "latencyMs": elapsed_ms,
        "peakRssBytes": peak_rss,
    }


def semantic_validation_summary(result: dict[str, Any]) -> tuple[bool, int]:
    """Return whether a durable edit has Roslyn validation and introduced errors."""
    validation = result.get("candidateCodeValidation")
    if not isinstance(validation, dict):
        return False, 0
    diagnostics = validation.get("diagnostics", [])
    introduced_errors = sum(
        1 for item in diagnostics
        if isinstance(item, dict) and item.get("kind") == "Introduced" and
        isinstance(item.get("diagnostic"), dict) and
        item["diagnostic"].get("severity") == "Error"
    )
    return validation.get("disposition") == "Validated", introduced_errors


def validate_run(scenario: Scenario, run: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    if run.get("terminalOutcome") != scenario.expected_outcome:
        failures.append(
            f"terminal outcome {run.get('terminalOutcome')!r} != {scenario.expected_outcome!r}")
    trace = run.get("toolTrace", [])
    tools = {str(item.get("tool")) for item in trace}
    for required in scenario.required_tools:
        if required not in tools:
            failures.append(f"required tool was not observed: {required}")
    if scenario.semantic_tools and not tools.intersection(scenario.semantic_tools):
        failures.append("no required semantic operation was observed")
    for item in trace:
        path = item.get("path")
        if path and scenario.allowed_paths and not any(
            path == allowed or path.startswith(allowed.rstrip("/") + "/")
            for allowed in scenario.allowed_paths
        ):
            failures.append(f"tool addressed disallowed path: {path}")
    evidence = {str(item.get("evidenceKind")) for item in run.get("evidence", [])}
    for required in scenario.required_evidence:
        if required not in evidence:
            failures.append(f"required evidence was not observed: {required}")
    rewrite_lines = int(run.get("metrics", {}).get("rewriteLines", 0))
    if rewrite_lines > scenario.maximum_rewrite_lines:
        failures.append(
            f"rewrite size {rewrite_lines} exceeds {scenario.maximum_rewrite_lines}")
    return failures


def fixture_run(scenario: Scenario, revision: str) -> dict[str, Any]:
    started = time.monotonic()
    events = list(scenario.fixture_events)
    terminal = next(
        (item.get("outcome") for item in reversed(events) if item.get("kind") == "terminal"),
        None,
    )
    trace = [item for item in events if item.get("kind") == "tool"][:MAX_TRACE_ITEMS]
    evidence = [item for item in events if item.get("kind") == "evidence"][:MAX_TRACE_ITEMS]
    elapsed = max(1, round((time.monotonic() - started) * 1000))
    run = {
        "schemaVersion": SCHEMA_VERSION,
        "harnessRevision": revision,
        "scenario": {
            "id": scenario.scenario_id,
            "version": scenario.version,
            "prompt": scenario.prompt,
            "promptSha256": sha256_text(scenario.prompt),
        },
        "modelServer": {"provider": "DeterministicFake", "model": "fixture-v1"},
        "discoveredCapabilities": [],
        "routes": {},
        "startedAt": utc_now(),
        "finishedAt": utc_now(),
        "resource": {"peakRssBytes": 0},
        "toolTrace": trace,
        "diff": bounded_text("".join(str(item.get("diff", "")) for item in events)),
        "evidence": evidence,
        "terminalOutcome": terminal,
    }
    run["metrics"] = derive_metrics(events, elapsed, 0)
    run["validationFailures"] = validate_run(scenario, run)
    run["passed"] = not run["validationFailures"]
    return run


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{time.time_ns()}.tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(path)


def compare_runs(current: list[dict[str, Any]], baseline: list[dict[str, Any]]) -> dict[str, Any]:
    prior = {(item["scenario"]["id"], item["scenario"]["version"]): item for item in baseline}
    comparisons = []
    for item in current:
        identity = (item["scenario"]["id"], item["scenario"]["version"])
        old = prior.get(identity)
        deltas = {}
        if old is not None:
            for metric in (
                "retryCount", "toolErrors", "rewriteLines", "compilerRegressions",
                "reviewFindings", "latencyMs", "peakRssBytes",
            ):
                deltas[metric] = item["metrics"][metric] - old["metrics"][metric]
        regressions: list[str] = []
        improvements: list[str] = []
        if old is not None:
            if old["passed"] and not item["passed"]:
                regressions.append("scenario changed from passing to failing")
            elif not old["passed"] and item["passed"]:
                improvements.append("scenario changed from failing to passing")
            lower_is_better = {
                "retryCount", "toolErrors", "rewriteLines", "compilerRegressions",
                "reviewFindings", "latencyMs", "peakRssBytes",
            }
            for metric, delta in deltas.items():
                if metric in lower_is_better and delta > 0:
                    regressions.append(f"{metric} increased by {delta}")
                elif metric in lower_is_better and delta < 0:
                    improvements.append(f"{metric} decreased by {-delta}")
        classification = (
            "regressed" if regressions else
            "improved" if improvements else
            "unchanged" if old is not None else
            "new"
        )
        comparisons.append({
            "scenario": {"id": identity[0], "version": identity[1]},
            "baselineFound": old is not None,
            "passedChanged": None if old is None else item["passed"] != old["passed"],
            "metricDeltas": deltas,
            "classification": classification,
            "regressions": regressions,
            "improvements": improvements,
        })
    return {"schemaVersion": SCHEMA_VERSION, "comparisons": comparisons}
