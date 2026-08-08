CREATE TABLE remote_spend_preferences (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    mode TEXT NOT NULL CHECK (mode IN ('Unlimited', 'Capped', 'LocalOnly')),
    cap_microusd INTEGER NULL CHECK (cap_microusd IS NULL OR cap_microusd > 0),
    CHECK ((mode = 'Capped' AND cap_microusd IS NOT NULL) OR
           (mode <> 'Capped' AND cap_microusd IS NULL))
) STRICT;

INSERT INTO remote_spend_preferences (id, mode, cap_microusd)
VALUES (1, 'Unlimited', NULL);

-- Before ADR 014, a null budget was also the implicit default. Upgrade only goals
-- that have not crossed the planning/approval boundary so the old default does not
-- keep blocking model selection while active executions retain their authority.
UPDATE goals
SET remote_budget_microusd = 9223372036854775807
WHERE remote_budget_microusd IS NULL
  AND state IN ('Draft', 'NeedsPlanRevision');

UPDATE application_metadata SET value = '22' WHERE key = 'schema_version';
