CREATE TABLE semantic_index_partitions (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    dimensions INTEGER NOT NULL CHECK (dimensions > 0),
    chunking_version TEXT NOT NULL,
    collection_name TEXT NOT NULL UNIQUE,
    state TEXT NOT NULL CHECK (state IN ('Building', 'Ready', 'Superseded', 'Failed')),
    file_count INTEGER NOT NULL DEFAULT 0 CHECK (file_count >= 0),
    chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (chunk_count >= 0),
    created_at TEXT NOT NULL,
    completed_at TEXT NULL
) STRICT;

CREATE UNIQUE INDEX ux_semantic_index_ready_partition
ON semantic_index_partitions (
    workspace_id, provider, model, dimensions, chunking_version)
WHERE state = 'Ready';

CREATE INDEX ix_semantic_index_partition_history
ON semantic_index_partitions (
    workspace_id, provider, model, dimensions, chunking_version, created_at DESC);

UPDATE application_metadata SET value = '13' WHERE key = 'schema_version';
