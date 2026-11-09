BEGIN;
SET LOCAL synchronous_commit = on;
SET LOCAL lock_timeout = '60s';
CREATE TEMP TABLE confirmation (
    source_system text NOT NULL,
    delivery_id uuid NOT NULL,
    filename text NOT NULL,
    content_sha256 text NOT NULL CHECK (content_sha256 ~ '^[0-9a-fA-F]{64}$'),
    reference text NOT NULL CHECK (length(btrim(reference)) > 0)
) ON COMMIT DROP;
INSERT INTO confirmation VALUES (:'source', :'delivery_id'::uuid, :'filename', :'content_sha256', :'delivery_reference');
-- Same lock order as the task: source first, then delivery.
SELECT source_system FROM momentum_raindance.sources WHERE source_system = :'source' FOR UPDATE;
SELECT delivery_id FROM momentum_raindance.deliveries
WHERE source_system = :'source' AND delivery_id = :'delivery_id'::uuid FOR UPDATE;
DO $check$
BEGIN
    IF (SELECT max(version) FROM momentum_raindance.schema_migrations) IS DISTINCT FROM '004_database_checkpoint' THEN
        RAISE EXCEPTION 'Run the matching offline migrations before confirming delivery.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM momentum_raindance.sources s JOIN confirmation c USING (source_system)
        WHERE s.activated_at IS NOT NULL AND NULLIF(btrim(s.baseline_reference), '') IS NOT NULL) THEN
        RAISE EXCEPTION 'Source is not registered and activated.';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM momentum_raindance.deliveries d JOIN confirmation c USING (source_system, delivery_id)
        WHERE d.filename = c.filename AND lower(d.content_sha256) = lower(c.content_sha256)
    ) THEN
        RAISE EXCEPTION 'Evidence does not match a recorded delivery: verify source, delivery ID, exact filename and file SHA256. Nothing was confirmed.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM momentum_raindance.deliveries d JOIN confirmation c USING (source_system, delivery_id)
        WHERE d.status = 'delivered' AND d.delivery_reference IS DISTINCT FROM c.reference
    ) THEN
        RAISE EXCEPTION 'Delivery is already confirmed with different evidence. Existing evidence is immutable.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM momentum_raindance.deliveries d JOIN confirmation c USING (source_system, delivery_id)
        JOIN momentum_raindance.fetch_batches f ON f.fetch_id = d.fetch_id AND f.source_system = d.source_system
        JOIN momentum_raindance.sources s ON s.source_system = d.source_system
        WHERE d.status = 'reserved'
          AND (s.last_local_id <> f.from_local_id OR d.last_local_id <> f.through_local_id)
    ) THEN
        RAISE EXCEPTION 'Delivery does not match the current database checkpoint and complete-fetch receipt. Reconcile before confirming.';
    END IF;
END
$check$;
-- Capture only a newly confirmed trusted fetch. An idempotent old confirmation,
-- or a legacy reservation without a fetch receipt, must never move progress.
CREATE TEMP TABLE confirmed_progress ON COMMIT DROP AS
SELECT d.source_system, f.from_local_id, f.through_local_id
FROM momentum_raindance.deliveries d JOIN confirmation c USING (source_system, delivery_id)
JOIN momentum_raindance.fetch_batches f ON f.fetch_id = d.fetch_id AND f.source_system = d.source_system
WHERE d.status = 'reserved';
UPDATE momentum_raindance.deliveries SET status = 'delivered', delivered_at = now(), delivery_reference = :'delivery_reference'
WHERE source_system = :'source' AND delivery_id = :'delivery_id'::uuid AND status = 'reserved';
UPDATE momentum_raindance.sources s SET last_local_id = p.through_local_id
FROM confirmed_progress p WHERE s.source_system = p.source_system AND s.last_local_id = p.from_local_id;
SELECT delivery_id, filename, status, delivered_at, delivery_reference
FROM momentum_raindance.deliveries WHERE source_system = :'source' AND delivery_id = :'delivery_id'::uuid;
SELECT last_local_id AS committed_last_local_id FROM momentum_raindance.sources WHERE source_system = :'source';
COMMIT;
