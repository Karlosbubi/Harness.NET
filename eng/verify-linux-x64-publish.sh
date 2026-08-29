#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
publish_root=$(mktemp -d "${TMPDIR:-/tmp}/harness-linux-x64-publish.XXXXXX")
smoke_root=$(mktemp -d "${TMPDIR:-/tmp}/harness-linux-x64-smoke.XXXXXX")
recovery_root=$(mktemp -d "${TMPDIR:-/tmp}/harness-linux-x64-recovery.XXXXXX")
process_id=""
schema_version=$(sed -n \
  's/.*CurrentSchemaVersion = \([0-9][0-9]*\);/\1/p' \
  "$repository_root/src/Harness.DataAccess/Persistence/SqliteDatabaseInitializer.cs")
test -n "$schema_version"

sqlite_query() {
  python3 - "$1" "$2" <<'PY'
import sqlite3
import sys

database, statement = sys.argv[1:]
with sqlite3.connect(database) as connection:
    if statement.lstrip().upper().startswith(("PRAGMA", "SELECT")):
        row = connection.execute(statement).fetchone()
        if row is not None:
            print(row[0])
    else:
        connection.executescript(statement)
PY
}

cleanup() {
  if [[ -n "$process_id" ]] && kill -0 "$process_id" 2>/dev/null; then
    kill -TERM "$process_id" 2>/dev/null || true
    wait "$process_id" 2>/dev/null || true
  fi

  rm -rf "$publish_root" "$smoke_root" "$recovery_root"
}
trap cleanup EXIT

dotnet publish "$repository_root/src/Harness.Host/Harness.Host.csproj" \
  -p:PublishProfile=linux-x64 \
  --output "$publish_root"

test -x "$publish_root/Harness.Host"
test -f "$publish_root/harness.xml"
test -f "$publish_root/THIRD-PARTY-NOTICES.md"
grep -q 'ICSharpCode.Decompiler.*10.1.1.8388' "$publish_root/THIRD-PARTY-NOTICES.md"
test -n "$(find "$publish_root" -maxdepth 1 -type f -name '*.so' -print -quit)"
test -f "$publish_root/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll"
test -f "$publish_root/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json"
test ! -f "$publish_root/Microsoft.Build.dll"

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
  if grep -q "Harness.NET ready (schema $schema_version)" "$output_file"; then
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

database_path="$smoke_root/data/harness.net/harness.db"
sqlite_query "$database_path" \
  "INSERT INTO conversations (id, title, model, created_at, updated_at) VALUES ('release-proof', 'Release proof', 'none', '2026-07-28T00:00:00Z', '2026-07-28T00:00:00Z');"
backup_path="$smoke_root/harness-state.zip"
layout_directory="$smoke_root/state/harness.net"
layout_payload='{"Version":1}'
layout_payload_sha=$(printf '%s' "$layout_payload" | sha256sum | cut -d ' ' -f 1)
mkdir -p "$layout_directory"
printf '{"Format":"harness-workbench-layout-v1","Version":1,"Payload":"{\\"Version\\":1}","PayloadSha256":"%s"}' \
  "$layout_payload_sha" >"$layout_directory/workbench-layout.json"
chmod 600 "$layout_directory/workbench-layout.json"
env -i \
  PATH="$smoke_root/no-installed-tools" \
  DOTNET_ROOT="$smoke_root/no-installed-dotnet" \
  XDG_CONFIG_HOME="$smoke_root/config" \
  XDG_DATA_HOME="$smoke_root/data" \
  XDG_STATE_HOME="$smoke_root/state" \
  XDG_CACHE_HOME="$smoke_root/cache" \
  "$publish_root/Harness.Host" --backup-path="$backup_path" \
  >"$smoke_root/backup.log" 2>&1
grep -q "Harness.NET backup created (schema $schema_version" "$smoke_root/backup.log"
test -f "$backup_path"
test "$(unzip -Z1 "$backup_path" | sort | tr '\n' ' ')" = \
  "harness.db manifest.json workbench-layout.json "
manifest=$(unzip -p "$backup_path" manifest.json)
expected_database_sha=$(sed -n \
  's/.*"DatabaseSha256":"\([0-9a-f]\{64\}\)".*/\1/p' <<<"$manifest")
expected_layout_sha=$(sed -n \
  's/.*"WorkbenchLayout":{[^}]*"Sha256":"\([0-9a-f]\{64\}\)".*/\1/p' <<<"$manifest")
actual_database_sha=$(unzip -p "$backup_path" harness.db | sha256sum | cut -d ' ' -f 1)
actual_layout_sha=$(unzip -p "$backup_path" workbench-layout.json | sha256sum | cut -d ' ' -f 1)
test -n "$expected_database_sha"
test -n "$expected_layout_sha"
test "$actual_database_sha" = "$expected_database_sha"
test "$actual_layout_sha" = "$expected_layout_sha"
grep -q '"Format":"harness-backup-v2"' <<<"$manifest"
grep -q "\"SchemaVersion\":$schema_version" <<<"$manifest"

mkdir -p "$recovery_root/config" "$recovery_root/data/harness.net" \
  "$recovery_root/state/harness.net" "$recovery_root/cache"
unzip -p "$backup_path" harness.db >"$recovery_root/data/harness.net/harness.db"
unzip -p "$backup_path" workbench-layout.json \
  >"$recovery_root/state/harness.net/workbench-layout.json"
recovered_database="$recovery_root/data/harness.net/harness.db"
test "$(sha256sum "$recovery_root/state/harness.net/workbench-layout.json" | cut -d ' ' -f 1)" = \
  "$expected_layout_sha"
test "$(sqlite_query "$recovered_database" "PRAGMA integrity_check;")" = "ok"
test "$(sqlite_query "$recovered_database" \
  "SELECT COUNT(*) FROM conversations WHERE id='release-proof';")" = "1"

sqlite_query "$recovered_database" \
  "DROP TABLE appearance_preferences; DROP TABLE agent_role_defaults; DROP TABLE goal_budget_extensions; DROP TABLE remote_spend_preferences; DROP TABLE visual_capture_preferences; DROP TABLE editor_intelligence_preferences; DROP TABLE keybinding_preferences; DROP TABLE keybinding_configuration; DROP TABLE developer_dotnet_executions; DELETE FROM SchemaVersions WHERE ScriptName LIKE '%018_AppearancePreferences.sql' OR ScriptName LIKE '%019_AgentRoleDefaults.sql' OR ScriptName LIKE '%020_RenameEvidence.sql' OR ScriptName LIKE '%021_GoalBudgetExtensions.sql' OR ScriptName LIKE '%022_RemoteSpendPreferences.sql' OR ScriptName LIKE '%023_AgentOutputTokenLimits.sql' OR ScriptName LIKE '%024_RemoveAgentOutputTokenLimits.sql' OR ScriptName LIKE '%025_VisualCapturePreferences.sql' OR ScriptName LIKE '%026_EditorIntelligencePreferences.sql' OR ScriptName LIKE '%027_EditorFormattingPreferences.sql' OR ScriptName LIKE '%028_KeybindingPreferences.sql' OR ScriptName LIKE '%029_EditorInputMode.sql' OR ScriptName LIKE '%030_DeveloperDotNetExecutions.sql' OR ScriptName LIKE '%031_AgentReasoningPolicy.sql' OR ScriptName LIKE '%032_DeveloperDotNetBuildOperations.sql' OR ScriptName LIKE '%033_DeveloperDotNetTestOperations.sql' OR ScriptName LIKE '%034_DeveloperDotNetTestScopes.sql' OR ScriptName LIKE '%035_DeveloperDotNetTestSelections.sql'; UPDATE application_metadata SET value='17' WHERE key='schema_version';"
env -i \
  PATH="$recovery_root/no-installed-tools" \
  DOTNET_ROOT="$recovery_root/no-installed-dotnet" \
  XDG_CONFIG_HOME="$recovery_root/config" \
  XDG_DATA_HOME="$recovery_root/data" \
  XDG_STATE_HOME="$recovery_root/state" \
  XDG_CACHE_HOME="$recovery_root/cache" \
  "$publish_root/Harness.Host" --no-ui >"$recovery_root/upgrade.log" 2>&1
if ! grep -q "Harness.NET ready (schema $schema_version)" "$recovery_root/upgrade.log"; then
  sed -n '1,120p' "$recovery_root/upgrade.log"
  exit 1
fi
test -n "$(find "$recovery_root/data/harness.net/backups" \
  -type f -name 'pre-upgrade-*.zip' -print -quit)"
test "$(sqlite_query "$recovered_database" \
  "SELECT COUNT(*) FROM conversations WHERE id='release-proof';")" = "1"
test "$(sqlite_query "$recovered_database" \
  "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='appearance_preferences';")" = "1"

echo "linux-x64 publish verification passed"
