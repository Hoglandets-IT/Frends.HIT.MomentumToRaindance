# Frends.HIT.MomentumToRaindance

Frends task package for fetching Momentum ledger note accountings and converting them into a Raindance fixed-width import file.

## Tasks

### Fetch Ledger Note Accountings

`Frends.HIT.MomentumToRaindance.Main.FetchLedgerNoteAccountings`

Calls Momentum auth and GraphQL endpoints, then returns:

- `ResultFile`: UTF-8 string, pretty-printed raw GraphQL JSON.

Connection settings can be supplied in the same style as `RemoteFS`:

- `JSON String`: paste a JSON configuration into `JsonConfiguration`.
- `Hashicorp Vault`: provide `VaultPath`; the task reads the secret using the Infisical environment variables.
- `Manual Config`: enter the fields directly in Frends.

Valid JSON configuration:

```json
{
  "authurl": "https://example.invalid/momentum/auth",
  "graphqlurl": "https://example.invalid/momentum/graphql",
  "username": "momentum-username",
  "password": "Momentum API key"
}
```

For `Hashicorp Vault`, the secret value must contain the same JSON object. The task uses the same Infisical environment variables as `RemoteFS`: `INFISICAL_ADDR`, `INFISICAL_CLIENT_ID`, `INFISICAL_CLIENT_SECRET`, `INFISICAL_PROJECT`, and `INFISICAL_ENVIRONMENT`.

### Convert GraphQL Result

`Frends.HIT.MomentumToRaindance.Main.ConvertGraphQlResult`

Accepts a UTF-8 byte stream containing a Momentum GraphQL JSON response and returns:

- `ResultFile`: Raindance fixed-width string. Persist to disk/SFTP using ISO-8859-1/Latin-1 encoding.
- `NodeCount`: number of Momentum nodes converted.

## Build

```bash
dotnet build Frends.HIT.MomentumToRaindance.sln
dotnet pack --configuration Release --include-source --output . Frends.HIT.MomentumToRaindance/Frends.HIT.MomentumToRaindance.csproj
```

## Generate a file locally

Put the Momentum JSON configuration in `.env`, then run:

```bash
./generate-raindance.sh ./out/raindance.txt
```

The wrapper fetches all nodes after local ID `0`, converts them with the task package, and writes the output using ISO-8859-1/Latin-1. To fetch only newer nodes:

```bash
./generate-raindance.sh ./out/raindance.txt --last-local-id 123
```

To save the exact prettified Momentum response used for the conversion as UTF-8 JSON:

```bash
./generate-raindance.sh ./out/raindance.txt --json-output ./out/momentum.json
```

Run `./generate-raindance.sh --help` for all options.

The GraphQL query is embedded into the DLL as a resource; no query file has to be deployed beside the package.

## Output Notes

The Raindance output follows the mapping spreadsheet:

```text
S customer record
H invoice header
R invoice row
K accounting row
```

The H-record invoice-number field at positions 200–209 is intentionally blank so Raindance assigns the invoice number. Each R-record row text ends with the same formatted periodisation used at K positions 175–184. When necessary, the original row text is shortened so the period remains visible within the 60-character field.

The Raindance byte stream is ISO-8859-1/Latin-1 encoded. If the file is opened as UTF-8 in an editor, Swedish characters will appear broken even though the bytes are correct for the target format.
