# Ledger-note, ledger, and invoice types

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

## LedgerNote

A note in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The note's universally unique identifier. |
| `number` | `Long!` | The ledger note´s unique number. |
| `statusRemoved` | `Boolean!` | Indicates if the ledger note is removed. |
| `statusDraft` | `Boolean!` | Indicates if the ledger note is in draft state. |
| `statusReplaced` | `Boolean!` | Indicates if the ledger note is replaced. |
| `statusCreated` | `DateTime` | Date and time when the note was first created. |
| `accountingPeriod` | `AccountingPeriod` | The ledger notes's accounting period. |
| `transactionCreated` | `Transaction` | The transaction that created the ledger. |
| `transactionReplaced` | `Transaction` | The transaction that replaced the ledger. |
| `type` | `LedgerNoteType` | The note type. |
| `due` | `Date` | The note's due date.<br>Returns deferral date if set (dueDeferral), otherwise note's new due date (dueNew), otherwise original due date (dueOriginal). |
| `dueOriginal` | `Date` | The note's original due date when it was first created. |
| `dueNew` | `Date` | The note's due date if a new due date has been set (currenlty used on only rental ledger), otherwise null. |
| `dueDeferral` | `Date` | The note's deferral date if set (currently used on only invoices), otherwise null. |
| `refersToPeriodDisplayName` | `String` | The display name of the period the ledger note refers to. |
| `company` | `Company` | The company connected to the note. |
| `customer` | `Customer` | The customer connected to the note. |
| `object` | `Object` | The object the note is connected to. |
| `contract` | `Contract` | The contract the note is connected to. |
| `property` | `Property` | The property that the note is connected to. |
| `ledgerGroup` | `LedgerGroup` | The ledger group that the note is connected to. |
| `invoice` | `Invoice` | The invoice that the note is connected to and created from. |
| `fullyPaid` | `Date` | The date that the final playent was made. |
| `referenceNumber` | `String` | The ledger note's payment reference number. E.g. OCR, bankgiro, autogiro. |
| `payToNumber` | `String` | The ledger note's pay to number. E.g. `123-1234`. |
| `payToTypeDisplayName` | `String` | The ledger note's pay to number typ. E.g. `Bankgiro`. |
| `debitAmount` | `Decimal` | Debit amount of the note. |
| `balanceAmount` | `Decimal` | Balance amount of the note. |
| `toPay` | `Decimal` | The sum left to pay on the note in SEK. |
| `ledgers(where: LedgerNoteLedgerFilterInput, order: [LedgerNoteLedgerSortInput!])` | `[LedgerNoteLedger!]` | All ledgers that are related to the note. |
| `messages(where: LedgerNoteMessageFilterInput, order: [LedgerNoteMessageSortInput!])` | `[LedgerNoteMessage!]` | All messages that are related to the note. |

## Ledger

A ledger in the system

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger's universally unique identifier. |
| `statusRemoved` | `Boolean!` | Indicates if the ledger is removed. |
| `statusDraft` | `Boolean!` | Indicates if the ledger is in draft state. |
| `statusApproved` | `DateTime` | The date and time that the ledger was approved. Is null if status Draft is true. |
| `statusCreated` | `DateTime` | Date and time when the ledger was created. |
| `statusReplacedDate` | `DateTime` | Date and time when the ledger replace was approved. |
| `statusReplaced` | `Boolean!` | Indicates if the ledger is replaced. |
| `type` | `LedgerType` | The ledger type. |
| `group` | `LedgerGroup` | The ledger group that the ledger belongs to. |
| `accountingPeriod` | `AccountingPeriod` | The ledger's accounting period. |
| `transactionCreated` | `Transaction` | The transaction that created the ledger. |
| `transactionReplaced` | `Transaction` | The transaction that replaced the ledger. |
| `statusStart` | `Date` | Date and time from which the ledger is in effect. |
| `statusEnd` | `Date` | Date and time from which the ledger is no longer in effect. |
| `due` | `Date` | The ledger's due date.<br>Returns deferral date if set (dueDeferral), otherwise ledger's new due date (dueNew), otherwise original due date (dueOriginal). |
| `dueOriginal` | `Date` | The ledger's original due date when it was first created. |
| `dueNew` | `Date` | The ledger's due date if a new due date has been set (currenlty used on only rental ledger), otherwise null. |
| `dueDeferral` | `Date` | The ledger's deferral date if set (currently used on only invoices), otherwise null. |
| `paymentReceiver` | `ActorPayment` | Inforamtion about who will receive incomming payments. |
| `referenceNumber` | `String` | The related ledger notes's payment reference number. E.x. OCR, bankgiro, autogiro |
| `object` | `Object` | The object that the ledger is connected to. |
| `company` | `Company` | The company that the ledger is connected to. |
| `customer` | `Customer` | The customer connected to the ledger. |
| `invoice` | `Invoice` | The invoice that the ledger is connected to and created from. |
| `netAmount` | `Decimal!` | Net amount of the ledger in SEK. |
| `totalAmount` | `Decimal!` | The total value of the ledger in SEK. |
| `toPay` | `Decimal` | The sum left to pay on the ledger in SEK. |
| `vatCreated` | `Date` | The vat reporting date of the ledger. Used as accountingDate when accountingRecords are created. |
| `vatReplaced` | `Date` | The vat reporting date when the ledger was removed/replaced. Based on the approval date of the ledger but takes locked periods into account. Used as `accountingDate` for vat reports. |
| `vatAmount` | `Decimal!` | Value added tax amount of the ledger in SEK. |
| `vatAmountBase` | `Decimal!` | The net amount value added tax is calculated on (the basis for vat). |
| `accountingCreated` | `Date` | The accounting date of the ledger when it is considered created. The date is adjusted according to locked periods at the time of ledger creation. Used as accountingDate for accounting records. |
| `accountingReplaced` | `Date` | The accounting date of the ledger when it is considered replaced. The date is adjusted according to locked periods at the time of ledger replacement. Used as accountingDate for accounting records. |
| `rows(where: LedgerRowFilterInput, order: [LedgerRowSortInput!])` | `[LedgerRow!]` | All ledger rows who are and have been related to the ledger. |
| `ledgerNotes(where: LedgerNoteLedgerFilterInput, order: [LedgerNoteLedgerSortInput!])` | `[LedgerNoteLedger!]` | All ledger rows who are and have been related to the ledger. |

## LedgerRow

A ledger row in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger row's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the ledger row was removed. Is null if not removed. |
| `type` | `LedgerRowType` | The ledger row type. |
| `object` | `Object` | The object connected to the ledger row. |
| `contract` | `Contract` | The contract connected to the ledger row. |
| `pricing` | `Pricing` | The pricing which the ledger row is based on. |
| `vatType` | `VatType` | The ledger row's vat type. |
| `vatAmount` | `Decimal!` | Vat amount of a ledger row in SEK. |
| `netAmount` | `Decimal!` | Net amount of a ledger row in SEK. |
| `distributionAmount` | `Decimal!` | Distribution amount of a ledger row in SEK. |
| `ledger` | `Ledger` | The ledger which the ledger row is connected to. |
| `text` | `LedgerRowText` | The ledger row's text |

## LedgerRowText

Text related to a ledger row in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `text` | `String` | The ledger row's description text. |
| `textDetailed` | `String` | The ledger row's detailed description text. |

## LedgerNoteLedger

The relation between ledger and note

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `statusRemoved` | `Boolean` | Indicates if the relation is marked as removed. |
| `ledgerNote` | `LedgerNote!` | The note |
| `ledger` | `Ledger!` | The ledger. |

## LedgerNoteType

a note type in the system

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The note types's universally unique identifier. |
| `displayName` | `String` | Display name of the note type |
| `statusRemoved` | `DateTime` | The date and time that the note type was removed. Is null if not removed |

## LedgerType

A ledger type in the system

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger types's universally unique identifier. |
| `displayName` | `String` | Display name of the ledger type |
| `statusRemoved` | `DateTime` | The date and time that the ledger type was removed. Is null if not removed |

## LedgerRowType

A ledger row type in the system

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger row types's universally unique identifier. |
| `displayName` | `String` | Display name of the ledger row type |
| `statusRemoved` | `DateTime` | The date and time that the ledger row type was removed. Is null if not removed |
| `order` | `Byte` | The ordering of how ledger rows is displayed under a ledger in the system. From low to high, where low is higher priority. |

## Invoice

An invoice in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The invoice's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the invoice was removed. Is null if not removed. |
| `number` | `String` | The invoice number. |
| `billed` | `Date` | The date that the invoice was billed. Is null if not billed |
| `transactionCreated` | `Transaction` | The transaction that created the invoice. |
| `type` | `InvoiceType` | The invoice's type. |
| `ledgerPrimary` | `Ledger` | The invoice's main ledger. |
| `paymentTermsType` | `InvoicePaymentTermsType` | The invoice's payment terms type. |
| `creditInvoice` | `Invoice` | The invoice that is credited by this invoice. If this field has a value, the invoice is a credit invoice. |
| `rows(where: InvoiceRowFilterInput, order: [InvoiceRowSortInput!])` | `[InvoiceRow!]` | All invoice rows who are and have been related to the invoice. |
| `customers(where: InvoiceCustomerFilterInput, order: [InvoiceCustomerSortInput!])` | `[InvoiceCustomer!]` | All customer who are and have been related to the invoice. |
| `objectAndContract` | `InvoiceObjectContract` | The connection between the invoice and an object and/or contract. |

## InvoiceRow

An invoice row in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The invoice row's universally unique identifier. |
| `statusHasVAT` | `Boolean` | Indicates if the invoice row has VAT or not. |
| `statusRemoved` | `DateTime` | The date and time that the invoice row was removed. Is null if not removed. |
| `invoice` | `Invoice` | The invoice that the invoice row is connected to. |
| `ledgerRow` | `LedgerRow` | The ledgerRow that the invoice row is connected to. |
| `article` | `Article` | The article that the invoice row is connected to. |
| `text` | `String` | The invoice row's text. |
| `quantity` | `Decimal` | The quantity of items relted to the invoice row. Null functions the same as 1. |
| `price` | `Decimal` | The price a single item realted to the invoice row. |
| `accountDistribution` | `AccountDistribution` | The invoice row's account distribution. |

## InvoiceType

An invoice type in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The invoice type's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the invoice type was removed. Is null if not removed. |
| `displayName` | `String` | The invoice type's display name. |
| `shortDisplayName` | `String` | Short display name of the invoice type. |
| `ledgerType` | `LedgerType` | The ledger type related to the invoice type. |

## InvoiceCustomer

A reltion between an invoice and a customer in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `invoice` | `Invoice!` | The invoice that the customer is connected to. |
| `customer` | `Customer!` | The customer that the invoice is connected to. |
| `statusRemoved` | `DateTime` | The date and time that the connection was removed. Is null if not removed. |

## InvoiceObjectContract

Describes the relationship between an invoice and an object or contract in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `invoice` | `Invoice` | The invoice that the object or contract is connected to. |
| `object` | `Object` | The object that the invoice is connected to. |
| `contract` | `Contract` | The contract that the invoice is connected to. |

## InvoicePaymentTermsType

An invoice payment terms type in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The invoice payment terms type's universally unique identifier. |
| `statusIsAfterInvoiceDate` | `Boolean` | indicates if invoice payment terms type is used after the invoice date has passed. |
| `statusIsBeforeContractStart` | `Boolean` | indicates if invoice payment terms type is used before the start date of a contract. |
| `statusRemoved` | `DateTime` | The date and time that the invoice payment terms type was removed. Is null if not removed. |
| `displayName` | `String` | The invoice payment terms type's display name. |
| `shortDisplayName` | `String` | Short display name of the invoice payment terms type. |
| `numberOfDays` | `Short` | The number of days invoice payment terms are in effect. |
