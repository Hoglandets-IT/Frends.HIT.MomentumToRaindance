#!/usr/bin/env bash
# Dev-only operator end-to-end tests. Keeps synthetic history in a unique source
# namespace to exercise the same no-deletion policy as production.
set -euo pipefail
database_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test_source="operator-test-$(date +%Y%m%d%H%M%S)-$$"
scratch_dir=$(mktemp -d "${TMPDIR:-/tmp}/momentum-operator-tests.XXXXXX")
dev() { bash "$database_dir/dev.sh" "$@"; }
expect_failure() {
    local expected=$1
    shift
    if dev "$@" > "$scratch_dir/expected-failure.log" 2>&1; then
        printf 'Expected operator command to fail: %s\n' "$1" >&2
        exit 1
    fi
    if [[ $(< "$scratch_dir/expected-failure.log") != *"$expected"* ]]; then
        printf 'Failure did not contain expected diagnostic: %s\n' "$expected" >&2
        exit 1
    fi
}
assert_scalar() {
    local actual
    actual=$(dev psql -At -c "$1")
    [[ $actual == "$2" ]] || { printf 'Expected %s; got %s\n' "$2" "$actual" >&2; exit 1; }
}

printf '%s\n' 'ledger_note_id,invoice_number,ledger_note_number,reference' \
    'historical-1,invoice-display,ledger-display,Approved synthetic history' > "$scratch_dir/history.csv"
printf '%s\n' 'wrong_header,invoice_number,ledger_note_number,reference' \
    'bad-header-id,,,Synthetic' > "$scratch_dir/bad-header.csv"
printf '%s\n' 'ledger_note_id,invoice_number,ledger_note_number,reference' \
    ' padded-id ,,,Synthetic' > "$scratch_dir/padded-id.csv"
printf '%s\n' 'ledger_note_id,invoice_number,ledger_note_number,reference' \
    '\.' 'this-must-not-be-read,,,Synthetic' > "$scratch_dir/end-marker.csv"
touch "$scratch_dir/empty.csv"

dev register-source "$test_source" > "$scratch_dir/register.log"
expect_failure 'Register the source' seed-history "$test_source-missing" "$scratch_dir/history.csv"
expect_failure 'CSV is empty' seed-history "$test_source" "$scratch_dir/empty.csv"
expect_failure 'column name mismatch' seed-history "$test_source" "$scratch_dir/bad-header.csv"
expect_failure 'check constraint' seed-history "$test_source" "$scratch_dir/padded-id.csv"
expect_failure 'end-of-copy marker' seed-history "$test_source" "$scratch_dir/end-marker.csv"
assert_scalar "SELECT count(*) FROM momentum_raindance.invoices WHERE source_system='$test_source'" 0
dev seed-history "$test_source" "$scratch_dir/history.csv" > "$scratch_dir/seed.log"
dev seed-history "$test_source" "$scratch_dir/history.csv" > "$scratch_dir/reseed.log"
assert_scalar "SELECT count(*) FROM momentum_raindance.invoices WHERE source_system='$test_source'" 1
dev activate-source "$test_source" 'Synthetic baseline approval' > "$scratch_dir/activate.log"
dev activate-source "$test_source" 'Synthetic baseline approval' > "$scratch_dir/reactivate.log"
expect_failure 'different baseline evidence' activate-source "$test_source" 'Conflicting approval'
expect_failure 'only allowed before source activation' seed-history "$test_source" "$scratch_dir/history.csv"
expect_failure 'administrative/owner privileges' grant-runtime momentum_admin

delivery_id=$(dev psql -At -c 'SELECT gen_random_uuid()')
filename="operator-test-$delivery_id.txt"
hash=$(printf '%064d' 1)
dev psql -c "BEGIN; INSERT INTO momentum_raindance.deliveries (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status) VALUES ('$delivery_id','$test_source','$filename','$hash',100,1,5,'reserved'); INSERT INTO momentum_raindance.invoices (source_system,ledger_note_id,first_local_id,content_sha256,delivery_id) VALUES ('$test_source','runtime-1',5,'$hash','$delivery_id'); COMMIT;" > "$scratch_dir/reserve.log"
expect_failure 'Evidence does not match' confirm-delivery "$test_source" "$delivery_id" "$filename" "$(printf '%064d' 2)" 'Synthetic writer evidence'
expect_failure 'Evidence does not match' confirm-delivery "$test_source" "$delivery_id" 'wrong-filename.txt' "$hash" 'Synthetic writer evidence'
assert_scalar "SELECT status FROM momentum_raindance.deliveries WHERE delivery_id='$delivery_id'" reserved
dev confirm-delivery "$test_source" "$delivery_id" "$filename" "$hash" 'Synthetic writer evidence' > "$scratch_dir/confirm.log"
dev confirm-delivery "$test_source" "$delivery_id" "$filename" "$hash" 'Synthetic writer evidence' > "$scratch_dir/reconfirm.log"
expect_failure 'different evidence' confirm-delivery "$test_source" "$delivery_id" "$filename" "$hash" 'Conflicting writer evidence'
assert_scalar "SELECT status FROM momentum_raindance.deliveries WHERE delivery_id='$delivery_id'" delivered
dev report "$test_source" > "$scratch_dir/report.log"
printf 'Operator header/identity validation, inactive baseline import, idempotency, activation gate and evidence-matched confirmation passed. Synthetic history retained under %s. Logs: %s\n' "$test_source" "$scratch_dir"
