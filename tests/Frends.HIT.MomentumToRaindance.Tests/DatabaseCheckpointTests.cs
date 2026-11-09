using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

/// <summary>Complete-feed receipts and committed progress in an opt-in disposable database.</summary>
public sealed class DatabaseCheckpointTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [PostgresFact]
    public async Task Migration_from_schema003_preserves_history_and_starts_progress_at_zero()
    {
        var name = "checkpoint_upgrade_" + Guid.NewGuid().ToString("N");
        var adminString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync(Token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
            await create.ExecuteNonQueryAsync(Token);
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false };
            await using var scratch = new NpgsqlConnection(builder.ConnectionString);
            await scratch.OpenAsync(Token);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database", "migrations")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var migrations = Path.Combine(directory.FullName, "database", "migrations");
            await using (var schema = new NpgsqlCommand("CREATE SCHEMA momentum_raindance", scratch))
                await schema.ExecuteNonQueryAsync(Token);
            foreach (var version in new[] { "001_invoice_tracking", "002_canonical_identities", "003_invoice_metadata" })
            {
                await using var apply = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(migrations, version + ".sql"), Token), scratch);
                await apply.ExecuteNonQueryAsync(Token);
            }
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO momentum_raindance.sources (source_system,activated_at,baseline_reference)
                VALUES ('migration-test',now(),'Synthetic existing baseline');
                INSERT INTO momentum_raindance.invoices (source_system,ledger_note_id,first_local_id,historical_reference)
                VALUES ('migration-test','historical-id',0,'Synthetic historical evidence');
                INSERT INTO momentum_raindance.deliveries
                (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status,delivered_at,delivery_reference)
                VALUES ('00000000-0000-0000-0000-000000000004','migration-test','migration-test.txt',repeat('a',64),100,1,999,'delivered',now(),'Synthetic prior delivery');
                INSERT INTO momentum_raindance.invoices (source_system,ledger_note_id,first_local_id,content_sha256,delivery_id)
                VALUES ('migration-test','delivered-id',999,repeat('b',64),'00000000-0000-0000-0000-000000000004');
                """, scratch))
                await seed.ExecuteNonQueryAsync(Token);
            const string snapshot = """
                SELECT jsonb_build_object(
                  'sources',(SELECT jsonb_agg(to_jsonb(s)-'last_local_id') FROM momentum_raindance.sources s),
                  'invoices',(SELECT jsonb_agg(to_jsonb(i) ORDER BY ledger_note_id) FROM momentum_raindance.invoices i),
                  'deliveries',(SELECT jsonb_agg(to_jsonb(d)-'fetch_id') FROM momentum_raindance.deliveries d))::text
                """;
            await using var inspect = new NpgsqlCommand(snapshot, scratch);
            var before = await inspect.ExecuteScalarAsync(Token);
            await using (var apply = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(migrations, "004_database_checkpoint.sql"), Token), scratch))
                await apply.ExecuteNonQueryAsync(Token);
            Assert.Equal(before, await inspect.ExecuteScalarAsync(Token));
            await using var state = new NpgsqlCommand("SELECT last_local_id=0 AND (SELECT count(*) FROM momentum_raindance.fetch_batches)=0 AND (SELECT count(*) FROM momentum_raindance.deliveries WHERE fetch_id IS NOT NULL)=0 FROM momentum_raindance.sources", scratch);
            Assert.Equal(true, await state.ExecuteScalarAsync(Token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name}", admin);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [PostgresFact]
    public async Task Fetch_records_receipt_without_advancing_progress_or_reserving_invoices()
    {
        var database = await CreateAsync();
        Assert.Equal(0, await InvoiceTracking.ReadCheckpointAsync(database, Token));
        var fetched = await FetchAsync(database, Invoice(57));
        Assert.True(Guid.TryParse(fetched.FetchId, out _));
        Assert.Equal(0, await CheckpointAsync(database));
        Assert.Equal(0L, await CountAsync(database, "invoices"));
        Assert.Equal(0L, await CountAsync(database, "deliveries"));
        Assert.Equal(1L, await CountAsync(database, "fetch_batches"));
    }

    [PostgresFact]
    public async Task Changed_filtered_or_foreign_receipt_cannot_reserve_or_advance()
    {
        var database = await CreateAsync();
        var fetched = await FetchAsync(database, Invoice(1), Invoice(2));
        var changed = new ConvertInput { FetchId = fetched.FetchId, GraphQlResult = Complete(Invoice(2)) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(changed, database, Token));
        changed.GraphQlResult = fetched.GraphQlResult + " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(changed, database, Token));
        var otherSource = await CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(fetched, otherSource, Token));
        changed.FetchId = Guid.NewGuid().ToString("D");
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(changed, database, Token));
        Assert.Equal(0, await CheckpointAsync(database));
        Assert.Equal(0L, await CountAsync(database, "invoices"));
        Assert.Equal(0L, await CountAsync(database, "deliveries"));
    }

    [PostgresFact]
    public async Task Historical_zero_local_id_and_rounding_only_feed_advance_without_delivery()
    {
        var database = await CreateAsync();
        await ExecuteAsync(database,
            "INSERT INTO momentum_raindance.invoices (source_system,ledger_note_id,first_local_id,historical_reference) VALUES ($1,'invoice-56',0,'Synthetic confirmed history')");
        var rounding = Invoice(57);
        Fixture.Row(rounding)["ledgerRow"]!["text"]!["textDetailed"] = "Öresavrundning";
        var result = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(56), rounding), database, Token);
        Assert.Equal(0, result.NodeCount);
        Assert.Equal(1, result.SkippedInvoiceCount);
        Assert.Equal(57, result.LastLocalId);
        Assert.Equal(57, await InvoiceTracking.ReadCheckpointAsync(database, Token));
        Assert.Equal(0L, await CountAsync(database, "deliveries"));
        var empty = await InvoiceTracking.PrepareAsync(await FetchAsync(database), database, Token);
        Assert.Equal(57, empty.LastLocalId);
        Assert.Empty(empty.ResultFile);
    }

    [PostgresFact]
    public async Task Rounding_only_batch_advances_and_invalid_noop_batch_does_not()
    {
        var database = await CreateAsync();
        var rounding = Invoice(8);
        Fixture.Row(rounding)["ledgerRow"]!["text"]!["textDetailed"] = "Öresavrundning";
        var result = await InvoiceTracking.PrepareAsync(await FetchAsync(database, rounding), database, Token);
        Assert.Equal(8, result.LastLocalId);
        Assert.Equal(8, await CheckpointAsync(database));
        Assert.Equal(0, result.NodeCount);
        var invalid = Invoice(9);
        invalid["changeType"]!["id"] = "reversed";
        invalid["ledgers"] = new JArray();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvoiceTracking.PrepareAsync(await FetchAsync(database, invalid), database, Token));
        Assert.Equal(8, await CheckpointAsync(database));
    }

    [PostgresFact]
    public async Task Reservation_blocks_fetch_and_progress_until_atomic_confirmation()
    {
        var database = await CreateAsync();
        var fetched = await FetchAsync(database, Invoice(69));
        var result = await InvoiceTracking.PrepareAsync(fetched, database, Token);
        Assert.Equal(0, await CheckpointAsync(database));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.ReadCheckpointAsync(database, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.RegisterFetchAsync(Complete(), 0, database, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(fetched, database, Token));
        var confirmed = await InvoiceTracking.ConfirmAsync(Confirmation(result), database, Token);
        Assert.Equal(69, confirmed.LastLocalId);
        Assert.Equal(69, await InvoiceTracking.ReadCheckpointAsync(database, Token));
        Assert.Equal("delivered", await ScalarAsync(database,
            "SELECT status FROM momentum_raindance.deliveries WHERE source_system=$1"));
    }

    [PostgresFact]
    public async Task Stale_receipt_and_fetch_registration_cannot_skip_after_concurrent_progress()
    {
        var database = await CreateAsync();
        var emptyNode = Invoice(5);
        emptyNode["ledgers"] = new JArray();
        var first = await FetchAsync(database, emptyNode);
        var stale = await FetchAsync(database, Invoice(6));
        await InvoiceTracking.PrepareAsync(first, database, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.PrepareAsync(stale, database, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.RegisterFetchAsync(Complete(Invoice(6)), 0, database, Token));
        Assert.Equal(5, await CheckpointAsync(database));
        Assert.Equal(0L, await CountAsync(database, "invoices"));
    }

    [PostgresFact]
    public async Task Old_confirmation_is_idempotent_without_rewinding_later_progress()
    {
        var database = await CreateAsync();
        var first = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(1)), database, Token);
        await InvoiceTracking.ConfirmAsync(Confirmation(first), database, Token);
        var second = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(2)), database, Token);
        await InvoiceTracking.ConfirmAsync(Confirmation(second), database, Token);
        var repeated = await InvoiceTracking.ConfirmAsync(Confirmation(first), database, Token);
        Assert.Equal(2, repeated.LastLocalId);
        Assert.Equal(2, await CheckpointAsync(database));
        Assert.Equal(2L, await CountAsync(database, "deliveries"));
    }

    [PostgresFact]
    public async Task Legacy_confirmation_cannot_import_an_untrusted_checkpoint()
    {
        var database = await CreateAsync();
        var deliveryId = Guid.NewGuid().ToString("D");
        await ExecuteAsync(database,
            """
            INSERT INTO momentum_raindance.deliveries
            (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status)
            VALUES ($2::uuid,$1,$2 || '.txt',repeat('a',64),100,1,999,'reserved')
            """, deliveryId);
        var result = await InvoiceTracking.ConfirmAsync(new DeliveryConfirmation
        {
            DeliveryId = deliveryId, Filename = deliveryId + ".txt", ContentSha256 = new string('a', 64),
            DeliveryReference = "Synthetic legacy delivery evidence"
        }, database, Token);
        Assert.Equal(0, result.LastLocalId);
        Assert.Equal(0, await CheckpointAsync(database));
    }

    [PostgresFact]
    public async Task Offline_confirmation_uses_receipt_and_old_repeat_does_not_rewind()
    {
        var database = await CreateAsync();
        var first = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(1)), database, Token);
        await ConfirmOfflineAsync(database, Confirmation(first));
        Assert.Equal(1, await CheckpointAsync(database));
        var second = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(2)), database, Token);
        await ConfirmOfflineAsync(database, Confirmation(second));
        await ConfirmOfflineAsync(database, Confirmation(first));
        Assert.Equal(2, await CheckpointAsync(database));

        var legacyId = Guid.NewGuid().ToString("D");
        await ExecuteAsync(database,
            """
            INSERT INTO momentum_raindance.deliveries
            (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status)
            VALUES ($2::uuid,$1,$2 || '.txt',repeat('a',64),100,1,999,'reserved')
            """, legacyId);
        await ConfirmOfflineAsync(database, new DeliveryConfirmation
        {
            DeliveryId = legacyId, Filename = legacyId + ".txt", ContentSha256 = new string('a', 64),
            DeliveryReference = "Synthetic offline legacy evidence"
        });
        Assert.Equal(2, await CheckpointAsync(database));
    }

    [PostgresFact]
    public async Task Incomplete_or_inconsistent_feed_does_not_create_receipts()
    {
        var database = await CreateAsync();
        foreach (var json in new[]
        {
            Fixture.Envelope(Invoice(1)),
            Complete(Invoice(2), Invoice(1)),
            Complete(Invoice(1), Invoice(1)),
            Complete(Invoice(0)),
            Complete(Invoice(1)).Replace("\"hasNextPage\":false", "\"hasNextPage\":true", StringComparison.Ordinal),
            Complete(Invoice(1)).Replace("\"totalCount\":1", "\"totalCount\":2", StringComparison.Ordinal)
        })
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.RegisterFetchAsync(json, 0, database, Token));
        Assert.Equal(0L, await CountAsync(database, "fetch_batches"));
        Assert.Equal(0, await CheckpointAsync(database));
    }

    [PostgresFact]
    public async Task Source_checkpoint_and_fetch_receipts_reject_rewrites()
    {
        var database = await CreateAsync();
        var emptyNode = Invoice(7);
        emptyNode["ledgers"] = new JArray();
        await InvoiceTracking.PrepareAsync(await FetchAsync(database, emptyNode), database, Token);
        foreach (var sql in new[]
        {
            "UPDATE momentum_raindance.sources SET last_local_id=0 WHERE source_system=$1",
            "UPDATE momentum_raindance.sources SET last_local_id=8 WHERE source_system=$1",
            "UPDATE momentum_raindance.sources SET baseline_reference='changed' WHERE source_system=$1",
            "UPDATE momentum_raindance.fetch_batches SET through_local_id=8 WHERE source_system=$1",
            "DELETE FROM momentum_raindance.fetch_batches WHERE source_system=$1"
        })
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, sql));
        Assert.Equal(7, await CheckpointAsync(database));
    }

    [PostgresFact]
    public async Task Checkpoint_failure_rolls_back_confirmation_status()
    {
        var database = await CreateAsync();
        var pending = await InvoiceTracking.PrepareAsync(await FetchAsync(database, Invoice(1)), database, Token);
        // An additional synthetic legacy reservation simulates inconsistent external state.
        // Source advancement is forbidden until every reservation is reconciled.
        var extra = Guid.NewGuid().ToString("D");
        await ExecuteAsync(database,
            """
            INSERT INTO momentum_raindance.deliveries
            (delivery_id,source_system,filename,content_sha256,byte_count,invoice_count,last_local_id,status)
            VALUES ($2::uuid,$1,$2 || '.txt',repeat('b',64),100,1,2,'reserved')
            """, extra);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvoiceTracking.ConfirmAsync(Confirmation(pending), database, Token));
        Assert.Equal(0, await CheckpointAsync(database));
        Assert.Equal("reserved", await ScalarAsync(database,
            "SELECT status FROM momentum_raindance.deliveries WHERE source_system=$1 AND delivery_id=$2::uuid", pending.DeliveryId));
    }

    private static JObject Invoice(int localId)
    {
        var node = Fixture.Node(localId);
        node["ledgerNote"]!["id"] = "invoice-" + localId;
        return node;
    }

    private static string Complete(params JObject[] nodes)
    {
        var root = new JObject
        {
            ["data"] = new JObject
            {
                ["ledgerNoteAccountingsSync"] = new JObject
                {
                    ["nodes"] = new JArray(nodes), ["totalCount"] = nodes.Length,
                    ["pageInfo"] = new JObject { ["hasNextPage"] = false, ["endCursor"] = null }
                }
            }
        };
        return root.ToString(Formatting.None);
    }

    private static async Task<ConvertInput> FetchAsync(InvoiceTrackingConnection database, params JObject[] nodes)
    {
        var json = Complete(nodes);
        var checkpoint = await InvoiceTracking.ReadCheckpointAsync(database, Token);
        return new ConvertInput
        {
            GraphQlResult = json,
            FetchId = await InvoiceTracking.RegisterFetchAsync(json, checkpoint, database, Token)
        };
    }

    private static DeliveryConfirmation Confirmation(ConversionResult result) => new()
    {
        DeliveryId = result.DeliveryId, Filename = result.Filename, ContentSha256 = result.ContentSha256,
        DeliveryReference = "Synthetic write evidence:" + result.DeliveryId
    };

    private static async Task ConfirmOfflineAsync(InvoiceTrackingConnection database, DeliveryConfirmation confirmation)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database", "sql")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var sql = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "database", "sql", "confirm-delivery.sql"), Token);
        // Exercise the exact shared Bash/PowerShell SQL using synthetic values in
        // place of psql's safely quoted variables; no production configuration.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["source"] = database.SourceSystem, ["delivery_id"] = confirmation.DeliveryId,
            ["filename"] = confirmation.Filename, ["content_sha256"] = confirmation.ContentSha256,
            ["delivery_reference"] = confirmation.DeliveryReference
        })
            sql = sql.Replace(":'" + key + "'", "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'", StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<InvoiceTrackingConnection> CreateAsync()
    {
        var database = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Manual,
            ConnectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES_RUNTIME")
                ?? Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!,
            SourceSystem = "checkpoint-test-" + Guid.NewGuid().ToString("N"), TimeoutSeconds = 30
        };
        await ExecuteAsync(database,
            "INSERT INTO momentum_raindance.sources (source_system,activated_at,baseline_reference) VALUES ($1,now(),'Synthetic complete-feed checkpoint test')");
        return database;
    }

    private static async Task ExecuteAsync(InvoiceTrackingConnection database, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(database.SourceSystem);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<object?> ScalarAsync(InvoiceTrackingConnection database, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(database.SourceSystem);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync(Token);
    }

    private static async Task<int> CheckpointAsync(InvoiceTrackingConnection database) =>
        (int)(await ScalarAsync(database, "SELECT last_local_id FROM momentum_raindance.sources WHERE source_system=$1"))!;

    private static async Task<long> CountAsync(InvoiceTrackingConnection database, string table)
    {
        Assert.Contains(table, new[] { "invoices", "deliveries", "fetch_batches" });
        return (long)(await ScalarAsync(database, $"SELECT count(*) FROM momentum_raindance.{table} WHERE source_system=$1"))!;
    }
}
