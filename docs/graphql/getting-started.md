# Making a Momentum GraphQL request

## Connection and authentication

The module's existing configuration contains `AuthUrl`, `GraphQlUrl`, `Username`, and `Password`. Keep these in the configured secret store; never paste real credentials or access tokens into documentation, query files, source control, or logs. Both URLs must use HTTPS. The hostnames are deliberately omitted from this guide.

Authentication is a JSON POST to `AuthUrl` with this shape:

```json
{
  "method": "password",
  "identifier": "YOUR_API_USERNAME",
  "key": "YOUR_API_PASSWORD",
  "requestrefreshtoken": false
}
```

The successful response used by the module has `completed.accessToken`; the API model also exposes `completed.expiresInSeconds`. The research used no refresh token and did not test refresh-token behavior. Production code may request one while still using only the returned access token.

Send subsequent requests to `GraphQlUrl` with `Content-Type: application/json`, `Accept: application/json`, and `Authorization: Bearer ACCESS_TOKEN`. Do not follow redirects to unverified hosts with credentials. Use timeouts and cancellation. Authentication failures and non-success HTTP results must stop the operation; do not print the raw authentication response.

## Think of GraphQL as selecting fields

You name a root query and select the fields you need beneath it. The JSON result follows that selection. Object fields need a nested selection; scalar fields such as dates and numbers do not. Values omitted from a selection are not returned automatically. Variables carry changing arguments separately from query text. [GraphQL queries and variables](https://graphql.org/learn/queries/).

This is a **diagnostic date query**, not a replacement for the full accounting export query:

```graphql
query InspectLedgerNoteDates($lastLocalId: Long!) {
  ledgerNoteAccountingsSync(lastLocalId: $lastLocalId) {
    pageInfo {
      hasNextPage
      endCursor
    }
    nodes {
      localId
      ledgerNote {
        id
        statusCreated
        due
      }
    }
  }
}
```

Variables are a separate JSON object:

```json
{
  "lastLocalId": 0
}
```

The HTTP JSON body is an object with `query` containing the query text and `variables` containing the variables object. `Long!` is deliberate: the live Momentum schema uses a 64-bit integer for this cursor, not GraphQL's `Int`. A required variable may be supplied to the schema's nullable `lastLocalId: Long` argument.

Do not select a date, customer, amount, or status filter for the durable production feed just because a debugging report needs that filter. First fetch the approved feed correctly; isolate one-off report filtering from the delivery checkpoint and permanent reservation history.

## Reading types and nulls

Examples from this schema:

| Declaration | Meaning for a consumer |
| --- | --- |
| `localId: Long!` | A present accounting node has a non-null cursor. It is not a display invoice number. |
| `ledgerNote: LedgerNote!` | A present accounting node has a non-null ledger note, subject to GraphQL error propagation. |
| `nodes: [LedgerNoteAccounting!]` | The list can be null; an existing item cannot be null. Do not silently turn a failed/null connection into “no invoices.” |
| `statusCreated: DateTime` | The field may legitimately be null, even though the observed sample was populated. |
| `invoice: Invoice` | A note need not have an associated Faktura invoice. |
| `due: Date` | A calendar date, not a timestamp or an implied midnight UTC instant. |

Preserve offset-aware timestamps as such. Do not convert a source timestamp to a date using the developer machine's timezone implicitly. For a report such as “created today,” define the intended timezone, for example `Europe/Stockholm`, and compare the calendar day in that zone.

## Errors and safe retry

GraphQL can return an HTTP-success response that still includes `errors`, and a response can contain partial `data` alongside errors. Check the GraphQL envelope, not only the HTTP status. Do not export a partial accounting response or advance its checkpoint. [GraphQL response and errors](https://graphql.org/learn/response/).

Keep authentication and schema errors separate from an empty successful `nodes` list. Record safe context such as operation name, counts, local checkpoint, request correlation ID if available, and sanitized error category. Never log credentials or raw invoice JSON as routine diagnostics.

A failed **read** can be retried without intentionally changing remote accounting state, but fetching again is not permission to re-deliver an invoice. Local reservation and delivery-confirmation checks still govern whether bytes can be released.

## Ask the API what it supports

This read-only introspection query retrieves the description and exact type nesting for one type:

```graphql
query InspectLedgerNote {
  __type(name: "LedgerNote") {
    name
    description
    fields(includeDeprecated: true) {
      name
      description
      isDeprecated
      deprecationReason
      type {
        kind
        name
        ofType {
          kind
          name
          ofType {
            kind
            name
          }
        }
      }
    }
  }
}
```

Use `__type(name: "Query")` and select each field's `args { name description defaultValue type { kind name ofType { kind name } } }` to inspect root arguments. For an input object, select `inputFields` instead of `fields`. This is discovery; it does not run the business query being described.

The [generated references](README.md) preserve all nesting needed for the documented declarations. Schema descriptions occasionally use generic wording or typos; consult the actual field type, especially where a description says “date and time” but the scalar is `Date`.
