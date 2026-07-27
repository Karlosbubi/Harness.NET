ALTER TABLE workspaces
ADD COLUMN is_active INTEGER NOT NULL DEFAULT 0 CHECK (is_active IN (0, 1));

CREATE UNIQUE INDEX ix_workspaces_single_active
ON workspaces(is_active)
WHERE is_active = 1;

UPDATE application_metadata SET value = '4' WHERE key = 'schema_version';
