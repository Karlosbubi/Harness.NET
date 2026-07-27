ALTER TABLE tool_calls RENAME TO tool_calls_v9;

CREATE TABLE tool_calls (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    correlation_id TEXT NOT NULL,
    tool_name TEXT NOT NULL CHECK (tool_name IN ('FileEdit', 'Build', 'Test', 'Restore')),
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
FROM tool_calls_v9;

DROP TABLE tool_calls_v9;

CREATE INDEX ix_tool_calls_goal_started
ON tool_calls(goal_id, started_at, id);

CREATE TABLE capability_approvals (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    correlation_id TEXT NOT NULL,
    capability TEXT NOT NULL CHECK (capability IN ('Restore')),
    target TEXT NOT NULL,
    rationale TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Pending', 'Approved', 'Denied')),
    decision_reason TEXT NULL,
    requested_at TEXT NOT NULL,
    decided_at TEXT NULL,
    UNIQUE (goal_id, correlation_id, capability),
    CHECK (
        (state = 'Pending' AND decided_at IS NULL AND decision_reason IS NULL) OR
        (state = 'Approved' AND decided_at IS NOT NULL) OR
        (state = 'Denied' AND decided_at IS NOT NULL AND decision_reason IS NOT NULL)
    )
) STRICT;

CREATE INDEX ix_capability_approvals_goal_requested
ON capability_approvals(goal_id, requested_at, id);

UPDATE application_metadata SET value = '10' WHERE key = 'schema_version';
