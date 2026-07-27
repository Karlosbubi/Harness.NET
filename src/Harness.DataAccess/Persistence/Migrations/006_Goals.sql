CREATE TABLE goals (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    objective TEXT NOT NULL,
    review_cycle_limit INTEGER NOT NULL CHECK (review_cycle_limit BETWEEN 1 AND 20),
    remote_budget_microusd INTEGER NULL CHECK (remote_budget_microusd IS NULL OR remote_budget_microusd > 0),
    state TEXT NOT NULL CHECK (state IN ('Draft', 'AwaitingPlanApproval', 'Approved', 'NeedsPlanRevision')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

CREATE INDEX ix_goals_workspace_updated
ON goals(workspace_id, updated_at DESC);

UPDATE application_metadata SET value = '6' WHERE key = 'schema_version';
