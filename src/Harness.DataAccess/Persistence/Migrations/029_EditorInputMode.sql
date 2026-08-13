ALTER TABLE keybinding_configuration
ADD COLUMN input_mode TEXT NOT NULL DEFAULT 'Standard'
CHECK (input_mode IN ('Standard', 'Vim'));

UPDATE application_metadata SET value = '29' WHERE key = 'schema_version';
