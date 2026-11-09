# Can we ask the Momentum API a better question?

The task uses the vendor's accounting event feed with explicit dates, typed cursor variables, and complete page traversal. PostgreSQL owns progress and receipt validation. Replacing this with a date-filtered invoice query would change which business events are eligible for delivery, not merely improve the query.

The source-contract observations and comparison evidence below come from the live schema captured on **9 September 2026** and the supplied vendor Accounting Sync guide. The current implementation section also describes the later database-checkpoint and pagination changes. Historical probes do not establish deployment of the current code or guarantee behavior for every tenant.

## Changes prepared in the repository

The embedded `LedgerNoteAccountingsSync.graphql` query now uses this operation and argument:

```graphql
query LedgerNoteAccountingsSync($lastLocalId: Long!, $after: String) {
  ledgerNoteAccountingsSync(lastLocalId: $lastLocalId, after: $after) {
    nodes {
      localId
    }
  }
}
```

This shortened example illustrates the operation header and variable use, not the full export selection. Use the full embedded query for exports or the [date-query example](getting-started.md) for diagnostics.

| Change | Benefit | What it does not do |
| --- | --- | --- |
| A JSON variable `lastLocalId`, declared `Long!` | Uses the schema's cursor scalar and avoids inserting a value into query text. Production reads it from PostgreSQL. | Does not widen the C# node model or database checkpoints to 64-bit. |
| `ledgerNote.statusCreated` | Exposes the original note creation timestamp. | Does not filter out older notes or equate creation with rental period. |
| `transactionCreated { change { statusCreated } statusApproved }` | Exposes accounting change creation and transaction approval separately. | Does not claim they are invoice creation dates. |
| `ledgerNote.invoice.billed` | Exposes an optional Faktura billing date. | Does not invent a value for AVI notes with no linked invoice. |
| `after`, `totalCount`, and `pageInfo { hasNextPage endCursor }` | Fetch follows every page, checks forward progress and consistent counts, and returns one complete merged response. | Does not establish server snapshot isolation or retention guarantees. |
| Durable fetch receipt | Binds exact response bytes to a source and database cursor range, so Convert cannot acknowledge an edited or stale subset. | Does not replace stable invoice identities or prove file delivery. |

All existing selected fields remain; no `where`, date/status/customer filters, sort override, or explicit page-size argument was added. The new dates feed the [five-column immutable metadata snapshot](dates-and-metadata.md). Raindance's record mapping, byte encoding, amount/sign handling, and duplicate identity rules are not changed by the query addition.

These are repository changes, not evidence that the matching package and migration 004 are deployed. Public Fetch takes API and database settings, with no external checkpoint. It returns complete pretty-printed JSON, a durable `FetchId`, and the database starting cursor as an informational result. It does not advance delivery progress. Convert and Confirm commit progress only under the [database-owned checkpoint contract](../operations.md#checkpoint-contract).

## Live comparison evidence

At approximately **14:36 UTC on 9 September 2026**, the original embedded query and expanded query were both sent to the configured API with three checkpoint values:

| Checkpoint | Original nodes | Expanded nodes | Original selected values, identities, and order | Expanded `hasNextPage` |
| --- | --- | --- | --- | --- |
| `0` | 43 | 43 | Identical | `false` |
| `12` | 31 | 31 | Identical | `false` |
| `2147483647` | 0 | 0 | Identical | `false` |

Every returned node in the non-empty comparisons had a note creation timestamp, an accounting change creation timestamp, and an accounting approval timestamp. None had a populated linked-invoice billed date. The original and expanded selected values were compared structurally, not merely by record count.

These comparisons are evidence of **result compatibility for these observed cases**, not proof of source snapshot isolation, all event types, all tenants, better performance, or vendor support. Unlike the small date-only probes, the full-query comparisons necessarily fetched the existing invoice payload fields; those responses stay in ignored local research files and are not documentation examples. No API mutation, database operation, or file delivery was performed by the comparison script.

The sanitized results are in `out/graphql-research/query-comparison-summary.json`. Query SHA-256 values recorded by that historical script, before the later pagination argument was added:

```text
Original: e2a02e3be7a8efb3bca0560f8ee51e250e1d65633521f17370f0a10b539d0bc0
Expanded: 3e6d577c396addd161889d159b5f1286fa26c4ef74b84c89812333fc1235849b
```

## Vendor warning and the boundary of this change

The supplied Accounting Sync guide advises against altering or filtering its sync queries. The September comparison tested additive field selections and a variable replacing the same numeric checkpoint argument; it did not test the later database receipt workflow. Current paging also follows the exposed continuation cursor without date/status/customer filters. These changes do not intentionally change business membership, but local checks are not vendor approval. Confirm additive selections and production paging behavior with Momentum when establishing support expectations.

Do not remove nested accounting records, select only currently unpaid invoices, filter by creation date, exclude reversal events, or switch to an ordinary invoice query and continue advancing the accounting checkpoint. Such changes can hide an accounting event permanently from a later run. One-off debugging reports should use separate read-only queries/files and never update delivery state.

## Pagination and database-owned progress

The schema exposes `first`, `after`, `last`, `before`, and `pageInfo`. A two-page diagnostic probe with `first: 2` returned local IDs 1–2, then 3–4 using the first page's returned `endCursor`. This proves basic cursor traversal for that sample. It does not document the server's maximum/default page size, stable ordering guarantees, or consistency during concurrent accounting approvals.

The September implementation made one request and did not enforce `hasNextPage`. All 43 nodes in that sample fitted in one page; that observation was never a general completeness guarantee. The current implementation replaces that behavior with complete traversal, holding `lastLocalId` fixed and following the opaque `after` cursor until the last page. It rejects missing or repeated continuation cursors, inconsistent totals, malformed metadata, non-increasing/duplicate/out-of-range IDs, and GraphQL partial errors. The merged node count must match the connection's total.

Only a successful complete fetch can be registered as a durable receipt. A partial timeout/failure cannot reserve invoices or advance progress. Fetch rechecks the database cursor before registering the receipt, and Convert checks it again before processing. The exact JSON hash prevents filtering or reformatting between tasks from silently acknowledging omitted records. With new invoices, reservation leaves progress unchanged until Confirm updates delivery status and cursor atomically; validated historical/no-op batches can advance without a file.

These checks detect incomplete or inconsistent observed responses. They cannot prove the source did not omit an event, or establish snapshot isolation during concurrent source changes. Confirm the vendor's ordering/retention contract and retry a failed traversal rather than acknowledging partial data.

## Improvements still requiring separate implementation

### End-to-end 64-bit checkpoints

Momentum declares `lastLocalId: Long` and node `localId: Long!`. A direct diagnostic request with `2147483648` was accepted and returned zero nodes. It proves that argument value is accepted; it does not provide a real returned node beyond the 32-bit range.

**Current limitation:** the task normalizes checkpoints to `0..2147483647`; its results, typed node IDs, and database checkpoint columns remain 32-bit. A `Long!` query variable does not fix those downstream limits. Widening this contract requires a coordinated C# public/internal model change, database migration, Frends binding checks, and boundary tests. Do not silently clamp, wrap, or truncate a cursor.

### Full change-event support

The vendor distinguishes `created`, `reversed`, Faktura, and partial-credit workflows. The current converter explicitly rejects present change types other than `created` at node, ledger, and row levels. Legacy stored payloads without a change type remain accepted. Therefore the presence of reversal fields or date metadata in the schema is **not** proof that this module supports the complete vendor event protocol.

Representative reversal/credit payloads and agreement on identity/history semantics are prerequisites for expanding support. Keep unsupported cases fail-closed; do not filter them away or weaken permanent duplicate protection to make a batch pass. See [accounting event semantics](accounting-sync.md).

### Query size and performance

No latency, server-load, or large-backlog benchmark was performed. Extra fields and `totalCount` may have a cost. Removing fields or using a different graph route is not justified solely because a current code path appears not to consume them; saved JSON is also the reusable fetch task's output. Measure representative batch sizes and review every consumer before changing the established selection contract.

## Routes that are not substitutes

- `invoices` and `invoicesSync` expose invoice entities, not the approved ledger-note-accounting event sequence. `invoicesSync` uses `lastId: String`, a different cursor contract.
- `ledgerAccountingsSync` is for a distinct ledger-accounting workflow and requires its own checkpoint.
- `ledgerNoteChangesSync` / `EntityChangeLog` are not a durable accounting feed. The captured schema labels the log **beta** and says its rows are deleted after **seven days**.
- `ledgerNote.statusCreated` is useful for reporting; it is not an alternative delivery cursor.

The production query remains the vendor-shaped accounting sync selection, enriched with explicit metadata and complete paging. Database-owned progress removes Frends KV as a dependency. Remaining work includes the end-to-end 64-bit contract and representative unsupported business-event behavior, not an invoice-date filter.
