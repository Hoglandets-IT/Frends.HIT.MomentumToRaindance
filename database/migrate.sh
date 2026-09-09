#!/usr/bin/env bash
# Apply trusted, append-only migration files outside Frends in one locked transaction.
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
if [[ $# != 0 ]]; then
    printf '%s\n' 'Usage: database/migrate.sh (connection from libpq environment)' >&2
    exit 2
fi

versions=()
checksums=()
migrations=()
for migration in "$database_dir"/migrations/*.sql; do
    name=${migration##*/}
    if [[ ! -s $migration ]] || [[ ! $name =~ ^[0-9]{3,}_[a-z0-9_]+\.sql$ ]]; then
        printf 'Invalid or missing migration file: %s\n' "$name" >&2
        exit 2
    fi
    if command -v sha256sum >/dev/null 2>&1; then
        checksum=$(sha256sum "$migration")
    else
        checksum=$(shasum -a 256 "$migration")
    fi
    versions+=("${name%.sql}")
    checksums+=("${checksum%% *}")
    migrations+=("$migration")
done

emit_migrations() {
    cat <<'SQL'
BEGIN;
SET LOCAL synchronous_commit = on;
SET LOCAL lock_timeout = '60s';
DO $version$
BEGIN
    IF current_setting('server_version_num')::integer < 150000 THEN
        RAISE EXCEPTION 'PostgreSQL 15 or newer is required (historical CSV import validates column headers).';
    END IF;
END
$version$;
-- Stable application-specific transaction lock serializes all migration runners.
SELECT pg_advisory_xact_lock(726149031, 1);
CREATE SCHEMA IF NOT EXISTS momentum_raindance;
REVOKE ALL ON SCHEMA momentum_raindance FROM PUBLIC;
CREATE TABLE IF NOT EXISTS momentum_raindance.schema_migrations (
    version text PRIMARY KEY,
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL DEFAULT now()
);
CREATE TEMP TABLE expected_migrations (version text PRIMARY KEY, checksum text NOT NULL) ON COMMIT DROP;
SQL
    for ((i=0; i<${#versions[@]}; i++)); do
        printf "INSERT INTO expected_migrations VALUES ('%s', '%s');\n" "${versions[$i]}" "${checksums[$i]}"
    done
    cat <<'SQL'
DO $check$
BEGIN
    IF EXISTS (
        SELECT 1 FROM momentum_raindance.schema_migrations applied
        LEFT JOIN expected_migrations expected USING (version)
        WHERE expected.version IS NULL OR expected.checksum <> applied.checksum
    ) THEN
        RAISE EXCEPTION 'Migration history differs from local files. Restore the original migration files; add a new migration instead of modifying history.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM expected_migrations expected
        WHERE NOT EXISTS (SELECT 1 FROM momentum_raindance.schema_migrations applied WHERE applied.version = expected.version)
          AND expected.version < (SELECT max(version) FROM momentum_raindance.schema_migrations)
    ) THEN
        RAISE EXCEPTION 'New migrations must sort after all applied migrations; out-of-order changes are forbidden.';
    END IF;
END
$check$;
SQL
    for ((i=0; i<${#versions[@]}; i++)); do
        printf "SELECT NOT EXISTS (SELECT 1 FROM momentum_raindance.schema_migrations WHERE version = '%s') AS apply_migration \\gset\n" "${versions[$i]}"
        printf '\\if :apply_migration\n\\echo Applying %s\n' "${versions[$i]}"
        cat "${migrations[$i]}"
        printf "\nINSERT INTO momentum_raindance.schema_migrations (version, checksum) VALUES ('%s', '%s');\n" "${versions[$i]}" "${checksums[$i]}"
        printf '\\endif\n'
    done
    printf 'COMMIT;\n'
}

emit_migrations | run_psql
