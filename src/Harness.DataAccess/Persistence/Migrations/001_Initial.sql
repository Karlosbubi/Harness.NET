CREATE TABLE application_metadata
(
    key   TEXT PRIMARY KEY NOT NULL,
    value TEXT             NOT NULL
) STRICT;

INSERT INTO application_metadata (key, value)
VALUES ('schema_version', '1');
