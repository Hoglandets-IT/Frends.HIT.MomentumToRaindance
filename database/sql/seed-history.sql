BEGIN;
SET LOCAL synchronous_commit = on;
SET LOCAL lock_timeout = '60s';
CREATE TEMP TABLE bootstrap_source (source_system text PRIMARY KEY) ON COMMIT DROP;
INSERT INTO bootstrap_source VALUES (:'source');
SELECT source_system FROM momentum_raindance.sources WHERE source_system = :'source' FOR UPDATE;
DO $check$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM momentum_raindance.sources JOIN bootstrap_source USING (source_system)) THEN
        RAISE EXCEPTION 'Register the source before importing its historical baseline.';
    END IF;
    IF EXISTS (SELECT 1 FROM momentum_raindance.sources JOIN bootstrap_source USING (source_system) WHERE activated_at IS NOT NULL) THEN
        RAISE EXCEPTION 'Historical bootstrap is only allowed before source activation. Do not retroactively change the baseline of an active source.';
    END IF;
END
$check$;
CREATE TEMP TABLE historical_import (
    ledger_note_id text PRIMARY KEY CHECK (length(btrim(ledger_note_id)) BETWEEN 1 AND 300 AND momentum_raindance.is_canonical_identity(ledger_note_id)),
    invoice_number text,
    ledger_note_number text,
    reference text NOT NULL CHECK (length(btrim(reference)) > 0)
) ON COMMIT DROP;
\copy historical_import (ledger_note_id, invoice_number, ledger_note_number, reference) FROM pstdin WITH (FORMAT csv, HEADER MATCH, ENCODING 'UTF8')
WITH imported AS (
    INSERT INTO momentum_raindance.invoices (
        source_system, ledger_note_id, first_local_id, invoice_number, ledger_note_number, historical_reference
    )
    SELECT source_system, ledger_note_id, 0, invoice_number, ledger_note_number, reference
    FROM historical_import CROSS JOIN bootstrap_source
    ON CONFLICT (source_system, ledger_note_id) DO NOTHING
    RETURNING 1
)
SELECT (SELECT count(*) FROM historical_import) AS input_rows, count(*) AS new_identities FROM imported;
COMMIT;
