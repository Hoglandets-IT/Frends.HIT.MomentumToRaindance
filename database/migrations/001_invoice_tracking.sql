-- Append-only invoice identity ledger. Never edit an applied migration.
CREATE TABLE momentum_raindance.sources (
    source_system text PRIMARY KEY CHECK (length(btrim(source_system)) BETWEEN 1 AND 200),
    activated_at timestamptz,
    baseline_reference text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT source_activation_evidence CHECK (
        (activated_at IS NULL AND baseline_reference IS NULL)
        OR (activated_at IS NOT NULL AND baseline_reference IS NOT NULL AND length(btrim(baseline_reference)) > 0)
    )
);

CREATE TABLE momentum_raindance.deliveries (
    delivery_id uuid PRIMARY KEY,
    source_system text NOT NULL REFERENCES momentum_raindance.sources (source_system),
    filename text NOT NULL UNIQUE CHECK (length(btrim(filename)) > 0),
    content_sha256 text NOT NULL CHECK (content_sha256 ~ '^[0-9a-fA-F]{64}$'),
    byte_count integer NOT NULL CHECK (byte_count >= 0),
    invoice_count integer NOT NULL CHECK (invoice_count > 0),
    last_local_id integer NOT NULL CHECK (last_local_id >= 0),
    status text NOT NULL CHECK (status IN ('reserved', 'delivered')),
    created_at timestamptz NOT NULL DEFAULT now(),
    delivered_at timestamptz,
    delivery_reference text,
    UNIQUE (delivery_id, source_system),
    CONSTRAINT delivery_status_evidence CHECK (
        (status = 'reserved' AND delivered_at IS NULL AND delivery_reference IS NULL)
        OR (status = 'delivered' AND delivered_at IS NOT NULL AND delivery_reference IS NOT NULL AND length(btrim(delivery_reference)) > 0)
    )
);

CREATE TABLE momentum_raindance.invoices (
    source_system text NOT NULL REFERENCES momentum_raindance.sources (source_system),
    ledger_note_id text NOT NULL CHECK (length(btrim(ledger_note_id)) BETWEEN 1 AND 300),
    first_local_id integer NOT NULL CHECK (first_local_id >= 0),
    node_id text,
    invoice_number text,
    ledger_note_number text,
    content_sha256 text CHECK (content_sha256 ~ '^[0-9a-fA-F]{64}$'),
    delivery_id uuid,
    historical_reference text,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (source_system, ledger_note_id),
    FOREIGN KEY (delivery_id, source_system)
        REFERENCES momentum_raindance.deliveries (delivery_id, source_system),
    CONSTRAINT invoice_origin_evidence CHECK (
        (delivery_id IS NULL AND historical_reference IS NOT NULL AND length(btrim(historical_reference)) > 0)
        OR (delivery_id IS NOT NULL AND historical_reference IS NULL AND content_sha256 IS NOT NULL)
    )
);

CREATE INDEX deliveries_source_status ON momentum_raindance.deliveries (source_system, status, created_at);
CREATE INDEX invoices_delivery ON momentum_raindance.invoices (delivery_id);

CREATE FUNCTION momentum_raindance.reject_identity_mutation() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog AS $function$
BEGIN
    RAISE EXCEPTION 'Invoice tracking history is permanent: % is forbidden on %.', TG_OP, TG_TABLE_NAME;
END
$function$;

CREATE FUNCTION momentum_raindance.guard_source_update() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog AS $function$
BEGIN
    IF NEW IS NOT DISTINCT FROM OLD THEN
        RETURN NEW;
    END IF;
    IF NEW.source_system IS DISTINCT FROM OLD.source_system
       OR NEW.created_at IS DISTINCT FROM OLD.created_at
       OR OLD.activated_at IS NOT NULL
       OR NEW.activated_at IS NULL
       OR NEW.baseline_reference IS NULL
       OR length(btrim(NEW.baseline_reference)) = 0 THEN
        RAISE EXCEPTION 'A source can only be activated once, with historical-baseline evidence; source identity is permanent.';
    END IF;
    RETURN NEW;
END
$function$;

CREATE FUNCTION momentum_raindance.guard_delivery_update() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog AS $function$
BEGIN
    IF NEW IS NOT DISTINCT FROM OLD THEN
        RETURN NEW;
    END IF;
    IF OLD.status <> 'reserved' OR NEW.status <> 'delivered'
       OR NEW.delivered_at IS NULL
       OR NEW.delivery_reference IS NULL OR length(btrim(NEW.delivery_reference)) = 0
       OR (to_jsonb(NEW) - ARRAY['status', 'delivered_at', 'delivery_reference'])
          IS DISTINCT FROM
          (to_jsonb(OLD) - ARRAY['status', 'delivered_at', 'delivery_reference']) THEN
        RAISE EXCEPTION 'A delivery can only transition from reserved to delivered with evidence; reservation identity and contents are permanent.';
    END IF;
    RETURN NEW;
END
$function$;

CREATE TRIGGER sources_guard_update BEFORE UPDATE ON momentum_raindance.sources
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.guard_source_update();
CREATE TRIGGER sources_no_delete BEFORE DELETE ON momentum_raindance.sources
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();
CREATE TRIGGER sources_no_truncate BEFORE TRUNCATE ON momentum_raindance.sources
    FOR EACH STATEMENT EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();

CREATE TRIGGER deliveries_guard_update BEFORE UPDATE ON momentum_raindance.deliveries
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.guard_delivery_update();
CREATE TRIGGER deliveries_no_delete BEFORE DELETE ON momentum_raindance.deliveries
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();
CREATE TRIGGER deliveries_no_truncate BEFORE TRUNCATE ON momentum_raindance.deliveries
    FOR EACH STATEMENT EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();

CREATE TRIGGER invoices_no_update_delete BEFORE UPDATE OR DELETE ON momentum_raindance.invoices
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();
CREATE TRIGGER invoices_no_truncate BEFORE TRUNCATE ON momentum_raindance.invoices
    FOR EACH STATEMENT EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();

REVOKE ALL ON ALL TABLES IN SCHEMA momentum_raindance FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA momentum_raindance FROM PUBLIC;

COMMENT ON TABLE momentum_raindance.invoices IS 'Permanent invoice identities: even unconfirmed reservations suppress re-emission. Never expire or delete.';
COMMENT ON COLUMN momentum_raindance.invoices.ledger_note_id IS 'Stable Momentum ledgerNote.id, namespaced by a permanent source-system identifier; not a change-feed localId.';
COMMENT ON TABLE momentum_raindance.deliveries IS 'Reserved means bytes may already have escaped to the writer. Delivered requires explicit evidence; neither state permits re-emission.';
COMMENT ON COLUMN momentum_raindance.sources.baseline_reference IS 'Operator evidence that pre-tracking invoice history was reconciled before first production use.';
