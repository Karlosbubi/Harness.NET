ALTER TABLE developer_dotnet_executions
ADD COLUMN run_mode TEXT NOT NULL DEFAULT 'Standard'
CHECK (run_mode IN ('Standard', 'HotReload'));

UPDATE application_metadata SET value = '38' WHERE key = 'schema_version';
