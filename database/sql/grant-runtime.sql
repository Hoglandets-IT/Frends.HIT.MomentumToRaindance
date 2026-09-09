-- Provision this existing non-owner role and its credentials out of band.
-- Run as the schema owner. No passwords are accepted by this script.
BEGIN;
SET LOCAL synchronous_commit = on;
CREATE TEMP TABLE requested_runtime_role (role_name text NOT NULL) ON COMMIT DROP;
INSERT INTO requested_runtime_role VALUES (:'runtime_role');
DO $check$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles JOIN requested_runtime_role ON rolname = role_name) THEN
        RAISE EXCEPTION 'Create a dedicated non-owner runtime role out of band before granting access.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_roles r JOIN requested_runtime_role ON r.rolname = role_name
        JOIN pg_namespace n ON n.nspname = 'momentum_raindance'
        JOIN pg_database d ON d.datname = current_database()
        WHERE r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls
           OR pg_has_role(r.oid, n.nspowner, 'MEMBER') OR pg_has_role(r.oid, d.datdba, 'MEMBER')
    ) THEN
        RAISE EXCEPTION 'Runtime role has administrative/owner privileges or owner membership. Use a dedicated restricted role.';
    END IF;
END
$check$;
GRANT USAGE ON SCHEMA momentum_raindance TO :"runtime_role";
GRANT EXECUTE ON FUNCTION momentum_raindance.is_canonical_identity(text) TO :"runtime_role";
GRANT SELECT ON momentum_raindance.schema_migrations, momentum_raindance.sources,
    momentum_raindance.deliveries, momentum_raindance.invoices TO :"runtime_role";
-- SELECT ... FOR UPDATE requires UPDATE on at least one column. The source
-- trigger rejects changing created_at, so this allows locking, not activation.
GRANT UPDATE (created_at) ON momentum_raindance.sources TO :"runtime_role";
-- Remove any broader grants from an earlier release of this script before
-- granting only runtime columns; historical imports remain an operator action.
REVOKE INSERT ON momentum_raindance.deliveries, momentum_raindance.invoices FROM :"runtime_role";
GRANT INSERT (delivery_id, source_system, filename, content_sha256, byte_count, invoice_count, last_local_id, status)
    ON momentum_raindance.deliveries TO :"runtime_role";
GRANT INSERT (source_system, ledger_note_id, first_local_id, node_id, invoice_number, ledger_note_number, content_sha256, delivery_id)
    ON momentum_raindance.invoices TO :"runtime_role";
GRANT UPDATE (status, delivered_at, delivery_reference) ON momentum_raindance.deliveries TO :"runtime_role";
COMMIT;
