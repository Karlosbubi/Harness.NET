CREATE TABLE keybinding_configuration (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    use_defaults INTEGER NOT NULL CHECK (use_defaults IN (0, 1))
) STRICT;

INSERT INTO keybinding_configuration (id, use_defaults) VALUES (1, 1);

CREATE TABLE keybinding_preferences (
    command_name TEXT NOT NULL CHECK (length(command_name) BETWEEN 1 AND 80),
    position INTEGER NOT NULL CHECK (position BETWEEN 0 AND 7),
    gesture_text TEXT NOT NULL CHECK (length(gesture_text) BETWEEN 1 AND 80),
    PRIMARY KEY (command_name, position)
) STRICT;

UPDATE application_metadata SET value = '28' WHERE key = 'schema_version';
