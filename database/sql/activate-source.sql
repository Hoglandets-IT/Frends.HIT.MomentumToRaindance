BEGIN;
SET LOCAL synchronous_commit = on;
SET LOCAL lock_timeout = '60s';
CREATE TEMP TABLE activation (source_system text PRIMARY KEY, reference text NOT NULL CHECK (length(btrim(reference)) > 0)) ON COMMIT DROP;
INSERT INTO activation VALUES (:'source', :'baseline_reference');
SELECT source_system FROM momentum_raindance.sources WHERE source_system = :'source' FOR UPDATE;
DO $check$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM momentum_raindance.sources JOIN activation USING (source_system)) THEN
        RAISE EXCEPTION 'Register and reconcile the historical baseline before activation.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM momentum_raindance.sources JOIN activation USING (source_system)
        WHERE activated_at IS NOT NULL AND baseline_reference IS DISTINCT FROM reference
    ) THEN
        RAISE EXCEPTION 'Source is already activated with different baseline evidence; the recorded baseline is immutable.';
    END IF;
END
$check$;
UPDATE momentum_raindance.sources SET activated_at = now(), baseline_reference = :'baseline_reference'
WHERE source_system = :'source' AND activated_at IS NULL;
SELECT source_system, activated_at, baseline_reference FROM momentum_raindance.sources WHERE source_system = :'source';
COMMIT;
