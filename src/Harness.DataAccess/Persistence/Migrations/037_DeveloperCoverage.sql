CREATE TABLE developer_coverage_imports (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    goal_id TEXT NULL,
    source_description TEXT NOT NULL,
    report_path TEXT NOT NULL CHECK (length(report_path) BETWEEN 1 AND 1024),
    report_hash TEXT NOT NULL CHECK (
        length(report_hash) = 64 AND report_hash NOT GLOB '*[^0-9a-f]*'),
    format TEXT NOT NULL CHECK (format = 'Cobertura'),
    producer TEXT NOT NULL CHECK (length(producer) BETWEEN 1 AND 128),
    producer_version TEXT NOT NULL CHECK (length(producer_version) BETWEEN 1 AND 128),
    generated_at TEXT NULL,
    imported_at TEXT NOT NULL,
    unmapped_file_count INTEGER NOT NULL CHECK (unmapped_file_count >= 0),
    is_truncated INTEGER NOT NULL CHECK (is_truncated IN (0, 1))
) STRICT;

CREATE INDEX ix_developer_coverage_imports_context_imported
ON developer_coverage_imports (workspace_id, goal_id, imported_at DESC);

CREATE TABLE developer_coverage_lines (
    import_id TEXT NOT NULL,
    source_path TEXT NOT NULL CHECK (length(source_path) BETWEEN 1 AND 1024),
    line_number INTEGER NOT NULL CHECK (line_number > 0),
    hit_count INTEGER NOT NULL CHECK (hit_count >= 0),
    PRIMARY KEY (import_id, source_path, line_number),
    FOREIGN KEY (import_id) REFERENCES developer_coverage_imports(id) ON DELETE CASCADE
) STRICT;

CREATE INDEX ix_developer_coverage_lines_import_path
ON developer_coverage_lines (import_id, source_path, line_number);

UPDATE application_metadata SET value = '37' WHERE key = 'schema_version';
