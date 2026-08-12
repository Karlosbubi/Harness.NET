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

    def __init__(self, endpoint: str, token: str, client_id: str = "local-regression"):
        self.endpoint = endpoint
        self.token = token
        self.client_id = client_id

    def call(self, tool: str, arguments: dict[str, Any]) -> Any:
        self._post({
            "jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {
                "protocolVersion": "2025-06-18", "capabilities": {},
                "clientInfo": {"name": "Harness.NET local regression", "version": "1"},
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
            raise RuntimeError(f"MCP {tool} returned an error")
        text = next(
            (item.get("text") for item in result.get("content", [])
             if item.get("type") == "text"), None,
        )
        return None if text is None else json.loads(text)

    def _post(self, payload: dict[str, Any]) -> dict[str, Any]:
        request = Request(
            self.endpoint,
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json, text/event-stream",
                "Authorization": f"Bearer {self.token}",
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
    return {
        "provider": "Ollama",
        "model": model,
        "available": True,
        "digest": found.get("digest"),
        "parameterSize": found.get("details", {}).get("parameter_size"),
        "quantization": found.get("details", {}).get("quantization_level"),
        "capabilities": sorted(show.get("capabilities", [])),
        "declaredContextLength": show.get("model_info", {}).get("context_length"),
        "effectiveContextLength": None if loaded is None else loaded.get("context_length"),
        "loadedVramBytes": None if loaded is None else loaded.get("size_vram"),
    }


def derive_metrics(events: Iterable[dict[str, Any]], elapsed_ms: int, peak_rss: int) -> dict[str, Any]:
    values = list(events)
    tool_events = [item for item in values if item.get("kind") == "tool"]
    return {
        "planValid": any(item.get("kind") == "plan" and item.get("valid") is True for item in values),
        "completed": any(item.get("kind") == "terminal" and item.get("outcome") == "completed" for item in values),
        "partialCompletion": any(item.get("kind") == "checkpoint" and item.get("partial") is True for item in values),
        "retryCount": sum(item.get("kind") == "retry" for item in values),
        "toolErrors": sum(item.get("error") is not None for item in tool_events),
        "rewriteLines": sum(int(item.get("changedLines", 0)) for item in tool_events if item.get("mutation")),
        "compilerRegressions": sum(item.get("kind") == "compiler" and item.get("introduced", 0) > 0 for item in values),
        "reviewFindings": sum(int(item.get("findings", 0)) for item in values if item.get("kind") == "review"),
        "latencyMs": elapsed_ms,
        "peakRssBytes": peak_rss,
    }


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
        comparisons.append({
            "scenario": {"id": identity[0], "version": identity[1]},
            "baselineFound": old is not None,
            "passedChanged": None if old is None else item["passed"] != old["passed"],
            "metricDeltas": deltas,
        })
    return {"schemaVersion": SCHEMA_VERSION, "comparisons": comparisons}
