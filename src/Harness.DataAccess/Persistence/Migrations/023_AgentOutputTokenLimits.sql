CREATE TABLE agent_role_defaults_v23 (
    role TEXT PRIMARY KEY CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    maximum_output_tokens INTEGER NOT NULL CHECK (
        maximum_output_tokens BETWEEN 1 AND 10000000),
    updated_at TEXT NOT NULL
) STRICT;

INSERT INTO agent_role_defaults_v23 (
    role,
    provider,
    model,
    maximum_output_tokens,
    updated_at)
SELECT
    role,
    provider,
    model,
    maximum_output_tokens,
    updated_at
FROM agent_role_defaults;

DROP TABLE agent_role_defaults;
ALTER TABLE agent_role_defaults_v23 RENAME TO agent_role_defaults;

UPDATE application_metadata SET value = '23' WHERE key = 'schema_version';
