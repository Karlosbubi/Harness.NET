CREATE TABLE remote_model_cost_reservations (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    operation TEXT NOT NULL CHECK (operation IN ('Chat', 'Embedding')),
    estimated_microusd INTEGER NOT NULL CHECK (estimated_microusd >= 0),
    actual_microusd INTEGER NULL CHECK (actual_microusd IS NULL OR actual_microusd >= 0),
    state TEXT NOT NULL CHECK (state IN ('Reserved', 'Reconciled', 'Released')),
    created_at TEXT NOT NULL,
    completed_at TEXT NULL
) STRICT;

CREATE INDEX ix_remote_model_cost_reservations_goal
ON remote_model_cost_reservations(goal_id, state);

UPDATE application_metadata SET value = '12' WHERE key = 'schema_version';
