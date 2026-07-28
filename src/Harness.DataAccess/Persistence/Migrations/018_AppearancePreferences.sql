CREATE TABLE appearance_preferences (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    selected_theme_id TEXT NOT NULL
) STRICT;

INSERT INTO appearance_preferences (id, selected_theme_id)
VALUES (1, 'system');

UPDATE application_metadata SET value = '18' WHERE key = 'schema_version';
