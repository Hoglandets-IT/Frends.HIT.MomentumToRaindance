BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT source_system, activated_at, baseline_reference, created_at
FROM momentum_raindance.sources WHERE source_system = :'source';
SELECT CASE WHEN i.delivery_id IS NULL THEN 'historical' ELSE d.status END AS state, count(*) AS invoice_count
FROM momentum_raindance.invoices i
LEFT JOIN momentum_raindance.deliveries d ON d.delivery_id = i.delivery_id
WHERE i.source_system = :'source'
GROUP BY 1 ORDER BY 1;
SELECT delivery_id, filename, content_sha256, byte_count, invoice_count, last_local_id,
       status, created_at, delivered_at, delivery_reference
FROM momentum_raindance.deliveries WHERE source_system = :'source'
ORDER BY created_at DESC, delivery_id;
COMMIT;
