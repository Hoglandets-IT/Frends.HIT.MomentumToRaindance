# Database setup — start here

**Use the PostgreSQL database and login you already have. You do not need to create another user.** The same login can install the module's database objects and be used by Frends. For this single-login setup, **skip `grant-runtime`**.

The scripts create the `momentum_raindance` schema and its tables **inside an existing database**. They do not install PostgreSQL, create a database, create users, or change passwords. Frends does not run migrations automatically.

## Windows Server: native PowerShell administration

### 1. Get the connection details and scripts

You need the existing server hostname, port, database name, login and password. The database must use UTF-8 and PostgreSQL 15 or newer. Both the administration machine and the Frends agent must be able to reach it.

Install PostgreSQL client tools (`psql.exe`, version 15 or newer) on the administration machine. Windows PowerShell 5.1 and PowerShell 7 are supported; no Bash, WSL, Docker, .NET SDK or local PostgreSQL server is needed.

Copy the complete release's `database` directory to, for example, `C:\MomentumToRaindance\database`. Keep `lib.ps1`, `migrations` and `sql` together. Migration files must retain their original UTF-8 without BOM / LF bytes; retain the repository's `.gitattributes` when using Git.

Your existing login needs permission to connect, create the schema and its objects, and use temporary tables. **Permission to create users (`CREATEROLE`) or databases (`CREATEDB`) is not required.** If your login lacks installation permissions, ask the DBA to grant the necessary rights to that login or install the objects for you with appropriate access. The scripts cannot bypass database permissions.

### 2. Connect using your existing login

Open PowerShell and replace the uppercase placeholders. Adjust the client path to your installed version:

```powershell
Set-Location 'C:\MomentumToRaindance'
$ErrorActionPreference = 'Stop'
$psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

Remove-Item Env:PGSERVICE -ErrorAction SilentlyContinue
$env:PGHOST = 'YOUR_POSTGRES_HOST'
$env:PGPORT = '5432'
$env:PGDATABASE = 'YOUR_EXISTING_DATABASE'
$env:PGSSLMODE = 'verify-full'
# If your server needs a private CA, set its actual certificate path:
# $env:PGSSLROOTCERT = 'C:\Secure\postgres-root-ca.crt'

$login = Get-Credential -UserName 'YOUR_EXISTING_LOGIN' -Message 'Existing PostgreSQL login'
$env:PGUSER = $login.UserName
$env:PGPASSWORD = $login.GetNetworkCredential().Password
```

This prompts for the password instead of putting it in command history. It temporarily exposes the password to child processes through this PowerShell session's environment: do not log the environment, and clear it at step 7. For unattended use, see [password files](#unattended-credentials-and-powershell-policy) below.

Check the connection and installation permissions:

```powershell
$check = @'
SELECT current_database() AS database, current_user AS login,
       current_setting('server_version') AS postgres_version,
       current_setting('server_encoding') AS encoding,
       has_database_privilege(current_user, current_database(), 'CREATE') AS can_create_schema,
       has_database_privilege(current_user, current_database(), 'TEMPORARY') AS can_use_temp_tables;
'@
& $psql -X --no-password --set=ON_ERROR_STOP=1 -c $check
if ($LASTEXITCODE -ne 0) { throw 'Connection check failed. Stop here.' }
```

Expect your database/login, PostgreSQL 15+, UTF8 and `t` for both permissions. These checks cover first-install database permissions; an existing schema may require additional ownership/access. If a check fails, resolve it before continuing. Do not disable certificate validation to work around a TLS error.

### 3. Create the schema and tables

Pause exports. Back up the database before upgrading an existing installation. Run:

```powershell
.\database\migrate.ps1 -PsqlPath $psql
```

**This is the command that installs the database objects.** It creates:

| Object in `momentum_raindance` | Purpose |
| --- | --- |
| `schema_migrations` | Applied migration versions and checksums. |
| `sources` | Registered Momentum datasets and their activation evidence. |
| `invoices` | Permanent invoice identities that must not be emitted again. |
| `deliveries` | Reserved files, fingerprints and delivery confirmations. |

Indexes, functions and protection triggers are installed too. No invoice history is imported and no source is activated yet.

Verify the installation (refresh the schema list in your database client if necessary):

```powershell
& $psql -X --no-password --set=ON_ERROR_STOP=1 -c 'SELECT version, applied_at FROM momentum_raindance.schema_migrations ORDER BY version;'
if ($LASTEXITCODE -ne 0) { throw 'Schema verification failed. Stop here.' }
```

For this release, expect `001_invoice_tracking` and `002_canonical_identities`. Re-running migrations checks existing versions and applies only pending migrations; it does not clear data.

**Do not run `grant-runtime` for your same-login setup.** The account that creates these objects owns them and already has access.

### 4. Register the Momentum dataset

Choose one permanent name, and use exactly the same value later in both Frends tasks:

```powershell
$source = 'momentum-nassjo-production'
.\database\operator.ps1 -Command register-source -Source $source -PsqlPath $psql
```

This name is not a username or database name. It identifies the actual Momentum dataset, is case-sensitive, and must remain stable across process renames or credential changes. A new name starts with no duplicate history; never rename it to bypass tracking. Registration leaves the source **inactive**.

### 5. Establish history, then activate

Choose the case that actually applies:

**A. This dataset has already sent invoices before tracking was installed.** Keep exports paused. Reconcile those deliveries to Momentum's stable `ledgerNote.id` values, then prepare an approved UTF-8 CSV with this exact header:

```csv
ledger_note_id,invoice_number,ledger_note_number,reference
```

Add the actual historical identities and evidence, not placeholder rows. A saved highest `localId` is not a substitute for this baseline. See [historical cutover](#historical-cutover-required-before-activation) for validation rules.

```powershell
.\database\operator.ps1 -Command seed-history -Source $source -CsvFile 'C:\Secure\reconciled invoices.csv' -PsqlPath $psql
.\database\operator.ps1 -Command report -Source $source -PsqlPath $psql
# Only after reviewing the report and approving the complete baseline:
.\database\operator.ps1 -Command activate-source -Source $source -BaselineReference 'YOUR_APPROVED_RECONCILIATION_REFERENCE' -PsqlPath $psql
```

**B. This dataset genuinely has never sent an invoice.** Skip CSV import and explicitly record that fact with your real approval reference:

```powershell
.\database\operator.ps1 -Command activate-source -Source $source -BaselineReference 'YOUR_APPROVAL_REFERENCE: dataset has no previous deliveries' -PsqlPath $psql
```

A newly created tracking database does **not** make an existing Momentum dataset new. For isolated local testing, use a separate test source and clearly label the evidence as local-test-only.

In either case, verify:

```powershell
.\database\operator.ps1 -Command report -Source $source -PsqlPath $psql
```

The source must now have an activation timestamp and the correct baseline evidence/counts. Activation is one-way; do not activate production with missing history.

### 6. Configure Frends with the same database and login

On **Convert GraphQL Result** and **Confirm Invoice Delivery**, fill in the **Invoice tracking database** tab. Fetch does not need a tracking-database connection.

For `ConfigurationSource = Manual`, set `ConnectionString` to the following with your actual values:

```text
Host=YOUR_POSTGRES_HOST;Port=5432;Database=YOUR_EXISTING_DATABASE;Username=YOUR_EXISTING_LOGIN;Password=YOUR_PASSWORD;SSL Mode=VerifyFull
```

Set `SourceSystem` to exactly the value registered at step 4 on both tasks. Use connection-string quoting for passwords containing delimiters; store real credentials only in the protected configuration.

For canonical `HcpVault`, put this object in your database secret and set `VaultPath` to that secret's path instead:

```json
{
  "connectionString": "Host=YOUR_POSTGRES_HOST;Port=5432;Database=YOUR_EXISTING_DATABASE;Username=YOUR_EXISTING_LOGIN;Password=YOUR_PASSWORD;SSL Mode=VerifyFull"
}
```

The `Json` option accepts the same object in `JsonConfiguration`. These are **the existing database credentials**, not a new login. PowerShell environment settings are not automatically transferred to Frends.

The agent must trust the server's certificate. If using a private CA, install trust there or configure Npgsql's `Root Certificate` path to a certificate accessible **on the agent**, not just the administration machine.

Wire the process as [documented in the operations guide](../docs/operations.md): Fetch → Convert → write original bytes once → Confirm → save checkpoint. Run the writer/confirmation only when there is output. Do not advance the checkpoint before successful delivery confirmation.

### 7. Clear the temporary password

When finished with this administration session:

```powershell
Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
Remove-Variable login -ErrorAction SilentlyContinue
```

Setup is complete when the schema exists, the correct source/history is activated, and both tasks use that source and connection. Test against an isolated destination before enabling production exports. The developer test suites below are **not** installation steps.

## Optional: a separate restricted login

A second login is optional hardening, not a runtime requirement. If you only have one login, use it and skip this section. Normal tracking transactions and protection triggers still operate; however, an object owner can alter/drop its own objects and bypass safeguards. Restrict access to that credential accordingly. PostgreSQL explains [ownership and privileges](https://www.postgresql.org/docs/17/ddl-priv.html).

If a DBA has separately supplied a restricted, non-owner login, run this as the object owner, then use that second login in Frends:

```powershell
.\database\operator.ps1 -Command grant-runtime -RuntimeRole 'YOUR_EXISTING_SECOND_LOGIN' -PsqlPath $psql
```

This command does not create a user or set a password. It intentionally rejects administrator/owner roles: applying it to your sole setup login is neither needed nor supported. It adds restricted runtime grants, but does not revoke unrelated pre-existing privileges.

## Unattended credentials and PowerShell policy

For unattended administration, use a protected `PGPASSFILE` instead of the temporary `PGPASSWORD` above. Remove stale `PGPASSWORD` first. A password-file entry is `hostname:port:database:username:password`; escape literal colons and backslashes with a backslash. On Windows, protect the file with NTFS ACLs for authorized accounts. The default location is `%APPDATA%\postgresql\pgpass.conf`. See [PostgreSQL password files](https://www.postgresql.org/docs/17/libpq-pgpass.html).

Alternatively configure an approved `PGSERVICE`. Avoid stale service/connection settings from other environments. Offline tools use libpq settings, not Frends HcpVault resolution, and do not prompt themselves for a password.

Follow your organization's execution/signing policy; reviewed downloaded scripts may need signing or `Unblock-File`. Do not disable machine-wide execution policy.

PowerShell CSV imports accept UTF-8 with or without an initial UTF-8 BOM and preserve CRLF and quoted multiline fields. Only the initial BOM is skipped in memory; the original CSV file is never rewritten. Empty/BOM-only files, UTF-16, invalid UTF-8, NUL characters and standalone `\.` end-of-copy markers are rejected. Even an intentionally empty baseline CSV must contain the required header. Do not pipe `Get-Content` to `psql.exe`: Windows PowerShell's native pipeline encoding can damage non-ASCII text. The supplied scripts use explicit UTF-8 byte streams and keep CSV separate from SQL. PowerShell's [encoding defaults vary by version](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_character_encoding), so explicitly choose UTF-8 when exporting your CSV.

### Reports and delivery reconciliation

```powershell
.\database\operator.ps1 -Command report -Source $source -PsqlPath $psql
```

Only after verifying the original file's durable delivery, record confirmation with its exact fingerprint and evidence. This example uses splatting to avoid multiline quoting/backtick problems:

```powershell
$confirmation = @{
    Command = 'confirm-delivery'
    Source = $source
    DeliveryId = 'delivery-uuid-from-report'
    Filename = 'exact-filename-from-report.txt'
    ContentSha256 = '64-hex-digit-sha256-from-the-verified-file'
    DeliveryReference = 'Verified remote archive/import receipt or incident evidence'
    PsqlPath = $psql
}
.\database\operator.ps1 @confirmation
```

Use `Get-FileHash -LiteralPath 'C:\Secure\verified-original-file.txt' -Algorithm SHA256` on the actual original bytes when reconciling an archived file. A hash match is not, by itself, proof of delivery. See [reconciliation after uncertain delivery](#reconciliation-after-uncertain-file-delivery) before confirming anything. No command clears a reservation or resends a file.

All commands throw a terminating error on failure; a failed `powershell.exe -File ...` or `pwsh -File ...` invocation returns a nonzero exit code. Stop a runbook on errors; do not continue to activation or confirmation after a failed import. `-Verbose` includes successful `psql` notices. CSV import briefly uses a randomly named SQL command file in the administering user's temporary directory; it contains encoded source metadata, not CSV data or passwords, and is deleted on completion. Protect that user's temporary directory and review leftover files after a forced process termination.

## Local development with Docker (macOS/Linux)

From the repository root:

```sh
database/dev.sh up
database/dev.sh migrate
database/dev.sh register-source momentum-development
# Only for an isolated test source known never to have sent a real invoice:
database/dev.sh activate-source momentum-development 'Isolated development dataset; no historical deliveries'
database/dev.sh report momentum-development
```

The dedicated Compose project exposes PostgreSQL only on `127.0.0.1:55432`, database `momentum_tracking`, username `momentum_admin`, password `momentum_dev_only`. These are deliberately public **development-only credentials**, not production defaults. Use only synthetic/redacted invoices locally. The named Docker volume persists when the container stops. `database/dev.sh stop` stops the service without deleting data; there is no reset helper.

`database/dev.sh psql` opens the bundled client. All operator commands are also available through `dev.sh`, so a host `psql` installation is not needed for development. `database/dev.sh status` shows service state. A bind mount exposes only this `database/` directory read-only to the container, not the repository's `.env` or other credentials.

## Shared migration behavior and macOS/Linux installation

1. Use the existing database and login supplied to you. That login needs schema/object installation permissions; a second login is optional. This tool does not create databases, login roles, passwords or HcpVault secrets.
2. Configure `psql` through a protected libpq service/passfile, for example `PGSERVICE=momentum_tracking_admin`. Alternatively set all of `PGHOST`, `PGDATABASE`, `PGUSER`, plus `PGPORT` as needed. Use `PGPASSFILE` with permissions `0600` on macOS/Linux, or protected NTFS ACLs on Windows; do not put passwords in command arguments, shell history or this repository. For a remote production server use certificate validation, e.g. `PGSSLMODE=verify-full` and the appropriate trusted CA configuration.
3. Back up and test restoration before every deployment. Run the checked-out release's migration command as the schema owner:

   ```sh
   database/migrate.sh
   ```

4. Bootstrap and activate each source using the next section before wiring Frends to it. Configure Frends with that same login, or an optional separately provisioned restricted login. Database HcpVault configuration belongs to the module's database option; these offline tools use libpq configuration, not Frends secret-resolution code.

Both `migrate.ps1` and `migrate.sh` ignore local `psqlrc`, do not prompt for passwords, stop on the first SQL error, take the **same** transaction-scoped advisory lock and apply the complete pending migration batch in one transaction. Concurrent runners serialize, including mixed PowerShell/Bash runners. Both use the same SQL files, migration names, and raw-byte SHA-256 history, so switching operating systems does not create a new migration history. PowerShell explicitly rejects BOM/CRLF migration files; restore the original release files instead of normalizing a stored checksum. On any failure, the transaction is rolled back. All mutating offline tools explicitly set `synchronous_commit=on` so a role's asynchronous default cannot weaken their commit acknowledgment. Every previously applied migration must still exist locally with the same SHA-256; missing files or checksum changes fail without applying anything. Keep applied files immutable; add a new numbered migration for future changes. Execute migrations during a coordinated deployment window: the migration advisory lock coordinates migration runners, not in-flight application requests. Do not change the schema while invoice conversions are in progress.

The optional runtime grant script requires an existing role and rejects administrative roles/database or schema owners and their members. It grants schema usage; reads of the four tables; inserts of only runtime invoice/delivery columns; execution of the immutable identity-validation function; and updates of delivery status, time and evidence. It does not grant insertion of historical evidence or database-managed timestamps. `SELECT ... FOR UPDATE` requires `UPDATE` on at least one column, so the source grants permit `UPDATE(created_at)` solely to acquire row locks. A trigger rejects actual changes to that column. There is no source insert/activation grant, no invoice update/delete, no truncate and no schema creation privilege. Use a new dedicated role with no other inherited grants; the script does not audit every unrelated membership or revoke unrelated pre-existing privileges. Table triggers also reject removing or rewriting identities and moving a delivery back to `reserved`.

## Historical cutover: required before activation

The new database cannot discover which invoices were sent before it existed. A remembered highest local ID is **not** historical proof. Keep the production export paused until a responsible operator has reconciled all prior deliveries against stable Momentum ledger-note IDs.

```sh
database/operator.sh register-source momentum-nassjo-production
database/operator.sh seed-history momentum-nassjo-production /secure/reconciled-invoices.csv
database/operator.sh report momentum-nassjo-production
database/operator.sh activate-source momentum-nassjo-production 'Approved reconciliation ticket/change record'
```

CSV is UTF-8 and must have exactly these columns in this order (the PostgreSQL importer verifies the header):

```csv
ledger_note_id,invoice_number,ledger_note_number,reference
stable-id-from-momentum,display-invoice-number,display-ledger-number,Archived delivery or reconciliation evidence
```

`history.example.csv` contains **placeholders only**. Replace them with approved identities; do not import example rows as real history. Display numbers may be empty. Stable IDs and evidence references must be nonblank; source names and stable IDs must have no surrounding Unicode whitespace or embedded control characters. Duplicate IDs within a single CSV fail the entire import. Re-importing a CSV before activation is idempotent: already-known identities remain unchanged, including their original evidence. Each command locks the source using the same source-first ordering as the runtime. Imports are only allowed while inactive. Activation is one-way and requires a nonblank baseline reference; repeating the exact activation reference is a no-op. A legitimately new dataset may have no historical rows, but the operator must explicitly document that fact in the activation reference.

Do not guess stable IDs from invoice numbers or invent a cutoff to get past this gate. If historic deliveries cannot be mapped reliably, that is a production cutover blocker requiring reconciliation, not permission to activate an empty baseline. The software validates structure and records the operator's assertion; it cannot verify that their evidence is truthful or complete.

## Reconciliation after uncertain file delivery

Tracking uses `(source_system, ledger_note_id)`, where the ID is Momentum's stable `ledgerNote.id`, not `localId` or an invoice display number. Historical and reserved identities both suppress future generation. A `reserved` delivery is possibly already sent; `delivered` records delivery evidence, not necessarily successful Raindance import.

PostgreSQL and the external writer cannot commit atomically. Never automatically release a reservation or replay a writer whose outcome is uncertain. These tools provide no delete, reset, expiry, requeue or file-regeneration command. Replaying cached task bytes or manually copying an old file bypasses generation-time tracking.

Keep permanent, protected backups and test restoration. After restoring an older database snapshot, stop exports and reconcile all deliveries since the recovery point before resuming: otherwise previously sent identities may have been lost.

```sh
database/operator.sh report momentum-nassjo-production
```

Reports include historical/reserved/delivered counts plus each delivery's ID, filename, SHA-256, size and evidence. For a `reserved` delivery, first inspect the remote destination, archive and/or Raindance import evidence using the recorded filename. Verify the **actual raw file's** SHA-256, not a text re-encoding or JSON/base64 representation. Account for files already consumed or moved by the receiving system: absence at the original path is not proof of nondelivery.

Only when there is positive evidence of successful delivery can an operator confirm the existing reservation:

```sh
database/operator.sh confirm-delivery momentum-nassjo-production \
  'delivery-uuid-from-report' 'exact-filename-from-report.txt' \
  '64-hex-digit-sha256-from-the-verified-file' 'Remote archive/import receipt or incident evidence'
```

This checks the source, delivery UUID, filename and SHA-256, then atomically records delivery evidence. An exact repeat is idempotent; conflicting evidence is rejected instead of overwriting the audit record. Confirmation never generates bytes or sends a file. The tool cannot inspect Raindance or validate an evidence reference's truth itself. If delivery cannot be proven, leave it reserved and investigate; do not clear the row. If nondelivery is conclusively established and a recovery transmission is needed, that requires a separately reviewed operational recovery procedure. It is deliberately not automated here.

## Development verification

These are developer/CI tests, **not first-time installation requirements**. Their scratch database/role creation needs extra permissions that ordinary setup in an existing database does not need. Never run them against production or an inspection database whose data you want to keep unchanged.

### Native Windows

Use a **disposable local PostgreSQL** server with an administrator login capable of creating a scratch database and role. Do not use production. Set `PGHOST` to `127.0.0.1`, `PGDATABASE` and `PGUSER` explicitly, configure its test credentials, and do not set `PGSERVICE`. Then run either shell:

```powershell
.\database\tests\windows.ps1 -PsqlPath $psql -AllowCreateTestDatabase
```

The test script creates and removes only its uniquely named scratch database and role, preserving synthetic logs in the user's temporary directory. It checks atomic rollback, concurrent first installation, repeat migrations, raw-byte checksums, drift/missing/out-of-order/CRLF rejection, all operator commands, CSV validation and encoding, Swedish text/quoting, idempotency, evidence validation, and runtime privileges. No Pester, Bash or WSL is required. The `Windows database administration` CI workflow runs the suite against native PostgreSQL on Windows in a `powershell` (5.1) / `pwsh` (7) matrix. Check its actual result before release; a local PowerShell 7 run on Linux is not Windows PowerShell 5.1 validation.

### macOS/Linux Docker helpers

With the development container running and migrated:

```sh
database/tests/migration-runner.sh
database/tests/operator.sh
```

Migration tests create and remove only a uniquely named scratch database, testing first-install concurrency, atomic rollback, idempotency, checksum drift, missing files and out-of-order migrations. Operator tests preserve synthetic rows in a unique `operator-test-*` source, covering CSV validation, idempotent imports, activation gates and evidence checks. Both leave test logs in a temporary directory.

To exercise grants and row-lock privileges, create this **development-only** login once, then apply the same grant script used in production:

```sh
database/dev.sh psql -c "CREATE ROLE momentum_tracking_runtime LOGIN PASSWORD 'momentum_runtime_dev_only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;"
database/dev.sh grant-runtime momentum_tracking_runtime
database/dev.sh psql --file=/workspace/database/tests/runtime-privileges.sql
```

The SQL test checks reservation/confirmation under that restricted role and rejects source creation/activation, historical forgery, identity mutation, deletion, truncation and DDL. Its synthetic rows are rolled back. This runtime login also permits testing the real library against the local server without using its administrator account.

## Implementation references

The tooling follows PostgreSQL's documented [psql error handling and CSV input behavior](https://www.postgresql.org/docs/17/app-psql.html), [transaction-scoped advisory locks](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS), and [row-lock privilege requirements](https://www.postgresql.org/docs/17/sql-select.html). Historical CSV is read from `pstdin` while commands come from a fixed script, so CSV contents cannot become SQL or psql commands. Input containing a standalone `\.` line is rejected to prevent silent truncation by the client's COPY end marker.
