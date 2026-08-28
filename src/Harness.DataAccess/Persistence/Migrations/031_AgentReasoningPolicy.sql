CREATE TABLE agent_role_defaults_v31 (
    role TEXT PRIMARY KEY CHECK (role IN ('Lead', 'Implementer', 'Reviewer')),
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    reasoning_policy TEXT NOT NULL CHECK (
        reasoning_policy IN ('ProviderDefault', 'Disabled')),
    updated_at TEXT NOT NULL
) STRICT;

INSERT INTO agent_role_defaults_v31 (
    role, provider, model, reasoning_policy, updated_at)
SELECT role, provider, model, 'ProviderDefault', updated_at
FROM agent_role_defaults;

DROP TABLE agent_role_defaults;
ALTER TABLE agent_role_defaults_v31 RENAME TO agent_role_defaults;

UPDATE application_metadata SET value = '31' WHERE key = 'schema_version';
