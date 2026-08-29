CREATE TABLE developer_terminal_sessions (
    id TEXT PRIMARY KEY CHECK (length(id) BETWEEN 1 AND 80),
    workspace_id TEXT NOT NULL CHECK (length(workspace_id) BETWEEN 1 AND 128),
    goal_id TEXT NULL CHECK (goal_id IS NULL OR length(goal_id) BETWEEN 1 AND 128),
    source_scope TEXT NOT NULL CHECK (
        source_scope IN ('OriginalWorkspace', 'ApprovedGoalWorktree')),
    source_branch TEXT NULL CHECK (source_branch IS NULL OR length(source_branch) BETWEEN 1 AND 256),
    source_description TEXT NOT NULL CHECK (length(source_description) BETWEEN 1 AND 512),
    working_directory TEXT NOT NULL CHECK (working_directory = '.'),
    shell_name TEXT NOT NULL CHECK (
        length(shell_name) BETWEEN 1 AND 128 AND
        shell_name NOT LIKE '%/%' AND shell_name NOT LIKE '%\%'),
    environment_profile TEXT NOT NULL CHECK (environment_profile = 'InheritedLocked'),
    content_policy TEXT NOT NULL CHECK (content_policy = 'Transient'),
    columns INTEGER NOT NULL CHECK (columns BETWEEN 20 AND 400),
    rows INTEGER NOT NULL CHECK (rows BETWEEN 5 AND 200),
    state TEXT NOT NULL CHECK (state IN ('Running', 'Exited', 'Stopped', 'Failed', 'Interrupted')),
    started_at TEXT NOT NULL,
    completed_at TEXT NULL,
    exit_code INTEGER NULL,
    error_code TEXT NULL CHECK (error_code IS NULL OR length(error_code) <= 128),
    error TEXT NULL CHECK (error IS NULL OR length(error) <= 512)
) STRICT;

CREATE INDEX ix_developer_terminal_sessions_context_started
ON developer_terminal_sessions (workspace_id, goal_id, started_at DESC);

UPDATE application_metadata SET value = '40' WHERE key = 'schema_version';
