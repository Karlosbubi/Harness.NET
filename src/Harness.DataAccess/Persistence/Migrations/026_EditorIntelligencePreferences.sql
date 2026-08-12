CREATE TABLE editor_intelligence_preferences (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    show_parameter_name_hints INTEGER NOT NULL CHECK (show_parameter_name_hints IN (0, 1)),
    show_inferred_type_hints INTEGER NOT NULL CHECK (show_inferred_type_hints IN (0, 1)),
    show_reference_code_lens INTEGER NOT NULL CHECK (show_reference_code_lens IN (0, 1)),
    show_implementation_code_lens INTEGER NOT NULL CHECK (show_implementation_code_lens IN (0, 1)),
    show_test_code_lens INTEGER NOT NULL CHECK (show_test_code_lens IN (0, 1))
) STRICT;

INSERT INTO editor_intelligence_preferences (
    id, show_parameter_name_hints, show_inferred_type_hints,
    show_reference_code_lens, show_implementation_code_lens, show_test_code_lens)
VALUES (1, 1, 1, 1, 1, 1);

UPDATE application_metadata SET value = '26' WHERE key = 'schema_version';
