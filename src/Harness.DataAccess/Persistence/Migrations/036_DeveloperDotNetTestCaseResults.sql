ALTER TABLE developer_dotnet_executions
ADD COLUMN test_cases_truncated INTEGER NOT NULL DEFAULT 0
CHECK (test_cases_truncated IN (0, 1));

CREATE TABLE developer_dotnet_test_case_results (
    execution_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL CHECK (ordinal >= 0 AND ordinal < 2000),
    fully_qualified_name TEXT NOT NULL CHECK (
        length(fully_qualified_name) BETWEEN 1 AND 512),
    outcome TEXT NOT NULL CHECK (outcome IN ('Passed', 'Failed', 'Skipped', 'Other')),
    duration_milliseconds INTEGER NOT NULL CHECK (duration_milliseconds >= 0),
    PRIMARY KEY (execution_id, ordinal),
    FOREIGN KEY (execution_id) REFERENCES developer_dotnet_executions(id) ON DELETE CASCADE
) STRICT;

CREATE INDEX ix_developer_dotnet_test_case_results_execution
ON developer_dotnet_test_case_results (execution_id, ordinal);

UPDATE application_metadata SET value = '36' WHERE key = 'schema_version';
