CREATE TABLE goal_plans (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    revision INTEGER NOT NULL CHECK (revision > 0),
    content TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Pending', 'Approved', 'Denied')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (goal_id, revision)
) STRICT;

CREATE INDEX ix_goal_plans_goal_revision
ON goal_plans(goal_id, revision DESC);

CREATE TABLE approvals (
    id TEXT PRIMARY KEY,
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    plan_id TEXT NOT NULL REFERENCES goal_plans(id) ON DELETE CASCADE,
    kind TEXT NOT NULL CHECK (kind IN ('Plan')),
    decision TEXT NOT NULL CHECK (decision IN ('Approved', 'Denied')),
    reason TEXT NULL,
    decided_at TEXT NOT NULL
) STRICT;

CREATE UNIQUE INDEX ix_approvals_plan
ON approvals(plan_id);

UPDATE application_metadata SET value = '7' WHERE key = 'schema_version';
