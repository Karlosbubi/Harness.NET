#!/usr/bin/env python3
"""Run the versioned deterministic corpus or explicit local Ollama comparisons."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import subprocess
import sys
from typing import Any

from local_model_regression import (
    SCHEMA_VERSION, collect_ollama_identity, compare_runs, fixture_run, git_revision,
    load_corpus, utc_now, validate_run, write_json,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", action="append", default=[])
    parser.add_argument("--live", action="store_true",
                        help="authorize local Ollama inference; never enables OpenRouter")
    parser.add_argument("--model", action="append", default=[],
                        help="Ollama model to run sequentially; may be repeated")
    parser.add_argument("--implementer-model",
                        help="optional Implementer route for every live comparison")
    parser.add_argument("--reviewer-model",
                        help="optional Reviewer route for every live comparison")
    parser.add_argument("--ollama-endpoint", default="http://127.0.0.1:11434")
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output-root", type=Path)
    parser.add_argument("--clean", type=Path,
                        help="delete exactly one prior run below artifacts/local-model-regression")
    return parser.parse_args()


def clean(repository: Path, target: Path) -> int:
    root = (repository / "artifacts/local-model-regression").resolve()
    resolved = target.resolve()
    if resolved == root or root not in resolved.parents:
        raise SystemExit("--clean must name one run below artifacts/local-model-regression")
    if resolved.exists():
        shutil.rmtree(resolved)
    return 0


def load_runs(path: Path) -> list[dict[str, Any]]:
    value = json.loads(path.read_text(encoding="utf-8"))
    return value["runs"] if isinstance(value, dict) else value


def main() -> int:
    args = parse_args()
    repository = Path(__file__).resolve().parent.parent
    if args.clean is not None:
        return clean(repository, args.clean)

    corpus_root = repository / "eng/local-model-regression/scenarios/v1"
    scenarios = load_corpus(corpus_root)
    if args.scenario:
        requested = set(args.scenario)
        scenarios = [item for item in scenarios if item.scenario_id in requested]
        missing = requested - {item.scenario_id for item in scenarios}
        if missing:
            raise SystemExit(f"unknown scenario(s): {sorted(missing)}")

    live_scenarios = [item for item in scenarios if item.kind == "live"]
    if live_scenarios and not args.live:
        scenarios = [item for item in scenarios if item.kind == "fixture"]
    if args.model and not args.live:
        raise SystemExit("--model requires --live; local inference is explicit opt-in")
    if args.live and not args.model:
        raise SystemExit("--live requires at least one explicit --model")

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output = (args.output_root or
              repository / "artifacts/local-model-regression" / timestamp).resolve()
    if output.exists():
        raise SystemExit(f"output directory already exists: {output}")

    revision = git_revision(repository)
    runs = [fixture_run(item, revision) for item in scenarios if item.kind == "fixture"]
    model_identities = []
    if args.live:
        identity_models = list(dict.fromkeys([
            *args.model,
            *([args.implementer_model] if args.implementer_model else []),
            *([args.reviewer_model] if args.reviewer_model else []),
        ]))
        identities = {
            model: collect_ollama_identity(args.ollama_endpoint, model)
            for model in identity_models
        }
        model_identities.extend(identities.values())
        for model, identity in identities.items():
            if not identity.get("available"):
                raise SystemExit(f"Ollama model is unavailable: {model}")
            if int(identity.get("modelSizeBytes", 0) or 0) > 16 * 1024**3:
                raise SystemExit(f"Ollama model exceeds the 16 GB limit: {model}")
        for index, model in enumerate(args.model):
            identity = identities[model]
            for scenario in live_scenarios:
                if scenario.scenario_id != "tictactoe":
                    raise SystemExit(
                        f"no live adapter is registered for {scenario.scenario_id}")
                safe_model = "".join(
                    character if character.isalnum() or character in "-." else "-"
                    for character in model
                )
                live_output = output / "live" / f"{scenario.scenario_id}-{safe_model}"
                command = [
                    sys.executable,
                    str(repository / "eng/verify-ollama-tictactoe-usability.py"),
                    "--ollama-endpoint", args.ollama_endpoint,
                    "--model", model,
                    "--output-root", str(live_output),
                ]
                if args.implementer_model:
                    command.extend(["--implementer-model", args.implementer_model])
                if args.reviewer_model:
                    command.extend(["--reviewer-model", args.reviewer_model])
                if index > 0:
                    command.append("--skip-host-build")
                result = subprocess.run(
                    command, cwd=repository, text=True,
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False,
                )
                live_output.mkdir(parents=True, exist_ok=True)
                (live_output / "runner.log").write_text(
                    result.stdout or "", encoding="utf-8")
                usability_path = live_output / "usability-report.json"
                usability = (
                    json.loads(usability_path.read_text(encoding="utf-8"))
                    if usability_path.is_file() else {}
                )
                run_result = usability.get("regression_run")
                if run_result is None:
                    run_result = {
                        "schemaVersion": SCHEMA_VERSION,
                        "harnessRevision": revision,
                        "scenario": {
                            "id": scenario.scenario_id,
                            "version": scenario.version,
                            "prompt": scenario.prompt,
                        },
                        "modelServer": identity,
                        "discoveredCapabilities": identity.get("capabilities", []),
                        "routes": {},
                        "startedAt": usability.get("started_at", utc_now()),
                        "finishedAt": usability.get("finished_at", utc_now()),
                        "resource": {},
                        "toolTrace": [],
                        "diff": "",
                        "evidence": [],
                        "terminalOutcome": "failed",
                        "metrics": {
                            "planValid": False, "completed": False,
                            "partialCompletion": False, "retryCount": 0,
                            "toolErrors": 0, "rewriteLines": 0,
                            "compilerRegressions": 0, "reviewFindings": 0,
                            "latencyMs": 0, "peakRssBytes": 0,
                        },
                        "passed": False,
                        "validationFailures": [
                            usability.get("error") or
                            f"live adapter exited with status {result.returncode}"
                        ],
                    }
                else:
                    run_result["scenario"]["corpusPrompt"] = scenario.prompt
                    run_result["validationFailures"] = validate_run(scenario, run_result)
                    run_result["passed"] = (
                        result.returncode == 0 and not run_result["validationFailures"])
                runs.append(run_result)

    report = {
        "schemaVersion": SCHEMA_VERSION,
        "harnessRevision": revision,
        "createdAt": datetime.now(timezone.utc).isoformat(),
        "execution": {
            "liveInference": args.live,
            "provider": "Ollama" if args.live else "DeterministicFake",
            "boundedConcurrency": 1,
            "paidInference": False,
        },
        "modelIdentities": model_identities,
        "runs": runs,
        "passed": all(item["passed"] for item in runs),
    }
    write_json(output / "report.json", report)
    if args.baseline:
        write_json(output / "comparison.json", compare_runs(runs, load_runs(args.baseline)))
    print(output)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
