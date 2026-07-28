CREATE TABLE goal_commit_approvals (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    workflow_run_id TEXT NOT NULL REFERENCES goal_workflow_runs(id) ON DELETE CASCADE,
    branch TEXT NOT NULL,
    expected_head TEXT NOT NULL,
    diff_sha256 TEXT NOT NULL,
    diff_text TEXT NOT NULL,
    changed_file_count INTEGER NOT NULL CHECK (changed_file_count > 0),
    commit_message TEXT NOT NULL,
    author_name TEXT NOT NULL,
    author_email TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Pending', 'Approved', 'Denied', 'Committed')),
    decision_reason TEXT NULL,
    commit_sha TEXT NULL,
    requested_at TEXT NOT NULL,
    decided_at TEXT NULL,
    completed_at TEXT NULL,
    UNIQUE (goal_id, workflow_run_id),
    CHECK (
        (state = 'Pending' AND decided_at IS NULL AND completed_at IS NULL AND commit_sha IS NULL) OR
        (state = 'Approved' AND decided_at IS NOT NULL AND completed_at IS NULL AND commit_sha IS NULL) OR
        (state = 'Denied' AND decided_at IS NOT NULL AND decision_reason IS NOT NULL AND completed_at IS NULL AND commit_sha IS NULL) OR
        (state = 'Committed' AND decided_at IS NOT NULL AND completed_at IS NOT NULL AND commit_sha IS NOT NULL)
    )
) STRICT;

CREATE INDEX ix_goal_commit_approvals_goal_requested
ON goal_commit_approvals(goal_id, requested_at, id);

UPDATE application_metadata SET value = '16' WHERE key = 'schema_version';
