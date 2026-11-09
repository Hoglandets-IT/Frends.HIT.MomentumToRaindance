#!/usr/bin/env bash
# Verify schema 002 -> 003 preserves permanent identity/delivery history.
# Only a newly created local scratch database is modified and removed.
set -euo pipefail
database_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
export MOMENTUM_DATABASE_TEST_COMPOSE="$database_dir/compose.yaml"
export PATH="$database_dir/tests/client:$PATH"
export PGHOST=127.0.0.1 PGUSER=momentum_admin
export PGDATABASE="momentum_metadata_test_$(date +%Y%m%d%H%M%S)_$$"
unset MOMENTUM_DATABASE_DEV
scratch_dir=$(mktemp -d "${TMPDIR:-/tmp}/momentum-metadata-tests.XXXXXX")
mkdir "$scratch_dir/migrations"
cp "$database_dir/lib.sh" "$database_dir/migrate.sh" "$scratch_dir/"
cp "$database_dir/migrations/001_invoice_tracking.sql" "$database_dir/migrations/002_canonical_identities.sql" "$scratch_dir/migrations/"
[[ $PGDATABASE =~ ^momentum_metadata_test_[0-9]+_[0-9]+$ ]] || exit 2
docker compose -f "$MOMENTUM_DATABASE_TEST_COMPOSE" exec -T postgres createdb --username=momentum_admin "$PGDATABASE"
cleanup() {
    docker compose -f "$MOMENTUM_DATABASE_TEST_COMPOSE" exec -T postgres dropdb --username=momentum_admin "$PGDATABASE"
}
trap cleanup EXIT
bash "$scratch_dir/migrate.sh" > "$scratch_dir/initial.log"
psql -X --no-password --set=ON_ERROR_STOP=1 <<'SQL'
INSERT INTO momentum_raindance.sources (source_system, activated_at, baseline_reference)
VALUES ('metadata-upgrade-test', now(), 'Synthetic migration test');
INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, historical_reference)
VALUES ('metadata-upgrade-test', 'historical-id', 0, 'Synthetic historical evidence');
INSERT INTO momentum_raindance.deliveries (delivery_id, source_system, filename, content_sha256, byte_count, invoice_count, last_local_id, status)
VALUES ('00000000-0000-0000-0000-000000000003', 'metadata-upgrade-test', '300K24_20260909_000003.txt', repeat('a',64), 100, 1, 7, 'reserved');
INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, content_sha256, delivery_id)
VALUES ('metadata-upgrade-test', 'reserved-id', 7, repeat('a',64), '00000000-0000-0000-0000-000000000003');
SQL
snapshot="SELECT jsonb_build_object('sources',(SELECT jsonb_agg(to_jsonb(s) ORDER BY source_system) FROM momentum_raindance.sources s), 'deliveries',(SELECT jsonb_agg(to_jsonb(d) ORDER BY delivery_id) FROM momentum_raindance.deliveries d), 'invoices',(SELECT jsonb_agg(to_jsonb(i)-ARRAY['source_created_at','due_date','invoice_billed_date','accounting_event_created_at','accounting_approved_at'] ORDER BY ledger_note_id) FROM momentum_raindance.invoices i));"
before=$(psql -X --no-password --set=ON_ERROR_STOP=1 -At -c "$snapshot")
cp "$database_dir/migrations/003_invoice_metadata.sql" "$scratch_dir/migrations/"
bash "$scratch_dir/migrate.sh" > "$scratch_dir/upgrade.log"
bash "$scratch_dir/migrate.sh" > "$scratch_dir/repeat.log"
after=$(psql -X --no-password --set=ON_ERROR_STOP=1 -At -c "$snapshot")
[[ $before == "$after" ]] || { printf '%s\n' 'Upgrade changed existing history.' >&2; exit 1; }
psql -X --no-password --set=ON_ERROR_STOP=1 <<'SQL'
DO $test$
BEGIN
    IF (SELECT count(*) FROM momentum_raindance.schema_migrations) <> 3 THEN
        RAISE EXCEPTION 'Expected exactly three migrations';
    END IF;
    IF EXISTS (SELECT 1 FROM momentum_raindance.invoices WHERE
        source_created_at IS NOT NULL OR due_date IS NOT NULL OR invoice_billed_date IS NOT NULL
        OR accounting_event_created_at IS NOT NULL OR accounting_approved_at IS NOT NULL) THEN
        RAISE EXCEPTION 'Upgrade invented historical metadata';
    END IF;
    BEGIN
        UPDATE momentum_raindance.invoices SET source_created_at = now();
        RAISE EXCEPTION 'Metadata unexpectedly mutable';
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM NOT LIKE 'Invoice tracking history is permanent:%' THEN RAISE; END IF;
    END;
END
$test$;
SQL
printf 'Metadata upgrade preserved history, NULLs and immutability; repeat migration passed. Logs: %s\n' "$scratch_dir"
