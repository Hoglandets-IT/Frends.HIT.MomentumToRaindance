#!/usr/bin/env bash
# Shared by the offline scripts; connection secrets are never echoed or passed as arguments.
set -euo pipefail
database_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

run_psql() {
    if [[ ${MOMENTUM_DATABASE_DEV:-0} == 1 ]]; then
        docker compose -f "$database_dir/compose.yaml" exec -T postgres \
            psql -X --no-password --set=ON_ERROR_STOP=1 --set=VERBOSITY=terse \
            --username=momentum_admin --dbname=momentum_tracking "$@"
    else
        # libpq reads PGHOST/PGPORT/PGDATABASE/PGUSER/PGSERVICE and .pgpass/PGPASSFILE.
        # Refuse implicit connection defaults that could select the wrong database.
        if [[ -z ${PGSERVICE:-} ]] && { [[ -z ${PGHOST:-} ]] || [[ -z ${PGDATABASE:-} ]] || [[ -z ${PGUSER:-} ]]; }; then
            printf '%s\n' 'Set PGSERVICE, or all of PGHOST, PGDATABASE and PGUSER. Use a protected PGPASSFILE for passwords.' >&2
            return 2
        fi
        psql -X --no-password --set=ON_ERROR_STOP=1 --set=VERBOSITY=terse "$@"
    fi
}

run_sql_file() {
    local sql_file=$1
    shift
    if [[ ${MOMENTUM_DATABASE_DEV:-0} == 1 ]]; then
        run_psql --file="/workspace/database/sql/$sql_file" "$@"
    else
        run_psql --file="$database_dir/sql/$sql_file" "$@"
    fi
}
