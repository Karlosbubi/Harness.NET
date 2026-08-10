CREATE TABLE visual_capture_preferences (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    is_enabled INTEGER NOT NULL CHECK (is_enabled IN (0, 1)),
    maximum_bytes INTEGER NOT NULL CHECK (maximum_bytes BETWEEN 1048576 AND 16777216),
    retention_days INTEGER NOT NULL CHECK (retention_days BETWEEN 1 AND 90),
    maximum_captures_per_goal INTEGER NOT NULL CHECK (maximum_captures_per_goal BETWEEN 1 AND 100),
    allow_remote_model_access INTEGER NOT NULL CHECK (allow_remote_model_access IN (0, 1))
) STRICT;

INSERT INTO visual_capture_preferences (
    id, is_enabled, maximum_bytes, retention_days,
    maximum_captures_per_goal, allow_remote_model_access)
VALUES (1, 1, 5242880, 7, 20, 0);

UPDATE application_metadata SET value = '25' WHERE key = 'schema_version';
