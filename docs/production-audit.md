# Production audit — PostgreSQL tracking, 2026-09-09

This records the 2.x production-safety design and verification scope. It is not a claim that historical invoices have been reconciled, the package is deployed, or the real Raindance importer has accepted it. The previous checkpoint-only design was insufficient: Raindance may create another invoice if an old file is sent again.

## Changes and safety boundaries

| Area | Current design |
| --- | --- |
| Invoice identity | Permanent PostgreSQL primary key `(source_system, ledger_note_id)`, using stable Momentum `ledgerNote.id`; never a fallback to sync `localId`, invoice text or amount. |
| Admission | Complete batch validation before mutation; source-row transaction lock; new invoice and delivery records committed together before file bytes are returned. Concurrent converters cannot both reserve the same identity. |
| Uncertain delivery | Any outstanding `reserved` delivery blocks the complete source, including no-op calls. No timeout expiry, release, delete, reset or automatic resend. |
| Confirmation | Separate Confirm Invoice Delivery task after the external writer succeeds. Checks UUID, filename, file hash and evidence; identical confirmation can repeat without resending. `delivered` is file-delivery evidence, not Raindance import confirmation. |
| Historical baseline | Offline source registration, CSV seeding and activation with evidence. Unknown/inactive sources fail. Historical rows suppress identity only because their exact old rendered hashes are unavailable. |
| Change detection | Previously recorded invoice identity with different per-invoice rendered bytes fails without reserving other invoices in the batch. Unused raw JSON changes are outside this byte hash. |
| Checkpoint | Feed optimization only. Fetch preserves it. Convert skips stale nodes; confirmed/historical duplicates can advance without a file, whereas entirely empty/rounding-only work preserves it. New output is acknowledged only after delivery confirmation. |
| Credentials | Required database property tab supports Manual, JSON and canonical HcpVault using the existing Infisical resolver. Secret inputs masked; verified TLS for remote PostgreSQL; sensitive diagnostics disabled. |
| Database durability | Ambient transaction enlistment disabled and synchronous commit requested. Append-only migrations/checksums, offline schema installation, optional restricted runtime grants, permanent history guards. A single existing login is supported; object owners can alter safeguards. DBA/server durability and restore procedures remain operational responsibilities. |
| Windows administration | Native PowerShell 5.1/7 entry points for migrations and every operator command, using `psql.exe` without Bash/WSL. Shared SQL/checksums, explicit UTF-8 data transport, and LF migration checkout rules. |
| Frends contract | Three discoverable asynchronous tasks. Fetch JSON remains string; conversion Filename remains string and ResultFile remains Latin-1 `byte[]`. Convert's required database parameter is a breaking 2.x workflow change. RemoteFS is unchanged. |
| Output format | Fixed-width Latin-1/CRLF, blank H invoice number, separate period-only R rows, 54-character priced R text, revenue grouping and monetary/encoding validation retained. |
| Local tool | Fetch/raw JSON dump and internal untracked rendering remain available only for diagnostic previews. CLI warnings explicitly prohibit delivery or production-checkpoint updates from a preview. |
| Build/release | .NET 8 library/tests, .NET 10 local tool, Npgsql 8.0.9. CI provisions PostgreSQL 17 and migrates twice before testing and packaging. No Frends-time DDL. |

## Verification scope

Local release validation on 2026-09-09 completed a **2.0.0 Release build with zero warnings/errors** and **171 tests passed, zero failed/skipped**, running on **.NET 8.0.30** with real PostgreSQL 17 and schema `002_canonical_identities`. Tracking operations used the restricted runtime role. A production-library advisory check, including Npgsql and transitive packages, reported no known vulnerabilities from the configured NuGet.org source at that time; this is not a guarantee against undisclosed vulnerabilities.

Offline tooling verification passed migration atomic rollback, concurrent first installation, repeat no-op, checksum drift, missing-file and out-of-order rejection, historical CSV validation/import, source activation and evidence-based reconciliation. Runtime permission checks exercised successful reserve/confirm operations while rejecting DDL, source activation, forged historical rows, identity mutation, deletion, truncation and reverting delivery status. Uniquely created scratch databases were removed; permanent synthetic development history was retained.

The PowerShell administration suite also passed **56 assertions** on PowerShell 7.4.6/Linux, using native `psql` 15.19 against the local PostgreSQL 17 instance. Coverage includes the migration/operator scenarios above, CRLF migration rejection, UTF-8/BOM and invalid-encoding CSV handling, empty/BOM-only import rejection, Swedish text/quoting, native paths containing spaces, and console-encoding preservation. PowerShell and Bash both replayed the existing Bash-applied development migration history as a no-op. The Bash operator suite also passed after adding its empty-file safeguard. A separate native Windows 2025 CI matrix runs the suite under Windows PowerShell 5.1 and PowerShell 7; those hosted Windows results have **not** been executed locally and must be checked before release.

The local `2.0.0` NuGet and source packages were built and inspected: three task entries, .NET 8 DLL/XML, current package readme and Npgsql dependency present; no credentials, customer dumps, CLI/test binaries or database-admin scripts included. Workflow/Compose YAML parsed, shell syntax checks passed, and CLI help displayed the diagnostic-only warning. Publishing and hosted CI were not executed.

The repository's tracking tests cover public Frends signatures/property tabs and result types; masked database configuration and canonical HcpVault; strict source IDs and TLS; hidden credential details; and fail-closed behavior when the database is unavailable. Existing format, checkpoint-binding, serialization and HTTP tests continue to cover the conversion/fetch foundation.

Real PostgreSQL integration scenarios exercise reservation-before-return, confirmed replay under a different sync ID, concurrency, blocking of unconfirmed deliveries, historical suppression, mixed old/new batches, rollback on invalid later invoices, changed-content rejection, missing/duplicate stable IDs, source activation, confirmation fingerprint/evidence mismatches and cancellation. These tests require `MOMENTUM_TEST_POSTGRES`; an omitted connection skips them. A green run with skipped database tests is not evidence for durable duplicate protection. See [the reproducible test commands](../README.md#build-test-and-package) and [database verification](../database/README.md).

Retain the actual command results for each release's warnings-as-errors build, unit and real PostgreSQL tests on .NET 8, migration replay/checksum checks, runtime-role permissions, and NuGet inspection. Inspect for the three method entries, task DLL/XML, readme, and declared dependencies; credentials, local dumps, CLI binaries and database administration credentials must not appear in the package. Rerun these checks for later changes rather than inheriting the totals above.

This test scope does not exercise the hosted Frends designer, real HcpVault credentials, remote file delivery, database-loss failover, a production historical baseline, or the receiving Raindance importer. Local checks and CI configuration do not prove those external outcomes.

## Production acceptance gates

1. **Historical coverage:** reconcile every invoice already sent before tracking, using stable Momentum IDs and trustworthy delivery/import evidence; seed and review the baseline before activation. Blank H invoice numbers mean old Raindance files alone are insufficient. Do not treat an unrecoverable baseline as completed.
2. **Identity and feed contract:** verify `ledgerNote.id` uniqueness/stability within the permanent source scope; confirm sync batching, monotonicity, retention, paging and creation-event semantics. A single query cannot detect omitted earlier records.
3. **Database operations:** deploy compatible offline migrations, the existing login with appropriate permissions (or an optional separate restricted login), verified TLS, separate Vault database credentials, permanent retention, backups and a tested restore/reconciliation procedure.
4. **Frends workflow:** upgrade and rebind the 2.x asynchronous Convert/database input; use one RAW writer with overwrite off, then Confirm, then checkpoint storage. Disable cached-writer replay and automatic writer retry. Validate all three task definitions and refresh stale designer steps when necessary.
5. **Failure drills:** test concurrent calls, duplicate identities, unresolved reservations, database outages, commit uncertainty, writer/confirmation/cursor-store failure, and operator reconciliation. Never unblock by resetting identities or falsely confirming delivery.
6. **Raindance acceptance:** verify ordinary/credit/VAT invoices, split coding, customer classes, Latin-1 Swedish text, one-/multi-month printed rental periods, K periodisation, and blank H defaults against the actual importer setup.

The module prevents duplicate **admission through its tracked conversion** under the documented identity/history rules; it cannot prevent an operator or downstream component from sending already-returned bytes twice. Database history lost in a restore must be reconciled before delivery resumes. There is no distributed exactly-once transaction with RemoteFS or Raindance.

See [operations and recovery](operations.md) for the full response to ambiguous delivery. The [format documentation](raindance-format.md) also records unsupported event/VAT/customer cases and the lack of whole-invoice `toPay`, VAT, or identity-checksum validation.
