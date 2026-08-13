CREATE TABLE developer_dotnet_executions (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    goal_id TEXT NULL,
    source_description TEXT NOT NULL,
    project_path TEXT NOT NULL,
    target_framework TEXT NULL,
    declaration_id TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Running', 'Succeeded', 'Failed', 'Cancelled', 'Interrupted')),
    started_at TEXT NOT NULL,
    completed_at TEXT NULL,
    exit_code INTEGER NULL,
    duration_milliseconds INTEGER NOT NULL DEFAULT 0 CHECK (duration_milliseconds >= 0),
    error_code TEXT NULL,
    error TEXT NULL
) STRICT;

CREATE INDEX ix_developer_dotnet_executions_context_started
ON developer_dotnet_executions (workspace_id, goal_id, started_at DESC);

ALTER TABLE editor_intelligence_preferences
ADD COLUMN show_run_code_lens INTEGER NOT NULL DEFAULT 1
CHECK (show_run_code_lens IN (0, 1));

ALTER TABLE editor_intelligence_preferences
ADD COLUMN show_debug_code_lens INTEGER NOT NULL DEFAULT 1
CHECK (show_debug_code_lens IN (0, 1));

UPDATE application_metadata SET value = '30' WHERE key = 'schema_version';
