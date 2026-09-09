-- Whitespace variants must never create a second identity namespace or a seed
-- which fails to match the actual stable ID. The trim set matches Unicode
-- whitespace recognized by .NET; PostgreSQL text already forbids NUL.
CREATE FUNCTION momentum_raindance.is_canonical_identity(value text) RETURNS boolean
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE SET search_path = pg_catalog AS $function$
    SELECT value = btrim(value, U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000')
        AND value !~ U&'[\0001-\001F\007F-\009F]'
$function$;

ALTER TABLE momentum_raindance.sources
    ADD CONSTRAINT source_canonical_identity CHECK (momentum_raindance.is_canonical_identity(source_system));
ALTER TABLE momentum_raindance.invoices
    ADD CONSTRAINT invoice_canonical_identity CHECK (momentum_raindance.is_canonical_identity(ledger_note_id));

-- The runtime must execute this immutable validation function for table checks.
-- It only examines the supplied string; it does not read or modify any data.
REVOKE ALL ON FUNCTION momentum_raindance.is_canonical_identity(text) FROM PUBLIC;
