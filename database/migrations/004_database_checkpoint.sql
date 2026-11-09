-- Database-owned progress starts at zero: legacy delivery maxima and historical
-- identities do not prove that an unfiltered, complete feed was handled.
ALTER TABLE momentum_raindance.sources
    ADD COLUMN last_local_id integer NOT NULL DEFAULT 0 CHECK (last_local_id >= 0);

CREATE TABLE momentum_raindance.fetch_batches (
    fetch_id uuid PRIMARY KEY,
    source_system text NOT NULL REFERENCES momentum_raindance.sources (source_system),
    from_local_id integer NOT NULL CHECK (from_local_id >= 0),
    through_local_id integer NOT NULL CHECK (through_local_id >= from_local_id),
    node_count integer NOT NULL CHECK (node_count >= 0),
    payload_sha256 text NOT NULL CHECK (payload_sha256 ~ '^[0-9a-f]{64}$'),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (fetch_id, source_system),
    CHECK ((node_count = 0 AND through_local_id = from_local_id)
        OR (node_count > 0 AND through_local_id > from_local_id))
);
CREATE INDEX fetch_batches_source ON momentum_raindance.fetch_batches (source_system, created_at);

ALTER TABLE momentum_raindance.deliveries
    ADD COLUMN fetch_id uuid,
    ADD CONSTRAINT delivery_fetch_source FOREIGN KEY (fetch_id, source_system)
        REFERENCES momentum_raindance.fetch_batches (fetch_id, source_system);
CREATE UNIQUE INDEX deliveries_fetch ON momentum_raindance.deliveries (fetch_id) WHERE fetch_id IS NOT NULL;

CREATE TRIGGER fetch_batches_no_update_delete BEFORE UPDATE OR DELETE ON momentum_raindance.fetch_batches
    FOR EACH ROW EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();
CREATE TRIGGER fetch_batches_no_truncate BEFORE TRUNCATE ON momentum_raindance.fetch_batches
    FOR EACH STATEMENT EXECUTE FUNCTION momentum_raindance.reject_identity_mutation();

CREATE OR REPLACE FUNCTION momentum_raindance.guard_source_update() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog AS $function$
BEGIN
    IF NEW IS NOT DISTINCT FROM OLD THEN
        RETURN NEW;
    END IF;
    IF NEW.last_local_id IS DISTINCT FROM OLD.last_local_id THEN
        IF NEW.last_local_id < OLD.last_local_id
           OR OLD.activated_at IS NULL
           OR (to_jsonb(NEW) - 'last_local_id') IS DISTINCT FROM (to_jsonb(OLD) - 'last_local_id')
           OR EXISTS (SELECT 1 FROM momentum_raindance.deliveries
               WHERE source_system = OLD.source_system AND status = 'reserved')
           OR NOT EXISTS (SELECT 1 FROM momentum_raindance.fetch_batches
               WHERE source_system = OLD.source_system
                 AND from_local_id = OLD.last_local_id AND through_local_id = NEW.last_local_id) THEN
            RAISE EXCEPTION 'Source checkpoint may only advance from a complete fetch with unchanged source identity and no unresolved delivery.';
        END IF;
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

REVOKE ALL ON momentum_raindance.fetch_batches FROM PUBLIC;
COMMENT ON COLUMN momentum_raindance.sources.last_local_id IS
    'Committed complete-feed checkpoint. Initialized to zero, never inferred from legacy deliveries or historical identities. Advance only after no-output reconciliation or delivery confirmation.';
COMMENT ON TABLE momentum_raindance.fetch_batches IS
    'Immutable receipt for a complete unfiltered feed fetched from the database checkpoint. Stores a SHA256 of exact UTF8 JSON, never customer payloads. Retain permanently.';
COMMENT ON COLUMN momentum_raindance.deliveries.fetch_id IS
    'Complete-feed receipt authorizing checkpoint advancement on confirmation. NULL legacy deliveries do not advance database progress.';
