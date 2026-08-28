#!/usr/bin/env python3
"""Verify durable documentation links and repository governance metadata."""

from __future__ import annotations

from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlparse
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent.parent
DECISIONS = ROOT / "docs/decisions"
ACCEPTANCE = ROOT / "docs/acceptance"
PACKAGES = ROOT / "Directory.Packages.props"
NOTICES = ROOT / "THIRD-PARTY-NOTICES.md"
PREVIEW_POLICY = DECISIONS / "027-contributor-verification-and-dependency-governance.md"

MARKDOWN_LINK = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
DECISION_STATUS = re.compile(r"^- Status: (\w+)$", re.MULTILINE)
INDEX_ROW = re.compile(r"^\| \[(\d{3})\]\([^)]+\) \| (\w+) \|", re.MULTILINE)
NOTICE_PACKAGE = re.compile(r"^Package: `([^`]+)` `([^`]+)`$", re.MULTILINE)


def markdown_files() -> list[Path]:
    roots = [ROOT / "README.md", ROOT / "CONTRIBUTING.md", ROOT / "AGENTS.md", NOTICES]
    return [path for path in roots if path.is_file()] + sorted((ROOT / "docs").rglob("*.md"))


def verify_local_links(errors: list[str]) -> int:
    checked = 0
    for document in markdown_files():
        text = document.read_text(encoding="utf-8")
        for match in MARKDOWN_LINK.finditer(text):
            raw = match.group(1).strip()
            if raw.startswith("<") and raw.endswith(">"):
                raw = raw[1:-1]
            raw = raw.split(maxsplit=1)[0]
            parsed = urlparse(raw)
            if parsed.scheme or raw.startswith("#"):
                continue
            relative = unquote(parsed.path)
            if not relative:
                continue
            target = (ROOT / relative.lstrip("/")) if relative.startswith("/") else (document.parent / relative)
            checked += 1
            if not target.exists():
                line = text.count("\n", 0, match.start()) + 1
                errors.append(f"{document.relative_to(ROOT)}:{line}: missing link target {raw}")
    return checked


def verify_decision_index(errors: list[str]) -> int:
    index = (DECISIONS / "README.md").read_text(encoding="utf-8")
    indexed = dict(INDEX_ROW.findall(index))
    checked = 0
    for decision in sorted(DECISIONS.glob("[0-9][0-9][0-9]-*.md")):
        number = decision.name[:3]
        if number == "000":
            continue
        match = DECISION_STATUS.search(decision.read_text(encoding="utf-8"))
        if match is None:
            errors.append(f"{decision.relative_to(ROOT)}: missing decision status")
            continue
        checked += 1
        actual = match.group(1)
        expected = indexed.get(number)
        if expected != actual:
            errors.append(
                f"docs/decisions/README.md: ADR {number} says {expected or 'missing'}, file says {actual}"
            )
    return checked


def central_packages() -> dict[str, str]:
    root = ET.parse(PACKAGES).getroot()
    return {
        item.attrib["Include"]: item.attrib["Version"]
        for item in root.findall(".//PackageVersion")
    }


def verify_notices_and_preview_policy(errors: list[str]) -> int:
    packages = central_packages()
    notices = NOTICE_PACKAGE.findall(NOTICES.read_text(encoding="utf-8"))
    checked = 0
    for name, version in notices:
        checked += 1
        central = packages.get(name)
        if central != version:
            errors.append(
                f"THIRD-PARTY-NOTICES.md: {name} is {version}, central version is {central or 'missing'}"
            )

    policy = PREVIEW_POLICY.read_text(encoding="utf-8") if PREVIEW_POLICY.is_file() else ""
    for name, version in packages.items():
        if "preview" not in version.lower():
            continue
        checked += 1
        if name not in policy or version not in policy:
            errors.append(
                f"{PACKAGES.name}: preview dependency {name} {version} lacks an exact ADR 027 exit record"
            )
    return checked


def verify_acceptance_artifact_labels(errors: list[str]) -> int:
    checked = 0
    for document in sorted(ACCEPTANCE.glob("*.md")):
        for number, line in enumerate(document.read_text(encoding="utf-8").splitlines(), start=1):
            if line.startswith("Artifact:"):
                errors.append(
                    f"{document.relative_to(ROOT)}:{number}: label ignored output as machine-local, not durable evidence"
                )
            checked += 1
    return checked


def verify_live_test_tiers(errors: list[str]) -> int:
    checked = 0
    for source in sorted((ROOT / "tests").rglob("*.cs")):
        lines = source.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if '[Trait("Category",' not in line or "Live" not in line:
                continue
            checked += 1
            nearby = "\n".join(lines[index + 1:index + 4])
            if '[Trait("Tier", "Live")]' not in nearby:
                errors.append(
                    f"{source.relative_to(ROOT)}:{index + 1}: live category lacks Tier=Live"
                )
    return checked


def main() -> int:
    errors: list[str] = []
    link_count = verify_local_links(errors)
    decision_count = verify_decision_index(errors)
    metadata_count = verify_notices_and_preview_policy(errors)
    acceptance_lines = verify_acceptance_artifact_labels(errors)
    live_tier_count = verify_live_test_tiers(errors)
    if errors:
        print("Repository metadata verification failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(
        "Repository metadata verified: "
        f"{link_count} local links, {decision_count} ADR statuses, "
        f"{metadata_count} dependency records, {acceptance_lines} acceptance lines, "
        f"{live_tier_count} live test tiers."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
