CREATE TABLE tool_calls (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    correlation_id TEXT NOT NULL,
    tool_name TEXT NOT NULL CHECK (tool_name IN ('FileEdit', 'Build', 'Test')),
    request_json TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Running', 'Succeeded', 'Failed', 'Cancelled', 'Uncertain')),
    result_json TEXT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT NULL,
    UNIQUE (goal_id, correlation_id)
) STRICT;

CREATE INDEX ix_tool_calls_goal_started
ON tool_calls(goal_id, started_at, id);

UPDATE application_metadata SET value = '9' WHERE key = 'schema_version';
