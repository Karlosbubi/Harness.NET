#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

# Deterministic tests use local fakes and never load repository .env credentials.
dotnet test "$repository_root/Harness.slnx" \
  --no-restore \
  --nologo \
  --verbosity minimal

"$repository_root/eng/verify-linux-x64-publish.sh"

echo "Harness.NET v1 release verification passed"
