CREATE TABLE goal_workflow_tasks (
    id TEXT PRIMARY KEY,
    run_id TEXT NOT NULL REFERENCES goal_workflow_runs(id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK (sequence BETWEEN 1 AND 12),
    title TEXT NOT NULL,
    objective TEXT NOT NULL,
    file_areas TEXT NOT NULL,
    acceptance_criteria TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Pending', 'InProgress', 'Completed')),
    report TEXT NULL,
    created_at TEXT NOT NULL,
    started_at TEXT NULL,
    completed_at TEXT NULL,
    UNIQUE (run_id, sequence),
    CHECK (
        (state = 'Pending' AND started_at IS NULL AND completed_at IS NULL AND report IS NULL) OR
        (state = 'InProgress' AND started_at IS NOT NULL AND completed_at IS NULL AND report IS NULL) OR
        (state = 'Completed' AND started_at IS NOT NULL AND completed_at IS NOT NULL AND report IS NOT NULL)
    )
) STRICT;

CREATE INDEX ix_goal_workflow_tasks_run_sequence
ON goal_workflow_tasks(run_id, sequence);

UPDATE application_metadata SET value = '17' WHERE key = 'schema_version';
