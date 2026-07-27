#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
publish_root=$(mktemp -d "${TMPDIR:-/tmp}/harness-linux-x64-publish.XXXXXX")
smoke_root=$(mktemp -d "${TMPDIR:-/tmp}/harness-linux-x64-smoke.XXXXXX")
process_id=""

cleanup() {
  if [[ -n "$process_id" ]] && kill -0 "$process_id" 2>/dev/null; then
    kill -TERM "$process_id" 2>/dev/null || true
    wait "$process_id" 2>/dev/null || true
  fi

  rm -rf "$publish_root" "$smoke_root"
}
trap cleanup EXIT

dotnet publish "$repository_root/src/Harness.Host/Harness.Host.csproj" \
  -p:PublishProfile=linux-x64 \
  --output "$publish_root"

test -x "$publish_root/Harness.Host"
test -f "$publish_root/harness.xml"
test -n "$(find "$publish_root" -maxdepth 1 -type f -name '*.so' -print -quit)"

output_file="$smoke_root/output.log"
env -i \
  PATH="$smoke_root/no-installed-tools" \
  DOTNET_ROOT="$smoke_root/no-installed-dotnet" \
  XDG_CONFIG_HOME="$smoke_root/config" \
  XDG_DATA_HOME="$smoke_root/data" \
  XDG_STATE_HOME="$smoke_root/state" \
  XDG_CACHE_HOME="$smoke_root/cache" \
  "$publish_root/Harness.Host" --wait-for-shutdown >"$output_file" 2>&1 &
process_id=$!

ready=0
for _ in $(seq 1 100); do
  if grep -q "Harness.NET ready (schema 11)" "$output_file"; then
    ready=1
    break
  fi

  if ! kill -0 "$process_id" 2>/dev/null; then
    break
  fi

  sleep 0.05
done

if [[ "$ready" -ne 1 ]]; then
  sed -n '1,120p' "$output_file"
  exit 1
fi

kill -TERM "$process_id"
wait "$process_id"
process_id=""

test -f "$smoke_root/data/harness.net/harness.db"
test -n "$(find "$smoke_root/state/harness.net/logs" -type f -name 'harness-*.jsonl' -print -quit)"
test ! -e "$smoke_root/config/harness.net/harness.xml"
test ! -e "$smoke_root/cache/harness.net"

echo "linux-x64 publish verification passed"
