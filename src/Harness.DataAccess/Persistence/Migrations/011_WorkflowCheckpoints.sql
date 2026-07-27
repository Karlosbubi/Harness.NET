CREATE TABLE workflow_runs (
    id TEXT PRIMARY KEY,
    state TEXT NOT NULL CHECK (state IN ('Running', 'Paused', 'Completed')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

CREATE TABLE workflow_checkpoints (
    id TEXT PRIMARY KEY,
    run_id TEXT NOT NULL REFERENCES workflow_runs(id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK (sequence > 0),
    kind TEXT NOT NULL CHECK (kind IN ('Started', 'PlanProposed', 'ImplementationProduced', 'ReviewCompleted')),
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

CREATE INDEX ix_workflow_runs_updated
ON workflow_runs(updated_at DESC, id);

CREATE INDEX ix_workflow_checkpoints_run_sequence
ON workflow_checkpoints(run_id, sequence);

UPDATE application_metadata SET value = '11' WHERE key = 'schema_version';
