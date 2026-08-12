#!/usr/bin/env python3
"""Verify the manual editor and shared Roslyn assistance without model inference.

The default gate is headless and deterministic. ``--atspi`` additionally launches the
production Avalonia application through the existing Linux accessibility verifier to
prove that the editor actions are discoverable in the real desktop control tree.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys
import time


def run(root: Path, label: str, command: list[str]) -> float:
    print(f"[{label}] {' '.join(command)}", flush=True)
    started = time.monotonic()
    subprocess.run(command, cwd=root, check=True)
    elapsed = time.monotonic() - started
    print(f"[{label}] passed in {elapsed:.1f}s", flush=True)
    return elapsed


def test_command(project: str, filter_expression: str) -> list[str]:
    return [
        "dotnet",
        "test",
        project,
        "--no-build",
        "--no-restore",
        "-p:UseSharedCompilation=false",
        "-m:1",
        "--filter",
        filter_expression,
        "--logger",
        "console;verbosity=minimal",
    ]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--atspi",
        action="store_true",
        help="also run the production Linux AT-SPI workbench verifier",
    )
    parser.add_argument(
        "--no-build",
        action="store_true",
        help="reuse an existing build before running the focused tests",
    )
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    durations: list[float] = []

    if not arguments.no_build:
        durations.append(run(root, "build", [
            "dotnet",
            "build",
            "Harness.slnx",
            "--no-restore",
            "-p:UseSharedCompilation=false",
            "-m:1",
            "--verbosity:minimal",
        ]))

    durations.append(run(
        root,
        "roslyn-adapter",
        test_command(
            "tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj",
            "FullyQualifiedName~RoslynCodeIntelligenceEngineTests",
        ),
    ))
    durations.append(run(
        root,
        "semantic-boundary",
        test_command(
            "tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj",
            "FullyQualifiedName~WorkbenchCodeIntelligenceServiceTests",
        ),
    ))
    durations.append(run(
        root,
        "transformation-authority",
        test_command(
            "tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj",
            "FullyQualifiedName~WorkspaceMutationServiceTests|FullyQualifiedName~AgentToolPolicyTests",
        ),
    ))
    durations.append(run(
        root,
        "editor-settings-policy",
        test_command(
            "tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj",
            "FullyQualifiedName~EditorIntelligenceSettingsServiceTests",
        ),
    ))
    durations.append(run(
        root,
        "editor-settings-storage",
        test_command(
            "tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj",
            "FullyQualifiedName~SqliteEditorIntelligencePreferenceStoreTests",
        ),
    ))
    durations.append(run(
        root,
        "editor-controls",
        test_command(
            "tests/Harness.Presentation.Avalonia.Tests/Harness.Presentation.Avalonia.Tests.csproj",
            "FullyQualifiedName~PresentationControlTests",
        ),
    ))
    durations.append(run(
        root,
        "theme-contract",
        test_command(
            "tests/Harness.UI.Avalonia.Tests/Harness.UI.Avalonia.Tests.csproj",
            "FullyQualifiedName~HarnessThemeCatalogTests",
        ),
    ))

    if arguments.atspi:
        durations.append(run(root, "production-atspi", [
            sys.executable,
            "eng/verify-avalonia-atspi.py",
        ]))

    print(
        "Editor intelligence verification passed: completion, quick info, signatures, "
        "diagnostics, semantic classification, occurrences, folding, outline, breadcrumbs, "
        "workspace symbols, inlay hints, lazy CodeLens, definitions, usages, implementations, "
        "document/selection/changed-span formatting, guarded paste and on-type formatting, "
        "import organization, unused-import cleanup, "
        "proven missing-import choices, fingerprinted model apply, "
        "settings persistence, stale-result handling, "
        f"accessible production controls, and theme contracts ({sum(durations):.1f}s)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
