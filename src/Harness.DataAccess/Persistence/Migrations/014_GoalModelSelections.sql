CREATE TABLE goal_model_selections (
    goal_id TEXT NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
    role TEXT NOT NULL CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    selected_at TEXT NOT NULL,
    PRIMARY KEY (goal_id, role)
) STRICT;

UPDATE application_metadata SET value = '14' WHERE key = 'schema_version';
