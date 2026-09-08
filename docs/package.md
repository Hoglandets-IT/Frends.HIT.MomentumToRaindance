# Momentum to Raindance

Two .NET 8 Frends tasks for fetching Momentum ledger accounting JSON and converting it to this integration's fixed-width Raindance invoice format.

- **Fetch Ledger Note Accountings** returns prettified JSON and the unchanged input `LastLocalId`. Optional custom processing can run after fetching.
- **Convert GraphQL Result** takes that JSON and the same checkpoint, and returns `ResultFile`, `NodeCount`, and `LastLocalId`. No-work results preserve the input checkpoint. Successful conversion of new invoices returns the highest handled ID.

The conversion's `ResultFile` is a `byte[]`, already encoded as ISO-8859-1/Latin-1 with fixed-width spaces and CRLF line endings. Pass it directly to a file writer's byte-content input in RAW mode without re-encoding. Save the conversion checkpoint **only after successful delivery**. Serialize runs and reconcile ambiguous delivery failures before replaying invoices. The package does not write files or store checkpoints itself.

Both tasks support cancellation. Fetch accepts JSON, manual, or HcpVault connection settings and requires HTTPS with normal certificate validation. HcpVault is the canonical secret-store option, currently backed by Infisical. Failed requests, GraphQL errors, or invalid invoice data throw rather than returning partial output.

Read the [setup and API documentation](https://github.com/Hoglandets-IT/Frends.HIT.MomentumToRaindance#readme), [field mapping](https://github.com/Hoglandets-IT/Frends.HIT.MomentumToRaindance/blob/main/docs/raindance-format.md), and [production runbook](https://github.com/Hoglandets-IT/Frends.HIT.MomentumToRaindance/blob/main/docs/operations.md) before deployment. Validate the source batching contract and the actual Raindance importer configuration in your environment.
