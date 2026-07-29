#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

"$repository_root/eng/verify-v1-release.sh"
"$repository_root/eng/verify-avalonia-atspi.py" --with-orca
"$repository_root/eng/verify-avalonia-workflow.py"

echo "Harness.NET 1.0 desktop release verification passed"
