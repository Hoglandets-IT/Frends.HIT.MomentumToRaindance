BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT source_system, activated_at, baseline_reference, created_at, last_local_id
FROM momentum_raindance.sources WHERE source_system = :'source';
SELECT CASE WHEN i.delivery_id IS NULL THEN 'historical' ELSE d.status END AS state, count(*) AS invoice_count
FROM momentum_raindance.invoices i
LEFT JOIN momentum_raindance.deliveries d ON d.delivery_id = i.delivery_id
WHERE i.source_system = :'source'
GROUP BY 1 ORDER BY 1;
SELECT delivery_id, filename, content_sha256, byte_count, invoice_count, last_local_id,
       status, created_at, delivered_at, delivery_reference, fetch_id
FROM momentum_raindance.deliveries WHERE source_system = :'source'
ORDER BY created_at DESC, delivery_id;
SELECT fetch_id, from_local_id, through_local_id, node_count, payload_sha256, created_at
FROM momentum_raindance.fetch_batches WHERE source_system = :'source'
ORDER BY created_at DESC, fetch_id;
-- Immutable source-date snapshots for diagnosis; no customer names, addresses or raw source JSON.
-- For a Swedish creation-date filter, use (source_created_at AT TIME ZONE 'Europe/Stockholm')::date.
SELECT ledger_note_id, first_local_id, source_created_at, due_date, invoice_billed_date,
       accounting_event_created_at, accounting_approved_at, recorded_at, delivery_id
FROM momentum_raindance.invoices WHERE source_system = :'source'
ORDER BY first_local_id, ledger_note_id;
COMMIT;
