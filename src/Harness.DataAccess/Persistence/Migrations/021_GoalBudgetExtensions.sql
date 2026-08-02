CREATE TABLE goal_budget_extensions (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    previous_budget_microusd INTEGER NULL
        CHECK (previous_budget_microusd IS NULL OR previous_budget_microusd > 0),
    new_budget_microusd INTEGER NOT NULL CHECK (new_budget_microusd > 0),
    reason TEXT NOT NULL CHECK (length(reason) BETWEEN 1 AND 2000),
    approved_at TEXT NOT NULL
) STRICT;

CREATE INDEX ix_goal_budget_extensions_goal_approved
ON goal_budget_extensions(goal_id, approved_at);

UPDATE application_metadata SET value = '21' WHERE key = 'schema_version';
