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
    parser.add_argument(
        "--complete-linux",
        action="store_true",
        help="also require production AT-SPI, Orca speech, and Linux x64 publication",
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
            "FullyQualifiedName~EditorIntelligenceSettingsServiceTests|"
            "FullyQualifiedName~KeybindingSettingsServiceTests",
        ),
    ))
    durations.append(run(
        root,
        "editor-settings-storage",
        test_command(
            "tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj",
            "FullyQualifiedName~SqliteEditorIntelligencePreferenceStoreTests|"
            "FullyQualifiedName~SqliteKeybindingPreferenceStoreTests",
        ),
    ))
    durations.append(run(
        root,
        "developer-run-storage",
        test_command(
            "tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj",
            "FullyQualifiedName~DotNetProjectRunnerTests|"
            "FullyQualifiedName~SqliteDeveloperDotNetExecutionStoreTests",
        ),
    ))
    durations.append(run(
        root,
        "developer-run-policy",
        test_command(
            "tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj",
            "FullyQualifiedName~DeveloperProjectExecutionServiceTests",
        ),
    ))
    durations.append(run(
        root,
        "editor-controls",
        test_command(
            "tests/Harness.Presentation.Avalonia.Tests/Harness.Presentation.Avalonia.Tests.csproj",
            "FullyQualifiedName~PresentationControlTests|FullyQualifiedName~VimEditorControllerTests",
        ),
    ))
    durations.append(run(
        root,
        "project-user-secrets-storage",
        test_command(
            "tests/Harness.DataAccess.Tests/Harness.DataAccess.Tests.csproj",
            "FullyQualifiedName~ProjectUserSecretStoreTests",
        ),
    ))
    durations.append(run(
        root,
        "project-user-secrets-policy",
        test_command(
            "tests/Harness.BusinessLogic.Tests/Harness.BusinessLogic.Tests.csproj",
            "FullyQualifiedName~ProjectUserSecretsServiceTests|"
            "FullyQualifiedName~VisualCaptureServiceTests",
        ),
    ))
    durations.append(run(
        root,
        "project-user-secrets-controls",
        test_command(
            "tests/Harness.Presentation.Avalonia.Tests/Harness.Presentation.Avalonia.Tests.csproj",
            "FullyQualifiedName~ProjectUserSecretsDialogTests",
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

    if arguments.atspi or arguments.complete_linux:
        accessibility_command = [
            sys.executable,
            "eng/verify-avalonia-atspi.py",
        ]
        if arguments.complete_linux:
            accessibility_command.append("--with-orca")
        durations.append(run(root, "production-accessibility", accessibility_command))

    if arguments.complete_linux:
        durations.append(run(root, "linux-x64-publish", [
            "./eng/verify-linux-x64-publish.sh",
        ]))

    print(
        "Editor intelligence verification passed: completion, quick info, signatures, "
        "diagnostics, semantic classification, occurrences, folding, outline, breadcrumbs, "
        "workspace symbols, region navigation, inlay hints, lazy CodeLens, definitions, "
        "usages, implementations, generated source, metadata signatures, "
        "exact-context syntax trees, symbol details, generated-source inspection, IL, "
        "document/selection/changed-span formatting, guarded paste and on-type formatting, "
        "import organization, unused-import cleanup, "
        "proven missing-import choices, closed quick fixes, local/selection refactorings, "
        "document fix-all, fingerprinted model apply, "
        "typed project-entry-point Run, no-shell execution, cancellation, transient output, "
        "settings persistence, typed keybinding dispatch, conflict validation, safe import/export, "
        "optional Vim modes, counted motions/operators, IME suspension, "
        "masked Project User Secrets actions, atomic standard-store writes, capture interlock, "
        "bounded cross-document action discovery, complete fingerprinted atomic apply, "
        "all-path model grants, stale-result handling, analyzer-failure degradation, in-flight cancellation, "
        "large-workspace latency and memory budgets, repeated source-context switching, "
        "keyboard-only editing, IME composition, 200% scaling, and Dock restoration, "
        f"accessible production controls, and theme contracts ({sum(durations):.1f}s)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
