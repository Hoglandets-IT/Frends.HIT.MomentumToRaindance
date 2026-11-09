# Accounting sync semantics and safe checkpoints

## Use the accounting event feed

The vendor's Accounting Sync guide specifies these prerequisites: ledger management is inactive, the accounting run has executed and been approved, and all account-coding parts have the required padding (`Utfyllnad`). These are Momentum configuration/business prerequisites, not settings that this module changes.

The two feeds serve different cases:

| Entry point | Purpose | Saved cursor |
| --- | --- | --- |
| `ledgerNoteAccountingsSync(lastLocalId: Long)` | Accounting associated with a ledger note; may include AVI and Faktura flows. | Highest successfully handled ledger-note-accounting `localId`. |
| `ledgerAccountingsSync(lastLocalId: Long)` | Ledger accounting without a ledger-note accounting, such as unrented/own ledgers. | A separate highest ledger-accounting `localId`. |

These cursor sequences are not interchangeable. The current Momentum-to-Raindance task uses the first feed; adding the second would be a separate business workflow, not a query optimization.

## The object chain

```text
ledgerNoteAccountingsSync
  nodes[]: LedgerNoteAccounting       accounting event id + localId + changeType
    transactionCreated               accounting event's transaction
    ledgerNote                       related note and its current exposed metadata
      invoice                        optional Faktura relationship
    distributions[]                  how/where the note can be distributed
    ledgers[]: LedgerAccounting       ledger accounting event
      ledger                         ledger type and effective/accounting dates
      rows[]: LedgerRowAccounting
        ledgerRow                    original row text/pricing/net/VAT information
        records[]                    accounting postings: date, debit flag, amount, coding
```

The root event, note, ledger, accounting row, and accounting record all have their own identities. A field named `id` at one level must not be substituted for another level's ID.

## IDs are not the same thing

| Field | Role |
| --- | --- |
| `nodes[].id` | UUID-like string identifying the `LedgerNoteAccounting` event. |
| `nodes[].localId` | Local accounting sequence/cursor, declared `Long!`. |
| `nodes[].ledgerNote.id` | Stable ledger-note ID; used with the configured source by the module's permanent duplicate protection. |
| `nodes[].ledgerNote.number` | Note number, declared `Long!`; useful operational reference, not a cursor. |
| `nodes[].ledgerNote.invoice.id` | ID of an associated Faktura invoice, only if that relationship exists. |
| `nodes[].ledgerNote.invoice.number` | Optional string invoice number; not the ledger-note ID or local cursor. |

A source-system label is a local configuration boundary, not a field returned by Momentum. Keep it stable for one dataset. Never change the source name or clear history to bypass duplicate protection.

## Created, reversed, invoice, invoiceCredit

The vendor guide distinguishes these cases using categorical IDs, not translated display names:

| Source case | Vendor-described action |
| --- | --- |
| Non-`invoice`/non-`invoiceCredit` ledger; accounting `changeType.id = created` | Create an invoice from the AVI. |
| Non-`invoice`/non-`invoiceCredit` ledger; accounting `changeType.id = reversed` | Create a credit invoice for the reversed AVI. |
| `ledger.type.id = invoice` | Faktura invoice flow. |
| `ledger.type.id = invoiceCredit` | Faktura credit flow. |

The vendor describes an AVI change as original creation, original reversal, then a new creation. For a Faktura credit, `ledgerNote.invoice.creditInvoice` points to the invoice being credited. Multiple partial credits may refer to the same original invoice. The guide also mentions `objectOwn` as a potential own-ledger exclusion; deciding how those ledgers should be handled is a business rule, not a safe silent filter.

`LedgerAccountingChangeType` and `LedgerType` are object types with string IDs, **not GraphQL enums** listing every possible business value. The schema alone does not establish an exhaustive ID set.

All 43 observed nodes were `created` with ledger type `object`. Consequently, reversal/partial-credit behavior has not been proven against live examples. In particular, the guide does not explicitly settle whether a reversal always reuses an earlier `ledgerNote.id`. Permanent note-level deduplication must be reviewed against representative reversal events before claiming that this implements the complete vendor change protocol. Do not weaken duplicate protection or silently re-key historical records based on an assumption.

The current converter explicitly rejects any present non-`created` change type on a pending node, ledger, or row. Legacy stored payloads with omitted change types remain accepted. This is a fail-closed limitation, not implemented reversal support; do not filter the unsupported event out and advance past it.

## Pagination and advancing a checkpoint

The live `ledgerNoteAccountingsSync` signature accepts `lastLocalId`, `first`, `after`, `last`, `before`, `where`, and `order`. Its connection exposes `nodes`, `edges`, `pageInfo`, and `totalCount`. The complete signature and descriptions are in [the references](reference-query-root.md).

The vendor says to store the highest returned local ID for subsequent fetches. This integration stores that progress in PostgreSQL `sources.last_local_id`, not Frends KV. For a new invoice, “handled” additionally requires the reservation, file write, and confirmation protocol: reading a row is not proof that Raindance received it.

Fetch reads the database cursor and keeps that same `lastLocalId` lower bound throughout traversal, following the returned opaque `endCursor` through the `after` variable until `hasNextPage` is false. It does not decode, synthesize, or treat that cursor as a local ID. A missing/repeated continuation cursor, inconsistent total/count, invalid or non-increasing local ID, malformed metadata, or GraphQL error fails the complete fetch. No partially fetched batch is acknowledged.

The limited live probe verified two pages of two items, not snapshot isolation under concurrent source changes, maximum/default page size, ordering guarantees for arbitrary `where`/`order`, or retention of the accounting feed. Those remain vendor-confirmation questions. Do not pass `first` without reading all pages, and do not advance the production checkpoint from a filtered debugging query.

Fetch registers a durable receipt only after all pages succeed and the database cursor is still unchanged. Convert requires that receipt's `FetchId` and exact JSON, validates the entire batch, then checks current progress under the same source lock used for reservation. A stale or edited response fails. When every event is historical, delivered, or an intentional no-op, Convert can advance progress without output. With new invoices, Confirm advances it in the same transaction as delivery evidence. This preserves progress across Frends resets without treating a reservation as delivery. See [current behavior and remaining source-contract limits](query-assessment.md).

Migration 004 starts existing sources at cursor zero, because old delivery maxima and historical `first_local_id` values do not prove complete feed coverage. The first complete scan suppresses recorded identities and establishes progress safely. Never seed a new cursor from an unverified KV value or reset source identities to bypass history.

## Accounting signs and period fields

`ledgers[].rows[].records[].debit` is a required boolean on the sync record; `amount` is a required decimal. Keep both. The separate general `AccountingRecord` type instead exposes `statusHasDebit`; substituting a different route would require a fresh mapping review.

`LedgerRowAccountingRecord.accountingDate` is a nullable **Date**, while the general `AccountingRecord.accountingDate` is a nullable **DateTime**. They are not interchangeable declarations.

`accountDistributionCoding` is the raw coding string. The vendor requires configured padding so fixed coding segments can be interpreted. Do not trim away significant padding or invent account splits from an unpadded example.

`ledgerNote.refersToPeriodDisplayName` identifies the note's rental/service period text. It is not the note creation date, the due date, or necessarily the accounting period. Raindance's printed rental-period R line and K-record periodization may both use this source value while remaining separate output fields. See [date distinctions](dates-and-metadata.md).

## Distribution and privacy

The vendor says `postalAddress` is the postal address, whereas `address` may be an email address. E-invoice and Svefaktura-specific fields are not universally populated. Preserve existing postal-address selection rules in the exporter; do not copy a generic `address` into a postal fixed-width field without examining its type.

Customers, addresses, person/organization identifiers, contact details, payment references, contract/property links, and free text are invoice-processing payload. They must not become generic tracking metadata or be copied into documentation examples. Tracking metadata uses an explicit, much smaller allowlist.
