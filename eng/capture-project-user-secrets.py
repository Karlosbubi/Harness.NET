#!/usr/bin/env python3
"""Capture the production Project User Secrets dialog without reading a secret."""

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
    raise SystemExit("python3-dbus is required to capture Project User Secrets") from error

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
    destination.unlink(missing_ok=True)
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent.parent
        / "docs/acceptance/project-user-secrets-2026-08-13.png",
    )
    arguments = parser.parse_args()
    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("capturing Project User Secrets requires a graphical Linux session")

    atspi = load_verifier()
    repository_root = Path(__file__).resolve().parent.parent
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    atspi.run(
        [
            "dotnet", "build", "src/Harness.Host/Harness.Host.csproj", "--no-restore",
            "--nologo", "--verbosity", "quiet", "-p:UseSharedCompilation=false", "-m:1",
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
        with tempfile.TemporaryDirectory(prefix="harness-user-secrets-capture-") as temporary:
            root = Path(temporary)
            repository = root / "repository"
            atspi.run(
                [
                    "dotnet", "new", "console", "--framework", "net10.0", "--name",
                    "Representative", "--output", str(repository), "--no-restore",
                ],
                repository_root,
                quiet=True,
            )
            project = repository / "Representative.csproj"
            project.write_text(
                project.read_text(encoding="utf-8").replace(
                    "</Project>",
                    "  <PropertyGroup>\n"
                    f"    <UserSecretsId>harness-capture-{root.name}</UserSecretsId>\n"
                    "  </PropertyGroup>\n"
                    "</Project>",
                ),
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
            application.invoke("Open the command palette")
            application.wait_for_name("Command palette filter", "entry")
            application.set_text("Command palette filter", "project user secrets")
            application.wait_for_name("Manage project User Secrets…", "push button")
            application.invoke("Manage project User Secrets…")
            application.wait_for_name("Project User Secrets", "frame")
            application.wait_for_name("Add project secret", "push button")
            application.invoke("Add project secret")
            application.wait_for_name("Add project secret", "frame")
            application.invoke("Cancel")
            application.wait_for_name("Project User Secrets", "frame")
            if shutil.which("wmctrl") is not None:
                subprocess.run(
                    ["wmctrl", "-r", "Project User Secrets", "-e", "0,160,80,760,650"],
                    check=True,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                )
                subprocess.run(
                    ["wmctrl", "-a", "Project User Secrets"],
                    check=True,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                )
            time.sleep(1)
            screenshot(arguments.output)
    finally:
        if process is not None:
            atspi.stop(process)
        status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled))

    print(f"captured Project User Secrets to {arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
