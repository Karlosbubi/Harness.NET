CREATE TABLE goal_workflow_runs (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    state TEXT NOT NULL CHECK (state IN (
        'Running', 'AwaitingPlanApproval', 'AwaitingAcceptance',
        'NeedsDirection', 'Completed')),
    review_cycle INTEGER NOT NULL CHECK (review_cycle >= 0),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

CREATE TABLE goal_workflow_checkpoints (
    id TEXT PRIMARY KEY,
    run_id TEXT NOT NULL REFERENCES goal_workflow_runs(id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK (sequence > 0),
    kind TEXT NOT NULL CHECK (kind IN (
        'Started', 'LeadCallStarted', 'PlanProposed', 'PlanApproved',
        'ImplementerCallStarted', 'ImplementationProduced',
        'ReviewerCallStarted', 'ReviewCompleted',
        'UserDirectionRequired', 'Accepted')),
    actor TEXT NOT NULL CHECK (actor IN ('System', 'Lead', 'Implementer', 'Reviewer')),
    summary TEXT NOT NULL,
    evidence_title TEXT NULL,
    evidence_content TEXT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (run_id, sequence),
    CHECK (
        (evidence_title IS NULL AND evidence_content IS NULL) OR
        (evidence_title IS NOT NULL AND evidence_content IS NOT NULL)
    )
) STRICT;

CREATE INDEX ix_goal_workflow_runs_goal_updated
ON goal_workflow_runs(goal_id, updated_at DESC, id);

CREATE UNIQUE INDEX ux_goal_workflow_runs_active
ON goal_workflow_runs(goal_id)
WHERE state <> 'Completed';

CREATE INDEX ix_goal_workflow_checkpoints_run_sequence
ON goal_workflow_checkpoints(run_id, sequence);

UPDATE application_metadata SET value = '15' WHERE key = 'schema_version';
