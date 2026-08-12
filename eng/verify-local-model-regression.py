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
    load_corpus, write_json,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", action="append", default=[])
    parser.add_argument("--live", action="store_true",
                        help="authorize local Ollama inference; never enables OpenRouter")
    parser.add_argument("--model", action="append", default=[],
                        help="Ollama model to run sequentially; may be repeated")
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
        # Models are intentionally inspected and run one at a time. The legacy
        # Tic-Tac-Toe adapter is migrated by the next Task 038 slice; fail closed
        # instead of silently falling back to UI automation or paid inference.
        for model in args.model:
            identity = collect_ollama_identity(args.ollama_endpoint, model)
            model_identities.append(identity)
            if not identity.get("available"):
                raise SystemExit(f"Ollama model is unavailable: {model}")
        if live_scenarios:
            raise SystemExit(
                "live scenario adapter is not installed yet; deterministic results were not written")

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
