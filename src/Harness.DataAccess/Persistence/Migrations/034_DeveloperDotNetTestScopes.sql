ALTER TABLE developer_dotnet_executions RENAME TO developer_dotnet_executions_v33;
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
    test_scope TEXT NULL CHECK (test_scope IN ('Exact', 'Type', 'Project')),
    CHECK (
        (operation = 'Run' AND declaration_id <> '') OR
        (operation <> 'Run' AND declaration_id = '')),
    CHECK (
        (operation = 'Test' AND test_id IS NOT NULL AND test_name IS NOT NULL
            AND test_scope IS NOT NULL) OR
        (operation <> 'Test' AND test_id IS NULL AND test_name IS NULL
            AND test_scope IS NULL))
) STRICT;

INSERT INTO developer_dotnet_executions (
    id, workspace_id, goal_id, source_description, project_path, target_framework,
    declaration_id, state, started_at, completed_at, exit_code, duration_milliseconds,
    error_code, error, operation, configuration, test_id, test_name, test_scope)
SELECT id, workspace_id, goal_id, source_description, project_path, target_framework,
       declaration_id, state, started_at, completed_at, exit_code, duration_milliseconds,
       error_code, error, operation, configuration, test_id, test_name,
       CASE WHEN operation = 'Test' THEN 'Exact' ELSE NULL END
FROM developer_dotnet_executions_v33;

DROP TABLE developer_dotnet_executions_v33;

CREATE INDEX ix_developer_dotnet_executions_context_started
ON developer_dotnet_executions (workspace_id, goal_id, started_at DESC);

UPDATE application_metadata SET value = '34' WHERE key = 'schema_version';
