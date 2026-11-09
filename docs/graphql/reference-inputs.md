# Accounting sync argument and filter types

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

These filters are documented as schema capabilities, **not recommendations for the production accounting sync**. The vendor advises against altering/filtering its sync query; see [query assessment](query-assessment.md). Nested input references not expanded on this page can be retrieved with `__type(name: "InputTypeName") { inputFields { name type { kind name ofType { kind name } } } }`.

## LedgerNoteAccountingFilterInput

Contains connections of a ledger note accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `and` | `[LedgerNoteAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `or` | `[LedgerNoteAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `id` | `StringOperationFilterInput` | Unspecified | The ledger note accounting's universally unique identifier. |
| `ledgerNote` | `LedgerNoteFilterInput` | Unspecified | The ledger note |
| `transactionCreated` | `TransactionFilterInput` | Unspecified | The transaction that created the ledger note accounting. |
| `changeType` | `LedgerAccountingChangeTypeFilterInput` | Unspecified | The type of change made |
| `company` | `CompanyFilterInput` | Unspecified | The connected company |
| `distributions` | `ListFilterInputTypeOfLedgerNoteAccountingDistributionFilterInput` | Unspecified | The ledger accounting's connected ledger note accounting distribution. |
| `ledgers` | `ListFilterInputTypeOfLedgerAccountingFilterInput` | Unspecified | The ledger accounting's connected ledger note accounting. |
| `localId` | `LongOperationFilterInput` | Unspecified | The ledger note accounting's local id value. |

## LedgerNoteAccountingSortInput

Contains connections of a ledger note accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `id` | `SortEnumType` | Unspecified | The ledger note accounting's universally unique identifier. |
| `ledgerNote` | `LedgerNoteSortInput` | Unspecified | The ledger note |
| `transactionCreated` | `TransactionSortInput` | Unspecified | The transaction that created the ledger note accounting. |
| `changeType` | `LedgerAccountingChangeTypeSortInput` | Unspecified | The type of change made |
| `company` | `CompanySortInput` | Unspecified | The connected company |
| `localId` | `SortEnumType` | Unspecified | The ledger note accounting's local id value. |

## LedgerAccountingFilterInput

Contains connections of a ledger accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `and` | `[LedgerAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `or` | `[LedgerAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `id` | `StringOperationFilterInput` | Unspecified | The ledger accounting's universally unique identifier. |
| `ledger` | `LedgerFilterInput` | Unspecified | The ledger |
| `transactionCreated` | `TransactionFilterInput` | Unspecified | The transaction that created the ledger accounting. |
| `company` | `CompanyFilterInput` | Unspecified | The connected company |
| `changeType` | `LedgerAccountingChangeTypeFilterInput` | Unspecified | The type of change made |
| `ledgerNoteAccounting` | `LedgerNoteAccountingFilterInput` | Unspecified | The ledger note accounting this ledger accounting is connected to. |
| `rows` | `ListFilterInputTypeOfLedgerRowAccountingFilterInput` | Unspecified | The ledger row accounting's connected ledger accounting. |
| `localId` | `LongOperationFilterInput` | Unspecified | The ledger accounting's local id value. |

## LedgerAccountingSortInput

Contains connections of a ledger accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `id` | `SortEnumType` | Unspecified | The ledger accounting's universally unique identifier. |
| `ledger` | `LedgerSortInput` | Unspecified | The ledger |
| `transactionCreated` | `TransactionSortInput` | Unspecified | The transaction that created the ledger accounting. |
| `company` | `CompanySortInput` | Unspecified | The connected company |
| `changeType` | `LedgerAccountingChangeTypeSortInput` | Unspecified | The type of change made |
| `ledgerNoteAccounting` | `LedgerNoteAccountingSortInput` | Unspecified | The ledger note accounting this ledger accounting is connected to. |
| `localId` | `SortEnumType` | Unspecified | The ledger accounting's local id value. |

## LedgerRowAccountingFilterInput

Contains connections of a ledger row accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `and` | `[LedgerRowAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `or` | `[LedgerRowAccountingFilterInput!]` | Unspecified | Not described by the schema. |
| `id` | `StringOperationFilterInput` | Unspecified | The ledger row accounting's universally unique identifier. |
| `ledgerRow` | `LedgerRowFilterInput` | Unspecified | The ledger row |
| `transactionCreated` | `TransactionFilterInput` | Unspecified | The transaction that created the ledger row accounting. |
| `changeType` | `LedgerAccountingChangeTypeFilterInput` | Unspecified | The type of change made |
| `ledgerAccounting` | `LedgerAccountingFilterInput` | Unspecified | The ledger accounting this ledger row accounting is connected to. |
| `records` | `ListFilterInputTypeOfLedgerRowAccountingRecordFilterInput` | Unspecified | The ledger row accounting records's connected ledger row accounting. |
| `localId` | `LongOperationFilterInput` | Unspecified | The ledger row accounting's local id value. |

## LedgerRowAccountingSortInput

Contains connections of a ledger row accounting

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `id` | `SortEnumType` | Unspecified | The ledger row accounting's universally unique identifier. |
| `ledgerRow` | `LedgerRowSortInput` | Unspecified | The ledger row |
| `transactionCreated` | `TransactionSortInput` | Unspecified | The transaction that created the ledger row accounting. |
| `changeType` | `LedgerAccountingChangeTypeSortInput` | Unspecified | The type of change made |
| `ledgerAccounting` | `LedgerAccountingSortInput` | Unspecified | The ledger accounting this ledger row accounting is connected to. |
| `localId` | `SortEnumType` | Unspecified | The ledger row accounting's local id value. |

## LedgerRowAccountingRecordFilterInput

Contains connections of a ledger row accounting record

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `and` | `[LedgerRowAccountingRecordFilterInput!]` | Unspecified | Not described by the schema. |
| `or` | `[LedgerRowAccountingRecordFilterInput!]` | Unspecified | Not described by the schema. |
| `id` | `StringOperationFilterInput` | Unspecified | The ledger row accounting record's universally unique identifier. |
| `accountingRecord` | `AccountingRecordFilterInput` | Unspecified | The accounting record |
| `transactionCreated` | `TransactionFilterInput` | Unspecified | The transaction that created the ledger row accounting record. |
| `changeType` | `LedgerAccountingChangeTypeFilterInput` | Unspecified | The type of change made |
| `amount` | `DecimalOperationFilterInput` | Unspecified | The ledger row accounting record's amount in SEK. |
| `debit` | `BooleanOperationFilterInput` | Unspecified | Indicates if the ledger row accounting record has debit or not. |
| `accountDistributionCoding` | `StringOperationFilterInput` | Unspecified | The ledger row accounting record's account distribution coding. |
| `accountDistributionCodingType` | `AccountDistributionCodingTypeFilterInput` | Unspecified | The ledger row accounting record's account distribution coding type. |
| `accountingPeriod` | `AccountingPeriodFilterInput` | Unspecified | The ledger row accounting record's accounting period. |
| `accountingDate` | `DateOperationFilterInput` | Unspecified | The ledger row accounting record's accounting date. |
| `ledgerRowAccounting` | `LedgerRowAccountingFilterInput` | Unspecified | The ledger row accounting this ledger row accounting record is connected to. |
| `localId` | `LongOperationFilterInput` | Unspecified | The ledger row accounting record's local id value. |

## LedgerRowAccountingRecordSortInput

Contains connections of a ledger row accounting record

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `id` | `SortEnumType` | Unspecified | The ledger row accounting record's universally unique identifier. |
| `accountingRecord` | `AccountingRecordSortInput` | Unspecified | The accounting record |
| `transactionCreated` | `TransactionSortInput` | Unspecified | The transaction that created the ledger row accounting record. |
| `changeType` | `LedgerAccountingChangeTypeSortInput` | Unspecified | The type of change made |
| `amount` | `SortEnumType` | Unspecified | The ledger row accounting record's amount in SEK. |
| `debit` | `SortEnumType` | Unspecified | Indicates if the ledger row accounting record has debit or not. |
| `accountDistributionCoding` | `SortEnumType` | Unspecified | The ledger row accounting record's account distribution coding. |
| `accountDistributionCodingType` | `AccountDistributionCodingTypeSortInput` | Unspecified | The ledger row accounting record's account distribution coding type. |
| `accountingPeriod` | `AccountingPeriodSortInput` | Unspecified | The ledger row accounting record's accounting period. |
| `accountingDate` | `SortEnumType` | Unspecified | The ledger row accounting record's accounting date. |
| `ledgerRowAccounting` | `LedgerRowAccountingSortInput` | Unspecified | The ledger row accounting this ledger row accounting record is connected to. |
| `localId` | `SortEnumType` | Unspecified | The ledger row accounting record's local id value. |

## LedgerNoteFilterInput

A note in the system.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `and` | `[LedgerNoteFilterInput!]` | Unspecified | Not described by the schema. |
| `or` | `[LedgerNoteFilterInput!]` | Unspecified | Not described by the schema. |
| `id` | `StringOperationFilterInput` | Unspecified | The note's universally unique identifier. |
| `number` | `LongOperationFilterInput` | Unspecified | The ledger note´s unique number. |
| `statusRemoved` | `BooleanOperationFilterInput` | Unspecified | Indicates if the ledger note is removed. |
| `statusDraft` | `BooleanOperationFilterInput` | Unspecified | Indicates if the ledger note is in draft state. |
| `statusReplaced` | `BooleanOperationFilterInput` | Unspecified | Indicates if the ledger note is replaced. |
| `statusCreated` | `DateTimeOperationFilterInput` | Unspecified | Date and time when the note was first created. |
| `accountingPeriod` | `AccountingPeriodFilterInput` | Unspecified | The ledger notes's accounting period. |
| `transactionCreated` | `TransactionFilterInput` | Unspecified | The transaction that created the ledger. |
| `transactionReplaced` | `TransactionFilterInput` | Unspecified | The transaction that replaced the ledger. |
| `type` | `LedgerNoteTypeFilterInput` | Unspecified | The note type. |
| `due` | `DateOperationFilterInput` | Unspecified | The note's due date.<br>Returns deferral date if set (dueDeferral), otherwise note's new due date (dueNew), otherwise original due date (dueOriginal). |
| `dueOriginal` | `DateOperationFilterInput` | Unspecified | The note's original due date when it was first created. |
| `dueNew` | `DateOperationFilterInput` | Unspecified | The note's due date if a new due date has been set (currenlty used on only rental ledger), otherwise null. |
| `dueDeferral` | `DateOperationFilterInput` | Unspecified | The note's deferral date if set (currently used on only invoices), otherwise null. |
| `refersToPeriodDisplayName` | `StringOperationFilterInput` | Unspecified | The display name of the period the ledger note refers to. |
| `company` | `CompanyFilterInput` | Unspecified | The company connected to the note. |
| `customer` | `CustomerFilterInput` | Unspecified | The customer connected to the note. |
| `object` | `ObjectFilterInput` | Unspecified | The object the note is connected to. |
| `contract` | `ContractFilterInput` | Unspecified | The contract the note is connected to. |
| `property` | `PropertyFilterInput` | Unspecified | The property that the note is connected to. |
| `ledgerGroup` | `LedgerGroupFilterInput` | Unspecified | The ledger group that the note is connected to. |
| `invoice` | `InvoiceFilterInput` | Unspecified | The invoice that the note is connected to and created from. |
| `fullyPaid` | `DateOperationFilterInput` | Unspecified | The date that the final playent was made. |
| `referenceNumber` | `StringOperationFilterInput` | Unspecified | The ledger note's payment reference number. E.g. OCR, bankgiro, autogiro. |
| `payToNumber` | `StringOperationFilterInput` | Unspecified | The ledger note's pay to number. E.g. `123-1234`. |
| `payToTypeDisplayName` | `StringOperationFilterInput` | Unspecified | The ledger note's pay to number typ. E.g. `Bankgiro`. |
| `debitAmount` | `DecimalOperationFilterInput` | Unspecified | Debit amount of the note. |
| `balanceAmount` | `DecimalOperationFilterInput` | Unspecified | Balance amount of the note. |
| `toPay` | `DecimalOperationFilterInput` | Unspecified | The sum left to pay on the note in SEK. |
| `ledgers` | `ListFilterInputTypeOfLedgerNoteLedgerFilterInput` | Unspecified | All ledgers that are related to the note. |
| `messages` | `ListFilterInputTypeOfLedgerNoteMessageFilterInput` | Unspecified | All messages that are related to the note. |

## LedgerNoteSortInput

A note in the system.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `id` | `SortEnumType` | Unspecified | The note's universally unique identifier. |
| `number` | `SortEnumType` | Unspecified | The ledger note´s unique number. |
| `statusRemoved` | `SortEnumType` | Unspecified | Indicates if the ledger note is removed. |
| `statusDraft` | `SortEnumType` | Unspecified | Indicates if the ledger note is in draft state. |
| `statusReplaced` | `SortEnumType` | Unspecified | Indicates if the ledger note is replaced. |
| `statusCreated` | `SortEnumType` | Unspecified | Date and time when the note was first created. |
| `accountingPeriod` | `AccountingPeriodSortInput` | Unspecified | The ledger notes's accounting period. |
| `transactionCreated` | `TransactionSortInput` | Unspecified | The transaction that created the ledger. |
| `transactionReplaced` | `TransactionSortInput` | Unspecified | The transaction that replaced the ledger. |
| `type` | `LedgerNoteTypeSortInput` | Unspecified | The note type. |
| `due` | `SortEnumType` | Unspecified | The note's due date.<br>Returns deferral date if set (dueDeferral), otherwise note's new due date (dueNew), otherwise original due date (dueOriginal). |
| `dueOriginal` | `SortEnumType` | Unspecified | The note's original due date when it was first created. |
| `dueNew` | `SortEnumType` | Unspecified | The note's due date if a new due date has been set (currenlty used on only rental ledger), otherwise null. |
| `dueDeferral` | `SortEnumType` | Unspecified | The note's deferral date if set (currently used on only invoices), otherwise null. |
| `refersToPeriodDisplayName` | `SortEnumType` | Unspecified | The display name of the period the ledger note refers to. |
| `company` | `CompanySortInput` | Unspecified | The company connected to the note. |
| `customer` | `CustomerSortInput` | Unspecified | The customer connected to the note. |
| `object` | `ObjectSortInput` | Unspecified | The object the note is connected to. |
| `contract` | `ContractSortInput` | Unspecified | The contract the note is connected to. |
| `property` | `PropertySortInput` | Unspecified | The property that the note is connected to. |
| `ledgerGroup` | `LedgerGroupSortInput` | Unspecified | The ledger group that the note is connected to. |
| `invoice` | `InvoiceSortInput` | Unspecified | The invoice that the note is connected to and created from. |
| `fullyPaid` | `SortEnumType` | Unspecified | The date that the final playent was made. |
| `referenceNumber` | `SortEnumType` | Unspecified | The ledger note's payment reference number. E.g. OCR, bankgiro, autogiro. |
| `payToNumber` | `SortEnumType` | Unspecified | The ledger note's pay to number. E.g. `123-1234`. |
| `payToTypeDisplayName` | `SortEnumType` | Unspecified | The ledger note's pay to number typ. E.g. `Bankgiro`. |
| `debitAmount` | `SortEnumType` | Unspecified | Debit amount of the note. |
| `balanceAmount` | `SortEnumType` | Unspecified | Balance amount of the note. |
| `toPay` | `SortEnumType` | Unspecified | The sum left to pay on the note in SEK. |

## LongOperationFilterInput

No type description supplied.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `eq` | `Long` | Unspecified | Not described by the schema. |
| `neq` | `Long` | Unspecified | Not described by the schema. |
| `in` | `[Long]` | Unspecified | Not described by the schema. |
| `nin` | `[Long]` | Unspecified | Not described by the schema. |
| `gt` | `Long` | Unspecified | Not described by the schema. |
| `ngt` | `Long` | Unspecified | Not described by the schema. |
| `gte` | `Long` | Unspecified | Not described by the schema. |
| `ngte` | `Long` | Unspecified | Not described by the schema. |
| `lt` | `Long` | Unspecified | Not described by the schema. |
| `nlt` | `Long` | Unspecified | Not described by the schema. |
| `lte` | `Long` | Unspecified | Not described by the schema. |
| `nlte` | `Long` | Unspecified | Not described by the schema. |

## DateTimeOperationFilterInput

No type description supplied.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `eq` | `DateTime` | Unspecified | Not described by the schema. |
| `neq` | `DateTime` | Unspecified | Not described by the schema. |
| `in` | `[DateTime]` | Unspecified | Not described by the schema. |
| `nin` | `[DateTime]` | Unspecified | Not described by the schema. |
| `gt` | `DateTime` | Unspecified | Not described by the schema. |
| `ngt` | `DateTime` | Unspecified | Not described by the schema. |
| `gte` | `DateTime` | Unspecified | Not described by the schema. |
| `ngte` | `DateTime` | Unspecified | Not described by the schema. |
| `lt` | `DateTime` | Unspecified | Not described by the schema. |
| `nlt` | `DateTime` | Unspecified | Not described by the schema. |
| `lte` | `DateTime` | Unspecified | Not described by the schema. |
| `nlte` | `DateTime` | Unspecified | Not described by the schema. |

## DateOperationFilterInput

No type description supplied.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `eq` | `Date` | Unspecified | Not described by the schema. |
| `neq` | `Date` | Unspecified | Not described by the schema. |
| `in` | `[Date]` | Unspecified | Not described by the schema. |
| `nin` | `[Date]` | Unspecified | Not described by the schema. |
| `gt` | `Date` | Unspecified | Not described by the schema. |
| `ngt` | `Date` | Unspecified | Not described by the schema. |
| `gte` | `Date` | Unspecified | Not described by the schema. |
| `ngte` | `Date` | Unspecified | Not described by the schema. |
| `lt` | `Date` | Unspecified | Not described by the schema. |
| `nlt` | `Date` | Unspecified | Not described by the schema. |
| `lte` | `Date` | Unspecified | Not described by the schema. |
| `nlte` | `Date` | Unspecified | Not described by the schema. |

## BooleanOperationFilterInput

No type description supplied.

| Input field | Exact type | Default | Schema description |
| --- | --- | --- | --- |
| `eq` | `Boolean` | Unspecified | Not described by the schema. |
| `neq` | `Boolean` | Unspecified | Not described by the schema. |
