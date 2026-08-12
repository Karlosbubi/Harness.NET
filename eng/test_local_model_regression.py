#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest

from local_model_regression import (
    LiveGoalDriver, SCHEMA_VERSION, Scenario, StatelessMcpClient, compare_runs,
    fixture_run, load_corpus,
    semantic_validation_summary,
)


CORPUS = Path(__file__).parent / "local-model-regression/scenarios/v1"


class LocalModelRegressionTests(unittest.TestCase):
    def test_versioned_corpus_is_unique_and_complete(self) -> None:
        scenarios = load_corpus(CORPUS)
        ids = {item.scenario_id for item in scenarios}
        self.assertEqual({
            "tictactoe", "semantic-edit", "multi-file-build-test", "failure-retry",
            "partial-completion", "cancellation", "unavailable-model", "server-restart",
            "truncated-output", "malformed-tool-call", "unsupported-reasoning-tools",
        }, ids)

    def test_every_fixture_matches_its_deterministic_contract(self) -> None:
        revision = "a" * 40
        for scenario in load_corpus(CORPUS):
            if scenario.kind != "fixture":
                continue
            with self.subTest(scenario=scenario.scenario_id):
                result = fixture_run(scenario, revision)
                self.assertTrue(result["passed"], result["validationFailures"])
                self.assertEqual(SCHEMA_VERSION, result["schemaVersion"])
                self.assertEqual(revision, result["harnessRevision"])

    def test_disallowed_path_and_large_rewrite_fail_validation(self) -> None:
        scenario = Scenario(
            "bounded", 1, "fixture", "prompt", "completed", ("src/Allowed.cs",),
            ("FileEdit",), (), ("build",), 2,
            (
                {"kind": "plan", "valid": True},
                {"kind": "tool", "tool": "FileEdit", "path": "src/Other.cs",
                 "mutation": True, "changedLines": 3},
                {"kind": "evidence", "evidenceKind": "build"},
                {"kind": "terminal", "outcome": "completed"},
            ),
        )
        result = fixture_run(scenario, "b" * 40)
        self.assertFalse(result["passed"])
        self.assertTrue(any("disallowed path" in item for item in result["validationFailures"]))
        self.assertTrue(any("rewrite size" in item for item in result["validationFailures"]))

    def test_comparison_uses_metrics_not_generated_patch_text(self) -> None:
        current = [{
            "scenario": {"id": "one", "version": 1}, "passed": True,
            "metrics": {"retryCount": 0, "toolErrors": 0, "rewriteLines": 2,
                        "compilerRegressions": 0, "reviewFindings": 0,
                        "latencyMs": 20, "peakRssBytes": 10},
        }]
        baseline = json.loads(json.dumps(current))
        baseline[0]["metrics"]["rewriteLines"] = 5
        result = compare_runs(current, baseline)
        comparison = result["comparisons"][0]
        self.assertEqual(-3, comparison["metricDeltas"]["rewriteLines"])
        self.assertEqual("improved", comparison["classification"])
        self.assertIn("rewriteLines decreased by 3", comparison["improvements"])

    def test_stateless_client_records_success_and_failure(self) -> None:
        client = StatelessMcpClient("http://localhost.invalid/mcp", "token")
        responses = iter([
            {"jsonrpc": "2.0", "id": 1, "result": {}},
            {"jsonrpc": "2.0", "id": 2, "result": {
                "content": [{"type": "text", "text": '{"ok":true}'}],
            }},
        ])
        client._post = lambda _: next(responses)  # type: ignore[method-assign]
        self.assertEqual({"ok": True}, client.call("harness_application", {}))
        self.assertTrue(client.trace[-1]["succeeded"])
        self.assertEqual("harness_application", client.trace[-1]["tool"])

        responses = iter([
            {"jsonrpc": "2.0", "id": 1, "result": {}},
            {"jsonrpc": "2.0", "id": 2, "result": {
                "isError": True,
                "content": [{"type": "text", "text": "denied"}],
            }},
        ])
        client._post = lambda _: next(responses)  # type: ignore[method-assign]
        with self.assertRaisesRegex(RuntimeError, "denied"):
            client.call("harness_build", {})
        self.assertFalse(client.trace[-1]["succeeded"])

    def test_live_driver_uses_exact_typed_goal_lifecycle(self) -> None:
        class FakeClient:
            def __init__(self) -> None:
                self.calls: list[tuple[str, dict[str, object]]] = []

            def call(self, tool: str, arguments: dict[str, object]) -> dict[str, object]:
                self.calls.append((tool, arguments))
                if tool == "harness_create_goal":
                    return {"result": {"goal": {"id": {"value": "goal-1"}}}}
                if tool == "harness_goals":
                    return {"goals": [{
                        "goal": {"id": {"value": "goal-1"}},
                        "plan": {"id": {"value": "plan-1"}},
                        "workflow": {"state": LiveGoalDriver.WORKFLOW_AWAITING_PLAN,
                                     "activities": []},
                        "inboundOperation": {"state": 1},
                    }]}
                return {}

        client = FakeClient()
        driver = LiveGoalDriver(client, "instance", "workspace", 1)  # type: ignore[arg-type]
        goal_id = driver.create_goal("title", "objective")
        self.assertEqual("goal-1", goal_id)
        goal = driver.start_planning(goal_id)
        driver.approve_plan(goal, "bounded local plan")
        tools = [item[0] for item in client.calls]
        self.assertEqual([
            "harness_create_goal", "harness_start_planning", "harness_goals",
            "harness_decide_plan",
        ], tools)
        self.assertIsNone(client.calls[0][1]["remoteBudgetMicrousd"])

    def test_successful_implementer_retry_returns_running_boundary(self) -> None:
        class FakeClient:
            def __init__(self) -> None:
                self.calls: list[tuple[str, dict[str, object]]] = []

            def call(self, tool: str, arguments: dict[str, object]) -> dict[str, object]:
                self.calls.append((tool, arguments))
                if tool == "harness_goals":
                    return {"goals": [{
                        "workflow": {"state": LiveGoalDriver.WORKFLOW_RUNNING,
                                     "activities": []},
                        "inboundOperation": {"state": 1},
                    }]}
                return {}

        client = FakeClient()
        driver = LiveGoalDriver(client, "instance", "workspace", 1)  # type: ignore[arg-type]
        goal = driver.retry("goal-1", "Implementer", None)

        self.assertEqual(LiveGoalDriver.WORKFLOW_RUNNING, goal["workflow"]["state"])
        self.assertEqual(
            ["harness_retry_goal", "harness_goals"],
            [item[0] for item in client.calls],
        )

    def test_running_retry_waits_until_inbound_operation_is_idle(self) -> None:
        class FakeClient:
            def __init__(self) -> None:
                self.polls = 0

            def call(self, tool: str, arguments: dict[str, object]) -> dict[str, object]:
                if tool == "harness_goals":
                    self.polls += 1
                    return {"goals": [{
                        "workflow": {"state": LiveGoalDriver.WORKFLOW_RUNNING,
                                     "activities": []},
                        "inboundOperation": {"state": 0 if self.polls == 1 else 1},
                    }]}
                return {}

        client = FakeClient()
        driver = LiveGoalDriver(client, "instance", "workspace", 2)  # type: ignore[arg-type]
        goal = driver.retry("goal-1", "Implementer", "guidance")

        self.assertEqual(LiveGoalDriver.WORKFLOW_RUNNING, goal["workflow"]["state"])
        self.assertEqual(2, client.polls)

    def test_roslyn_validation_is_derived_from_durable_edit_evidence(self) -> None:
        validated, errors = semantic_validation_summary({
            "candidateCodeValidation": {
                "disposition": "Validated",
                "diagnostics": [
                    {"kind": "Retained", "diagnostic": {"severity": "Error"}},
                    {"kind": "Introduced", "diagnostic": {"severity": "Error"}},
                    {"kind": "Introduced", "diagnostic": {"severity": "Warning"}},
                ],
            },
        })

        self.assertTrue(validated)
        self.assertEqual(1, errors)
        self.assertEqual((False, 0), semantic_validation_summary({}))

    def test_invalid_contract_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.json"
            path.write_text('{"schemaVersion":"wrong"}', encoding="utf-8")
            with self.assertRaises(ValueError):
                Scenario.load(path)


if __name__ == "__main__":
    unittest.main()
