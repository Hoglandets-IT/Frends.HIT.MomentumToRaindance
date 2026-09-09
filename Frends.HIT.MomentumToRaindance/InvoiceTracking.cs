using System.Globalization;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>Durable at-most-once admission; never exposes bytes before the reservation commits.</summary>
internal static class InvoiceTracking
{
    private sealed record Candidate(LedgerNoteAccountingNode Node, string Id, byte[] Bytes, string Hash);

    internal static async Task<ConversionResult> PrepareAsync(ConvertInput input, InvoiceTrackingConnection database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        var checkpoint = Main.ParseCheckpoint(input.LastLocalId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(database.TimeoutSeconds));
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();

        // Validate/render the entire candidate batch before changing durable state.
        var response = Main.DeserializeGraphQlResult(input.GraphQlResult);
        var candidates = new List<Candidate>();
        var localIds = new HashSet<int>();
        var invoiceIds = new HashSet<string>(StringComparer.Ordinal);
        var highest = checkpoint;
        foreach (var node in response.Data!.LedgerNoteAccountingsSync!.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (node?.LocalId is not int localId || localId <= 0)
                throw new InvalidOperationException("Every Momentum node must have a positive localId.");
            if (localId <= checkpoint)
                continue;
            if (!localIds.Add(localId))
                throw new InvalidOperationException("Momentum returned duplicate pending local IDs.");
            Main.ValidateNode(node);
            highest = Math.Max(highest, localId);
            var bytes = RaindanceWriter.ToBytes(Envelope(new[] { node }), token);
            if (bytes.Length == 0)
                continue;
            var id = node.LedgerNote!.Id;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 300 || id != id.Trim() || id.Any(char.IsControl))
                throw new InvalidOperationException("Every emitted invoice requires a stable ledgerNote.id (1–300 characters, no surrounding whitespace or controls). No localId fallback is allowed.");
            if (!invoiceIds.Add(id))
                throw new InvalidOperationException("Momentum returned the same ledgerNote.id more than once in this batch. Reconcile the input before exporting.");
            candidates.Add(new Candidate(node, id, bytes, Hash(bytes)));
        }
        candidates.Sort((left, right) => left.Node.LocalId!.Value.CompareTo(right.Node.LocalId!.Value));

        var connectionString = await database.ResolveAsync(token).ConfigureAwait(false);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            await using (var outstanding = Command(connection, transaction,
                "SELECT delivery_id FROM momentum_raindance.deliveries WHERE source_system=$1 AND status='reserved' LIMIT 1", database.SourceSystem))
            {
                if (await outstanding.ExecuteScalarAsync(token).ConfigureAwait(false) is Guid deliveryId)
                    throw new InvalidOperationException($"Source has unresolved delivery {deliveryId:D}. No file is returned. Reconcile delivery evidence and confirm it; do not resend or reset the reservation.");
            }

            var selected = new List<Candidate>();
            var skipped = 0;
            foreach (var candidate in candidates)
            {
                await using var lookup = Command(connection, transaction,
                    "SELECT content_sha256 FROM momentum_raindance.invoices WHERE source_system=$1 AND ledger_note_id=$2",
                    database.SourceSystem, candidate.Id);
                await using var reader = await lookup.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    selected.Add(candidate);
                    continue;
                }
                // A NULL historical hash means identity-only suppression, not verified historical content.
                if (!reader.IsDBNull(0) && !string.Equals(reader.GetString(0), candidate.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A previously recorded invoice identity now has different Raindance content. No invoices were reserved; reconcile this change instead of creating a second invoice.");
                skipped++;
            }

            var nextCheckpoint = candidates.Count == 0 ? checkpoint : highest;
            if (selected.Count == 0)
            {
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return new ConversionResult(true, 0, Array.Empty<byte>(),
                    "No unrecorded invoices. No file or reservation created.", nextCheckpoint) { SkippedInvoiceCount = skipped };
            }

            using var stream = new MemoryStream();
            foreach (var item in selected)
                stream.Write(item.Bytes);
            var payload = stream.ToArray();
            var hash = Hash(payload);
            var reservationId = Guid.NewGuid();
            var filename = await InsertDeliveryAsync(connection, transaction, database.SourceSystem, reservationId,
                hash, payload.Length, selected.Count, nextCheckpoint, token).ConfigureAwait(false);

            foreach (var item in selected)
            {
                await using var insert = Command(connection, transaction,
                    """
                    INSERT INTO momentum_raindance.invoices
                    (source_system,ledger_note_id,first_local_id,node_id,invoice_number,ledger_note_number,content_sha256,delivery_id)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
                    """, database.SourceSystem, item.Id, item.Node.LocalId!.Value,
                    (object?)item.Node.Id ?? DBNull.Value, (object?)item.Node.LedgerNote!.Invoice?.Number ?? DBNull.Value,
                    (object?)item.Node.LedgerNote.Number ?? DBNull.Value, item.Hash, reservationId);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            var result = new ConversionResult(true, selected.Count, payload,
                "Reserved once in PostgreSQL. Write this file once with overwrite disabled, then confirm delivery. Never automatically replay the writer.", nextCheckpoint)
            {
                Filename = filename,
                DeliveryId = reservationId.ToString("D"),
                ContentSha256 = hash,
                SkippedInvoiceCount = skipped
            };
            // If cancellation/network failure makes commit ambiguous, no bytes escape this invocation.
            // If it committed, the durable reservation deliberately blocks the next attempt.
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("Invoice-tracking database operation failed. No file was returned. Check connectivity, migrations and permissions; inspect outstanding reservations before retrying. Database details are withheld to protect credentials and invoice data.");
        }
    }

    internal static async Task<DeliveryResult> ConfirmAsync(DeliveryConfirmation input, InvoiceTrackingConnection database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        if (!Guid.TryParseExact(input.DeliveryId, "D", out var id) || id == Guid.Empty
            || string.IsNullOrWhiteSpace(input.Filename) || string.IsNullOrEmpty(input.ContentSha256) || input.ContentSha256.Length != 64
            || !input.ContentSha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(input.DeliveryReference)
            || input.DeliveryReference.Length > 2000 || input.DeliveryReference.Any(char.IsControl))
            throw new ArgumentException("Confirmation requires DeliveryId, Filename, ContentSha256 and a nonblank delivery evidence reference (max 2000 characters, no controls).");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(database.TimeoutSeconds));
        var token = timeout.Token;
        var connectionString = await database.ResolveAsync(token).ConfigureAwait(false);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            int count;
            int checkpoint;
            string status;
            string? reference;
            await using (var select = Command(connection, transaction,
                "SELECT filename,content_sha256,invoice_count,last_local_id,status,delivery_reference FROM momentum_raindance.deliveries WHERE source_system=$1 AND delivery_id=$2 FOR UPDATE",
                database.SourceSystem, id))
            await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                    throw new InvalidOperationException("Delivery reservation was not found for this SourceSystem.");
                if (reader.GetString(0) != input.Filename || !string.Equals(reader.GetString(1), input.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Delivery confirmation fingerprint does not match the reserved filename and file hash.");
                count = reader.GetInt32(2);
                checkpoint = reader.GetInt32(3);
                status = reader.GetString(4);
                reference = reader.IsDBNull(5) ? null : reader.GetString(5);
            }
            if (status == "reserved")
            {
                await using var update = Command(connection, transaction,
                    "UPDATE momentum_raindance.deliveries SET status='delivered',delivered_at=clock_timestamp(),delivery_reference=$3 WHERE source_system=$1 AND delivery_id=$2 AND status='reserved'",
                    database.SourceSystem, id, input.DeliveryReference);
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Delivery confirmation did not update exactly one reservation.");
            }
            else if (status != "delivered")
                throw new InvalidOperationException("Unexpected delivery status; reconciliation is required.");
            else if (reference != input.DeliveryReference)
                throw new InvalidOperationException("Delivery is already confirmed with different evidence. Existing delivery evidence is immutable.");
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return new DeliveryResult { Success = true, DeliveryId = id.ToString("D"), NodeCount = count, LastLocalId = checkpoint };
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("Recording delivery failed. Do not write the file again. Inspect the reservation and retry only confirmation with the same fingerprint after database recovery.");
        }
    }

    private static async Task LockSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string source, CancellationToken token)
    {
        // A session/role default must not acknowledge a reservation before WAL is durable.
        await using (var durability = Command(connection, transaction, "SET LOCAL synchronous_commit = on"))
            await durability.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        // Fail closed on an unknown database/schema; DDL is exclusively an offline migration concern.
        await using (var version = Command(connection, transaction,
            "SELECT version FROM momentum_raindance.schema_migrations ORDER BY version DESC LIMIT 1"))
        {
            if (await version.ExecuteScalarAsync(token).ConfigureAwait(false) is not string schemaVersion || schemaVersion != "002_canonical_identities")
                throw new InvalidOperationException("Unsupported invoice-tracking schema. Run the matching offline migrations before enabling delivery.");
        }
        await using var command = Command(connection, transaction,
            "SELECT activated_at IS NOT NULL AND NULLIF(btrim(baseline_reference),'') IS NOT NULL FROM momentum_raindance.sources WHERE source_system=$1 FOR UPDATE", source);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("SourceSystem is not registered and activated. Reconcile/seed historical invoices and activate the source offline before any delivery.");
    }

    private static async Task<string> InsertDeliveryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, Guid id, string hash, int length, int count, int checkpoint, CancellationToken token)
    {
        var timestamp = DateTime.Now;
        // Keep the existing filename format while reserving unique seconds across sources/processes.
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var filename = "300K24_" + timestamp.AddSeconds(attempt).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt";
            await using var command = Command(connection, transaction,
                """
                INSERT INTO momentum_raindance.deliveries
                (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status)
                VALUES ($1,$2,$3,$4,$5,$6,$7,'reserved') ON CONFLICT (filename) DO NOTHING
                """, id, source, filename, hash, length, count, checkpoint);
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1)
                return filename;
        }
        throw new InvalidOperationException("Could not allocate a unique Raindance filename. No invoices were reserved; retry conversion later.");
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            // All nullable audit fields are text. Explicit typing is required for SQL NULL parameters.
            if (value is DBNull)
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = DBNull.Value });
            else
                command.Parameters.Add(new NpgsqlParameter { Value = value });
        }
        return command;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static GraphQlResponse<LedgerNoteAccountingsSyncData> Envelope(IReadOnlyList<LedgerNoteAccountingNode> nodes) =>
        new(new LedgerNoteAccountingsSyncData(new LedgerNoteAccountingsSyncConnection(nodes)), null);
}
