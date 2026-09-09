#!/usr/bin/env bash
# Dev-only tests create/drop a uniquely named scratch database, not the shared
# development database. Failed fixture migrations remain in a /tmp test folder.
set -euo pipefail
database_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
export MOMENTUM_DATABASE_TEST_COMPOSE="$database_dir/compose.yaml"
export PATH="$database_dir/tests/client:$PATH"
export PGHOST=127.0.0.1 PGUSER=momentum_admin
export PGDATABASE="momentum_migration_test_$(date +%Y%m%d%H%M%S)_$$"
unset MOMENTUM_DATABASE_DEV
scratch_dir=$(mktemp -d "${TMPDIR:-/tmp}/momentum-migration-tests.XXXXXX")
mkdir "$scratch_dir/migrations"
cp "$database_dir/lib.sh" "$database_dir/migrate.sh" "$scratch_dir/"
cp "$database_dir"/migrations/*.sql "$scratch_dir/migrations/"
[[ $PGDATABASE =~ ^momentum_migration_test_[0-9]+_[0-9]+$ ]] || exit 2
docker compose -f "$MOMENTUM_DATABASE_TEST_COMPOSE" exec -T postgres \
    createdb --username=momentum_admin "$PGDATABASE"
cleanup() {
    # Only the exact scratch database successfully created above is removed.
    docker compose -f "$MOMENTUM_DATABASE_TEST_COMPOSE" exec -T postgres \
        dropdb --username=momentum_admin "$PGDATABASE"
}
trap cleanup EXIT

expect_failure() {
    if bash "$scratch_dir/migrate.sh" >"$scratch_dir/expected-failure.log" 2>&1; then
        printf '%s\n' 'Expected migration failure but command succeeded.' >&2
        exit 1
    fi
    if [[ $(< "$scratch_dir/expected-failure.log") != *"$1"* ]]; then
        printf 'Failure did not contain expected diagnostic: %s\n' "$1" >&2
        exit 1
    fi
}
assert_scalar() {
    local actual
    actual=$(psql -X --no-password --set=ON_ERROR_STOP=1 -At -c "$1")
    [[ $actual == "$2" ]] || { printf 'Expected %s; got %s\n' "$2" "$actual" >&2; exit 1; }
}

printf '%s\n' 'CREATE TABLE momentum_raindance.must_rollback (id integer);' 'SELECT 1 / 0;' > "$scratch_dir/migrations/999_deliberate_failure.sql"
expect_failure 'division by zero'
assert_scalar "SELECT to_regnamespace('momentum_raindance') IS NULL" t
mv "$scratch_dir/migrations/999_deliberate_failure.sql" "$scratch_dir/failed-migration.fixture"
# Both first-install runners must succeed under the same advisory lock, without
# racing schema creation or duplicate migration entries. A synthetic slow final
# migration ensures overlap even when Docker exec has variable startup latency.
printf 'SELECT pg_sleep(2);\n' > "$scratch_dir/migrations/998_slow_concurrency.sql"
bash "$scratch_dir/migrate.sh" > "$scratch_dir/concurrent-a.log" & first_pid=$!
bash "$scratch_dir/migrate.sh" > "$scratch_dir/concurrent-b.log" & second_pid=$!
wait "$first_pid"
wait "$second_pid"
migration_files=("$scratch_dir"/migrations/*.sql)
expected_count=${#migration_files[@]}
assert_scalar 'SELECT count(*) FROM momentum_raindance.schema_migrations' "$expected_count"
bash "$scratch_dir/migrate.sh" > "$scratch_dir/repeat.log"

printf '\n-- simulated checksum drift\n' >> "$scratch_dir/migrations/001_invoice_tracking.sql"
expect_failure 'Migration history differs'
assert_scalar 'SELECT count(*) FROM momentum_raindance.schema_migrations' "$expected_count"
cp "$database_dir/migrations/001_invoice_tracking.sql" "$scratch_dir/migrations/001_invoice_tracking.sql"
mv "$scratch_dir/migrations/002_canonical_identities.sql" "$scratch_dir/missing-migration.fixture"
expect_failure 'Migration history differs'
mv "$scratch_dir/missing-migration.fixture" "$scratch_dir/migrations/002_canonical_identities.sql"
printf 'SELECT 1;\n' > "$scratch_dir/migrations/000_out_of_order.sql"
expect_failure 'out-of-order changes are forbidden'
assert_scalar 'SELECT count(*) FROM momentum_raindance.schema_migrations' "$expected_count"
printf 'Migration atomic rollback, idempotency, concurrency, checksum drift, missing history and ordering tests passed. Logs: %s\n' "$scratch_dir"
