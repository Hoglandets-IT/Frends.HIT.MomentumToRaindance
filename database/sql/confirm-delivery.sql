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
END
$check$;
UPDATE momentum_raindance.deliveries SET status = 'delivered', delivered_at = now(), delivery_reference = :'delivery_reference'
WHERE source_system = :'source' AND delivery_id = :'delivery_id'::uuid AND status = 'reserved';
SELECT delivery_id, filename, status, delivered_at, delivery_reference
FROM momentum_raindance.deliveries WHERE source_system = :'source' AND delivery_id = :'delivery_id'::uuid;
COMMIT;
