ALTER TABLE developer_dotnet_executions RENAME TO developer_dotnet_executions_v34;
DROP INDEX ix_developer_dotnet_executions_context_started;

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
    error TEXT NULL,
    operation TEXT NOT NULL CHECK (operation IN ('Run', 'Build', 'Rebuild', 'Test')),
    configuration TEXT NULL,
    test_id TEXT NULL,
    test_name TEXT NULL,
    test_scope TEXT NULL CHECK (test_scope IN ('Exact', 'Type', 'Project', 'Selection')),
    test_selection_json TEXT NULL CHECK (
        test_selection_json IS NULL OR
        (json_valid(test_selection_json) AND json_type(test_selection_json) = 'array'
            AND json_array_length(test_selection_json) BETWEEN 2 AND 24)),
    CHECK (
        (operation = 'Run' AND declaration_id <> '') OR
        (operation <> 'Run' AND declaration_id = '')),
    CHECK (
        (operation = 'Test' AND test_id IS NOT NULL AND test_name IS NOT NULL
            AND test_scope IS NOT NULL) OR
        (operation <> 'Test' AND test_id IS NULL AND test_name IS NULL
            AND test_scope IS NULL)),
    CHECK (
        (test_scope = 'Selection' AND test_selection_json IS NOT NULL) OR
        (test_scope <> 'Selection' AND test_selection_json IS NULL) OR
        (test_scope IS NULL AND test_selection_json IS NULL))
) STRICT;

INSERT INTO developer_dotnet_executions (
    id, workspace_id, goal_id, source_description, project_path, target_framework,
    declaration_id, state, started_at, completed_at, exit_code, duration_milliseconds,
    error_code, error, operation, configuration, test_id, test_name, test_scope,
    test_selection_json)
SELECT id, workspace_id, goal_id, source_description, project_path, target_framework,
       declaration_id, state, started_at, completed_at, exit_code, duration_milliseconds,
       error_code, error, operation, configuration, test_id, test_name, test_scope, NULL
FROM developer_dotnet_executions_v34;

DROP TABLE developer_dotnet_executions_v34;

CREATE INDEX ix_developer_dotnet_executions_context_started
ON developer_dotnet_executions (workspace_id, goal_id, started_at DESC);

UPDATE application_metadata SET value = '35' WHERE key = 'schema_version';
