ALTER TABLE developer_dotnet_executions
ADD COLUMN operation TEXT NOT NULL DEFAULT 'Run'
CHECK (operation IN ('Run', 'Build', 'Rebuild'));

ALTER TABLE developer_dotnet_executions
ADD COLUMN configuration TEXT NULL;

UPDATE application_metadata SET value = '32' WHERE key = 'schema_version';
