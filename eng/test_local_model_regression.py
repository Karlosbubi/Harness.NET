#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest

from local_model_regression import (
    SCHEMA_VERSION, Scenario, compare_runs, fixture_run, load_corpus,
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
        self.assertEqual(-3, result["comparisons"][0]["metricDeltas"]["rewriteLines"])

    def test_invalid_contract_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.json"
            path.write_text('{"schemaVersion":"wrong"}', encoding="utf-8")
            with self.assertRaises(ValueError):
                Scenario.load(path)


if __name__ == "__main__":
    unittest.main()
