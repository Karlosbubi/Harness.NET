ALTER TABLE developer_dotnet_executions
ADD COLUMN debug_mode TEXT NOT NULL DEFAULT 'None'
CHECK (debug_mode IN ('None', 'Project', 'Test'));

UPDATE application_metadata SET value = '39' WHERE key = 'schema_version';
