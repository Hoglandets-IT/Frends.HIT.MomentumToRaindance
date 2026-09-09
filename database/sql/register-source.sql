BEGIN;
SET LOCAL synchronous_commit = on;
INSERT INTO momentum_raindance.sources (source_system)
VALUES (:'source') ON CONFLICT (source_system) DO NOTHING;
SELECT source_system, activated_at, baseline_reference
FROM momentum_raindance.sources WHERE source_system = :'source';
COMMIT;
