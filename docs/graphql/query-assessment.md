# Can we ask the Momentum API a better question?

Yes: add explicit dates and page-completeness information, and pass the checkpoint as a typed variable. Keep the vendor's accounting event feed and all existing export fields. Replacing it with a date-filtered invoice query would change which business events are eligible for delivery, not merely improve the query.

This assessment is based on the live schema captured on **9 September 2026**, the supplied vendor Accounting Sync guide, read-only comparisons, and the repository implementation. It distinguishes changes prepared here from recommendations that have **not** been implemented.

## Changes prepared in the repository

The embedded `LedgerNoteAccountingsSync.graphql` query now uses this operation and argument:

```graphql
query LedgerNoteAccountingsSync($lastLocalId: Long!) {
  ledgerNoteAccountingsSync(lastLocalId: $lastLocalId) {
    nodes {
      localId
    }
  }
}
```

This shortened example illustrates the operation header and variable use, not the full export selection. Use the full embedded query for exports or the [date-query example](getting-started.md) for diagnostics.

| Change | Benefit | What it does not do |
| --- | --- | --- |
| A JSON variable `lastLocalId`, declared `Long!` | Uses the schema's cursor scalar and avoids inserting a value into query text. | Does not widen the Frends result/input parsing, C# node model, or database checkpoints to 64-bit. |
| `ledgerNote.statusCreated` | Exposes the original note creation timestamp. | Does not filter out older notes or equate creation with rental period. |
| `transactionCreated { change { statusCreated } statusApproved }` | Exposes accounting change creation and transaction approval separately. | Does not claim they are invoice creation dates. |
| `ledgerNote.invoice.billed` | Exposes an optional Faktura billing date. | Does not invent a value for AVI notes with no linked invoice. |
| `totalCount`, `pageInfo { hasNextPage endCursor }` | Makes completeness information visible in the fetched JSON. | Does not automatically traverse pages or enforce completeness in the converter. |

All existing selected fields remain; no `where`, date/status/customer filters, sort override, or explicit page-size argument was added. The new dates feed the [five-column immutable metadata snapshot](dates-and-metadata.md). Raindance's record mapping, byte encoding, amount/sign handling, and duplicate identity rules are not changed by the query addition.

These are local repository changes, not evidence that the new query is deployed in Frends. The configured public fetch still performs one accounting-sync request and returns its pretty-printed JSON with the **input checkpoint unchanged**.

## Live comparison evidence

At approximately **14:36 UTC on 9 September 2026**, the original embedded query and expanded query were both sent to the configured API with three checkpoint values:

| Checkpoint | Original nodes | Expanded nodes | Original selected values, identities, and order | Expanded `hasNextPage` |
| --- | --- | --- | --- | --- |
| `0` | 43 | 43 | Identical | `false` |
| `12` | 31 | 31 | Identical | `false` |
| `2147483647` | 0 | 0 | Identical | `false` |

Every returned node in the non-empty comparisons had a note creation timestamp, an accounting change creation timestamp, and an accounting approval timestamp. None had a populated linked-invoice billed date. The original and expanded selected values were compared structurally, not merely by record count.

These comparisons are evidence of **result compatibility for these observed cases**, not proof of source snapshot isolation, all event types, all tenants, better performance, or vendor support. Unlike the small date-only probes, the full-query comparisons necessarily fetched the existing invoice payload fields; those responses stay in ignored local research files and are not documentation examples. No API mutation, database operation, or file delivery was performed by the comparison script.

The sanitized results are in `out/graphql-research/query-comparison-summary.json`. Query SHA-256 values recorded by that script:

```text
Original: e2a02e3be7a8efb3bca0560f8ee51e250e1d65633521f17370f0a10b539d0bc0
Expanded: 3e6d577c396addd161889d159b5f1286fa26c4ef74b84c89812333fc1235849b
```

## Vendor warning and the boundary of this change

The supplied Accounting Sync guide advises against altering or filtering its sync queries. The changes here are additive field selections and a variable replacing the same numeric checkpoint argument; they do not intentionally change membership or business semantics. Live compatibility checks support that narrow change, but they are not vendor approval. Confirm additive selections and production paging behavior with Momentum when establishing support expectations.

Do not remove nested accounting records, select only currently unpaid invoices, filter by creation date, exclude reversal events, or switch to an ordinary invoice query and continue advancing the accounting checkpoint. Such changes can hide an accounting event permanently from a later run. One-off debugging reports should use separate read-only queries/files and never update delivery state.

## Improvements still requiring separate implementation

### Explicit pagination and completeness handling

The schema exposes `first`, `after`, `last`, `before`, and `pageInfo`. A two-page diagnostic probe with `first: 2` returned local IDs 1–2, then 3–4 using the first page's returned `endCursor`. This proves basic cursor traversal for that sample. It does not document the server's maximum/default page size, stable ordering guarantees, or consistency during concurrent accounting approvals.

**Current limitation:** fetch makes one request; the typed conversion model consumes `nodes` and does not reject `hasNextPage: true`. Page metadata is preserved in raw fetch JSON, not promoted to separate typed result properties. All 43 current nodes fitted in one page, but that is not a general completeness guarantee. `totalCount` describes the connection, not necessarily the number fetched into this response; selecting it does not fetch missing rows.

A hardened future implementation should retain the same `lastLocalId` lower bound, traverse the returned opaque cursors until complete, validate cursor progress, and only expose a complete successful batch for reservation. Alternatively, an explicit fail-closed guard can stop partial responses until traversal is supported. Neither behavior is implemented by adding `pageInfo` alone. Do not assume it is safe to acknowledge the highest ID of a partial page without a vendor-backed ordering/continuation contract.

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

The best immediate query remains the vendor-shaped accounting sync selection, enriched with explicit metadata. The next reliability work is completeness handling and the end-to-end cursor contract, not an invoice-date filter.
