#!/usr/bin/env python3
"""Capture the production source editor against a real approved goal worktree.

This is design evidence, not a release gate. It uses the real host, a temporary Git
repository, durable goal/plan approval, and the production document boundary. It
never invokes a model provider and removes its repository and private XDG state.
"""

from __future__ import annotations

import argparse
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

try:
    import dbus
except ImportError as error:  # pragma: no cover - environment guard
    raise SystemExit("python3-dbus is required to capture the source editor") from error

PROPERTIES = "org.freedesktop.DBus.Properties"


def load_verifier():
    path = Path(__file__).resolve().parent / "verify-avalonia-atspi.py"
    spec = importlib.util.spec_from_file_location("atspi_verifier", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def screenshot(destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    commands = (
        ["spectacle", "--activewindow", "--background", "-n", "-o", str(destination)],
        ["grim", str(destination)],
        ["import", "-window", "root", str(destination)],
    )
    for command in commands:
        if shutil.which(command[0]) is None:
            continue
        completed = subprocess.run(
            command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False
        )
        if completed.returncode == 0 and destination.is_file():
            return
    raise SystemExit("no usable screenshot tool (tried spectacle, grim, import)")


def resize(width: int, height: int) -> None:
    if shutil.which("wmctrl") is None:
        raise SystemExit("wmctrl is required for repeatable source-editor dimensions")
    subprocess.run(
        ["wmctrl", "-F", "-r", "Harness.NET", "-e", f"0,80,60,{width},{height}"],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    subprocess.run(
        ["wmctrl", "-F", "-a", "Harness.NET"],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    time.sleep(1.0)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "docs/acceptance",
    )
    arguments = parser.parse_args()
    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("capturing the source editor requires a graphical Linux session")

    atspi = load_verifier()
    repository_root = Path(__file__).resolve().parent.parent
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    atspi.run(
        [
            "dotnet",
            "build",
            "src/Harness.Host/Harness.Host.csproj",
            "--no-restore",
            "--nologo",
            "--verbosity",
            "quiet",
        ],
        repository_root,
    )

    session_bus = dbus.SessionBus()
    status_object = session_bus.get_object("org.a11y.Bus", "/org/a11y/bus")
    status_properties = dbus.Interface(status_object, PROPERTIES)
    original_enabled = bool(status_properties.Get("org.a11y.Status", "IsEnabled"))
    status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(True))
    accessibility_bus = dbus.bus.BusConnection(
        str(dbus.Interface(status_object, "org.a11y.Bus").GetAddress())
    )

    process = None
    try:
        with tempfile.TemporaryDirectory(prefix="harness-source-capture-") as temporary:
            root = Path(temporary)
            repository = root / "repository"
            atspi.run(
                [
                    "dotnet",
                    "new",
                    "console",
                    "--framework",
                    "net10.0",
                    "--name",
                    "Representative",
                    "--output",
                    str(repository),
                    "--no-restore",
                ],
                repository_root,
                quiet=True,
            )
            (repository / "Program.cs").write_text(
                "using System;\n\n"
                "namespace Representative;\n\n"
                "internal static class Program\n"
                "{\n"
                "    private static int Main(string[] arguments)\n"
                "    {\n"
                "        if (arguments.Length == 0)\n"
                "        {\n"
                "            Console.WriteLine(\"Usage: Representative <name>\");\n"
                "            return 1;\n"
                "        }\n\n"
                "        Console.WriteLine($\"Hello, {arguments[0]}!\");\n"
                "        return 0;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            (repository / "Broken.cs").write_text(
                "namespace Representative;\n\n"
                "internal sealed class Broken\n"
                "{\n"
                "    private int value = ;\n"
                "}\n",
                encoding="utf-8",
            )
            atspi.run(["git", "init", "-q"], repository)
            atspi.run(["git", "config", "user.name", "Harness Acceptance"], repository)
            atspi.run(
                ["git", "config", "user.email", "acceptance@invalid.example"], repository
            )
            atspi.run(["git", "add", "."], repository)
            atspi.run(["git", "commit", "-qm", "Initial representative repository"], repository)

            environment = os.environ.copy()
            environment.update(
                {
                    "XDG_CONFIG_HOME": str(root / "config"),
                    "XDG_DATA_HOME": str(root / "data"),
                    "XDG_STATE_HOME": str(root / "state"),
                    "XDG_CACHE_HOME": str(root / "cache"),
                }
            )
            process, application = atspi.launch(executable, environment, accessibility_bus)
            atspi.register_workspace(application, repository)
            atspi.create_and_approve_goal(application)
            resize(1600, 1000)
            application.invoke("Conversation", "page tab")
            application.wait_for_name("Goal or message composer")
            screenshot(arguments.output / "chat-workflow-wide-2026-07-29.png")
            resize(900, 650)
            application.wait_for_name("Goal or message composer")
            screenshot(arguments.output / "chat-workflow-compact-2026-07-29.png")
            resize(1600, 1000)
            application.invoke("Files", "page tab")
            application.wait_for_name("Program.cs", "push button")
            application.invoke("Program.cs")
            application.wait_for_name("Editable source editor for Program.cs", "panel")

            resize(1600, 1000)
            screenshot(arguments.output / "source-editor-wide-2026-07-29.png")
            resize(900, 650)
            screenshot(arguments.output / "source-editor-compact-2026-07-29.png")
            resize(1600, 1000)
            application.invoke("Files", "page tab")
            application.wait_for_name("Broken.cs", "push button")
            application.invoke("Broken.cs")
            application.wait_for_name("Editable source editor for Broken.cs", "panel")
            application.invoke("Problems", "page tab")
            application.wait_for_name_containing("CS")
            screenshot(arguments.output / "roslyn-diagnostics-wide-2026-07-31.png")
            resize(900, 650)
            application.wait_for_name_containing("CS")
            screenshot(arguments.output / "roslyn-diagnostics-compact-2026-07-31.png")
    finally:
        if process is not None:
            atspi.stop(process)
        status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled))

    print(f"captured the source editor into {arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
