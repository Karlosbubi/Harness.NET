ALTER TABLE editor_intelligence_preferences
ADD COLUMN format_on_paste INTEGER NOT NULL DEFAULT 1
CHECK (format_on_paste IN (0, 1));

ALTER TABLE editor_intelligence_preferences
ADD COLUMN format_on_type INTEGER NOT NULL DEFAULT 1
CHECK (format_on_type IN (0, 1));

UPDATE application_metadata SET value = '27' WHERE key = 'schema_version';
