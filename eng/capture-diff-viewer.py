#!/usr/bin/env python3
"""Capture the production diff viewer against a real repository working tree.

This is a design-evidence capture tool, not a release gate. It launches the real
Avalonia host with an isolated XDG home, registers and trusts a real Git repository
that has genuine uncommitted changes, opens the bounded working-tree diff, and
screenshots the inline and side-by-side modes. It never invokes a model provider.
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
    raise SystemExit("python3-dbus is required to capture the diff viewer") from error

PROPERTIES = "org.freedesktop.DBus.Properties"


def load_verifier():
    """Reuse the AT-SPI helpers from the production verifier instead of copying them."""
    path = Path(__file__).resolve().parent / "verify-avalonia-atspi.py"
    spec = importlib.util.spec_from_file_location("atspi_verifier", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def screenshot(destination: Path) -> None:
    for command in (
        ["grim", str(destination)],
        ["spectacle", "-b", "-n", "-f", "-o", str(destination)],
        ["import", "-window", "root", str(destination)],
    ):
        if shutil.which(command[0]) is None:
            continue
        completed = subprocess.run(
            command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False
        )
        if completed.returncode == 0 and destination.is_file():
            return
    raise SystemExit("no usable screenshot tool (tried grim, spectacle, import)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path(tempfile.gettempdir()))
    arguments = parser.parse_args()

    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("capturing the diff viewer requires a graphical Linux session")

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
        with tempfile.TemporaryDirectory(prefix="harness-diff-capture-") as temporary:
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
            atspi.run(["git", "init", "-q"], repository)
            atspi.run(["git", "config", "user.name", "Harness Acceptance"], repository)
            atspi.run(
                ["git", "config", "user.email", "acceptance@invalid.example"], repository
            )
            atspi.run(["git", "add", "."], repository)
            atspi.run(["git", "commit", "-qm", "Initial representative repository"], repository)

            # Produce a real working-tree change with additions, removals, and context.
            (repository / "Program.cs").write_text(
                "using System;\n"
                "\n"
                "namespace Representative;\n"
                "\n"
                "internal static class Program\n"
                "{\n"
                "    private static int Main(string[] arguments)\n"
                "    {\n"
                "        if (arguments.Length == 0)\n"
                "        {\n"
                "            Console.WriteLine(\"Usage: Representative <name>\");\n"
                "            return 1;\n"
                "        }\n"
                "\n"
                "        Console.WriteLine($\"Hello, {arguments[0]}!\");\n"
                "        return 0;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )

            environment = os.environ.copy()
            environment.update(
                {
                    "XDG_CONFIG_HOME": str(root / "config"),
                    "XDG_DATA_HOME": str(root / "data"),
                    "XDG_STATE_HOME": str(root / "state"),
                    "XDG_CACHE_HOME": str(root / "cache"),
                }
            )

            process, application = atspi.launch(
                executable, environment, accessibility_bus
            )
            atspi.register_workspace(application, repository)
            application.invoke("Git", "page tab")
            application.wait_for_name("Open bounded Git working-tree diff", "push button")
            application.invoke("Open bounded Git working-tree diff")
            application.wait_for_name("Git working-tree diff", "panel")
            time.sleep(1.5)
            arguments.output.mkdir(parents=True, exist_ok=True)
            screenshot(arguments.output / "diff-inline-real.png")

            application.invoke("Compare Git state side by side", "toggle button")
            time.sleep(1.5)
            screenshot(arguments.output / "diff-side-by-side-real.png")
    finally:
        if process is not None:
            atspi.stop(process)
        status_properties.Set(
            "org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled)
        )

    print(f"captured the diff viewer into {arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
