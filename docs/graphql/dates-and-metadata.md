# Dates and privacy-conscious invoice metadata

For “when was this AVI/invoice note created?”, use **`ledgerNote.statusCreated`**. Neither its rental period nor its due date answers that question. The accounting event also has its own creation and approval timestamps, which can differ from the original note's creation.

All source paths below start at `data.ledgerNoteAccountingsSync.nodes[]`. The exact declarations come from the captured live schema; see the [ledger/invoice reference](reference-ledger-invoice.md) and [transaction/scalar reference](reference-lifecycle.md).

## Which date means what?

| Source path | Schema type | Meaning | Added to the tracking snapshot? |
| --- | --- | --- | --- |
| `ledgerNote.statusCreated` | `DateTime` | When the ledger note was first created. This is the creation timestamp used for AVI/note investigations. | Yes: `source_created_at`. |
| `transactionCreated.change.statusCreated` | `DateTime` | When the change associated with the accounting event's originating transaction was created. Not the original note creation timestamp. | Yes: `accounting_event_created_at`. |
| `transactionCreated.statusApproved` | `DateTime` | When the accounting event's originating transaction was approved. | Yes: `accounting_approved_at`. |
| `ledgerNote.due` | `Date` | Effective due date: deferral date if present, otherwise new due date if present, otherwise original due date. | Yes: `due_date`. |
| `ledgerNote.invoice.billed` | `Date` | Billing date of the optional related Faktura invoice. A missing invoice relationship means this is unavailable. | Yes: `invoice_billed_date`. |
| `ledgerNote.dueOriginal` | `Date` | Original due date when the note was first created. | No; available for a separate diagnostic query. |
| `ledgerNote.dueNew` | `Date` | Changed due date, if one has been set. | No. |
| `ledgerNote.dueDeferral` | `Date` | Deferred due date, if one has been set. | No. |
| `ledgerNote.fullyPaid` | `Date` | Date of final payment. | No; payment state can change after reservation. |
| `ledgers[].ledger.statusStart` / `statusEnd` | `Date` | Start/end of the ledger's effective period. | No. |
| `ledgers[].rows[].records[].accountingDate` | `Date` | Accounting date of that individual posting. A note may have multiple postings. | No additional tracking column. |

All of these date/timestamp fields are nullable in the schema. Nested relationships can also be absent: the accounting node's `transactionCreated` is `Transaction!`, but its `change` is nullable, and `ledgerNote.invoice` is nullable. A transaction has no direct `statusCreated` field; the timestamp is reached through `change`.

The `status` prefix does not establish a field's type. On **LedgerNote**, `statusDraft`, `statusRemoved`, and `statusReplaced` are boolean flags, not dates. On **Invoice**, `statusRemoved` is a nullable timestamp. Always inspect the particular type.

`ledgerNote.refersToPeriodDisplayName` is a nullable **string**, for example the synthetic value `2026-09 - 2026-11`. It describes the period the note concerns. The module uses it for the printed rental-period R line and K-record periodization; it must not be substituted for `statusCreated`. The K and R records remain distinct outputs even when they share a source value.

## What the live sample established

The read-only date probe on 9 September 2026 returned 43 accounting nodes:

- All 43 had `ledgerNote.statusCreated`, `ledgerNote.due`, and `ledgerNote.dueOriginal`.
- All 43 had `transactionCreated.change.statusCreated` and `transactionCreated.statusApproved`.
- All 43 had a null `ledgerNote.invoice`, so **no real billed-date value was verified** in this sample.
- `dueNew`, `dueDeferral`, and `fullyPaid` were null for all 43.
- All were created AVI/note events with ledger type `object`; this does not demonstrate reversal or Faktura behavior.

These are observations of that sample, not required-field guarantees. Later expanded-query comparisons preserved every pre-existing selected field value; see [query assessment and evidence](query-assessment.md).

## Database mapping

Migration **`003_invoice_metadata`** adds five nullable columns to `momentum_raindance.invoices`:

| Column | PostgreSQL type | Storage rule |
| --- | --- | --- |
| `source_created_at` | `timestamptz` | Note creation instant. |
| `accounting_event_created_at` | `timestamptz` | Accounting change creation instant. |
| `accounting_approved_at` | `timestamptz` | Accounting transaction approval instant. |
| `due_date` | `date` | Effective due calendar date without timezone conversion. |
| `invoice_billed_date` | `date` | Related invoice's billed calendar date, when available. |

These are **immutable first-reservation snapshots**, not a live mirror of Momentum. New invoices receive metadata in the same transaction that permanently reserves their identities and output delivery. Source changes after that reservation do not refresh the columns. Existing/historically seeded rows stay null; migration does not guess, backfill, or rewrite their history.

A duplicate with unchanged Raindance content remains suppressed even if these dates later differ. Metadata-only differences neither alter the rendered file's content hash nor authorize another export. A previously recorded identity with different rendered Raindance content still fails for reconciliation. Source dates are not used as duplicate keys or delivery checkpoints.

`invoices.recorded_at` is when this integration recorded the invoice locally. `deliveries.created_at` is the local reservation time. Neither means the invoice was created in Momentum. Likewise, a stored `delivered_at` records the integration's delivery confirmation; it does not establish a source creation date.

Migration 003 introduced these fields. The current tasks additionally require **`004_database_checkpoint`** for database-owned progress and complete-fetch receipts. Use the existing database login on Fetch, Convert, and Confirm; a separate login is not required. Installations using optional restricted-runtime grants must reapply the updated helper after migrating. Follow the [coordinated upgrade](../../database/README.md#upgrading-to-database-owned-progress); do not edit applied migrations, clear invoice history, or backfill historical metadata as part of that upgrade.

The source cursor and receipt range are accounting-feed state, not dates inferred from these snapshots. Fetch reads its cursor from PostgreSQL, and only validated no-file conversion or successful delivery confirmation advances it. Neither creation/billing dates nor a Frends KV value can override that progress.

This describes the repository's metadata change. It does not assert that any deployed Frends package or production database has already been upgraded.

## Timezones and missing values

The source `DateTime` scalar promises an ISO-8601 date/time representation. The new metadata timestamp parser specifically accepts an explicit `Z` or numeric offset; missing/invalid offsets fail conversion instead of silently assuming this machine's timezone. PostgreSQL receives the equivalent UTC instant. `timestamptz` preserves the instant, **not the original textual offset**, and its displayed value follows the SQL session timezone.

Calendar dates are different: `2026-09-30` is stored as that date, not converted to midnight UTC. The new billed-date field requires `yyyy-MM-dd`. A nullable source value remains SQL `NULL`; it does not become the current time, an epoch, or an empty date.

For “created today in Sweden”, compare against the date in `Europe/Stockholm`, not an implicit server/developer timezone. This read-only example counts only the snapshots recorded for the chosen source:

```sql
SELECT count(*) AS invoices_created_today
FROM momentum_raindance.invoices
WHERE source_system = 'YOUR_SOURCE_SYSTEM'
  AND (source_created_at AT TIME ZONE 'Europe/Stockholm')::date
      = (CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Stockholm')::date;
```

Null creation timestamps are excluded from that count. This is not a count of every invoice currently in Momentum: unreserved invoices and older rows without metadata are absent from the answer. To inspect a fixed debugging date, use an explicit date literal such as `DATE '2026-09-09'` on the right-hand side. The operator report includes all five metadata columns and `recorded_at` so source and local times can be compared.

## Why the snapshot stays small

The added columns contain only dates/timestamps. They do not add customer names, addresses, personal/organization identity numbers, payment details, raw GraphQL JSON, free-text comments, contract/property descriptions, or period-display text to the tracking database.

The existing source and invoice identifiers are still **pseudonymous, correlatable identifiers**, not anonymous data. Date metadata linked to an invoice must receive the same access controls and retention treatment as the tracking history. Actual API payloads and rendered files can contain customer information and are not made non-sensitive by this smaller database snapshot.

Do not add a generic “sum” column from `ledgerNote.toPay`: the schema describes that field as the **amount left to pay in SEK**, not a guaranteed original invoice total. Original totals, credit interpretation, and balances require a separate accounting definition. They are deliberately outside this metadata addition.
