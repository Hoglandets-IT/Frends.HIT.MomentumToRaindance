# Accounting sync and accounting-record types

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

## LedgerNoteAccounting

Contains connections of a ledger note accounting

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note accounting's universally unique identifier. |
| `ledgerNote` | `LedgerNote!` | The ledger note |
| `transactionCreated` | `Transaction!` | The transaction that created the ledger note accounting. |
| `changeType` | `LedgerAccountingChangeType!` | The type of change made |
| `company` | `Company` | The connected company |
| `distributions(where: LedgerNoteAccountingDistributionFilterInput, order: [LedgerNoteAccountingDistributionSortInput!])` | `[LedgerNoteAccountingDistribution!]` | The ledger accounting's connected ledger note accounting distribution. |
| `ledgers(where: LedgerAccountingFilterInput, order: [LedgerAccountingSortInput!])` | `[LedgerAccounting!]` | The ledger accounting's connected ledger note accounting. |
| `localId` | `Long!` | The ledger note accounting's local id value. |

## LedgerAccounting

Contains connections of a ledger accounting

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger accounting's universally unique identifier. |
| `ledger` | `Ledger!` | The ledger |
| `transactionCreated` | `Transaction!` | The transaction that created the ledger accounting. |
| `company` | `Company` | The connected company |
| `changeType` | `LedgerAccountingChangeType!` | The type of change made |
| `ledgerNoteAccounting` | `LedgerNoteAccounting` | The ledger note accounting this ledger accounting is connected to. |
| `rows(where: LedgerRowAccountingFilterInput, order: [LedgerRowAccountingSortInput!])` | `[LedgerRowAccounting!]` | The ledger row accounting's connected ledger accounting. |
| `localId` | `Long!` | The ledger accounting's local id value. |

## LedgerRowAccounting

Contains connections of a ledger row accounting

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger row accounting's universally unique identifier. |
| `ledgerRow` | `LedgerRow!` | The ledger row |
| `transactionCreated` | `Transaction!` | The transaction that created the ledger row accounting. |
| `changeType` | `LedgerAccountingChangeType!` | The type of change made |
| `ledgerAccounting` | `LedgerAccounting` | The ledger accounting this ledger row accounting is connected to. |
| `records(where: LedgerRowAccountingRecordFilterInput, order: [LedgerRowAccountingRecordSortInput!])` | `[LedgerRowAccountingRecord!]` | The ledger row accounting records's connected ledger row accounting. |
| `localId` | `Long!` | The ledger row accounting's local id value. |

## LedgerRowAccountingRecord

Contains connections of a ledger row accounting record

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger row accounting record's universally unique identifier. |
| `accountingRecord` | `AccountingRecord` | The accounting record |
| `transactionCreated` | `Transaction!` | The transaction that created the ledger row accounting record. |
| `changeType` | `LedgerAccountingChangeType!` | The type of change made |
| `amount` | `Decimal!` | The ledger row accounting record's amount in SEK. |
| `debit` | `Boolean!` | Indicates if the ledger row accounting record has debit or not. |
| `accountDistributionCoding` | `String` | The ledger row accounting record's account distribution coding. |
| `accountDistributionCodingType` | `AccountDistributionCodingType!` | The ledger row accounting record's account distribution coding type. |
| `accountingPeriod` | `AccountingPeriod!` | The ledger row accounting record's accounting period. |
| `accountingDate` | `Date` | The ledger row accounting record's accounting date. |
| `ledgerRowAccounting` | `LedgerRowAccounting` | The ledger row accounting this ledger row accounting record is connected to. |
| `localId` | `Long!` | The ledger row accounting record's local id value. |

## LedgerAccountingChangeType

The change type of a row in ledger accounting

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The change type's universally unique identifier. |
| `displayName` | `String` | Display name of the change type |

## LedgerNoteAccountingsSyncConnection

A connection to a list of items.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `pageInfo` | `PageInfo!` | Information to aid in pagination. |
| `edges` | `[LedgerNoteAccountingsSyncEdge!]` | A list of edges. |
| `nodes` | `[LedgerNoteAccounting!]` | A flattened list of the nodes. |
| `totalCount` | `Int!` | Identifies the total count of items in the connection. |

## LedgerNoteAccountingsSyncEdge

An edge in a connection.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `cursor` | `String!` | A cursor for use in pagination. |
| `node` | `LedgerNoteAccounting!` | The item at the end of the edge. |

## LedgerAccountingsSyncConnection

A connection to a list of items.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `pageInfo` | `PageInfo!` | Information to aid in pagination. |
| `edges` | `[LedgerAccountingsSyncEdge!]` | A list of edges. |
| `nodes` | `[LedgerAccounting!]` | A flattened list of the nodes. |
| `totalCount` | `Int!` | Identifies the total count of items in the connection. |

## LedgerAccountingsSyncEdge

An edge in a connection.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `cursor` | `String!` | A cursor for use in pagination. |
| `node` | `LedgerAccounting!` | The item at the end of the edge. |

## PageInfo

Information about pagination in a connection.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `hasNextPage` | `Boolean!` | Indicates whether more edges exist following the set defined by the clients arguments. |
| `hasPreviousPage` | `Boolean!` | Indicates whether more edges exist prior the set defined by the clients arguments. |
| `startCursor` | `String` | When paginating backwards, the cursor to continue. |
| `endCursor` | `String` | When paginating forwards, the cursor to continue. |

## AccountingRecord

An accounting record row in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The accounting record's universally unique identifier. |
| `statusRemovedDate` | `DateTime` | The date and time that the accounting record was removed. Is null if not removed. |
| `statusRemoved` | `Boolean!` | Indicates if the accounting record is removed. |
| `statusDraft` | `Boolean!` | Indicates if the accounting record is in draft state. E.g. The accounting record has been created but not accepted or has been removed. |
| `statusReversed` | `Boolean!` | Indicates if the accounting record is cancelled or not. |
| `statusHasDebit` | `Boolean!` | Indicates if the  accounting record has debit or not. |
| `statusIsPeriodized` | `Boolean!` | Indicates if the accounting record is periodized or not. |
| `transactionCreated` | `Transaction` | The transaction that created the accounting record. |
| `accountingPeriod` | `AccountingPeriod` | The accounting record's accounting period. |
| `ledgerRow` | `LedgerRow` | The ledger row connected to the accounting record. |
| `paymentPart` | `PaymentPart` | The payment part connected to the accounting record. |
| `accountDistributionCoding` | `String` | The accounting record's account distribution coding. |
| `amount` | `Decimal` | The accounting record's amount in SEK . |
| `transactionCancelled` | `Transaction` | The transaction that cancelled the accounting record. |
| `accountDistributionRow` | `AccountDistributionRow` | The account distribution row connected to the accounting record. Only exists when `Direktkonterad`! |
| `accountDistributionTemplateRow` | `AccountDistributionTemplateRow` | The account distribution template row connected to the accounting record. |
| `accountDistributionCodingType` | `AccountDistributionCodingType` | The accounting record's account distribution coding type. |
| `accountingDate` | `DateTime` | The accounting record's accounting date. |
| `periodizationOfAccountingPeriod` | `AccountingPeriod` | The accounting period that is periodized by the accounting. |
| `accountingVerificationRow` | `AccountingVerificationRow` | The accounting record's accounting verification row. |
| `cancelledByAccountingRecord` | `AccountingRecord` | The accounting record that cancelled this accounting record. |
| `cancelsAccountingRecords(where: AccountingRecordFilterInput, order: [AccountingRecordSortInput!])` | `[AccountingRecord!]` | All accounting records cancelled by this accounting record. |

## AccountingPeriod

An accounting period in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `statusStart` | `Date` | The start date of the accounting period. |
| `statusEnd` | `Date` | The end date of the accounting period. |
| `displayName` | `String` | The accounting period's display name. |

## AccountingVerification

An accounting verification in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The accounting verification's universally unique identifier. |
| `statusRemovedDate` | `DateTime` | The date and time that the accounting verification was removed. Is null if not removed. |
| `statusRemoved` | `Boolean!` | Indicates if the accounting verification is removed. |
| `statusIsCorrection` | `Boolean!` | Indicates if the accounting verification is a correction or not. |
| `statusDraft` | `Boolean!` | Indicates if the accounting verification is in draft state. E.g. The accounting verification has been created but not accepted or has been removed. |
| `transactionCreated` | `Transaction` | The transaction that created the accounting verification. |
| `sourceTransaction` | `Transaction` | The accounting verification source's transaction. |
| `accountingPeriod` | `AccountingPeriod` | The accounting verification's accounting period. |
| `sourceAccountingPeriod` | `AccountingPeriod` | The accounting verification source's accounting period. |
| `accountingDate` | `Date` | The date of the accounting. |
| `verificationNumber` | `Long` | The accounting verification's verification number. |
| `debitAmount` | `Decimal` | The accounting verification's debit amount in SEK. |
| `creditAmount` | `Decimal` | The accounting verification's credit amount in SEK. |
| `invoice` | `Invoice` | The invoice connected to the accounting verification. |
| `paymentPart` | `PaymentPart` | The payment part connected to the accounting verification. |
| `paymentPartTransaction` | `PaymentPartTransfer` | The payment part transaction connected to the accounting verification. |
| `paymentMethodType` | `PaymentMethodType` | The accounting verification's payment method type. |
| `property` | `Property` | The property connected to the accounting verification. |
| `company` | `Company` | The company connected to the accounting verification. |
| `verificationText` | `String` | The accounting verification's verification text. |
| `periodizationOfAccountingPeriod` | `AccountingPeriod` | The accounting period that is periodized by the accounting. |
| `rows(where: AccountingVerificationRowFilterInput, order: [AccountingVerificationRowSortInput!])` | `[AccountingVerificationRow!]` | All verification rows related to the accounting verification. |

## AccountingVerificationRow

An accounting verification row in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The accounting verification row's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the accounting verification row was removed. Is null if not removed. |
| `statusIsCancellation` | `Boolean` | Indicates if the accounting verification row is a cancellation or not. |
| `statusIsPeriodized` | `Boolean` | Indicates if the accounting verification row is periodized or not. |
| `accountingVerification` | `AccountingVerification` | The accounting verification that the accounting verification row is a part of. |
| `debitAmount` | `Decimal` | The accounting verification's debit amount in SEK. |
| `creditAmount` | `Decimal` | The accounting verification's credit amount in SEK. |
| `ledger` | `Ledger` | The ledger connected to the accounting verification row. |
| `accountingRecords(where: AccountingRecordFilterInput, order: [AccountingRecordSortInput!])` | `[AccountingRecord!]` | All accounting records related to the accounting verification row. |
