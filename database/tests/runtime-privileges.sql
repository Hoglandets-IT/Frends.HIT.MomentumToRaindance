-- Development verification only. Run after grant-runtime momentum_tracking_runtime.
-- All fixture rows are rolled back; this never deletes existing history.
BEGIN;
SET LOCAL synchronous_commit = on;
SELECT set_config('momentum.test_source', 'database-tooling-test-' || gen_random_uuid(), true);
SELECT set_config('momentum.test_delivery', gen_random_uuid()::text, true);
INSERT INTO momentum_raindance.sources (source_system, activated_at, baseline_reference)
VALUES (current_setting('momentum.test_source'), now(), 'Synthetic privilege test');

SET LOCAL ROLE momentum_tracking_runtime;
SELECT source_system FROM momentum_raindance.sources
WHERE source_system = current_setting('momentum.test_source') FOR UPDATE;
INSERT INTO momentum_raindance.deliveries
    (delivery_id, source_system, filename, content_sha256, byte_count, invoice_count, last_local_id, status)
VALUES (current_setting('momentum.test_delivery')::uuid, current_setting('momentum.test_source'),
    current_setting('momentum.test_delivery') || '.txt', repeat('a', 64), 100, 1, 10, 'reserved');
INSERT INTO momentum_raindance.invoices
    (source_system, ledger_note_id, first_local_id, content_sha256, delivery_id)
VALUES (current_setting('momentum.test_source'), 'stable-invoice-1', 10, repeat('b', 64), current_setting('momentum.test_delivery')::uuid);
SELECT delivery_id FROM momentum_raindance.deliveries
WHERE delivery_id = current_setting('momentum.test_delivery')::uuid FOR UPDATE;

DO $tests$
BEGIN
    IF momentum_raindance.is_canonical_identity(' invoice')
       OR momentum_raindance.is_canonical_identity(U&'invoice\00A0')
       OR momentum_raindance.is_canonical_identity(U&'in\0085voice') THEN
        RAISE EXCEPTION 'Canonical identity validation accepted whitespace/control ambiguity.';
    END IF;
    BEGIN
        INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, content_sha256, delivery_id)
        VALUES (current_setting('momentum.test_source'), 'stable-invoice-1', 99, repeat('b', 64), current_setting('momentum.test_delivery')::uuid);
        RAISE EXCEPTION 'Duplicate identity accepted.';
    EXCEPTION WHEN unique_violation THEN NULL; END;
    BEGIN
        INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, content_sha256, delivery_id)
        VALUES (current_setting('momentum.test_source'), ' padded-id ', 0, repeat('b', 64), current_setting('momentum.test_delivery')::uuid);
        RAISE EXCEPTION 'Noncanonical identity accepted.';
    EXCEPTION WHEN check_violation THEN NULL; END;
    BEGIN
        INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, historical_reference)
        VALUES (current_setting('momentum.test_source'), 'forged-history', 0, 'runtime cannot forge history');
        RAISE EXCEPTION 'Runtime can insert historical baseline rows.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id)
        VALUES (current_setting('momentum.test_source'), 'missing-evidence', 0);
        RAISE EXCEPTION 'History without evidence accepted.';
    EXCEPTION WHEN check_violation THEN NULL; END;
    BEGIN
        INSERT INTO momentum_raindance.sources (source_system) VALUES ('forbidden-runtime-source');
        RAISE EXCEPTION 'Runtime can register sources.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        UPDATE momentum_raindance.sources SET activated_at = now() WHERE source_system = current_setting('momentum.test_source');
        RAISE EXCEPTION 'Runtime can activate sources.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        DELETE FROM momentum_raindance.invoices WHERE source_system = current_setting('momentum.test_source');
        RAISE EXCEPTION 'Runtime can delete invoice history.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        TRUNCATE momentum_raindance.invoices;
        RAISE EXCEPTION 'Runtime can truncate invoice history.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        CREATE TABLE momentum_raindance.forbidden_runtime_ddl (id integer);
        RAISE EXCEPTION 'Runtime can create schema objects.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
        UPDATE momentum_raindance.deliveries SET filename = 'modified.txt'
        WHERE delivery_id = current_setting('momentum.test_delivery')::uuid;
        RAISE EXCEPTION 'Runtime can overwrite delivery identity.';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END
$tests$;

UPDATE momentum_raindance.deliveries
SET status = 'delivered', delivered_at = now(), delivery_reference = 'Synthetic writer receipt'
WHERE delivery_id = current_setting('momentum.test_delivery')::uuid;

-- Trigger failures share SQLSTATE P0001; a flag distinguishes expected rejection
-- from the test's own failed assertion.
DO $tests$
DECLARE rejected boolean;
BEGIN
    rejected := false;
    BEGIN
        UPDATE momentum_raindance.sources SET created_at = now() - interval '1 day'
        WHERE source_system = current_setting('momentum.test_source');
    EXCEPTION WHEN raise_exception THEN rejected := true; END;
    IF NOT rejected THEN RAISE EXCEPTION 'Source locking grant permits mutating created_at.'; END IF;
    rejected := false;
    BEGIN
        UPDATE momentum_raindance.deliveries SET status = 'reserved', delivered_at = NULL, delivery_reference = NULL
        WHERE delivery_id = current_setting('momentum.test_delivery')::uuid;
    EXCEPTION WHEN raise_exception THEN rejected := true; END;
    IF NOT rejected THEN RAISE EXCEPTION 'Delivered reservation can be reverted.'; END IF;
END
$tests$;

RESET ROLE;
DO $tests$
DECLARE rejected boolean;
BEGIN
    rejected := false;
    BEGIN
        DELETE FROM momentum_raindance.invoices WHERE source_system = current_setting('momentum.test_source');
    EXCEPTION WHEN raise_exception THEN rejected := true; END;
    IF NOT rejected THEN RAISE EXCEPTION 'Owner accidental history deletion was not blocked.'; END IF;
    rejected := false;
    BEGIN
        UPDATE momentum_raindance.invoices SET ledger_note_id = 'rewritten'
        WHERE source_system = current_setting('momentum.test_source');
    EXCEPTION WHEN raise_exception THEN rejected := true; END;
    IF NOT rejected THEN RAISE EXCEPTION 'Owner accidental identity rewrite was not blocked.'; END IF;
END
$tests$;
ROLLBACK;
\echo Runtime privileges and append-only invariants passed; synthetic rows rolled back.
