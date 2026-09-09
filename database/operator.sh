#!/usr/bin/env bash
# Administrative bootstrap/reconciliation only. Not called by Frends.
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
usage() {
    printf '%s\n' \
        'Usage: database/operator.sh register-source SOURCE' \
        '       database/operator.sh seed-history SOURCE CSV_FILE' \
        '       database/operator.sh activate-source SOURCE BASELINE_REFERENCE' \
        '       database/operator.sh report SOURCE' \
        '       database/operator.sh confirm-delivery SOURCE DELIVERY_UUID FILENAME SHA256 EVIDENCE_REFERENCE' \
        '       database/operator.sh grant-runtime EXISTING_ROLE' >&2
    exit 2
}
[[ $# -gt 0 ]] || usage
command=$1
shift
case $command in
    register-source)
        [[ $# == 1 ]] || usage
        run_sql_file register-source.sql --set=source="$1"
        ;;
    seed-history)
        [[ $# == 2 ]] || usage
        [[ -r $2 && -f $2 ]] || { printf '%s\n' 'CSV file does not exist or cannot be read.' >&2; exit 2; }
        [[ -s $2 ]] || { printf '%s\n' 'CSV is empty. The required column header must be present, even for an empty baseline.' >&2; exit 2; }
        # psql treats a line containing only \. as EOF even inside CSV quoting.
        # Reject it instead of silently importing an incomplete historical baseline.
        if ! LC_ALL=C awk '/^\\\.\r?$/ { exit 1 }' "$2"; then
            printf '%s\n' 'CSV contains a psql end-of-copy marker on its own line; refusing incomplete import.' >&2
            exit 2
        fi
        # Commands come from a fixed script, CSV only from pstdin. CSV can never
        # become SQL or psql commands, even if it contains malicious values.
        run_sql_file seed-history.sql --set=source="$1" < "$2"
        ;;
    activate-source)
        [[ $# == 2 ]] || usage
        run_sql_file activate-source.sql --set=source="$1" --set=baseline_reference="$2"
        ;;
    report)
        [[ $# == 1 ]] || usage
        run_sql_file report.sql --set=source="$1"
        ;;
    confirm-delivery)
        [[ $# == 5 ]] || usage
        run_sql_file confirm-delivery.sql --set=source="$1" --set=delivery_id="$2" \
            --set=filename="$3" --set=content_sha256="$4" --set=delivery_reference="$5"
        ;;
    grant-runtime)
        [[ $# == 1 ]] || usage
        run_sql_file grant-runtime.sql --set=runtime_role="$1"
        ;;
    *) usage ;;
esac
