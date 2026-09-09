#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
export MOMENTUM_DATABASE_DEV=1
command=${1:-help}
if [[ $# -gt 0 ]]; then shift; fi
case $command in
    up) docker compose -f "$database_dir/compose.yaml" up -d --wait "$@" ;;
    stop) docker compose -f "$database_dir/compose.yaml" stop "$@" ;;
    status) docker compose -f "$database_dir/compose.yaml" ps "$@" ;;
    psql) run_psql "$@" ;;
    migrate) exec bash "$database_dir/migrate.sh" "$@" ;;
    register-source|seed-history|activate-source|report|confirm-delivery|grant-runtime)
        exec bash "$database_dir/operator.sh" "$command" "$@" ;;
    *) printf '%s\n' 'Usage: database/dev.sh up|stop|status|psql|migrate|register-source|seed-history|activate-source|report|confirm-delivery|grant-runtime [arguments]' >&2; exit 2 ;;
esac
