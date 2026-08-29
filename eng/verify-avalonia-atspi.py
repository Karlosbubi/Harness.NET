#!/usr/bin/env python3
"""Exercise the production Avalonia workbench through Linux AT-SPI.

This verifier is intentionally separate from the deterministic release gate because
it needs a running graphical Linux session and the session accessibility bus. It
uses an isolated XDG home and a temporary real Git repository. It never sends a
conversation message or invokes a model.
"""

from __future__ import annotations

import argparse
import logging
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time
from typing import Callable

try:
    import dbus
except ImportError as error:
    raise SystemExit("python3-dbus is required for the Avalonia AT-SPI verifier") from error


ACCESSIBLE = "org.a11y.atspi.Accessible"
ACTION = "org.a11y.atspi.Action"
COMPONENT = "org.a11y.atspi.Component"
EDITABLE_TEXT = "org.a11y.atspi.EditableText"
PROPERTIES = "org.freedesktop.DBus.Properties"
TEXT = "org.a11y.atspi.Text"
ROOT_PATH = "/org/a11y/atspi/accessible/root"

logging.getLogger("dbus.proxies").setLevel(logging.CRITICAL)


class AccessibleNode:
    def __init__(self, path: str, role: str, name: str, interfaces: list[str]):
        self.path = path
        self.role = role
        self.name = name
        self.interfaces = interfaces


class AtSpiApplication:
    def __init__(self, bus: dbus.bus.BusConnection, destination: str):
        self.bus = bus
        self.destination = destination

    def nodes(self) -> list[AccessibleNode]:
        result: list[AccessibleNode] = []
        visited: set[str] = set()

        def walk(path: str) -> None:
            if path in visited:
                return
            visited.add(path)
            obj = self.bus.get_object(self.destination, path)
            accessible = dbus.Interface(obj, ACCESSIBLE)
            properties = dbus.Interface(obj, PROPERTIES)
            try:
                role = str(accessible.GetRoleName())
                name = str(properties.Get(ACCESSIBLE, "Name"))
                interfaces = [str(item) for item in accessible.GetInterfaces()]
                children = list(accessible.GetChildren())
            except dbus.DBusException:
                return
            result.append(AccessibleNode(path, role, name, interfaces))
            for child_destination, child_path in children:
                if str(child_destination) in ("", self.destination):
                    walk(str(child_path))

        walk(ROOT_PATH)
        return result

    def find(self, name: str, role: str | None = None) -> AccessibleNode:
        matches = [
            node
            for node in self.nodes()
            if node.name == name and (role is None or node.role == role)
        ]
        if not matches:
            qualifier = f" {role}" if role else ""
            raise AssertionError(f"AT-SPI did not expose{qualifier} {name!r}")
        return matches[-1]

    def find_containing(self, text: str, role: str | None = None) -> AccessibleNode:
        matches = [
            node
            for node in self.nodes()
            if text in node.name and (role is None or node.role == role)
        ]
        if not matches:
            qualifier = f" {role}" if role else ""
            raise AssertionError(f"AT-SPI did not expose{qualifier} containing {text!r}")
        return matches[-1]

    def wait_for(
        self,
        predicate: Callable[[list[AccessibleNode]], bool],
        message: str,
        timeout: float = 10,
    ) -> None:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate(self.nodes()):
                return
            time.sleep(0.1)
        raise AssertionError(message)

    def wait_for_name(
        self, name: str, role: str | None = None, timeout: float = 10
    ) -> None:
        self.wait_for(
            lambda nodes: any(
                node.name == name and (role is None or node.role == role)
                for node in nodes
            ),
            f"AT-SPI did not expose {role or 'control'} {name!r}",
            timeout,
        )

    def wait_for_name_containing(
        self,
        text: str,
        role: str | None = None,
        timeout: float = 10,
    ) -> None:
        self.wait_for(
            lambda nodes: any(
                text in node.name and (role is None or node.role == role)
                for node in nodes
            ),
            f"AT-SPI did not expose {role or 'control'} containing {text!r}",
            timeout,
        )

    def invoke(self, name: str, role: str = "push button") -> None:
        self.invoke_node(self.find(name, role))

    def invoke_node(self, node: AccessibleNode) -> None:
        if ACTION not in node.interfaces:
            raise AssertionError(
                f"{node.role} {node.name!r} does not expose an AT-SPI action"
            )
        action = dbus.Interface(self.bus.get_object(self.destination, node.path), ACTION)
        if not bool(action.DoAction(0)):
            raise AssertionError(f"AT-SPI action failed for {node.role} {node.name!r}")

    def invoke_containing(self, text: str, role: str) -> None:
        node = self.find_containing(text, role)
        if ACTION not in node.interfaces:
            raise AssertionError(f"{role} containing {text!r} has no AT-SPI action")
        action = dbus.Interface(self.bus.get_object(self.destination, node.path), ACTION)
        if not bool(action.DoAction(0)):
            raise AssertionError(f"AT-SPI action failed for {role} containing {text!r}")

    def wait_and_invoke(self, name: str, role: str, timeout: float = 30) -> None:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            matches = [
                node
                for node in self.nodes()
                if node.name == name and node.role == role and ACTION in node.interfaces
            ]
            for node in reversed(matches):
                action = dbus.Interface(
                    self.bus.get_object(self.destination, node.path), ACTION
                )
                try:
                    if bool(action.DoAction(0)):
                        return
                except dbus.DBusException as error:
                    if "Element not enabled" not in str(error):
                        raise
            time.sleep(0.1)
        raise AssertionError(f"AT-SPI could not invoke {role} {name!r}")

    def focus(self, name: str, role: str | None) -> None:
        node = self.find(name, role)
        component = dbus.Interface(
            self.bus.get_object(self.destination, node.path), COMPONENT
        )
        if not bool(component.GrabFocus()):
            raise AssertionError(
                f"AT-SPI could not focus {role or 'control'} {name!r}"
            )
        time.sleep(0.5)

    def set_text(self, name: str, value: str) -> None:
        nodes = self.nodes()
        matches = [node for node in nodes if node.name == name]
        if not matches:
            available = [
                (node.role, node.name)
                for node in nodes
                if EDITABLE_TEXT in node.interfaces
            ]
            raise AssertionError(
                f"AT-SPI did not expose editable control {name!r}; available: {available}"
            )
        node = next((item for item in reversed(matches)
                     if EDITABLE_TEXT in item.interfaces), matches[-1])
        if EDITABLE_TEXT not in node.interfaces:
            raise AssertionError(
                f"{node.role} {name!r} is not editable through AT-SPI"
            )
        editable = dbus.Interface(
            self.bus.get_object(self.destination, node.path), EDITABLE_TEXT
        )
        if not bool(editable.SetTextContents(value)):
            raise AssertionError(f"AT-SPI could not set {node.role} {name!r}")

    def text(self, name: str, role: str) -> str:
        node = self.find(name, role)
        if TEXT not in node.interfaces:
            raise AssertionError(f"{role} {name!r} does not expose AT-SPI text")
        text = dbus.Interface(self.bus.get_object(self.destination, node.path), TEXT)
        return str(text.GetText(0, -1))

def run(command: list[str], cwd: Path, quiet: bool = False) -> None:
    output = subprocess.DEVNULL if quiet else None
    subprocess.run(
        command,
        cwd=cwd,
        check=True,
        stdout=output,
        stderr=output,
    )


def wait_for_application(
    accessibility_bus: dbus.bus.BusConnection, process_id: int
) -> AtSpiApplication:
    dbus_object = accessibility_bus.get_object(
        "org.freedesktop.DBus", "/org/freedesktop/DBus"
    )
    dbus_interface = dbus.Interface(dbus_object, "org.freedesktop.DBus")
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        for destination in dbus_interface.ListNames():
            destination = str(destination)
            if not destination.startswith(":"):
                continue
            try:
                owner_process_id = int(
                    dbus_interface.GetConnectionUnixProcessID(destination)
                )
            except dbus.DBusException:
                continue
            if owner_process_id != process_id:
                continue
            application = AtSpiApplication(accessibility_bus, destination)
            try:
                application.wait_for_name("Harness.NET", "frame")
                return application
            except (AssertionError, dbus.DBusException):
                continue
        time.sleep(0.1)
    raise AssertionError("Harness.Host did not register on the AT-SPI bus")


def launch(
    executable: Path,
    environment: dict[str, str],
    accessibility_bus: dbus.bus.BusConnection,
) -> tuple[subprocess.Popen[bytes], AtSpiApplication]:
    process = subprocess.Popen(
        [str(executable), "--ui=avalonia"],
        env=environment,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    try:
        return process, wait_for_application(accessibility_bus, process.pid)
    except BaseException:
        stop(process)
        raise


def stop(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)


def verify_initial_accessibility(application: AtSpiApplication) -> None:
    application.wait_for_name("Files", "page tab")
    application.invoke("Files", "page tab")
    application.wait_for_name("Filter repository file tree", "entry")
    application.wait_for_name("Repository file tree", "tree")
    application.wait_for_name("Search tracked workspace text", "entry")
    application.wait_for_name("Files panel controls", "title bar")
    application.invoke("Workspace", "page tab")
    expected = [
        ("Docked workspace workbench", "panel"),
        ("Open editor documents", "combo box"),
        ("Selected goal evidence", "list"),
        ("Conversation panel controls", "title bar"),
        ("Goal context panel controls", "title bar"),
        ("Resize adjacent workbench panels", "push button"),
    ]
    for name, role in expected:
        application.wait_for_name(name, role)
    generic_actions = [
        node.name
        for node in application.nodes()
        if ACTION in node.interfaces and "Viewbox" in node.name
    ]
    if generic_actions:
        raise AssertionError(f"generic Dock actions remain exposed: {generic_actions}")


def exercise_orca_speech(application: AtSpiApplication) -> None:
    application.wait_for_name("Conversation model")
    application.focus("Conversation model", None)
    application.wait_for_name("Open editor documents", "combo box")
    application.focus("Open editor documents", "combo box")
    application.invoke("Open the command palette")
    application.wait_for_name("Command palette filter", "entry")
    application.set_text("Command palette filter", "save layout")
    application.wait_for_name("Save workbench layout", "push button")
    application.focus("Save workbench layout", "push button")
    application.invoke("Save workbench layout")
    application.wait_for_name("Workspace", "page tab")
    application.focus("Workspace", "page tab")
    application.invoke("Workspace", "page tab")
    application.wait_for_name("Manage workspaces", "push button")
    application.focus("Manage workspaces", "push button")
    application.invoke("Manage workspaces")
    application.wait_for_name("Manage workspaces", "frame")
    application.focus("Repository path", "entry")
    application.focus("Browse for repository folder", "push button")
    application.focus("Inspect", "push button")
    application.focus("Close", "push button")
    application.invoke("Close")
    time.sleep(1)


def verify_visual_settings_accessibility(application: AtSpiApplication) -> None:
    application.invoke("Open Settings")
    application.wait_for_name("Settings", "frame")
    application.invoke_containing("Visual verification", "list item")
    for name in (
        "Enable visual verification capture",
        "Maximum visual capture size in MiB",
        "Visual capture retention days",
        "Maximum visual captures per goal",
        "Allow remote model access to visual captures",
        "Capture one visual verification frame",
        "Stored visual captures for selected goal",
        "Delete selected visual capture",
    ):
        application.wait_for_name(name)
    application.invoke("Close")
    time.sleep(1)


def verify_project_user_secrets_accessibility(application: AtSpiApplication) -> None:
    application.invoke("Open the command palette")
    application.wait_for_name("Command palette filter", "entry")
    application.set_text("Command palette filter", "project user secrets")
    application.wait_for_name("Manage project User Secrets…", "push button")
    application.invoke("Manage project User Secrets…")
    application.wait_for_name("Project User Secrets", "frame")
    for name in (
        "Project User Secrets project",
        "Project User Secret keys",
        "Selected project secret value",
        "Reveal selected project secret",
        "Copy selected project secret",
        "Add project secret",
        "Change selected project secret",
        "Delete selected project secret",
        "Refresh Project User Secrets",
    ):
        application.wait_for_name(name)
    application.invoke("Close")
    time.sleep(1)


def verify_solution_build_accessibility(application: AtSpiApplication) -> None:
    application.invoke("Open the command palette")
    application.wait_for_name("Command palette filter", "entry")
    application.set_text("Command palette filter", "build startup project")
    application.wait_for_name("Build startup project", "push button")
    application.invoke("Build startup project")
    application.wait_for_name_containing(
        "Build Representative.csproj", "list item", timeout=30
    )


def verify_orca_speech(debug_log: Path) -> None:
    speech_lines = [
        line for line in debug_log.read_text(encoding="utf-8").splitlines()
        if "SPEECH OUTPUT:" in line
    ]
    utterances = [
        match.group(1)
        for line in speech_lines
        if (match := re.search(r"SPEECH OUTPUT: '([^']*)'", line)) is not None
    ]
    expected = [
        "Conversation model",
        "Open editor documents",
        "Save workbench layout",
        "Workspace",
        "Manage workspaces",
        "Inspect",
    ]
    missing = [
        expected_name
        for expected_name in expected
        if not any(
            utterance == expected_name or utterance.startswith(f"{expected_name} ")
            for utterance in utterances
        )
    ]
    if missing:
        raise AssertionError(f"Orca did not generate expected speech: {missing}")

    implementation_names = [
        "Grid",
        "StackPanel",
        "Border",
        "ContentPresenter",
        "ScrollContentPresenter",
        "DockableControl",
        "DeferredContentControl",
        "DeferredContentPresenter",
        "VisualLayerManager",
    ]
    leaked = [
        name for name in implementation_names
        if any(item == name or item.startswith(f"{name} ") for item in utterances)
    ]
    if leaked:
        raise AssertionError(f"Orca spoke framework implementation names: {leaked}")


def read_gsettings(schema: str, key: str) -> str:
    return subprocess.check_output(
        ["gsettings", "get", schema, key], text=True
    ).strip()


def restore_gsettings(schema: str, key: str, value: str) -> None:
    subprocess.run(
        ["gsettings", "set", schema, key, value],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def register_workspace(application: AtSpiApplication, repository: Path) -> None:
    application.invoke("Workspace", "page tab")
    application.wait_for_name("Manage workspaces", "push button")
    application.invoke("Manage workspaces")
    application.wait_for_name("Manage workspaces", "frame")
    application.set_text("Repository path", str(repository))
    application.invoke("Inspect")
    application.wait_for_name("Representative.csproj", "list item")
    application.invoke("Register")
    application.wait_for_name_containing("untrusted", "list item")
    application.invoke("Trust…")
    application.wait_for_name("Trust workspace", "frame")
    application.invoke("Trust workspace")
    application.wait_for_name_containing("trusted", "list item")
    application.invoke("Close")
    application.wait_for_name_containing("Trust: Trusted", "label")


def create_and_approve_goal(application: AtSpiApplication) -> None:
    application.invoke("Conversation", "page tab")
    application.focus("Goal or message composer", "entry")
    description = (
        "AT-SPI representative change: add one real source change and preserve "
        "deterministic verification."
    )
    application.set_text(
        "Goal or message composer",
        description,
    )
    if application.text("Goal or message composer", "entry") != description:
        raise AssertionError("AT-SPI composer text did not round-trip after editing")
    application.wait_and_invoke("Submit composer", "push button")
    application.wait_for_name_containing(
        "Goal: AT-SPI representative change", "panel"
    )

    application.invoke("Write plan manually")
    application.wait_for_name("Write plan manually", "frame")
    application.set_text(
        "Plan content",
        "1. Update Program.cs in the isolated goal worktree.\n"
        "2. Build and test before requesting exact commit approval.",
    )
    application.invoke("Save plan")
    application.wait_for_name_containing("Plan: Plan revision 1, Pending", "panel")

    application.invoke("Approve plan")
    application.wait_for_name("Approve plan and capabilities", "frame")
    application.invoke("Approve and create worktree")
    application.wait_for_name_containing(
        ", Approved", "panel"
    )


def verify_documents_and_search(
    application: AtSpiApplication, repository: Path
) -> None:
    worktree_lines = subprocess.check_output(
        ["git", "-C", str(repository), "worktree", "list", "--porcelain"],
        text=True,
    ).splitlines()
    worktrees = [
        Path(line.removeprefix("worktree "))
        for line in worktree_lines
        if line.startswith("worktree ")
    ]
    goal_worktrees = [path for path in worktrees if path.resolve() != repository.resolve()]
    if len(goal_worktrees) != 1:
        raise AssertionError(f"expected one isolated goal worktree, found {goal_worktrees}")
    goal_worktree = goal_worktrees[0]
    run(
        [
            "dotnet",
            "restore",
            str(goal_worktree / "Representative.csproj"),
            "--nologo",
            "--verbosity",
            "quiet",
        ],
        goal_worktree,
        quiet=True,
    )

    application.invoke("Files", "page tab")
    application.wait_for_name("Program.cs", "push button")
    application.invoke("Program.cs")
    application.wait_for_name("Editable source editor for Program.cs", "panel")
    application.wait_for_name("Show CodeLens actions", "push button", timeout=30)

    application.wait_for_name("Representative.csproj", "push button")
    application.invoke("Representative.csproj")
    application.wait_for_name(
        "Editable source editor for Representative.csproj", "panel"
    )

    if not application.text("Open editor documents", "combo box").startswith(
        "Representative.csproj · "
    ):
        raise AssertionError("the accessible document switcher did not track the active document")

    application.invoke("Open editor documents", "combo box")
    application.wait_for_name_containing("Program.cs · ", "list item")
    application.invoke_containing("Program.cs · ", "list item")
    application.wait_for_name("Editable source editor for Program.cs", "panel")
    if not application.text("Open editor documents", "combo box").startswith("Program.cs · "):
        raise AssertionError("AT-SPI could not switch between real source documents")

    application.wait_for_name("Save Program.cs", "push button")
    for action in (
        "Show IntelliSense for Program.cs",
        "Show symbol information for Program.cs",
        "Go to definition in Program.cs",
        "Find usages in Program.cs",
        "Go to implementation in Program.cs",
        "Show quick fixes for Program.cs",
        "Transform Program.cs",
    ):
        application.wait_for_name(action, "push button")
    application.invoke("Focus the active editor document")
    application.wait_and_invoke("Show CodeLens actions", "push button")
    try:
        application.wait_for_name_containing(
            "Run project at line", "push button", timeout=30
        )
    except AssertionError as error:
        nodes = application.nodes()
        code_lens_buttons = [
            node.name
            for node in nodes
            if node.role == "push button"
            and any(term in node.name for term in ("line", "reference", "test", "Run"))
        ]
        intelligence_status = [
            node.name
            for node in nodes
            if node.role == "label"
            and any(
                term in node.name.lower()
                for term in (
                    "code intelligence",
                    "problem",
                    "semantic",
                    "roslyn",
                    "stale",
                    "unavailable",
                )
            )
        ]
        editor_status: list[str] = []
        for node in nodes:
            if node.name not in (
                "Editing status for Program.cs",
                "Caret and format for Program.cs",
                "Code intelligence status",
            ) or TEXT not in node.interfaces:
                continue
            text = dbus.Interface(
                application.bus.get_object(application.destination, node.path), TEXT
            )
            editor_status.append(f"{node.role}: {text.GetText(0, -1)}")
        raise AssertionError(
            f"{error}; related buttons={code_lens_buttons}; "
            f"code intelligence status={intelligence_status}; "
            f"editor status={editor_status}"
        ) from error
    application.invoke_containing("Run project at line", "push button")
    application.wait_for_name_containing("Run Representative.csproj", "list item")
    if not (goal_worktree / "Program.cs").is_file():
        raise AssertionError("the approved goal worktree does not contain Program.cs")

    application.invoke("Files", "page tab")
    application.wait_for_name("Missing.cs", "push button")
    application.invoke("Missing.cs")
    application.wait_for_name("Editable source editor for Missing.cs", "panel")
    application.invoke("Problems", "page tab")
    application.wait_for_name_containing("CS0246", "list item", timeout=30)
    application.invoke_containing("CS0246", "list item")
    application.invoke("Show quick fixes for Missing.cs")
    application.wait_for_name(
        "Add using System.Text for System.Text.StringBuilder", "push button"
    )
    application.invoke("Add using System.Text for System.Text.StringBuilder")
    application.wait_for_name_containing("Added using System.Text", "label")
    application.invoke("Transform Missing.cs")
    application.wait_for_name("Format changed code in Missing.cs", "push button")
    application.invoke("Format changed code in Missing.cs")
    application.wait_for_name_containing("Changed code is already formatted", "label")
    application.invoke("Save Missing.cs")
    application.wait_for_name_containing("bytes to harness/goal-", "label")
    saved_missing = (goal_worktree / "Missing.cs").read_text(encoding="utf-8")
    if not saved_missing.startswith("using System.Text;"):
        raise AssertionError("the missing-import quick fix was not saved to the goal worktree")

    application.set_text("Search tracked workspace text", "Hello")
    application.invoke("Run tracked workspace search")
    application.wait_for_name_containing("match(es)", "label")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--with-orca",
        action="store_true",
        help="also verify generated screen-reader speech with an isolated Orca process",
    )
    arguments = parser.parse_args()
    if sys.platform != "linux" or not os.environ.get("DISPLAY"):
        raise SystemExit("the Avalonia AT-SPI verifier requires a graphical Linux session")
    if arguments.with_orca and shutil.which("orca") is None:
        raise SystemExit("--with-orca requires Orca on PATH")
    if arguments.with_orca and subprocess.run(
        ["pgrep", "-x", "orca"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    ).returncode == 0:
        raise SystemExit("--with-orca will not replace an existing Orca process")

    repository_root = Path(__file__).resolve().parent.parent
    executable = repository_root / "src/Harness.Host/bin/Debug/net10.0/Harness.Host"
    run(
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
    original_screen_reader = bool(
        status_properties.Get("org.a11y.Status", "ScreenReaderEnabled")
    )
    gsettings_values: dict[tuple[str, str], str] = {}
    if arguments.with_orca:
        for setting in (
            ("org.gnome.desktop.a11y.applications", "screen-reader-enabled"),
            ("org.gnome.desktop.interface", "toolkit-accessibility"),
        ):
            gsettings_values[setting] = read_gsettings(*setting)
    status_properties.Set("org.a11y.Status", "IsEnabled", dbus.Boolean(True))
    status_properties.Set(
        "org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(True)
    )
    accessibility_address = str(
        dbus.Interface(status_object, "org.a11y.Bus").GetAddress()
    )
    accessibility_bus = dbus.bus.BusConnection(accessibility_address)

    process: subprocess.Popen[bytes] | None = None
    orca_process: subprocess.Popen[bytes] | None = None
    try:
        with tempfile.TemporaryDirectory(prefix="harness-atspi-") as temporary:
            root = Path(temporary)
            repository = root / "repository"
            run(
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
                "internal static class Program\n"
                "{\n"
                "    public static void Main()\n"
                "    {\n"
                "        Console.WriteLine(\"Harness run acceptance\");\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            (repository / "Missing.cs").write_text(
                "internal sealed class Missing\n"
                "{\n"
                "    public int Capacity()\n"
                "    {\n"
                "        StringBuilder value = new();\n"
                "        return value.Capacity;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            project_file = repository / "Representative.csproj"
            project_file.write_text(
                project_file.read_text(encoding="utf-8").replace(
                    "</Project>",
                    "  <PropertyGroup>\n"
                    "    <StartupObject>Program</StartupObject>\n"
                    f"    <UserSecretsId>harness-atspi-{root.name}</UserSecretsId>\n"
                    "  </PropertyGroup>\n"
                    "</Project>",
                ),
                encoding="utf-8",
            )
            run(
                ["dotnet", "restore", str(project_file), "--nologo", "--verbosity", "quiet"],
                repository_root,
                quiet=True,
            )
            run(["git", "init", "-q"], repository)
            run(["git", "config", "user.name", "Harness Acceptance"], repository)
            run(
                ["git", "config", "user.email", "acceptance@invalid.example"],
                repository,
            )
            run(["git", "add", "."], repository)
            run(["git", "commit", "-qm", "Initial representative repository"], repository)

            environment = os.environ.copy()
            environment.update(
                {
                    "XDG_CONFIG_HOME": str(root / "config"),
                    "XDG_DATA_HOME": str(root / "data"),
                    "XDG_STATE_HOME": str(root / "state"),
                    "XDG_CACHE_HOME": str(root / "cache"),
                }
            )

            orca_debug = root / "orca-debug.log"
            if arguments.with_orca:
                orca_process = subprocess.Popen(
                    ["orca", "--debug-file", str(orca_debug)],
                    env=environment,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                )
                time.sleep(1)

            process, application = launch(executable, environment, accessibility_bus)
            verify_initial_accessibility(application)
            verify_visual_settings_accessibility(application)
            if arguments.with_orca:
                exercise_orca_speech(application)
            register_workspace(application, repository)
            verify_project_user_secrets_accessibility(application)
            verify_solution_build_accessibility(application)
            create_and_approve_goal(application)
            verify_documents_and_search(application, repository)
            application.invoke("Open the command palette")
            application.wait_for_name("Command palette filter", "entry")
            application.set_text("Command palette filter", "save layout")
            application.wait_for_name("Save workbench layout", "push button")
            application.invoke("Save workbench layout")
            application.wait_for_name("Layout saved", "label")
            stop(process)
            process = None

            process, application = launch(executable, environment, accessibility_bus)
            application.wait_for(
                lambda nodes: any(
                    node.role == "label"
                    and (
                        node.name == "Layout restored"
                        or "Saved layout rejected" in node.name
                    )
                    for node in nodes
                ),
                "AT-SPI did not expose a restored or rejected layout status",
            )
            rejected = [
                node.name
                for node in application.nodes()
                if node.role == "label" and "Saved layout rejected" in node.name
            ]
            if rejected:
                raise AssertionError(rejected[-1])
            application.invoke("Workspace", "page tab")
            application.wait_for_name_containing("Trusted", "label")
            stop(process)
            process = None

            layout = root / "state/harness.net/workbench-layout.json"
            if not layout.is_file():
                raise AssertionError("the production host did not persist its private layout")
            layout.write_text(
                '{"Format":"harness-workbench-layout-v1","Version":1,'
                '"Payload":"corrupt","PayloadSha256":"invalid"}\n',
                encoding="utf-8",
            )

            process, application = launch(executable, environment, accessibility_bus)
            application.wait_for_name_containing("Saved layout rejected", "label")
            verify_initial_accessibility(application)
            stop(process)
            process = None
            if orca_process is not None:
                stop(orca_process)
                orca_process = None
                verify_orca_speech(orca_debug)
    finally:
        if process is not None:
            stop(process)
        if orca_process is not None:
            stop(orca_process)
        status_properties.Set(
            "org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(original_screen_reader)
        )
        status_properties.Set(
            "org.a11y.Status", "IsEnabled", dbus.Boolean(original_enabled)
        )
        for setting, value in gsettings_values.items():
            restore_gsettings(*setting, value)

    suffix = " with Orca speech" if arguments.with_orca else ""
    print(f"Avalonia production AT-SPI verification passed{suffix}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
