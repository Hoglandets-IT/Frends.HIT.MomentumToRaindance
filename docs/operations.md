# Production operations and recovery

## Responsibility boundary

This package fetches and converts. The surrounding Frends process owns source selection, scheduling, delivery, checkpoint storage, monitoring, and recovery. The Fetch and Convert tasks remain separate intentionally, so intermediate JSON inspection or processing is possible.

Treat `LastLocalId` as a durable delivery checkpoint, not as the largest number seen in a response. Use one checkpoint and one serialized execution stream per source account/query scope. Never share a checkpoint between unrelated Momentum environments or companies unless the source API explicitly defines a common sequence for them.

## Checkpoint contract

1. Read the persisted nonnegative checkpoint.
2. Pass it to Fetch as `FetchInput.LastLocalId`.
3. Pass the returned JSON and that same checkpoint to Convert as `ConvertInput.GraphQlResult` and `ConvertInput.LastLocalId`.
4. If `NodeCount == 0`, do not deliver an empty invoice file. `ResultFile` is empty and `LastLocalId` is unchanged.
5. Otherwise, write/deliver the complete `ResultFile` as ISO-8859-1/Latin-1, preserving all spaces and CRLFs.
6. Only after the delivery step succeeds under your operational acceptance policy, persist **the conversion result's** `LastLocalId`.

Example with input checkpoint `120`:

| Response / outcome | Conversion output | Returned checkpoint |
| --- | --- | ---: |
| Empty `nodes` | Empty file, zero invoices | `120` |
| Only nodes at/below `120` | Empty file, zero invoices | `120` |
| New valid invoice nodes `124`, `122` | Two invoices, ordered `122`, `124` | `124` |
| New nodes `121`, `122`, both explicitly empty or rounding-only | Empty file, zero invoices | `120` |
| Invoice `121`, intentional no-op `122` | One invoice | `122` |
| Valid invoice `121`, malformed invoice `122` | Exception; no result | None; retain `120` |
| Valid conversion, failed file delivery | Do not save conversion checkpoint | Persisted value remains `120` |

Fetch always returns `120` in these examples if it succeeds; fetching alone never advances the checkpoint. Convert also filters stale nodes itself, rather than relying entirely on the API's filter. Gaps in local IDs are allowed; duplicate newer IDs are not.

An all-no-op batch can therefore be fetched again on the next run. This is intentional: the specified no-work contract preserves the input value. If the source repeatedly returns only such nodes and prevents reaching subsequent data, investigate its batching contract before deciding on an explicit operational acknowledgement.

### Intermediate JSON processing

Preserve the GraphQL response envelope, valid node local IDs, and the full intended batch. Do not remove a problematic earlier invoice and then commit the higher checkpoint from a later invoice: that would permanently skip the removed item. Any filtering/merging performed outside this package must have its own explicit acknowledgement policy.

Using `ConvertInput.LastLocalId = 0` with an old saved dump intentionally enables replay. Do not use that default accidentally in a scheduled production process.

### Delivery is not an exactly-once transaction

The package does not atomically commit a remote file and a checkpoint. A crash after delivery but before checkpoint storage can cause the same batch to be sent again. A stored checkpoint after a failed or incomplete delivery can cause invoices to be lost.

Use a durable outbox or equivalent delivery ledger if available. At minimum, record the input/output checkpoint, output checksum, filename, invoice count, and delivery status together in restricted operational storage. Write to a temporary destination and rename/commit only when complete if the file transport supports that convention. This behaviour belongs in the surrounding process, not this converter.

After an ambiguous timeout or restart, reconcile the file and Raindance import status before retrying or manually moving the checkpoint. Do not reset the checkpoint to zero as a generic recovery action. Back up checkpoint state and document who may alter it.

## Before enabling scheduled production runs

- Install the package on a Frends agent compatible with its .NET 8 target; check that both tasks, their property tabs, XML help, and results appear correctly.
- Supply least-privilege Momentum credentials. If using HcpVault, verify the Infisical backend's agent environment variables and secret-path access. Ensure TLS trust is configured without certificate-validation bypasses.
- Confirm `ledgerNoteAccountingsSync(lastLocalId)` returns a complete safe batch for advancing to its maximum ID: monotonically advancing IDs in the selected scope, no unreported earlier nodes, and documented paging/retention semantics. The current query performs one request and does not iterate page tokens.
- Confirm the feed contains creation events. Non-`created` change types are deliberately rejected; this package does not apply update/delete events to existing Raindance invoices.
- Verify the [dimension convention and full field mapping](raindance-format.md), customer class strings, and supported VAT IDs against production source configuration. Currently only null VAT (`K00`) and `standard` (`K25`) are supported.
- Import synthetic/approved examples in Raindance test covering an ordinary invoice, a credit, VAT, split revenue coding, private and organisation customers, a one-month period, a multi-month period, and Swedish text. Verify invoice totals, customer matching, row printing, and K accounting periods in Raindance.
- Confirm defaults for deliberately blank H fields, especially invoice number, invoice date, and payment terms. The module does not copy Momentum's due date or invoice total into H.
- Verify the rounding-row exclusion and invoice-wide period source are appropriate. The exporter checks R/K revenue agreement, but does not reconcile the complete invoice against Momentum's `toPay` or calculate VAT totals.
- Configure ISO-8859-1/Latin-1 output, CRLF preservation, durable file delivery, checkpoint persistence order, run serialization, alerting, and the duplicate-delivery recovery procedure.
- Exercise failure paths: wrong credentials, unavailable endpoint, malformed JSON, unsupported characters, invalid invoice data, cancelled run, failed delivery, and checkpoint-store failure. The persisted checkpoint must remain safe.

Automated unit/integration tests are not evidence that a particular production Raindance configuration accepts the file. Keep importer acceptance and source batching confirmation as explicit deployment gates.

## Failure handling

Tasks throw on failure rather than returning `Success = false`. A successful empty batch still has `Success = true`. Branch on task success and `NodeCount`; do not parse `Info` strings.

| Symptom | Check / action |
| --- | --- |
| Authentication or HTTP failure | Check endpoint configuration, credential permissions, service health, and HTTP status. Configure bounded retries in the process only after considering delivery state. |
| TLS failure | Check certificates, hostnames, expiry, and the agent's trusted CA store. Do not disable validation. |
| Timeout or cancellation | Fetch uses one configured timeout budget for secret/auth/query requests. Conversion observes cancellation. Confirm no downstream delivery completed before retrying an interrupted process. |
| Non-JSON or GraphQL error | Check the configured endpoint and server-side diagnostics. HTTP 200 with GraphQL errors is still a failed batch. |
| Invalid local ID, duplicate newer ID, or missing structure | Inspect a restricted raw response dump and resolve the source/transform issue; do not save a higher checkpoint to bypass it. |
| Unsupported customer class, VAT, or change type | Verify source configuration and update the mapping deliberately if the case is required. Unknown values are not silently guessed. |
| R/K amount mismatch | Check `netAmount`, revenue account selection, `records[].amount`, `debit`, coding groups, and minor-unit rounding. |
| Field overflow or unsupported text | Inspect the relevant field. Descriptions have deliberate truncation; identity/code/amount fields must fit. Replace unsupported source characters only with an agreed business mapping. |
| Missing rental period on the invoice | Check `ledgerNote.refersToPeriodDisplayName` and the text-only R record. K periodisation alone does not print that text. |
| No work despite an expected invoice | Check the supplied checkpoint, response nodes, and whether every returned row is excluded rounding data. |

Automatic HTTP retries are not built in. Error messages deliberately avoid raw response-body previews and JSON parser details that could reveal secrets or customer data. Use restricted service diagnostics or an explicitly requested local JSON dump for deeper inspection; do not add credentials or complete API responses to general-purpose logs.

## Data handling and release maintenance

Fetched JSON and Raindance output contain personal and financial data. Masked input fields do not automatically make output data safe to log. Restrict Frends execution-result access, transfer locations, local dumps, and retention. `.gitignore` is a safety net, not access control.

The reference module under `derp/` is context, not a shipped dependency. Its connection-source UI pattern is reused; its older runtime/dependency choices and certificate-validation shortcuts are not requirements for this module.

For each release, run the solution tests and inspect the NuGet package. It should contain the task assembly, XML documentation, task metadata, and declared dependencies—not `.env`, saved customer dumps, or local output files. Review dependency advisories, test package discovery in Frends, and repeat relevant Raindance acceptance cases whenever mappings or validation rules change.

CI validates pull requests with restore, warnings-as-errors build, tests, and packaging without publishing credentials. Main-branch publishing depends on validation and retains the organisation's existing publishing action. Published-release builds also run tests before packaging/publishing. These workflows install both .NET 8 (task/test runtime) and .NET 10 (local tool SDK). Publishing credentials, repository permissions, and the organisation action remain deployment configuration to verify in GitHub; a local build does not exercise publishing.

Existing process integrations keep the two method identities. They must explicitly wire the new `ConvertInput.LastLocalId` and store `ConversionResult.LastLocalId`; wiring only the Fetch result cannot advance the checkpoint. `NodeCount` now counts actually emitted invoices rather than all raw response nodes. Stricter validation may reject inputs that previously produced silently truncated or incomplete output, so test representative production shapes before upgrading a scheduled process.
