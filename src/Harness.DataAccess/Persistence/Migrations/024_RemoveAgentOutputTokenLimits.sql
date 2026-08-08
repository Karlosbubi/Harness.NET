CREATE TABLE agent_role_defaults_v24 (
    role TEXT PRIMARY KEY CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

INSERT INTO agent_role_defaults_v24 (role, provider, model, updated_at)
SELECT role, provider, model, updated_at
FROM agent_role_defaults;

DROP TABLE agent_role_defaults;
ALTER TABLE agent_role_defaults_v24 RENAME TO agent_role_defaults;

UPDATE application_metadata SET value = '24' WHERE key = 'schema_version';
