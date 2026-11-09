# Date, lifecycle, and scalar types

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

## Transaction

A transaction in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The transaction's universally unique identifier. |
| `statusDraft` | `Boolean` | Indicates if the transaction is in draft state. |
| `statusRemoved` | `DateTime` | The date and time that the transaction was removed. Is null if not removed. |
| `statusApproved` | `DateTime` | The date and time that the transaction was approved. Is null if not approved. |
| `change` | `Change` | The change connected to the transaction. |
| `process` | `ProcessTransaction` | The process that the transaction is connected to. |

## Change

A change in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The change's universally unique identifier. |
| `statusCreated` | `DateTime` | The date and time when the change was created. |
| `internalComment` | `String` | The change's internalComment. |
| `source` | `String` | The source of the change. |

## ProcessTransaction

The reation between a process and a transaction in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `process` | `Process` | The process. |
| `transaction` | `Transaction` | The transaction. |

## EntityChangeLog

This is currently in `beta` and is subject to change. Use with caution.
Stores every change made to an entity and its change date.
LocalId is incremental, therefore store LocalId locally to keep track of the order of changes.
Every row gets deleted after 7 days.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `entityId` | `String!` | The changed entity's universally unique identifier. |
| `statusCreated` | `DateTime!` | The date and time that the entity was changed. |
| `pruned` | `Boolean!` | Signals that the log has been pruned and that this row now is the first row in the log. |
| `type` | `EntityType!` | The changed entity's type. |
| `localId` | `Long!` | Incremental and unique id that makes sure that the changes is in correct order.<br>Id max is always latest change. |

## EntityChange

No type description supplied.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `localId` | `Long!` | Incremental and unique id that makes sure that the changes is in correct order.<br>Id max is always latest change. |
| `entityId` | `String!` | The changed entity's universally unique identifier. |
| `entityTypeId` | `String!` | The type's id of the entity that has changed. |
| `userIds` | `[String!]` | The users that should be notified by the change, if applicable. |

## LedgerNoteMessage

A ledger note message in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note message type's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the ledger note message was removed. Is null if not removed. |
| `statusStart` | `Date!` | The date when the ledger note message first become active. |
| `statusEnd` | `Date!` | The date when the ledger note message will expire. |
| `transactionCreated` | `Transaction` | The transaction that created the ledger note message. |
| `ledgerNote` | `LedgerNote` | The ledger note this ledger note message is connected to. |
| `type` | `LedgerNoteMessageType` | The type of ledger note message |
| `heading` | `String` | The ledger note message's heading. |
| `text` | `String` | The ledger note message's text body. |

## LedgerNoteMessageType

A ledger note message type in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note message type's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the ledger note message type was removed. Is null if not removed. |
| `displayName` | `String` | The ledger note message type's display. |
| `heading` | `String` | The ledger note message type's main heading. |

## Upload

The `Upload` scalar type represents a file upload.


## String

The `String` scalar type represents textual data, represented as UTF-8 character sequences. The String type is most often used by GraphQL to represent free-form human-readable text.


## Boolean

The `Boolean` scalar type represents `true` or `false`.


## Int

The `Int` scalar type represents non-fractional signed whole numeric values. Int can represent values between -(2^31) and 2^31 - 1.


## MultiplierPath

The multiplier path scalar represents a valid GraphQL multiplier path string.


## Date

The `Date` scalar represents an ISO-8601 compliant date type.


## DateTime

The `DateTime` scalar represents an ISO-8601 compliant date time type.


## Long

The `Long` scalar type represents non-fractional signed whole 64-bit numeric values. Long can represent values between -(2^63) and 2^63 - 1.


## Decimal

The built-in `Decimal` scalar type.


## Short

The `Short` scalar type represents non-fractional signed whole 16-bit numeric values. Short can represent values between -(2^15) and 2^15 - 1.


## Byte

The `Byte` scalar type represents non-fractional whole numeric values. Byte can represent values between 0 and 255.
