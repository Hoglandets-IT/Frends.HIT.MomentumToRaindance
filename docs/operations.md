# Production operations and recovery

## Responsibility boundary

PostgreSQL owns permanent invoice identities, fetch receipts, and committed checkpoint storage. The surrounding Frends process owns scheduling, the remote file writer, delivery evidence, monitoring, and operator recovery. Fetch and Convert are still separate; Confirm Invoice Delivery runs after the external writer succeeds. Neither conversion nor confirmation calls Raindance to verify import.

The invariant is **at most one reservation may expose bytes for each `(SourceSystem, ledgerNote.id)`** through this workflow while its history remains intact. `SourceSystem` is a permanent, case-sensitive identifier for the real Momentum dataset/environment, not a process name, run ID, or rotating deployment name. Every exporter of the same invoices must use the same database and source identifier. Do not point a production writer at a development tracking database.

`localId` describes progress through the sync feed, not invoice identity. A different sync local ID, reset checkpoint, or re-fetched payload does not bypass recorded invoice identities. Invoice numbers and node IDs are retained as audit context, but are not deduplication keys. A credit with its own stable `ledgerNote.id` is a separate invoice; corrections must not reuse an existing identity to create another Raindance invoice.

## First production cutover: historical invoices are mandatory context

Stop scheduled and manual exports while establishing the baseline. The database cannot infer what older releases already sent.

1. Back up existing checkpoints, retained Momentum JSON, delivery logs, and available Raindance import evidence.
2. Select the permanent `SourceSystem` and confirm that `ledgerNote.id` is stable and unique across that dataset. All processes exporting it must use that exact value.
3. Use your existing supported PostgreSQL database and login, with backups and verified TLS. Follow the **offline** [first-time setup](../database/README.md); the login needs schema/object creation permissions, not permission to create users. Later migrations require ownership/access to installed objects. Never run migrations as part of a Frends execution.
4. Register the source, leaving it inactive. Collect and reconcile every invoice already delivered before tracking. Seed their stable `ledgerNote.id` values using the historical CSV import, with trustworthy evidence references. An empty baseline is valid only for a genuinely unused source, documented as such.
5. Review the history and outstanding-delivery report. Activate the source with an immutable baseline reference. Activation is an operator attestation that reconciliation is complete, not an automatic proof of its completeness.
6. Configure the same existing database login and source on Fetch, Convert, and Confirm, through HcpVault or another supported configuration source. No second user is required. Skip `grant-runtime` for a single-login setup; use it only if a separate restricted login has been supplied.
7. Upgrade/rebind the three task references, complete the process wiring below, and validate using an isolated test source/destination before production runs resume.

The import CSV header and commands are in [database/README.md](../database/README.md). Never import the example CSV unchanged. Historical records permanently suppress the imported identity; they have no historical byte hash and do not prove that today's source contents match the old delivery.

All offline steps are available natively on Windows Server through [PowerShell 5.1/7 and `psql.exe`](../database/README.md#windows-server-native-powershell-administration), without WSL or Bash. Use `database/migrate.ps1` for migrations and `database/operator.ps1 -Command ...` for grants, registration, CSV import, activation, reporting and evidence-based confirmation. Windows and Bash tools share the same immutable SQL and checksum history. Preserve the release's original LF migration bytes; do not resave them as CRLF.

Old Raindance files cannot provide a complete stable-ID baseline on their own: H `Fakturanummer` is intentionally blank and the Momentum `ledgerNote.id` is not output. Invoice/customer descriptions or amounts are not safe substitute keys. If source IDs cannot be recovered from retained raw data or reconciled delivery/source records, historical duplicate protection remains unproven. Do not activate an existing production source by pretending it has no history.

## Reservation and delivery lifecycle

| State | Meaning | Allowed next action |
| --- | --- | --- |
| Invoice absent | Identity has not been admitted in this database/source. | Validate and reserve through Convert. |
| `reserved` delivery | Identity and byte fingerprint committed; bytes may already have reached the caller or destination. This is not proof of delivery. | Write once during the original successful execution; then confirm with evidence. After a failure, reconcile first. |
| `delivered` delivery | Evidence of successful file delivery recorded. Not proof of Raindance import. | No re-export; safe repeat confirmation with identical evidence. |
| Historical invoice | Identity was reconciled and seeded before source activation. | Suppress re-export; retain baseline evidence permanently. |

Fetch reads the committed database cursor under a short source lock, checks for unresolved reservations, and traverses every API page outside the database transaction. A second source lock verifies that progress is unchanged before recording the fetch receipt. That receipt contains the source, starting and highest local IDs, node count, and SHA-256 of the exact UTF-8 JSON; it contains no raw customer payload and does not acknowledge delivery.

Convert checks the receipt, exact JSON, and current cursor, then validates/renders the complete batch. Under the source lock it rejects any outstanding `reserved` delivery and compares candidate identities and per-invoice byte hashes with history. New identities and the delivery record, including its receipt link, commit together before `ResultFile` can be returned. The committed cursor stays unchanged until delivery confirmation. A fully validated batch containing only historical/delivered identities or intentional no-ops can advance the cursor atomically without a file. SQL uses parameters, and database uniqueness constraints back the identity and filename rules; see [Npgsql parameters and transactions](https://www.npgsql.org/doc/basic-usage.html) and [PostgreSQL constraints](https://www.postgresql.org/docs/current/ddl-constraints.html).

An outstanding reservation blocks **all** subsequent fetching and conversion for that source, including empty/no-op calls. A crash after reservation must not be mistaken for an empty successful batch and advance state. A timeout or network failure during commit can leave a committed reservation without a returned result; inspect the database before trying another conversion. Ambient Frends transactions cannot roll the reservation back, and state-changing transactions use synchronous commit.

There is deliberately no reservation expiry, automatic release, delete, reset, or requeue API. Availability is sacrificed when delivery is uncertain so the module does not create another copy. DB triggers guard permanent identities and the one-way `reserved` → `delivered` transition; owner/superuser changes and data loss can still defeat those safeguards. Keep administrative access restricted.

### Exact Frends wiring

1. Configure API settings and the invoice-tracking database/source on Fetch. Fetch reads the cursor from PostgreSQL; remove the KV read step and old `LastLocalId` binding.
2. Pass Fetch's exact `ResultFile` string to Convert's `GraphQlResult` and its `FetchId` to Convert's `FetchId`. Configure the same database/source on Convert. The JSON may be inspected but must not be changed or reformatted.
3. If conversion `NodeCount == 0`, skip the writer and confirmation. The validated cursor is already committed in PostgreSQL.
4. Otherwise, write **once**, using `Filename` for File, `ResultFile` for Byte content, RAW encoding, and overwrite **off**. Preserve all bytes, trailing spaces, and CRLF.
5. Only after successful, durable file delivery, call Confirm Invoice Delivery with conversion `DeliveryId`, `Filename`, `ContentSha256`, and a durable `DeliveryReference`. Use the same database/source. Record a stable evidence string such as the original Frends execution ID plus destination path; do not generate a different string on retry.
6. Finish the process after confirmation. Confirmation has committed delivery status and progress together. Remove the KV save step; returned `LastLocalId` values are informational.

Disable automatic writer retries and process resume that replays cached file bytes. Serialize scheduled runs for predictability even though the database protects admission against concurrent conversions. Configure the transfer/import handoff so partially written files cannot be consumed. Overwrite off helps avoid clobbering but is not sufficient duplicate protection: Raindance may already have consumed and removed the original file.

The database stores the committed source cursor, fetch receipt, delivery UUID, filename, exact complete-file SHA-256, byte count, invoice count, proposed checkpoint, status, timestamps, and delivery evidence. Each invoice stores its stable ID, first sync local ID, source node/invoice/ledger-note numbers where available, per-invoice output hash, and delivery link or historical reference. It does **not** store the output file or full source JSON. Preserve any required delivery evidence under appropriate access and retention controls.

Confirmation validates stored source, UUID, filename, hash, and evidence but does not fetch the remote file. For a receipt-backed delivery, the delivery transition and cursor advancement are one transaction: a failure rolls back both. A matching repeat confirmation preserves evidence and cannot rewind a later cursor; conflicting confirmation evidence fails. Successful confirmation means file delivery only. If Raindance later rejects the file, reconcile that rejection rather than re-exporting existing invoice identities automatically.

## Checkpoint contract

PostgreSQL `sources.last_local_id` is authoritative. Neither Fetch nor Convert accepts an external checkpoint. Fetch holds that lower bound constant across every page, requires positive strictly increasing local IDs and complete counts, and records the exact response in a receipt by hash. Convert checks that the receipt starts at the still-current database cursor. Another run's advancement makes an older receipt stale; retry from Fetch instead of changing its JSON or cursor.

All invoice content is validated, including recorded identities, before progress can advance. Gaps are permitted, but Momentum must guarantee that a complete unfiltered feed does not omit earlier work. Example with database cursor `120`, an activated source and no outstanding reservation:

| Complete response / outcome | Conversion output | Database progress |
| --- | --- | --- |
| Empty `nodes` | Empty bytes, zero invoices, empty filename/ID/hash | Stays `120`; no writer or confirmation. |
| New valid identities at `122`, `124` | Two reserved invoices | Stays `120`; Confirm commits `124` after writing. |
| New nodes `121`, `122`, both empty or rounding-only | No reservation or output | Convert commits `122`; no writer or confirmation. |
| New identity `121`, intentional no-op `122` | One reserved invoice | Stays `120`; Confirm commits `122` after writing. |
| Delivered identity reappears at `125`, same output | No output; `SkippedInvoiceCount == 1` | Convert commits `125`; no writer or confirmation. |
| Historical identity at `125`, new identity at `126` | Only new identity reserved; one skipped | Stays `120`; Confirm commits `126` after writing. |
| Valid invoice `121`, malformed invoice `122` | Exception, no partial reservation or result | Retain `120`. |
| Previously delivered identity with changed output | Exception; no new identities reserved | Retain `120`; reconcile source change. |
| Partial page, changed JSON, wrong-source receipt, or stale receipt | Exception; no reservation or cursor advancement from this attempt | Retain current database cursor; restart Fetch after resolving the cause. |
| Any input while a reservation is unresolved | Exception, no returned bytes | Retain database cursor; reconcile delivery. |

Intentional no-ops advance progress after complete validation, avoiding repeated scanning. A Frends backend restart, lost task result, or empty KV store does not alter PostgreSQL. If a no-output conversion committed before its result was lost, the next Fetch uses the advanced database cursor. If a reservation committed before its result was lost, the source remains blocked for reconciliation. Permanent invoice identity history must remain available regardless of the cursor value.

### Inspecting the fetched JSON

Pass the exact `ResultFile` with its matching `FetchId`. The receipt hash deliberately rejects filtering, merging, reformatting, and field edits between Fetch and Convert: otherwise an omitted earlier invoice could be acknowledged by a later cursor. Make source corrections in Momentum, then fetch again. Use separate diagnostic copies for reports; they are not production conversion inputs. Never synthesize a new invoice identity to bypass a recorded one.

Byte hashes cover the rendered S/H/R/K records for one invoice, not its entire raw JSON. Changes to unused source fields do not change the hash. Changes to relevant mapped fields, row order, rendering rules, or output encoding can change it. A recorded identity with a changed hash is rejected; a mapping upgrade must not silently produce a replacement invoice for the same ID.

## Failure handling and recovery

Tasks throw on failure instead of returning `Success = false`. A successful no-output result still has `Success = true`. Branch on task completion and `NodeCount`, never parse `Info`.

| Situation | Safe response |
| --- | --- |
| Fetch authentication, HTTPS, timeout, or GraphQL failure | Fix credentials/TLS/service conditions and retry fetching as appropriate. Fetch reserves nothing. Do not skip source data. |
| Validation failure before reservation | Fix source data and fetch again. No partial batch is admitted. Do not advance the cursor to bypass the failing invoice. |
| Result JSON modified, receipt missing/wrong-source, or cursor advanced concurrently | Restart from Fetch using the same database/source. Do not edit or synthesize a receipt. |
| Database unavailable, wrong schema, unknown/inactive source | Fail closed. Check configuration, offline migrations, grants, and source activation. Never fall back to untracked conversion. |
| Convert fails ambiguously during database commit | Run the offline `report` command (`database/operator.ps1 -Command report -Source SOURCE` on Windows, or `database/operator.sh report SOURCE` on macOS/Linux). If a reservation exists, stop automatic execution and reconcile; absence of a returned result does not prove absence of a commit. |
| Writer fails, times out, or process stops after Convert | Treat the reservation as potentially delivered. Do not rerun the writer, reset the cursor, or delete/release the reservation. Check the original execution, destination/archive and importer evidence. |
| Delivery is verified, confirmation missing | Retry only confirmation with the original UUID, filename, hash and evidence reference, or use the audited offline confirmation command. Never write again. |
| Confirmation times out or its result is lost | Delivery and cursor commit together or both roll back. Inspect state and retry only confirmation with the original evidence if needed. A confirmed delivery is skipped on the next run. Never replay the writer. |
| Frends KV storage is cleared or the backend restarts | Fetch reads PostgreSQL progress. No external cursor repair or manual initialization is needed. Unresolved reservations still block rather than resending. |
| No reliable evidence whether the file was delivered | Leave the source blocked. Escalate for operator/importer reconciliation; this package offers no automatic resend or reservation reset. |
| Delivery is proven not to have happened | The source still blocks. A reviewed recovery procedure outside this module is required; do not falsely confirm delivery or delete its identity merely to resume. |
| New data has a previously recorded identity but different content | Investigate the correction and agreed business process. Do not change `SourceSystem` or `ledgerNote.id` to force a second invoice. |
| Correct types but old Frends compilation error after upgrade | Refresh/rebind task references and validate. Recreating a stale File Writer step resolved the previously observed cached-definition issue; RemoteFS does not need patching. |

Run administrative reporting/confirmation from the [offline tooling](../database/README.md), not through runtime schema privileges. Confirmation after investigation must reference actual durable evidence. Do not mark a reservation delivered merely because blocking is inconvenient; that falsely acknowledges invoices which may never have arrived.

There is no distributed exactly-once guarantee across PostgreSQL, RemoteFS, and Raindance. A Frends operator can replay cached `ResultFile`, a remote copy can be sent twice, an administrator can remove safeguards, or a database restore can lose recent reservations. Those paths lie outside conversion's admission check. Prevent them operationally with restricted permissions, no automatic writer replay, durable handoff evidence, and reconciled recovery.

## History retention, backup, and restore

Invoice identities, source names, fetch receipts, reservations and confirmations have **no TTL**. Never prune, truncate, rename sources, or delete tracking history to reduce table size or clear a failure. Restricted operational payload logs may have a shorter retention period, but enough evidence to reconcile uncertain delivery must remain accessible. Do not confuse payload-log retention with permanent duplicate-protection history.

Back up the tracking database, including migration/source/fetch-receipt/delivery/invoice tables and constraints, and test recovery. The server's durability, storage, backup and replication settings are part of the safety boundary. Synchronous local commit alone does not establish lossless failover to another server.

After a restore, failover with possible transaction loss, or accidental history alteration: **disable all writers first**. Reconcile every potentially delivered invoice since the recovery point against external delivery/import evidence before resuming. A valid old backup can still lack invoices already sent after that backup. Restoring a checkpoint does not repair missing identity history, and increasing it merely conceals the hole. Do not activate a new source name as a workaround. Keep database restore and reconciliation approval records.

## Production release gates

- Confirm Momentum stable invoice-ID scope and sync batching, paging/retention, monotonicity, and creation-event semantics. Fetch traverses all pages and rejects inconsistent results; those checks do not establish vendor snapshot/retention guarantees.
- Complete historical baseline reconciliation and source activation, verify the chosen login's access and HcpVault database-secret access (document the ownership tradeoff if using one login), and test backups/restore reconciliation.
- Install the matching schema-004 workflow on a .NET 8-compatible Frends agent. Verify all three discoverable task methods, database tabs, receipt bindings, result types, cancellation, atomic confirmation/progress, removed KV steps, and disabled writer replay.
- Exercise concurrent fetch/conversion, stale or modified receipts, multi-page failures, duplicate identities with changed local IDs, confirmed replay, mixed old/new invoices, outstanding reservations, database outages, cancellation, confirmation retry, and process restart using an isolated test source.
- Import approved ordinary/credit/VAT examples into Raindance test, with split accounting, private/organisation customers, one-/multi-month periods and Swedish text. Confirm totals, customer matching, printed periods, K periods, and blank H-field defaults.
- Confirm the [format's source assumptions](raindance-format.md): only supported customer classes, null/standard VAT, created changes, rounding exclusion and invoice-wide period source. The converter does not reconcile full invoice `toPay`, VAT totals or identity checksums.
- Verify the NuGet dependencies and metadata, run unit and real PostgreSQL tests on .NET 8, and inspect package contents. A unit-only run which skips database tests is not sufficient.

Automated tests cannot establish the historical baseline or prove acceptance by a production Frends/importer configuration. Keep those deployment checks explicit and record their evidence.

## Security and maintenance

Invoice JSON, files, database audit metadata and delivery references may contain personal or financial data. Restrict Frends result access, storage, database grants and diagnostics. UI masking and `.gitignore` are not access controls. Database secrets are resolved through the same HcpVault backend as API secrets, but should use separate paths/permissions. Production PostgreSQL requires verified TLS; do not weaken certificate checks.

Migration files are append-only, checksum-tracked and run offline. A changed applied migration or unsupported schema version fails closed. Ship the task and compatible schema as one planned release; never execute schema changes from the Frends task. The reference modules in `derp/` are context only and are not modified or shipped dependencies.

The current task requires `004_database_checkpoint`. Follow the [coordinated upgrade steps](../database/README.md#upgrading-to-database-owned-progress): pause execution, migrate with the existing installation login, rerun grants only for a separate restricted login, then deploy the matching task and receipt bindings. The new source cursor starts at zero because old delivery maxima do not prove a complete feed scan; known identities remain suppressed during replay. Do not reseed or reactivate an existing source. Migration 003's source dates remain nullable first-reservation snapshots, not current-state metadata or delivery evidence.

Outstanding legacy deliveries remain blocking. An operator can confirm one with the existing evidence-based tool, but a legacy delivery without a fetch receipt cannot advance the new database cursor. The subsequent complete scan verifies history and establishes progress without resending those identities.

CI provisions disposable PostgreSQL 17 and applies migrations twice before warnings-as-errors builds and tests. Main publishing follows validation, and published-release builds also run the database-backed suite. A separate Windows workflow verifies native administration with both Windows PowerShell 5.1 and PowerShell 7; check that result as a release gate too. Verify actual CI results and package discovery separately; local execution does not prove publishing or deployment. See [the audit record](production-audit.md) for current evidence and limitations.
