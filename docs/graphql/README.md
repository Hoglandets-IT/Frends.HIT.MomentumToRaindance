# Momentum GraphQL API — accounting integration guide

This is the integration team's reference for the Momentum API used by **Frends.HIT.MomentumToRaindance**. It combines live schema introspection, read-only probes, the vendor's Accounting Sync guide, and the module's source. It is not a vendor guarantee for other tenants or future releases.

## Start here

1. [Making a request: authentication, GraphQL, variables, and errors](getting-started.md).
2. [Accounting sync: which feed, identities, changes, and checkpoints](accounting-sync.md).
3. [Dates and privacy-safe invoice metadata](dates-and-metadata.md).
4. [Can the module ask a better question? Evidence and recommendations](query-assessment.md).

For exact field names, argument types, and nullability:

- [Accounting sync connections and accounting records](reference-sync.md).
- [Ledger notes, ledgers, rows, and invoices](reference-ledger-invoice.md).
- [Transactions, lifecycle fields, and scalars](reference-lifecycle.md).
- [Distribution, customers, companies, and period messages](reference-distribution.md).
- [Accounting filters and sort inputs](reference-inputs.md), documented for discovery, not recommended for production sync.
- [Every query entry point](reference-query-root.md), all 264 fields exposed by this schema.
- [Mutations and subscriptions](reference-mutations-subscriptions.md), inventory only; not invoked by this integration or the investigation.

## What was actually verified

The configured API was introspected on **9 September 2026 at 14:28 UTC**. It exposed 1,268 types: 553 objects, 664 input objects, 27 unions, 11 scalars, 10 enums, and 3 interfaces. The roots contain 264 queries, 27 mutations, and 19 subscriptions. Counts include GraphQL introspection types. Visibility of a field in the schema does **not** prove that a particular login can access every instance of that field.

Read-only accounting-sync probes at approximately 14:30 UTC returned 43 nodes, all with a note creation timestamp. Only schema metadata and a small allowlist of accounting IDs, dates, flags, and type IDs were requested by those probes. No customer names, contact details, addresses, payment references, or free-text invoice rows were requested. No API mutations, database changes, checkpoint changes, or file deliveries were made by the API research.

The observed nodes were all `changeType.id = created`, note type `note`, ledger type `object`, with no linked `Invoice`. That sample does **not** test reversal, Faktura, partial-credit, draft, removed, or replacement behavior.

## The important findings

| Question | Answer |
| --- | --- |
| When was an AVI/ledger note created? | `ledgerNote.statusCreated`, a nullable `DateTime`; populated for all 43 observed notes. |
| When was the accounting sync event approved? | `transactionCreated.statusApproved` on the accounting node. This is a different timestamp and event. |
| When was the transaction's change created? | `transactionCreated.change.statusCreated`. Do not call it the invoice creation date. |
| Can the invoice's billed date be read? | Yes: `ledgerNote.invoice.billed`, but the whole `invoice` relationship was null in all 43 observed nodes. |
| Is `localId` a small integer? | The schema says signed 64-bit `Long!`, even though current values happen to be 1–43. |
| Is the default response guaranteed complete? | No limit is declared by introspection. All 43 fitted in one observed response; `pageInfo` must be inspected to detect truncation. |
| Can ordinary invoice queries replace accounting sync? | They expose different entities/semantics. The vendor's approved accounting feed remains the source for exports. |

## Scope of the rest of the API

The query inventory also exposes these domains. They were inventoried, not sampled for customer/business data:

| Domain | Example entry points |
| --- | --- |
| Generic entity and graph lookup | `nodes`, `nodesSync`, `nodeById`, `graphs`, `nodeLinks` |
| Accounting | `ledgerNoteAccountingsSync`, `ledgerAccountingsSync`, `accountingRecords`, `accountingVerifications` |
| Invoices and receivables | `invoices`, `invoiceRows`, `ledgerNotes`, `ledgers`, `ledgerRows` |
| Payment data | `payments`, `paymentParts` |
| People and organizations | `actors`, `customers`, `companies` |
| Property hierarchy | `properties`, `areas`, `constructionWorks`, `buildingParts`, `stairwells`, `spaces`, `objects` |
| Contracts and pricing | `contracts`, `pricings`, `prices`, `pricingTypes` |
| Metering | `meters`, `meterFacilities`, `meterValues`, `consumptions` |
| Work and maintenance | `errands`, `errandRows`, `maintenances`, `processes` |
| Communication | `entityConversations`, `conversationById`, `notifications`, `notes` |
| Market/rental queues | `marketObjects`, `marketActorApplications`, `marketActorQueues`, `marketConsents` |
| Keys and configuration | `keys`, `keyGroups`, `nodeClasses`, `nodeTypes`, `entityTypes` |
| Change detection | `entityChangesSync`, `ledgerNoteChangesSync`, `contractChangesSync` |
| API usage | `requestStats` |

Similar suffixes do not imply interchangeable contracts. For example, `invoicesSync(lastId: String)` enumerates using an ID keyset; `ledgerNoteAccountingsSync(lastLocalId: Long)` uses an accounting cursor; `ledgerNoteChangesSync(lastEntityChangeLocalId: Long)` uses a short-lived change log.

## Evidence and maintenance

The ignored `out/graphql-research/` directory contains the captured `schema.json`, `schema-summary.json`, `metadata-sample.json`, `metadata-summary.json`, and the one-time read-only probe scripts. Schema JSON SHA-256:

```text
ed58353da1b1af544e523cb9105bcdfb7c37148c9d87b0b16b5d407d24dcd8c2
```

Raw schema data has no invoice payloads, but the metadata sample includes correlatable accounting IDs and is kept local rather than published. No credentials, bearer tokens, tenant URL, or raw customer data belong in Outline or these repository pages.

The vendor input was the supplied **Accounting Sync** guide, covering prerequisites, AVI/Faktura events, distribution, and account-coding padding. Its warning against changing/filtering sync queries is preserved in [the assessment](query-assessment.md). The schema is the authority for field shape; vendor prose is the authority used here for business event interpretation; successful probes establish only the behavior they actually exercised. Repeat introspection and compare types when the API is upgraded.
