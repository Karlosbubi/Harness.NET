CREATE TABLE agent_role_defaults (
    role TEXT PRIMARY KEY CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    maximum_output_tokens INTEGER NOT NULL CHECK (
        maximum_output_tokens BETWEEN 1 AND 8192),
    updated_at TEXT NOT NULL
) STRICT;

UPDATE application_metadata SET value = '19' WHERE key = 'schema_version';
