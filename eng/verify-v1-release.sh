#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

# Deterministic tests use local fakes and never load repository .env credentials.
python3 "$repository_root/eng/verify-repository-metadata.py"
python3 "$repository_root/eng/test_local_model_regression.py"

dotnet test "$repository_root/Harness.slnx" \
  --no-restore \
  --filter 'Tier!=Live' \
  --maxcpucount:1 \
  --nologo \
  --verbosity minimal

"$repository_root/eng/verify-linux-x64-publish.sh"

echo "Harness.NET v1 release verification passed"
