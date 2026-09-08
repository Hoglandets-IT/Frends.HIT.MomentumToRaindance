# Frends.HIT.MomentumToRaindance

Fetches invoice accounting data from Momentum GraphQL and converts it into the fixed-width Raindance import format used by this integration. The package exposes two Frends tasks so a process can inspect or transform the fetched JSON before conversion. It does not write files, send invoices, or store a checkpoint itself.

The task library targets **.NET 8**. The local command-line tool targets **.NET 10** and is not part of the Frends task package.

## Frends process setup

Use the tasks in this order:

```text
Read saved LastLocalId
  -> Fetch Ledger Note Accountings
  -> Optional JSON processing
  -> Convert GraphQL Result
  -> If NodeCount > 0: durably deliver the Latin-1 file
  -> Save the conversion result's LastLocalId after successful delivery
```

Pass the same saved checkpoint into both Fetch and Convert. Do not advance it from the fetched JSON yourself. On no work, the conversion returns an empty file and the input checkpoint unchanged; skip file delivery. Prevent overlapping runs for the same source/checkpoint. See [checkpoint and recovery rules](docs/operations.md) before production deployment.

### Fetch Ledger Note Accountings

Method: `Frends.HIT.MomentumToRaindance.Main.FetchLedgerNoteAccountings`

Inputs are `MomentumConnection`, `FetchInput`, and an optional `CancellationToken` supplied by the caller/Frends runtime. The task resolves connection settings, authenticates, executes the embedded `ledgerNoteAccountingsSync(lastLocalId: ...)` query once, and returns prettified JSON. It does not acknowledge processing or delivery.

| Fetch input | Type / default | Meaning |
| --- | --- | --- |
| `LastLocalId` | `int`, `0` | Last successfully delivered source local ID. Must be nonnegative. Zero requests the initial batch. |

| Fetch result | Meaning |
| --- | --- |
| `Success` | `true` on return; failures throw rather than returning a partially successful result. |
| `ResultFile` | Prettified GraphQL JSON as a .NET `string`, not a byte array or file path. |
| `LastLocalId` | The supplied input checkpoint, unchanged even when new data was fetched. |
| `Info` | Human-readable operation summary; not a machine-readable status code. |

GraphQL errors, including partial data accompanied by errors, are failures. A valid empty `nodes` array is a successful fetch. Fetch validates the response envelope; invoice-level validation happens in Convert.

### Convert GraphQL Result

Method: `Frends.HIT.MomentumToRaindance.Main.ConvertGraphQlResult`

Takes `ConvertInput` and an optional `CancellationToken`. It makes no HTTP requests. It validates and converts the complete candidate batch before returning any output or advanced checkpoint.

| Convert input | Type / default | Meaning |
| --- | --- | --- |
| `GraphQlResult` | `string` | Complete GraphQL response, including `data.ledgerNoteAccountingsSync.nodes`. |
| `LastLocalId` | `int`, `0` | Same checkpoint supplied to Fetch. Nodes at or below it are excluded. |

| Conversion result | Meaning |
| --- | --- |
| `Success` | `true` on return. Invalid data or cancellation throws; no advanced checkpoint is returned. |
| `ResultFile` | Fixed-width Raindance text, with CRLF line endings and a final CRLF when nonempty. Write it using ISO-8859-1/Latin-1 without a BOM; do not trim its spaces or rewrite its line endings. |
| `Filename` | Suggested name for the later file-writer task, e.g. `300K24_20260908_143025.txt`. Generated once using the Frends agent's local time, in `yyyyMMdd_HHmmss` format. Available on empty results too; still skip writing when `NodeCount == 0`. |
| `NodeCount` | Number of invoices emitted, not the number of raw nodes, R records, or K records. |
| `LastLocalId` | Input value on no work; otherwise the highest handled local ID in the validated new batch. Persist only after successful file delivery. |
| `Info` | Human-readable summary. |

Nodes are ordered by ascending `localId`. Missing/nonpositive IDs and duplicate newer IDs are rejected. Explicitly empty invoices and invoices containing only the excluded rounding rows produce no output. In a mixed batch, those intentional no-op nodes are included in the highest handled ID; an entirely no-op batch leaves the checkpoint unchanged. See [the format mapping](docs/raindance-format.md) for supported input and validation rules.

## Connection settings

`MomentumConnection.ConfigurationSource` supports the following choices:

| Source | Required settings |
| --- | --- |
| `Json` / JSON String, default | `JsonConfiguration` containing the object below. |
| `Manual` / Manual Config | `AuthUrl`, `GraphQlUrl`, `Username`, `Password`. |
| `HcpVault` | `VaultPath` pointing to a secret containing the same JSON object. This is the canonical secret-store option; the current backend uses Infisical. |

```json
{
  "authurl": "https://example.invalid/momentum/auth",
  "graphqlurl": "https://example.invalid/momentum/graphql",
  "username": "momentum-username",
  "password": "replace-with-api-key"
}
```

`TimeoutSeconds` defaults to `100`, with an allowed range of `1`–`3600`. It is the overall Fetch timeout covering secret lookup, Momentum authentication, and GraphQL retrieval, not a fresh timeout for each request. Convert supports cancellation but has no separate timeout setting.

All endpoints require HTTPS and normal certificate validation; redirects are not followed. Install the appropriate private CA on the Frends agent if required. The task uses pooled HTTP connections, does not share cookies, and does not retry HTTP requests automatically. JSON configuration and password fields are marked sensitive in the task UI; also configure process logging so fetched invoice/customer data and output files are not exposed unnecessarily.

### HcpVault configuration

The `HcpVault` option currently uses the Infisical backend.

Set these environment variables on every Frends agent that may run the process:

| Variable | Purpose |
| --- | --- |
| `INFISICAL_ADDR` | HTTPS Infisical base URL, without query or fragment. |
| `INFISICAL_CLIENT_ID` | Universal Auth client ID. |
| `INFISICAL_CLIENT_SECRET` | Universal Auth client secret. |
| `INFISICAL_PROJECT` | Workspace/project ID used by the secrets API. |
| `INFISICAL_ENVIRONMENT` | Infisical environment slug. |

`VaultPath` is a secret path such as `/integrations/momentum/config`. Its last segment is the secret name. A bare name addresses a secret in `/`. For compatibility with the reference RemoteFS module, dots in the directory portion are replaced with underscores; the secret name itself is unchanged. URI components are encoded before sending the request. The identity needs permission to authenticate and read that specific secret.

## Generate files locally

Requires the .NET 10 SDK and Bash. Put the JSON connection object above in `.env` at the repository root. Despite its name, this file is **JSON, not `KEY=value` dotenv syntax**. Keep it private; `.env` and the `out/` directory are Git-ignored.

```bash
./generate-raindance.sh ./out/raindance.txt --last-local-id 123
```

To also save the prettified response used for this conversion:

```bash
./generate-raindance.sh ./out/raindance.txt \
  --last-local-id 123 \
  --json-output ./out/momentum.json
```

Use `--env /path/to/config.json` for another configuration file, or `--help` for all options. Configuration and output paths must be distinct and must not contain symbolic links. JSON dumps are UTF-8; Raindance files are ISO-8859-1/Latin-1. A JSON dump contains customer and invoice data: restrict access, retain it only as needed, and do not commit it.

The wrapper prints the returned checkpoint but does not save it for subsequent runs; supply `--last-local-id` deliberately each time. On no work it does not write a Raindance file; an existing file at that path is left untouched, so use a fresh output filename or check the console result before sending anything. With `--json-output`, the fetched JSON is written before conversion and can remain available for diagnosis even if conversion fails.

Opening the Raindance file as UTF-8 can make Swedish characters appear broken even when its bytes are correct. Use an editor that supports ISO-8859-1, and avoid resaving the file with a different encoding.

## Build and test

Build the Frends library separately when only a .NET 8 SDK is available:

```bash
dotnet build Frends.HIT.MomentumToRaindance/Frends.HIT.MomentumToRaindance.csproj --configuration Release
```

With the .NET 10 SDK available for the local tool, build the full solution and run its tests:

```bash
dotnet build Frends.HIT.MomentumToRaindance.sln --configuration Release
dotnet test Frends.HIT.MomentumToRaindance.sln --configuration Release
```

Tests target .NET 8 and permit major-version runtime roll-forward for local machines that only have .NET 10 installed. Run them with the .NET 8 runtime available in release validation to exercise the deployed runtime as well. The automated tests use synthetic data and stubbed HTTP responses, not production credentials.

Pack only the task project:

```bash
dotnet pack Frends.HIT.MomentumToRaindance/Frends.HIT.MomentumToRaindance.csproj \
  --configuration Release --output ./artifacts
```

The package includes the .NET 8 task assembly, XML API documentation, and root `FrendsTaskMetadata.json`. The GraphQL query is embedded in the DLL; no external query file, `.env`, or local CLI is required on the agent. Metadata lists exactly the two task entry points described above. Public signatures, property tabs, masked credential fields, and XML documentation follow the [Frends custom-task conventions](https://docs.frends.com/guides/development/creating-custom-tasks).

## Further documentation

- [Production audit results](docs/production-audit.md): fixes, verification evidence, and remaining deployment gates.
- [Raindance records and Momentum field mapping](docs/raindance-format.md): positions, debit/credit, period text, limits, and intentionally unused fields.
- [Production operations and recovery](docs/operations.md): deployment checks, checkpoint examples, retries, security, and failure handling.

The converter implements this integration's agreed layout, not every variant of Raindance or every Momentum event type. Automated tests complement, but do not replace, acceptance testing in the actual Raindance import configuration.
