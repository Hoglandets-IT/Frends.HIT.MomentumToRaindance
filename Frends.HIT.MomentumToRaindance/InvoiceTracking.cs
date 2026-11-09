using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>Durable at-most-once admission; never exposes bytes before the reservation commits.</summary>
internal static class InvoiceTracking
{
    private sealed record Candidate(LedgerNoteAccountingNode Node, string Id, byte[] Bytes, string Hash);
    private sealed record FetchReceipt(int From, int Through, int NodeCount, string Hash);

    internal static async Task<int> ReadCheckpointAsync(InvoiceTrackingConnection database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(database.TimeoutSeconds));
        var token = timeout.Token;
        var connectionString = await database.ResolveAsync(token).ConfigureAwait(false);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            var checkpoint = await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            await EnsureNoOutstandingAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return checkpoint;
        }
        catch (NpgsqlException)
        {
            throw DatabaseFailure();
        }
    }

    internal static async Task<string> RegisterFetchAsync(string graphQlResult, int expectedCheckpoint,
        InvoiceTrackingConnection database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCheckpoint);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(database.TimeoutSeconds));
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();
        var response = Main.DeserializeGraphQlResult(graphQlResult);
        var nodes = response.Data!.LedgerNoteAccountingsSync!.Nodes;
        ValidateCompleteEnvelope(graphQlResult, nodes.Count);
        var through = expectedCheckpoint;
        foreach (var node in nodes)
        {
            token.ThrowIfCancellationRequested();
            if (node?.LocalId is not int localId || localId <= through)
                throw new InvalidOperationException("A complete fetch must contain unique, increasing local IDs above the database checkpoint.");
            through = localId;
        }
        var payloadHash = Hash(Encoding.UTF8.GetBytes(graphQlResult));
        var connectionString = await database.ResolveAsync(token).ConfigureAwait(false);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            var checkpoint = await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            await EnsureNoOutstandingAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            RequireCheckpoint(checkpoint, expectedCheckpoint);
            var id = Guid.NewGuid();
            await using var insert = Command(connection, transaction,
                """
                INSERT INTO momentum_raindance.fetch_batches
                (fetch_id,source_system,from_local_id,through_local_id,node_count,payload_sha256)
                VALUES ($1,$2,$3,$4,$5,$6)
                """, id, database.SourceSystem, expectedCheckpoint, through, nodes.Count, payloadHash);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return id.ToString("D");
        }
        catch (NpgsqlException)
        {
            throw DatabaseFailure();
        }
    }

    internal static async Task<ConversionResult> PrepareAsync(ConvertInput input, InvoiceTrackingConnection database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        if (!Guid.TryParseExact(input.FetchId, "D", out var fetchId) || fetchId == Guid.Empty)
            throw new ArgumentException("FetchId must identify a complete Fetch Ledger Note Accountings result from this database/source.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(database.TimeoutSeconds));
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();

        // Validate/render the entire candidate batch before changing durable state.
        var response = Main.DeserializeGraphQlResult(input.GraphQlResult);
        var candidates = new List<Candidate>();
        var localIds = new HashSet<int>();
        var invoiceIds = new HashSet<string>(StringComparer.Ordinal);
        var payloadHash = Hash(Encoding.UTF8.GetBytes(input.GraphQlResult));
        foreach (var node in response.Data!.LedgerNoteAccountingsSync!.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (node?.LocalId is not int localId || localId <= 0)
                throw new InvalidOperationException("Every Momentum node must have a positive localId.");
            if (!localIds.Add(localId))
                throw new InvalidOperationException("Momentum returned duplicate pending local IDs.");
            Main.ValidateNode(node);
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
            var checkpoint = await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            await EnsureNoOutstandingAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            var receipt = await ReadReceiptAsync(connection, transaction, database.SourceSystem, fetchId, token).ConfigureAwait(false);
            RequireCheckpoint(checkpoint, receipt.From);
            if (payloadHash != receipt.Hash || response.Data.LedgerNoteAccountingsSync.Nodes.Count != receipt.NodeCount)
                throw new InvalidOperationException("GraphQL JSON does not match the immutable fetch receipt. Pass the exact, complete Fetch result without filtering or editing it.");

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

            var nextCheckpoint = receipt.Through;
            if (selected.Count == 0)
            {
                await AdvanceCheckpointAsync(connection, transaction, database.SourceSystem, checkpoint, nextCheckpoint, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return new ConversionResult(true, 0, Array.Empty<byte>(),
                    "Complete fetch reconciled. Database checkpoint saved; no file or reservation created.", nextCheckpoint) { SkippedInvoiceCount = skipped };
            }

            using var stream = new MemoryStream();
            foreach (var item in selected)
                stream.Write(item.Bytes);
            var payload = stream.ToArray();
            var hash = Hash(payload);
            var reservationId = Guid.NewGuid();
            var filename = await InsertDeliveryAsync(connection, transaction, database.SourceSystem, reservationId,
                hash, payload.Length, selected.Count, nextCheckpoint, fetchId, token).ConfigureAwait(false);

            foreach (var item in selected)
            {
                await using var insert = Command(connection, transaction,
                    """
                    INSERT INTO momentum_raindance.invoices
                    (source_system,ledger_note_id,first_local_id,node_id,invoice_number,ledger_note_number,content_sha256,delivery_id,
                     source_created_at,due_date,invoice_billed_date,accounting_event_created_at,accounting_approved_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)
                    """, database.SourceSystem, item.Id, item.Node.LocalId!.Value,
                    (object?)item.Node.Id ?? DBNull.Value, (object?)item.Node.LedgerNote!.Invoice?.Number ?? DBNull.Value,
                    (object?)item.Node.LedgerNote.Number ?? DBNull.Value, item.Hash, reservationId,
                    OptionalParameter(NpgsqlDbType.TimestampTz, item.Node.LedgerNote.StatusCreated?.ToUniversalTime()),
                    OptionalParameter(NpgsqlDbType.Date, item.Node.LedgerNote.Due is { } due ? DateOnly.FromDateTime(due.DateTime) : null),
                    OptionalParameter(NpgsqlDbType.Date, item.Node.LedgerNote.Invoice?.Billed),
                    OptionalParameter(NpgsqlDbType.TimestampTz, item.Node.TransactionCreated?.Change?.StatusCreated?.ToUniversalTime()),
                    OptionalParameter(NpgsqlDbType.TimestampTz, item.Node.TransactionCreated?.StatusApproved?.ToUniversalTime()));
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
            throw DatabaseFailure();
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
            var committedCheckpoint = await LockSourceAsync(connection, transaction, database.SourceSystem, token).ConfigureAwait(false);
            int count;
            int checkpoint;
            string status;
            string? reference;
            Guid? fetchId;
            await using (var select = Command(connection, transaction,
                "SELECT filename,content_sha256,invoice_count,last_local_id,status,delivery_reference,fetch_id FROM momentum_raindance.deliveries WHERE source_system=$1 AND delivery_id=$2 FOR UPDATE",
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
                fetchId = reader.IsDBNull(6) ? null : reader.GetGuid(6);
            }
            if (status == "reserved")
            {
                FetchReceipt? receipt = null;
                if (fetchId is Guid receiptId)
                {
                    receipt = await ReadReceiptAsync(connection, transaction, database.SourceSystem, receiptId, token).ConfigureAwait(false);
                    RequireCheckpoint(committedCheckpoint, receipt.From);
                    if (receipt.Through != checkpoint)
                        throw new InvalidOperationException("Delivery checkpoint does not match its complete-fetch receipt; reconcile the database before confirmation.");
                }
                await using var update = Command(connection, transaction,
                    "UPDATE momentum_raindance.deliveries SET status='delivered',delivered_at=clock_timestamp(),delivery_reference=$3 WHERE source_system=$1 AND delivery_id=$2 AND status='reserved'",
                    database.SourceSystem, id, input.DeliveryReference);
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Delivery confirmation did not update exactly one reservation.");
                // Legacy reservations have no complete-fetch proof and cannot move progress.
                if (receipt is not null)
                {
                    await AdvanceCheckpointAsync(connection, transaction, database.SourceSystem,
                        committedCheckpoint, receipt.Through, token).ConfigureAwait(false);
                    committedCheckpoint = receipt.Through;
                }
            }
            else if (status != "delivered")
                throw new InvalidOperationException("Unexpected delivery status; reconciliation is required.");
            else if (reference != input.DeliveryReference)
                throw new InvalidOperationException("Delivery is already confirmed with different evidence. Existing delivery evidence is immutable.");
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return new DeliveryResult { Success = true, DeliveryId = id.ToString("D"), NodeCount = count, LastLocalId = committedCheckpoint };
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("Recording delivery failed. Do not write the file again. Inspect the reservation and retry only confirmation with the same fingerprint after database recovery.");
        }
    }

    private static async Task<int> LockSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string source, CancellationToken token)
    {
        // A session/role default must not acknowledge a reservation before WAL is durable.
        await using (var durability = Command(connection, transaction, "SET LOCAL synchronous_commit = on"))
            await durability.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        // Fail closed on an unknown database/schema; DDL is exclusively an offline migration concern.
        await using (var version = Command(connection, transaction,
            "SELECT version FROM momentum_raindance.schema_migrations ORDER BY version DESC LIMIT 1"))
        {
            if (await version.ExecuteScalarAsync(token).ConfigureAwait(false) is not string schemaVersion || schemaVersion != "004_database_checkpoint")
                throw new InvalidOperationException("Unsupported invoice-tracking schema. Run the matching offline migrations before enabling delivery.");
        }
        await using var command = Command(connection, transaction,
            "SELECT last_local_id FROM momentum_raindance.sources WHERE source_system=$1 AND activated_at IS NOT NULL AND NULLIF(btrim(baseline_reference),'') IS NOT NULL FOR UPDATE", source);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not int checkpoint)
            throw new InvalidOperationException("SourceSystem is not registered and activated. Reconcile/seed historical invoices and activate the source offline before any delivery.");
        return checkpoint;
    }

    private static async Task EnsureNoOutstandingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, CancellationToken token)
    {
        await using var outstanding = Command(connection, transaction,
            "SELECT delivery_id FROM momentum_raindance.deliveries WHERE source_system=$1 AND status='reserved' LIMIT 1", source);
        if (await outstanding.ExecuteScalarAsync(token).ConfigureAwait(false) is Guid deliveryId)
            throw new InvalidOperationException($"Source has unresolved delivery {deliveryId:D}. No file is returned. Reconcile delivery evidence and confirm it; do not resend or reset the reservation.");
    }

    private static async Task<FetchReceipt> ReadReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, Guid fetchId, CancellationToken token)
    {
        await using var select = Command(connection, transaction,
            "SELECT from_local_id,through_local_id,node_count,payload_sha256 FROM momentum_raindance.fetch_batches WHERE source_system=$1 AND fetch_id=$2", source, fetchId);
        await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("Fetch receipt was not found for this database and SourceSystem. Fetch a complete batch again.");
        return new FetchReceipt(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3));
    }

    private static void RequireCheckpoint(int actual, int expected)
    {
        if (actual != expected)
            throw new InvalidOperationException("The database checkpoint changed after fetching. No file was reserved; fetch a fresh complete batch.");
    }

    private static async Task AdvanceCheckpointAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, int expected, int through, CancellationToken token)
    {
        if (through < expected)
            throw new InvalidOperationException("A complete-fetch checkpoint cannot move backwards.");
        if (through == expected)
            return;
        await using var update = Command(connection, transaction,
            "UPDATE momentum_raindance.sources SET last_local_id=$3 WHERE source_system=$1 AND last_local_id=$2", source, expected, through);
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The database checkpoint changed. No progress was committed.");
    }

    private static void ValidateCompleteEnvelope(string payload, int nodeCount)
    {
        try
        {
            var root = JObject.Parse(payload, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var sync = root["data"]?["ledgerNoteAccountingsSync"];
            var hasNextPage = sync?["pageInfo"]?["hasNextPage"];
            var total = sync?["totalCount"];
            if (hasNextPage?.Type != JTokenType.Boolean || hasNextPage.Value<bool>()
                || total?.Type != JTokenType.Integer || total.Value<long>() != nodeCount)
                throw new InvalidOperationException("Fetch receipt requires the complete merged feed, hasNextPage=false, and totalCount matching its nodes.");
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or FormatException)
        {
            throw new InvalidOperationException("Complete-fetch metadata is invalid. No receipt was recorded.");
        }
    }

    private static InvalidOperationException DatabaseFailure() =>
        new("Invoice-tracking database operation failed. No file was returned. Check connectivity, migrations and permissions; inspect outstanding reservations before retrying. Database details are withheld to protect credentials and invoice data.");

    private static async Task<string> InsertDeliveryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, Guid id, string hash, int length, int count, int checkpoint, Guid fetchId, CancellationToken token)
    {
        var timestamp = DateTime.Now;
        // Keep the existing filename format while reserving unique seconds across sources/processes.
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var filename = "300K24_" + timestamp.AddSeconds(attempt).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt";
            await using var command = Command(connection, transaction,
                """
                INSERT INTO momentum_raindance.deliveries
                (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status,fetch_id)
                VALUES ($1,$2,$3,$4,$5,$6,$7,'reserved',$8) ON CONFLICT (filename) DO NOTHING
                """, id, source, filename, hash, length, count, checkpoint, fetchId);
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
            // Nullable metadata uses explicit PostgreSQL types, including when its value is SQL NULL.
            if (value is NpgsqlParameter parameter)
                command.Parameters.Add(parameter);
            else if (value is DBNull)
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = DBNull.Value });
            else
                command.Parameters.Add(new NpgsqlParameter { Value = value });
        }
        return command;
    }

    private static NpgsqlParameter OptionalParameter(NpgsqlDbType type, object? value) =>
        new() { NpgsqlDbType = type, Value = value ?? DBNull.Value };

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static GraphQlResponse<LedgerNoteAccountingsSyncData> Envelope(IReadOnlyList<LedgerNoteAccountingNode> nodes) =>
        new(new LedgerNoteAccountingsSyncData(new LedgerNoteAccountingsSyncConnection(nodes)), null);
}
