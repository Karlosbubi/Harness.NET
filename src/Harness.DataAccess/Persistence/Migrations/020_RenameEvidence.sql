ALTER TABLE tool_calls RENAME TO tool_calls_v19;

CREATE TABLE tool_calls (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    correlation_id TEXT NOT NULL,
    tool_name TEXT NOT NULL CHECK (tool_name IN ('FileEdit', 'Rename', 'Build', 'Test', 'Restore')),
    request_json TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Running', 'Succeeded', 'Failed', 'Cancelled', 'Uncertain')),
    result_json TEXT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT NULL,
    UNIQUE (goal_id, correlation_id)
) STRICT;

INSERT INTO tool_calls (
    id, goal_id, correlation_id, tool_name, request_json, state,
    result_json, started_at, completed_at)
SELECT id, goal_id, correlation_id, tool_name, request_json, state,
       result_json, started_at, completed_at
FROM tool_calls_v19;

DROP TABLE tool_calls_v19;

CREATE INDEX ix_tool_calls_goal_started
ON tool_calls(goal_id, started_at, id);

UPDATE application_metadata SET value = '20' WHERE key = 'schema_version';
