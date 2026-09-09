using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Transactions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

/// <summary>Database tests use synthetic invoices and a dedicated migrated PostgreSQL test database.</summary>
public sealed class InvoiceTrackingTests
{
    [Fact]
    public void Frends_conversion_contract_preserves_filename_and_encoded_file_types()
    {
        Assert.Equal(typeof(string), typeof(ConversionResult).GetProperty(nameof(ConversionResult.Filename))!.PropertyType);
        Assert.Equal(typeof(byte[]), typeof(ConversionResult).GetProperty(nameof(ConversionResult.ResultFile))!.PropertyType);
        Assert.Equal(typeof(string), typeof(ConversionResult).GetProperty(nameof(ConversionResult.DeliveryId))!.PropertyType);
        Assert.Equal(typeof(string), typeof(ConversionResult).GetProperty(nameof(ConversionResult.ContentSha256))!.PropertyType);
        var convert = typeof(Main).GetMethod(nameof(Main.ConvertGraphQlResult))!;
        Assert.Equal(typeof(Task<ConversionResult>), convert.ReturnType);
        var database = Assert.Single(convert.GetParameters(), parameter => parameter.ParameterType == typeof(InvoiceTrackingConnection));
        Assert.False(database.IsOptional);
        Assert.NotNull(Attribute.GetCustomAttribute(database, typeof(PropertyTabAttribute)));
        Assert.Equal(typeof(Task<DeliveryResult>), typeof(Main).GetMethod(nameof(Main.ConfirmInvoiceDelivery))!.ReturnType);
    }

    [Fact]
    public void Tracking_connection_has_masked_secrets_and_canonical_vault_option()
    {
        var connection = new InvoiceTrackingConnection();
        Assert.Equal(MomentumConfigurationSource.HcpVault, connection.ConfigurationSource);
        Assert.Equal("HcpVault", typeof(MomentumConfigurationSource).GetField(nameof(MomentumConfigurationSource.HcpVault))!
            .GetCustomAttributes(typeof(DisplayAttribute), false).Cast<DisplayAttribute>().Single().Name);
        foreach (var name in new[] { nameof(InvoiceTrackingConnection.ConnectionString), nameof(InvoiceTrackingConnection.JsonConfiguration) })
        {
            var property = typeof(InvoiceTrackingConnection).GetProperty(name)!;
            Assert.NotNull(Attribute.GetCustomAttribute(property, typeof(PasswordPropertyTextAttribute)));
            Assert.NotNull(Attribute.GetCustomAttribute(property, typeof(UIHintAttribute)));
        }
    }

    [Theory]
    [InlineData(MomentumConfigurationSource.Manual)]
    [InlineData(MomentumConfigurationSource.Json)]
    public async Task Database_configuration_disables_ambient_enlistment_and_sensitive_diagnostics(MomentumConfigurationSource source)
    {
        const string raw = "Host=127.0.0.1;Database=synthetic;Username=synthetic;Password=synthetic-secret;SSL Mode=Disable;Enlist=true;Multiplexing=true;Include Error Detail=true;Log Parameters=true;Persist Security Info=true";
        var connection = new InvoiceTrackingConnection
        {
            ConfigurationSource = source,
            SourceSystem = "synthetic-source",
            ConnectionString = raw,
            JsonConfiguration = JsonConvert.SerializeObject(new { connectionString = raw })
        };
        var resolved = new NpgsqlConnectionStringBuilder(await connection.ResolveAsync(TestContext.Current.CancellationToken));
        Assert.False(resolved.Enlist);
        Assert.False(resolved.Multiplexing);
        Assert.False(resolved.IncludeErrorDetail);
        Assert.False(resolved.LogParameters);
        Assert.False(resolved.PersistSecurityInfo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" source")]
    [InlineData("source ")]
    [InlineData("source\nname")]
    public async Task Invalid_source_identifier_fails_before_database_work(string source)
    {
        var connection = new InvoiceTrackingConnection { SourceSystem = source };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => connection.ResolveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remote_database_requires_verified_tls()
    {
        var connection = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Manual,
            SourceSystem = "synthetic-source",
            ConnectionString = "Host=database.example.invalid;Database=synthetic;Username=synthetic;Password=synthetic-secret;SSL Mode=Disable"
        };
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => connection.ResolveAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
    }

    [Fact]
    public async Task Invalid_secret_json_does_not_leak_credentials()
    {
        var connection = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Json,
            SourceSystem = "synthetic-source",
            JsonConfiguration = "{\"connectionString\": \"synthetic-secret\""
        };
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => connection.ResolveAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
    }

    [Fact]
    public async Task Unavailable_database_cannot_return_invoice_bytes()
    {
        var connection = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Manual,
            SourceSystem = "synthetic-source",
            ConnectionString = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Password=synthetic-secret;SSL Mode=Disable",
            TimeoutSeconds = 1
        };
        var exception = await Record.ExceptionAsync(() => ConvertAsync(connection, Invoice()));
        Assert.NotNull(exception);
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
        Assert.NotNull(await Record.ExceptionAsync(() => ConvertAsync(connection)));
    }

    [Fact]
    public async Task Even_empty_work_requires_database_configuration()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ConvertAsync(null!));
        var database = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Manual,
            SourceSystem = "synthetic-source"
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ConvertAsync(database));
    }

    [PostgresFact]
    public async Task Confirmed_invoice_is_not_exported_again_with_a_new_sync_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = Invoice(1, "stable-invoice");
        var prepared = await ConvertAsync(database.Connection, original);
        Assert.True(prepared.Success);
        Assert.Equal(1, prepared.NodeCount);
        Assert.Equal(1, prepared.LastLocalId);
        Assert.Equal(0, prepared.SkippedInvoiceCount);
        Assert.NotEmpty(prepared.ResultFile);
        Assert.Equal(Main.ConvertCore(new ConvertInput { GraphQlResult = Fixture.Envelope(original) },
            TestContext.Current.CancellationToken).ResultFile, prepared.ResultFile);
        Assert.True(Guid.TryParse(prepared.DeliveryId, out _));
        Assert.Matches("^300K24_[0-9]{8}_[0-9]{6}\\.txt$", prepared.Filename);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(prepared.ResultFile)), prepared.ContentSha256, ignoreCase: true);
        Assert.Equal("reserved", await database.DeliveryStatusAsync(prepared.DeliveryId));

        var confirmation = Confirmation(prepared);
        var delivered = await Main.ConfirmInvoiceDelivery(confirmation, database.Connection, TestContext.Current.CancellationToken);
        Assert.True(delivered.Success);
        Assert.Equal(prepared.DeliveryId, delivered.DeliveryId);
        Assert.Equal(1, delivered.NodeCount);
        Assert.Equal(1, delivered.LastLocalId);
        Assert.Equal("delivered", await database.DeliveryStatusAsync(prepared.DeliveryId));
        var repeatedConfirmation = await Main.ConfirmInvoiceDelivery(confirmation, database.Connection, TestContext.Current.CancellationToken);
        Assert.Equal(delivered.DeliveryId, repeatedConfirmation.DeliveryId);
        Assert.Equal(delivered.LastLocalId, repeatedConfirmation.LastLocalId);

        var replay = await ConvertAsync(database.Connection, Invoice(999, "stable-invoice"));
        Assert.True(replay.Success);
        Assert.Equal(0, replay.NodeCount);
        Assert.Equal(1, replay.SkippedInvoiceCount);
        Assert.Empty(replay.ResultFile);
        Assert.True(string.IsNullOrEmpty(replay.DeliveryId));
        Assert.Empty(replay.Filename);
        Assert.Empty(replay.ContentSha256);
        Assert.Equal(999, replay.LastLocalId);
        var changedEvidence = Confirmation(prepared);
        changedEvidence.DeliveryReference = "different-evidence";
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Main.ConfirmInvoiceDelivery(changedEvidence, database.Connection, TestContext.Current.CancellationToken));
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Unconfirmed_reservation_blocks_same_invoice_new_invoice_and_empty_response()
    {
        await using var database = await TestDatabase.CreateAsync();
        var prepared = await ConvertAsync(database.Connection, Invoice());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice(2, "invoice-1")));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice(3, "different-invoice")));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection));
        Assert.Equal("reserved", await database.DeliveryStatusAsync(prepared.DeliveryId));
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Concurrent_calls_can_only_reserve_an_invoice_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var calls = Enumerable.Range(1, 8).Select(async localId =>
        {
            try
            {
                return (Result: await ConvertAsync(database.Connection, Invoice(localId, "same-invoice")), Error: (Exception?)null);
            }
            catch (Exception exception)
            {
                return (Result: (ConversionResult?)null, Error: exception);
            }
        });
        var results = await Task.WhenAll(calls);
        Assert.Single(results, outcome => outcome.Result is { NodeCount: 1 });
        Assert.Equal(7, results.Count(outcome => outcome.Error is InvalidOperationException));
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Historical_identity_suppresses_export_without_a_local_id_or_content_hash_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedHistoryAsync("historical-invoice");
        var result = await ConvertAsync(database.Connection, Invoice(500, "historical-invoice"));
        Assert.Equal(0, result.NodeCount);
        Assert.Equal(1, result.SkippedInvoiceCount);
        Assert.Empty(result.ResultFile);
        Assert.Equal(500, result.LastLocalId);
        Assert.Empty(result.Filename);
        Assert.Empty(result.ContentSha256);
        Assert.Empty(result.DeliveryId);
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(0L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Mixed_history_and_new_invoices_only_emit_and_reserve_the_new_invoice()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedHistoryAsync("historical-invoice");
        var result = await ConvertAsync(database.Connection,
            Invoice(30, "historical-invoice", "Historical row"), Invoice(40, "new-invoice", "New row"));
        Assert.Equal(1, result.NodeCount);
        Assert.Equal(1, result.SkippedInvoiceCount);
        Assert.Equal(40, result.LastLocalId);
        var text = System.Text.Encoding.Latin1.GetString(result.ResultFile);
        Assert.Contains("New row", text);
        Assert.DoesNotContain("Historical row", text);
        Assert.Equal(2L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Malformed_later_invoice_leaves_no_partial_claims()
    {
        await using var database = await TestDatabase.CreateAsync();
        var invalid = Invoice(2, "invalid-invoice");
        Fixture.Record(invalid)["amount"] = 999m;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice(), invalid));
        Assert.Equal(0L, await database.CountAsync("invoices"));
        Assert.Equal(0L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Changed_output_for_delivered_identity_is_rejected_without_claiming_other_invoices()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await ConvertAsync(database.Connection, Invoice());
        await Main.ConfirmInvoiceDelivery(Confirmation(first), database.Connection, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection,
            Invoice(2, "invoice-1", "Changed invoice text"), Invoice(3, "new-invoice")));
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Duplicate_business_identity_with_different_sync_ids_is_rejected_before_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection,
            Invoice(1, "same-invoice"), Invoice(2, "same-invoice")));
        Assert.Equal(0L, await database.CountAsync("invoices"));
        Assert.Equal(0L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Missing_ledger_note_id_is_rejected_without_falling_back_to_sync_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        var invoice = Invoice();
        ((JObject)invoice["ledgerNote"]!).Remove("id");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, invoice));
        Assert.Equal(0L, await database.CountAsync("invoices"));
    }

    [PostgresFact]
    public async Task Inactive_or_unregistered_source_cannot_prepare_output()
    {
        await using var database = await TestDatabase.CreateAsync(activated: false);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice()));
        Assert.Equal(0L, await database.CountAsync("deliveries"));
        var unregistered = database.ConnectionFor("unregistered-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(unregistered, Invoice()));
    }

    [PostgresFact]
    public async Task Wrong_source_hash_filename_or_missing_evidence_cannot_confirm_delivery()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var other = await TestDatabase.CreateAsync();
        var prepared = await ConvertAsync(database.Connection, Invoice());
        var unknownId = Confirmation(prepared);
        unknownId.DeliveryId = Guid.NewGuid().ToString("D");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Main.ConfirmInvoiceDelivery(unknownId, database.Connection, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Main.ConfirmInvoiceDelivery(Confirmation(prepared), other.Connection, TestContext.Current.CancellationToken));

        var wrongHash = Confirmation(prepared);
        wrongHash.ContentSha256 = new string('0', 64);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Main.ConfirmInvoiceDelivery(wrongHash, database.Connection, TestContext.Current.CancellationToken));
        var wrongFilename = Confirmation(prepared);
        wrongFilename.Filename = "300K24_20000101_000000.txt";
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Main.ConfirmInvoiceDelivery(wrongFilename, database.Connection, TestContext.Current.CancellationToken));
        var noEvidence = Confirmation(prepared);
        noEvidence.DeliveryReference = "";
        Assert.NotNull(await Record.ExceptionAsync(() =>
            Main.ConfirmInvoiceDelivery(noEvidence, database.Connection, TestContext.Current.CancellationToken)));
        Assert.Equal("reserved", await database.DeliveryStatusAsync(prepared.DeliveryId));
    }

    [PostgresFact]
    public async Task Cancelled_confirmation_keeps_a_committed_reservation_blocked()
    {
        await using var database = await TestDatabase.CreateAsync();
        var prepared = await ConvertAsync(database.Connection, Invoice());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.ConfirmInvoiceDelivery(
            Confirmation(prepared), database.Connection, cancellation.Token));
        Assert.Equal("reserved", await database.DeliveryStatusAsync(prepared.DeliveryId));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice()));
    }

    [PostgresFact]
    public async Task Cancelled_conversion_cannot_reserve_an_invoice()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.ConvertGraphQlResult(
            new ConvertInput { GraphQlResult = Fixture.Envelope(Invoice()) }, database.Connection, cancellation.Token));
        Assert.Equal(0L, await database.CountAsync("invoices"));
        Assert.Equal(0L, await database.CountAsync("deliveries"));
    }

    [PostgresFact]
    public async Task Ambient_transaction_rollback_cannot_erase_a_reservation_after_bytes_are_returned()
    {
        await using var database = await TestDatabase.CreateAsync();
        ConversionResult prepared;
        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            prepared = await ConvertAsync(database.Connection, Invoice());
            // Intentionally do not complete the surrounding process transaction.
        }

        Assert.NotEmpty(prepared.ResultFile);
        Assert.Equal("reserved", await database.DeliveryStatusAsync(prepared.DeliveryId));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice()));
    }

    [PostgresFact]
    public async Task Different_credit_invoice_identity_is_not_suppressed_by_the_original_invoice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = await ConvertAsync(database.Connection, Invoice(1, "original-invoice"));
        await Main.ConfirmInvoiceDelivery(Confirmation(original), database.Connection, TestContext.Current.CancellationToken);
        var credit = Invoice(2, "credit-invoice", amount: -100m);
        var result = await ConvertAsync(database.Connection, credit);
        Assert.Equal(1, result.NodeCount);
        Assert.NotEmpty(result.ResultFile);
        Assert.Equal(2L, await database.CountAsync("invoices"));
        Assert.NotEqual(original.Filename, result.Filename);
    }

    [PostgresFact]
    public async Task Missing_or_unsupported_schema_fails_closed_even_for_empty_work()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!;
        var name = "momentum_schema_test_" + Guid.NewGuid().ToString("N");
        // This is a new database created solely by this test, not the shared migrated test database.
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name, Pooling = false };
            var database = new InvoiceTrackingConnection
            {
                ConfigurationSource = MomentumConfigurationSource.Manual,
                SourceSystem = "synthetic-source",
                ConnectionString = connectionString.ConnectionString
            };
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database, Invoice()));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database));
            await using (var empty = new NpgsqlConnection(connectionString.ConnectionString))
            {
                await empty.OpenAsync(TestContext.Current.CancellationToken);
                await using var schema = new NpgsqlCommand("""
                    CREATE SCHEMA momentum_raindance;
                    CREATE TABLE momentum_raindance.schema_migrations (version text PRIMARY KEY);
                    INSERT INTO momentum_raindance.schema_migrations VALUES ('999_unknown_schema');
                    """, empty);
                await schema.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database, Invoice()));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name}", admin);
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    [PostgresFact]
    public async Task Database_failure_after_delivery_insert_rolls_back_the_entire_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var function = "test_reject_invoice_" + suffix;
        var trigger = "test_reject_invoice_" + suffix;
        await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        // All identifiers and the source literal are generated internally from GUIDs. The trigger
        // affects only this test's source and cannot reject another test's or process's invoices.
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION momentum_raindance.{function}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Synthetic invoice insert failure after delivery reservation';
            END
            $body$;
            CREATE TRIGGER {trigger} BEFORE INSERT ON momentum_raindance.invoices
                FOR EACH ROW WHEN (NEW.source_system = '{database.Connection.SourceSystem}')
                EXECUTE FUNCTION momentum_raindance.{function}();
            """, admin))
            await install.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ConvertAsync(database.Connection, Invoice()));
            Assert.Equal(0L, await database.CountAsync("invoices"));
            Assert.Equal(0L, await database.CountAsync("deliveries"));
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"""
                DROP TRIGGER {trigger} ON momentum_raindance.invoices;
                DROP FUNCTION momentum_raindance.{function}();
                """, admin);
            await remove.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var validRetry = await ConvertAsync(database.Connection, Invoice());
        Assert.Equal(1, validRetry.NodeCount);
        Assert.Equal(1L, await database.CountAsync("invoices"));
        Assert.Equal(1L, await database.CountAsync("deliveries"));
    }

    private static JObject Invoice(int localId = 1, string ledgerNoteId = "invoice-1", string text = "Synthetic rent", decimal amount = 100m)
    {
        var node = Fixture.Node(localId, text, amount);
        node["ledgerNote"]!["id"] = ledgerNoteId;
        return node;
    }

    private static Task<ConversionResult> ConvertAsync(InvoiceTrackingConnection database, params JObject[] invoices) =>
        Main.ConvertGraphQlResult(new ConvertInput { GraphQlResult = Fixture.Envelope(invoices) }, database, TestContext.Current.CancellationToken);

    private static DeliveryConfirmation Confirmation(ConversionResult prepared) => new()
    {
        DeliveryId = prepared.DeliveryId,
        Filename = prepared.Filename,
        ContentSha256 = prepared.ContentSha256,
        DeliveryReference = "synthetic-test-write:" + prepared.DeliveryId
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;
        public InvoiceTrackingConnection Connection { get; }

        private TestDatabase(string connectionString)
        {
            _connectionString = connectionString;
            Connection = ConnectionFor("test-" + Guid.NewGuid().ToString("N"));
        }

        public InvoiceTrackingConnection ConnectionFor(string source) => new()
        {
            ConfigurationSource = MomentumConfigurationSource.Manual,
            SourceSystem = source,
            ConnectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES_RUNTIME") ?? _connectionString,
            TimeoutSeconds = 30
        };

        public static async Task<TestDatabase> CreateAsync(bool activated = true)
        {
            var database = new TestDatabase(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
            await using var connection = new NpgsqlConnection(database._connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO momentum_raindance.sources (source_system, activated_at, baseline_reference)
                VALUES (@source, CASE WHEN @activated THEN CURRENT_TIMESTAMP ELSE NULL END,
                    CASE WHEN @activated THEN 'Synthetic integration test baseline' ELSE NULL END)
                """, connection);
            command.Parameters.AddWithValue("source", database.Connection.SourceSystem);
            command.Parameters.AddWithValue("activated", activated);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            return database;
        }

        public async Task SeedHistoryAsync(string ledgerNoteId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO momentum_raindance.invoices
                    (source_system, ledger_note_id, first_local_id, node_id, ledger_note_number, historical_reference)
                VALUES (@source, @invoice, 1, 'synthetic-historical-node', 'synthetic-historical-number',
                    'Synthetic reconciled historical delivery')
                """, connection);
            command.Parameters.AddWithValue("source", Connection.SourceSystem);
            command.Parameters.AddWithValue("invoice", ledgerNoteId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public async Task<long> CountAsync(string table)
        {
            // The table is selected exclusively by these tests, never from user data.
            Assert.Contains(table, new[] { "invoices", "deliveries" });
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM momentum_raindance.{table} WHERE source_system = @source", connection);
            command.Parameters.AddWithValue("source", Connection.SourceSystem);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public async Task<string> DeliveryStatusAsync(string deliveryId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT status FROM momentum_raindance.deliveries WHERE source_system = @source AND delivery_id = @delivery
                """, connection);
            command.Parameters.AddWithValue("source", Connection.SourceSystem);
            command.Parameters.AddWithValue("delivery", Guid.Parse(deliveryId));
            return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        // The database deliberately rejects deletion of invoice history, including in tests.
        // Each scenario owns a fresh synthetic scope; discard the dedicated Docker volume separately.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Opt in only after migrating a disposable, dedicated PostgreSQL database.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")))
            Skip = "Set MOMENTUM_TEST_POSTGRES to a migrated disposable PostgreSQL test database.";
    }
}

[Collection("Infisical environment")]
public sealed class InvoiceTrackingVaultTests
{
    [Fact]
    public async Task Database_connection_resolves_the_selected_HcpVault_secret_without_using_manual_values()
    {
        using var environment = new VaultEnvironment();
        const string databaseSecret = "Host=127.0.0.1;Database=synthetic-db;Username=synthetic-user;Password=synthetic-password;SSL Mode=Disable";
        var requests = new List<(string Uri, string? Authorization)>();
        using var http = new HttpClient(new TransportTests.FakeHandler((request, _) =>
        {
            requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            return Task.FromResult(TransportTests.JsonResponse(requests.Count == 1
                ? "{\"accessToken\":\"synthetic-vault-token\"}"
                : JsonConvert.SerializeObject(new { secret = new { secretValue = JsonConvert.SerializeObject(new { connectionString = databaseSecret }) } })));
        }));
        var database = new InvoiceTrackingConnection
        {
            ConfigurationSource = MomentumConfigurationSource.HcpVault,
            SourceSystem = "synthetic-source",
            VaultPath = "/database.folder/tracking & secret",
            ConnectionString = "invalid-manual-configuration"
        };
        var normalized = new NpgsqlConnectionStringBuilder(await database.ResolveAsync(TestContext.Current.CancellationToken, http));
        Assert.Equal("synthetic-db", normalized.Database);
        Assert.Equal("synthetic-user", normalized.Username);
        Assert.Equal("synthetic-password", normalized.Password);
        Assert.Equal(2, requests.Count);
        Assert.Null(requests[0].Authorization);
        Assert.Equal("Bearer synthetic-vault-token", requests[1].Authorization);
        Assert.Equal("https://secrets.example.invalid/api/v3/secrets/raw/tracking%20%26%20secret?workspaceId=synthetic-project&secretPath=%2Fdatabase_folder&environment=synthetic-environment", requests[1].Uri);
    }

    [Fact]
    public async Task Invalid_database_secret_fails_without_exposing_its_value()
    {
        using var environment = new VaultEnvironment();
        var count = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler((_, _) => Task.FromResult(
            TransportTests.JsonResponse(++count == 1
                ? "{\"accessToken\":\"synthetic-vault-token\"}"
                : JsonConvert.SerializeObject(new { secret = new { secretValue = "{\"connectionString\":synthetic-private-value}" } })))));
        var database = new InvoiceTrackingConnection
        {
            SourceSystem = "synthetic-source",
            VaultPath = "/database/tracking"
        };
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => database.ResolveAsync(TestContext.Current.CancellationToken, http));
        Assert.Equal(2, count);
        Assert.DoesNotContain("synthetic-private-value", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    private sealed class VaultEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous;

        public VaultEnvironment()
        {
            var values = new Dictionary<string, string>
            {
                ["INFISICAL_ADDR"] = "https://secrets.example.invalid/",
                ["INFISICAL_CLIENT_ID"] = "synthetic-client",
                ["INFISICAL_CLIENT_SECRET"] = "synthetic-secret",
                ["INFISICAL_PROJECT"] = "synthetic-project",
                ["INFISICAL_ENVIRONMENT"] = "synthetic-environment"
            };
            _previous = values.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
