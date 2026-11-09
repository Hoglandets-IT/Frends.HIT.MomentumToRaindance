-- Append-only metadata snapshot at the first invoice reservation. Never edit an applied migration.
-- Existing and historical rows deliberately remain NULL; no source values are invented or backfilled.
ALTER TABLE momentum_raindance.invoices
    ADD COLUMN source_created_at timestamptz,
    ADD COLUMN due_date date,
    ADD COLUMN invoice_billed_date date,
    ADD COLUMN accounting_event_created_at timestamptz,
    ADD COLUMN accounting_approved_at timestamptz;

COMMENT ON COLUMN momentum_raindance.invoices.source_created_at IS
    'Momentum ledgerNote.statusCreated at first reservation: original note creation instant, not database recorded_at. Stored as an instant; original textual offset is not retained.';
COMMENT ON COLUMN momentum_raindance.invoices.due_date IS
    'Momentum ledgerNote.due at first reservation: effective calendar due date without timezone conversion. Not refreshed after reservation.';
COMMENT ON COLUMN momentum_raindance.invoices.invoice_billed_date IS
    'Momentum ledgerNote.invoice.billed at first reservation: associated invoice billing calendar date, when present; not the ledger note creation timestamp.';
COMMENT ON COLUMN momentum_raindance.invoices.accounting_event_created_at IS
    'Momentum transactionCreated.change.statusCreated at first reservation: creation instant of the originating accounting change, distinct from ledger note creation.';
COMMENT ON COLUMN momentum_raindance.invoices.accounting_approved_at IS
    'Momentum transactionCreated.statusApproved at first reservation: approval instant of the originating accounting transaction, when present.';

-- Existing append-only invoice triggers cover these columns too. Runtime never updates a snapshot.
-- Restricted-runtime installations must rerun the updated optional grant-runtime helper after migration.
