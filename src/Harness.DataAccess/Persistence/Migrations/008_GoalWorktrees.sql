CREATE TABLE goal_worktrees (
    goal_id TEXT PRIMARY KEY REFERENCES goals(id) ON DELETE CASCADE,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    branch TEXT NOT NULL UNIQUE,
    path TEXT NOT NULL UNIQUE,
    base_commit TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('Active')),
    created_at TEXT NOT NULL
) STRICT;

UPDATE application_metadata SET value = '8' WHERE key = 'schema_version';
