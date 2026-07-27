CREATE TABLE workspace_framework_overlays (
    workspace_id TEXT PRIMARY KEY REFERENCES workspaces(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

UPDATE application_metadata SET value = '5' WHERE key = 'schema_version';
