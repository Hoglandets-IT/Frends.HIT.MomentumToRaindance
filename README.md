# Frends.HIT.MomentumToRaindance

Fetches Momentum ledger accounting JSON, converts previously unseen invoices into this integration's fixed-width Raindance format, and records file-delivery evidence in PostgreSQL. Fetch and Convert remain separate so a Frends process can inspect or transform the JSON between them. A third task confirms delivery after the external file writer succeeds. This package does not write the remote file or persist Frends shared state.

The 2.x task library targets **.NET 8** and uses Npgsql **8.0.9**. The diagnostic local CLI targets **.NET 10** and is not shipped in the Frends package.

## Duplicate protection and first deployment

`LastLocalId` is a feed cursor, **not an invoice identity**. Production conversion requires a PostgreSQL database containing permanent identities keyed by `(SourceSystem, ledgerNote.id)`. Resetting a cursor or receiving the same invoice under another sync local ID must not make it a new invoice.

Convert validates the batch and commits a reservation **before returning any file bytes**. A recorded invoice cannot be emitted again through this workflow. An unresolved reservation blocks the entire source, even a call with no new invoices; it never expires or automatically becomes retryable. Successful file delivery is recorded separately. This is at-most-once admission, not an atomic transaction with the file server or Raindance: cached output, manual copies, writer retries, or loss of database history can still cause duplicates. Follow the [recovery rules](docs/operations.md), especially after an ambiguous failure.

Before enabling production, run the [offline database setup and migrations](database/README.md), register a permanent source identifier, reconcile and seed invoices already sent before tracking existed, and activate the source with documented baseline evidence. An unknown or inactive source fails closed. The converter never creates or migrates tables in Frends.

Windows Server administrators can use the [native PowerShell runbook](database/README.md#windows-server-native-powershell-administration): `database/migrate.ps1` and `database/operator.ps1` support Windows PowerShell 5.1/PowerShell 7 with `psql.exe`, without Bash, WSL, Docker, or a .NET SDK on the administration machine.

Old Raindance output alone cannot establish the historical baseline: its H invoice-number field is deliberately blank, and the stable Momentum `ledgerNote.id` is not written to the file. Recover those identities from retained Momentum JSON, trustworthy delivery records, or a reconciled source/import export. If the historical identity list cannot be established, do not claim that past duplicates are prevented or activate the production source on that assumption.

## Frends process setup

```text
Read saved LastLocalId
  -> Fetch Ledger Note Accountings
  -> Optional JSON processing
  -> Convert GraphQL Result + invoice tracking database
  -> NodeCount > 0?
       yes: Write File once (RAW, overwrite off)
              -> Confirm Invoice Delivery + same database/source
              -> Save confirmation LastLocalId
       no:  Skip writer and confirmation; save conversion LastLocalId
```

Pass the same saved checkpoint into Fetch and Convert. Never persist an advanced checkpoint before successful delivery and confirmation. Disable automatic retries/resume at the writer; on uncertainty, inspect delivery evidence instead of sending again. Use the same database and `SourceSystem` in every process that can export the same Momentum invoices.

### Fetch Ledger Note Accountings

Method: `Frends.HIT.MomentumToRaindance.Main.FetchLedgerNoteAccountings`

Takes `MomentumConnection`, `FetchInput`, and an optional `CancellationToken`. Resolves API credentials, authenticates, executes the embedded `ledgerNoteAccountingsSync(lastLocalId: ...)` query once, and returns prettified JSON. It does not contact the tracking database or acknowledge delivery.

| Fetch input | Type / default | Meaning |
| --- | --- | --- |
| `LastLocalId` | Integer or numeric `string`, default `0` | Saved feed cursor; must fit the nonnegative `int` range. |

| Fetch result | Meaning |
| --- | --- |
| `Success` | `true` on return; failures throw. |
| `ResultFile` | Prettified GraphQL JSON as a .NET `string`, not file bytes or a path. |
| `LastLocalId` | Supplied checkpoint normalized to `int`, unchanged. |
| `Info` | Human-readable summary, not a status code. |

GraphQL errors, including partial data accompanied by errors, fail the request. An explicit empty `nodes` array is a valid successful fetch. Invoice-level validation happens in Convert. Fetch does not expose `NodeCount`; branch on the conversion result after database checks.

### Convert GraphQL Result

Method: `Frends.HIT.MomentumToRaindance.Main.ConvertGraphQlResult`

Returns `Task<ConversionResult>`. Takes `ConvertInput`, the required `InvoiceTrackingConnection` database property tab, and an optional `CancellationToken`. It validates/renders the complete candidate batch, checks the permanent history, and reserves new invoice identities transactionally before returning bytes. There is no untracked production mode. HcpVault configuration may require an HTTP secret lookup; conversion itself does not fetch Momentum data.

| Convert input | Type / default | Meaning |
| --- | --- | --- |
| `GraphQlResult` | `string` | Complete response including `data.ledgerNoteAccountingsSync.nodes`. |
| `LastLocalId` | Integer or numeric `string`, default `0` | Same cursor passed to Fetch. Nodes at or below it are excluded. |

| Conversion result | Meaning |
| --- | --- |
| `Success` | `true` on return. Invalid input, database failure, unresolved delivery, or cancellation throws. |
| `ResultFile` | `byte[]`, already ISO-8859-1/Latin-1 encoded without BOM, with fixed-width spaces and CRLF. Empty when no new invoice is reserved. |
| `Filename` | `string`, e.g. `300K24_20260909_143025.txt`; reserved uniquely in the database. Uses the agent's local time and the first free second among 120 candidates, so a collision can place it slightly ahead of the clock. Empty on no output. |
| `DeliveryId` | Durable reservation UUID as a string; pass to confirmation after writing once. Empty on no output. |
| `ContentSha256` | Lowercase SHA-256 hex of the exact complete `ResultFile` bytes. Empty on no output. |
| `NodeCount` | Number of newly emitted invoices, not raw nodes or R/K records. |
| `SkippedInvoiceCount` | Candidate invoice identities already recorded as delivered or historical; these produce no bytes. |
| `LastLocalId` | Proposed cursor. All-no-op input preserves it; fully validated already-recorded invoices can advance it without a file. With new output, use the confirmation result to persist after delivery. |
| `Info` | Human-readable summary. |

Pending nodes are ordered by `localId`. Missing/nonpositive or duplicate pending local IDs fail. Every emitted invoice requires a stable, nonblank `ledgerNote.id`; there is no local-ID fallback. Duplicate invoice identities within one batch fail. An invoice already recorded with a different per-invoice Raindance byte hash fails rather than silently exporting an update. Historical bootstrap rows have no hash: they suppress identity only, without claiming a verified content match.

Explicitly empty and rounding-only invoices emit nothing. An entirely no-op batch preserves its checkpoint; a batch containing actual candidate invoices includes validated no-op nodes when calculating its highest handled ID. See [checkpoint examples](docs/operations.md#checkpoint-contract) and [format validation](docs/raindance-format.md).

### Write File and Confirm Invoice Delivery

For a conversion step named `ConvertGraphQL`, keep the existing RemoteFS writer:

| Write File input | Value |
| --- | --- |
| File | `#result[ConvertGraphQL].Filename` |
| Byte content | `#result[ConvertGraphQL].ResultFile` |
| Encoding | `RAW` |
| Overwrite | Off |

Do not re-encode, trim, change line endings, or send the filename as byte content. Configure the destination path and connection separately. The writer must only run when `NodeCount > 0`, and must not be automatically replayed after failure or process restart.

After the writer succeeds, call `Frends.HIT.MomentumToRaindance.Main.ConfirmInvoiceDelivery`. It returns `Task<DeliveryResult>` and takes `DeliveryConfirmation`, the **same** `InvoiceTrackingConnection`, and optional cancellation:

| Confirmation input | Value |
| --- | --- |
| `DeliveryId` | `#result[ConvertGraphQL].DeliveryId` |
| `Filename` | `#result[ConvertGraphQL].Filename` |
| `ContentSha256` | `#result[ConvertGraphQL].ContentSha256` |
| `DeliveryReference` | Durable evidence reference, such as the Frends execution ID and destination path. Nonblank, at most 2,000 characters, no controls or secrets. |

The result contains `Success`, `DeliveryId`, invoice `NodeCount`, and the `LastLocalId` now safe to save. Repeating confirmation with the same source, fingerprint and evidence reference is idempotent; changing the evidence or repeating the writer is not. Confirmation records **file delivery**, not successful Raindance import. The task checks the stored fingerprint, not the external file server; only call it with verified delivery evidence.

### Checkpoint types and package upgrades

Fetch and Convert accept `#result[LastRunLocalID].Value` as an integer or numeric string. Their input properties use `object` at the Frends binding boundary and normalize to `int`. Numeric strings may have surrounding whitespace and leading zeroes. Explicit null, blank/nonnumeric strings, negatives, decimal/floating-point values, and values above `2147483647` fail. Only an omitted input defaults to zero. All result checkpoints remain `int`.

This is a breaking 2.x workflow upgrade: Convert is now asynchronous with a required database tab, and delivery confirmation is a new task. Update task references, bind the database on Convert and Confirm, and recompile the process. `Filename` remains a string and conversion `ResultFile` remains `byte[]`; Fetch still returns string JSON. During an earlier result-type upgrade, an existing File Writer step retained stale configuration; recreating that step resolved the compilation error. If the upgraded bindings are correct but old type errors remain, recreate the affected step and validate. No RemoteFS code change is required.

## Connection settings

### Momentum API

`MomentumConnection.ConfigurationSource` supports `Json` (default), `Manual`, and canonical `HcpVault` (currently backed by Infisical). Manual configuration uses `AuthUrl`, `GraphQlUrl`, `Username`, and `Password`. JSON configuration and the Vault secret use:

```json
{
  "authurl": "https://example.invalid/momentum/auth",
  "graphqlurl": "https://example.invalid/momentum/graphql",
  "username": "momentum-username",
  "password": "replace-with-api-key"
}
```

`TimeoutSeconds` defaults to `100`, allowed `1`–`3600`, across secret lookup, authentication, and the GraphQL request. HTTPS and normal certificate validation are required; redirects are not followed. HTTP connections are pooled, cookies are not shared, and HTTP requests are not automatically retried.

### Invoice tracking database

Configure this property tab on both Convert and Confirm:

| Setting | Meaning |
| --- | --- |
| `ConfigurationSource` | `HcpVault` (default), `Json`, or `Manual`. |
| `VaultPath` | HcpVault secret containing the JSON object below. Uses the same secret resolver as API credentials. |
| `JsonConfiguration` | That JSON object directly, when using `Json`; masked in the UI. |
| `ConnectionString` | Npgsql connection string when using `Manual`; masked in the UI. |
| `SourceSystem` | Permanent, case-sensitive dataset/environment identifier, registered and activated offline. Required, 1–200 characters, no surrounding whitespace or controls. Never change it to bypass history. |
| `TimeoutSeconds` | Default `100`, allowed `1`–`3600`; overall secret/database operation timeout, not a fresh budget per SQL statement. |

```json
{
  "connectionString": "Host=postgres.example.invalid;Port=5432;Database=momentum_tracking;Username=momentum_runtime;Password=replace-with-secret;SSL Mode=VerifyFull"
}
```

Production connections require `SSL Mode=VerifyFull` with trusted certificates. `SSL Mode=Disable` is accepted only for `localhost`, `127.0.0.1`, or `::1` development. Follow the [first-time database setup](database/README.md) using your existing database and login. The same login is supported for setup and Frends; a separate restricted login is optional hardening. An object-owner login can alter database safeguards, so restrict access to its credentials. Ambient transaction enlistment is disabled so a later Frends rollback cannot undo a reservation after bytes have escaped; the task also sets synchronous commit on its transactions. Sensitive database error details and parameter logging are disabled.

### HcpVault / Infisical

Set these on each Frends agent that can execute the tasks:

| Environment variable | Purpose |
| --- | --- |
| `INFISICAL_ADDR` | HTTPS Infisical base URL, without query or fragment. |
| `INFISICAL_CLIENT_ID` | Universal Auth client ID. |
| `INFISICAL_CLIENT_SECRET` | Universal Auth client secret. |
| `INFISICAL_PROJECT` | Workspace/project ID. |
| `INFISICAL_ENVIRONMENT` | Environment slug. |

Use separate API and database secret paths. `VaultPath`'s last segment is the secret name; a bare name addresses `/`. Dots in the directory portion are replaced with underscores for compatibility with the existing convention; the secret name is unchanged. URI components are encoded. Give the identity read access only to the required secrets, and install private CA trust without certificate bypasses.

## Local development and diagnostic previews

For a disposable PostgreSQL 17 development instance, Docker and Bash are sufficient:

```bash
database/dev.sh up
database/dev.sh migrate
database/dev.sh status
```

It binds only `127.0.0.1:55432` and retains data in a named Docker volume. The committed credentials are local development fixtures, never production credentials. See [database setup](database/README.md) for source registration, historical CSV import, activation, runtime grants, migration verification, and operational reports.

The local dumping tool requires the .NET 10 SDK. Put the API JSON above in repository `.env` (**JSON, not dotenv syntax**). `.env` and `out/` are ignored by Git:

```bash
./generate-raindance.sh ./out/preview.txt \
  --last-local-id 123 \
  --json-output ./out/momentum.json
```

This tool is **diagnostic preview only**: it uses internal untracked rendering, never reserves invoices, and must not deliver its output to Raindance or update the production cursor. It is not a production delivery path. JSON dumps are UTF-8; preview files are Latin-1. Use `--env /path/to/config.json` for another configuration, or `--help`. Input/output paths must be distinct and contain no symbolic links. A no-op does not overwrite an existing preview file. With `--json-output`, JSON is saved before rendering and remains useful if validation fails. Restrict customer-data access and do not commit dumps.

## Build, test, and package

Build only the task library with a .NET 8 SDK:

```bash
dotnet build Frends.HIT.MomentumToRaindance/Frends.HIT.MomentumToRaindance.csproj --configuration Release --warnaserror
```

For the full solution, install the .NET 10 SDK and a .NET 8 runtime. Database integration tests require a **dedicated migrated disposable** database; without `MOMENTUM_TEST_POSTGRES` they are skipped, so a green unit-only run is not database validation:

```bash
database/dev.sh up
database/dev.sh migrate
# Create this dedicated test role once; on subsequent runs keep the existing role.
database/dev.sh psql -c "CREATE ROLE momentum_tracking_runtime LOGIN PASSWORD 'momentum_runtime_dev_only'"
database/dev.sh grant-runtime momentum_tracking_runtime
database/dev.sh psql -f /workspace/database/tests/runtime-privileges.sql
export MOMENTUM_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Database=momentum_tracking;Username=momentum_admin;Password=momentum_dev_only;SSL Mode=Disable'
export MOMENTUM_TEST_POSTGRES_RUNTIME='Host=127.0.0.1;Port=55432;Database=momentum_tracking;Username=momentum_tracking_runtime;Password=momentum_runtime_dev_only;SSL Mode=Disable'
dotnet build Frends.HIT.MomentumToRaindance.sln --configuration Release --warnaserror
dotnet test tests/Frends.HIT.MomentumToRaindance.Tests/Frends.HIT.MomentumToRaindance.Tests.csproj --configuration Release --no-build
```

Tests use synthetic invoices and isolated source identifiers but deliberately retain permanent test history. The admin connection creates fixtures, installs temporary failure-injection triggers and creates/drops a separately named test database; the runtime connection exercises conversion with restricted grants. Never point either connection at production. Without the runtime setting, tests fall back to the admin connection, which does not verify production-role permissions. Tests permit runtime roll-forward for developer convenience; release validation should run on .NET 8. CI provisions real PostgreSQL, applies migrations twice, checks runtime privileges, and executes the suite with a restricted runtime role before packaging. This is not a live Frends or Raindance acceptance test.

```bash
dotnet pack Frends.HIT.MomentumToRaindance/Frends.HIT.MomentumToRaindance.csproj \
  --configuration Release --output ./artifacts
```

The package includes the .NET 8 assembly, XML API documentation, readme, and root `FrendsTaskMetadata.json` listing **three** task methods. The GraphQL query is embedded. Npgsql is a NuGet dependency; the local CLI, credentials, database migration scripts, and database itself are not deployed by the task package. Deploy matching migrations separately. Task signatures, property tabs, and help follow [Frends custom-task conventions](https://docs.frends.com/guides/development/creating-custom-tasks).

## Further documentation

- [Production operations and recovery](docs/operations.md): reservation lifecycle, delivery evidence, history bootstrap, checkpoint examples, backups, and failure handling.
- [Database administration](database/README.md): native Windows PowerShell administration, local Docker, offline migrations, permissions, and operator commands.
- [Raindance records and Momentum field mapping](docs/raindance-format.md): byte positions, debit/credit, period text, limits, and unused fields.
- [Production audit](docs/production-audit.md): verification coverage and deployment gates.

The converter supports this integration's agreed layout, not every Raindance variant or Momentum event type. Test the actual importer with approved data before scheduling production delivery.
