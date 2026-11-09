using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

/// <summary>Metadata uses synthetic values only and must never change an invoice's file bytes.</summary>
public sealed class MetadataTests
{
    [Theory]
    [InlineData("2026-09-09T00:15:30+02:00", 120)]
    [InlineData("2026-09-09T00:15:30.1234567+02:00", 120)]
    [InlineData("2026-09-09T00:15:30-03:30", -210)]
    [InlineData("2026-09-09T00:15:30Z", 0)]
    [InlineData("2026-09-09T00:15:30.123Z", 0)]
    public void Source_timestamps_preserve_the_explicit_offset_and_instant(string timestamp, int offsetMinutes)
    {
        var node = Invoice();
        node["ledgerNote"]!["statusCreated"] = timestamp;
        node["transactionCreated"]!["change"]!["statusCreated"] = timestamp;
        node["transactionCreated"]!["statusApproved"] = timestamp;
        var parsed = Parse(node);
        var expected = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None);
        foreach (var value in new[] { parsed.LedgerNote!.StatusCreated, parsed.TransactionCreated!.Change!.StatusCreated,
                     parsed.TransactionCreated.StatusApproved })
        {
            Assert.Equal(expected, value);
            Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), value!.Value.Offset);
        }
    }

    [Fact]
    public void Billing_date_is_a_calendar_date_and_metadata_round_trips_without_timezone_inference()
    {
        var parsed = Parse(Invoice());
        Assert.Equal(new DateOnly(2026, 9, 9), parsed.LedgerNote!.Invoice!.Billed);
        var roundTrip = JsonConvert.SerializeObject(new GraphQlResponse<LedgerNoteAccountingsSyncData>(
            new LedgerNoteAccountingsSyncData(new LedgerNoteAccountingsSyncConnection(new[] { parsed })), null));
        Assert.Contains("\"Billed\":\"2026-09-09\"", roundTrip);
        var restored = Assert.Single(Main.DeserializeGraphQlResult(roundTrip).Data!.LedgerNoteAccountingsSync!.Nodes);
        Assert.Equal(parsed.LedgerNote.StatusCreated, restored.LedgerNote!.StatusCreated);
        Assert.Equal(parsed.LedgerNote.StatusCreated!.Value.Offset, restored.LedgerNote.StatusCreated!.Value.Offset);
        Assert.Equal(parsed.LedgerNote.Invoice.Billed, restored.LedgerNote.Invoice!.Billed);
        Assert.Equal(parsed.TransactionCreated!.Change!.StatusCreated, restored.TransactionCreated!.Change!.StatusCreated);
        Assert.Equal(parsed.TransactionCreated.StatusApproved, restored.TransactionCreated.StatusApproved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_explicit_null_metadata_remains_unknown(bool explicitNull)
    {
        var node = Fixture.Node();
        if (explicitNull)
        {
            node["ledgerNote"]!["statusCreated"] = null;
            node["ledgerNote"]!["due"] = null;
            node["ledgerNote"]!["invoice"] = new JObject { ["billed"] = null };
            node["transactionCreated"] = new JObject
            {
                ["change"] = new JObject { ["statusCreated"] = null },
                ["statusApproved"] = null
            };
        }
        var parsed = Parse(node);
        Assert.Null(parsed.LedgerNote!.StatusCreated);
        Assert.Null(parsed.LedgerNote.Due);
        Assert.Null(parsed.LedgerNote.Invoice?.Billed);
        Assert.Null(parsed.TransactionCreated?.Change?.StatusCreated);
        Assert.Null(parsed.TransactionCreated?.StatusApproved);
    }

    [Theory]
    [InlineData("ledgerNote.statusCreated", "2026-09-09T00:00:00")]
    [InlineData("transactionCreated.change.statusCreated", "2026-09-09T00:00:00")]
    [InlineData("transactionCreated.statusApproved", "2026-09-09T00:00:00")]
    [InlineData("ledgerNote.statusCreated", "2026-02-30T00:00:00Z")]
    [InlineData("ledgerNote.invoice.billed", "2026-02-30")]
    [InlineData("ledgerNote.invoice.billed", "2026-09-09T00:00:00Z")]
    [InlineData("ledgerNote.invoice.billed", "09/09/2026")]
    [InlineData("ledgerNote.statusCreated", "synthetic-private-invalid-date")]
    public void Invalid_metadata_fails_without_echoing_source_values(string path, string value)
    {
        var node = Invoice();
        node.SelectToken(path)!.Replace(new JValue(value));
        var exception = Assert.Throws<InvalidOperationException>(() => Parse(node));
        Assert.DoesNotContain(value, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("ledgerNote.statusCreated")]
    [InlineData("transactionCreated.change.statusCreated")]
    [InlineData("transactionCreated.statusApproved")]
    [InlineData("ledgerNote.invoice.billed")]
    public void Metadata_must_not_coerce_numbers_booleans_or_objects_to_dates(string path)
    {
        foreach (var invalid in new JToken[] { new JValue(0), new JValue(false), new JObject(), new JArray() })
        {
            var node = Invoice();
            node.SelectToken(path)!.Replace(invalid);
            Assert.Throws<InvalidOperationException>(() => Parse(node));
        }
    }

    [Fact]
    public void Additional_metadata_does_not_change_raindance_bytes_or_invoice_count()
    {
        var plain = Fixture.Node();
        var enriched = Invoice();
        var original = Fixture.Convert(0, plain);
        var result = Fixture.Convert(0, enriched);
        Assert.Equal(original.ResultFile, result.ResultFile);
        Assert.Equal(original.NodeCount, result.NodeCount);
        Assert.Equal(original.LastLocalId, result.LastLocalId);
    }

    [PostgresFact]
    public async Task First_reservation_stores_typed_snapshot_and_replay_does_not_refresh_it()
    {
        var database = await MetadataDatabase.CreateAsync();
        var original = Invoice();
        var prepared = await ConvertAsync(database.Connection, original);
        Assert.Equal(Fixture.Convert(0, original).ResultFile, prepared.ResultFile);
        var snapshot = await database.ReadAsync();
        Assert.Equal(new DateTime(2026, 9, 8, 22, 15, 30, 123, DateTimeKind.Utc), snapshot.SourceCreatedAt);
        // +14:00 midnight is the previous UTC day, but a due date must retain its source calendar day.
        Assert.Equal(new DateOnly(2026, 9, 9), snapshot.DueDate);
        Assert.Equal(new DateOnly(2026, 9, 9), snapshot.InvoiceBilledDate);
        Assert.Equal(new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc), snapshot.AccountingEventCreatedAt);
        Assert.Equal(new DateTime(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc), snapshot.AccountingApprovedAt);

        await Main.ConfirmInvoiceDelivery(new DeliveryConfirmation
        {
            DeliveryId = prepared.DeliveryId,
            Filename = prepared.Filename,
            ContentSha256 = prepared.ContentSha256,
            DeliveryReference = "synthetic-metadata-write:" + prepared.DeliveryId
        }, database.Connection, TestContext.Current.CancellationToken);

        var replay = Invoice(2);
        replay["ledgerNote"]!["statusCreated"] = "2026-10-01T12:00:00Z";
        replay["ledgerNote"]!["due"] = "2026-10-31";
        replay["ledgerNote"]!["invoice"]!["billed"] = "2026-10-01";
        replay["transactionCreated"]!["change"]!["statusCreated"] = "2026-10-02T12:00:00Z";
        replay["transactionCreated"]!["statusApproved"] = "2026-10-03T12:00:00Z";
        var skipped = await ConvertAsync(database.Connection, replay);
        Assert.Empty(skipped.ResultFile);
        Assert.Equal(0, skipped.NodeCount);
        Assert.Equal(1, skipped.SkippedInvoiceCount);
        Assert.Equal(2, skipped.LastLocalId);
        Assert.Equal(snapshot, await database.ReadAsync());
    }

    [PostgresFact]
    public async Task Legacy_payload_can_reserve_an_invoice_with_all_metadata_null()
    {
        var database = await MetadataDatabase.CreateAsync();
        var node = Fixture.Node();
        node["ledgerNote"]!["id"] = "metadata-invoice";
        var prepared = await ConvertAsync(database.Connection, node);
        Assert.Equal(1, prepared.NodeCount);
        Assert.Equal(new Snapshot(null, null, null, null, null), await database.ReadAsync());
    }

    [PostgresFact]
    public async Task Historical_null_metadata_is_not_backfilled_when_source_data_is_available()
    {
        var database = await MetadataDatabase.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, historical_reference)
            VALUES (@source, 'metadata-invoice', 0, 'Synthetic approved baseline');
            """);
        var result = await ConvertAsync(database.Connection, Invoice());
        Assert.Empty(result.ResultFile);
        Assert.Equal(1, result.SkippedInvoiceCount);
        Assert.Equal(new Snapshot(null, null, null, null, null), await database.ReadAsync());
    }

    [PostgresFact]
    public async Task All_five_snapshot_columns_are_protected_from_direct_updates()
    {
        var database = await MetadataDatabase.CreateAsync();
        await ConvertAsync(database.Connection, Invoice());
        var before = await database.ReadAsync();
        // Column names are a fixed test-owned allowlist, never input or source values.
        foreach (var column in new[] { "source_created_at", "due_date", "invoice_billed_date",
                     "accounting_event_created_at", "accounting_approved_at" })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                $"UPDATE momentum_raindance.invoices SET {column} = NULL WHERE source_system = @source"));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
            Assert.Contains("history is permanent", exception.MessageText);
        }
        Assert.Equal(before, await database.ReadAsync());
    }

    private static LedgerNoteAccountingNode Parse(JObject node) =>
        Assert.Single(Main.DeserializeGraphQlResult(Fixture.Envelope(node)).Data!.LedgerNoteAccountingsSync!.Nodes);

    private static JObject Invoice(int localId = 1)
    {
        var node = Fixture.Node(localId);
        node["ledgerNote"]!["id"] = "metadata-invoice";
        node["ledgerNote"]!["statusCreated"] = "2026-09-09T00:15:30.123+02:00";
        node["ledgerNote"]!["due"] = "2026-09-09T00:00:00+14:00";
        node["ledgerNote"]!["invoice"] = new JObject { ["billed"] = "2026-09-09" };
        node["transactionCreated"] = new JObject
        {
            ["change"] = new JObject { ["statusCreated"] = "2026-09-09T10:00:00+02:00" },
            ["statusApproved"] = "2026-09-09T06:00:00-03:00"
        };
        return node;
    }

    private static async Task<ConversionResult> ConvertAsync(InvoiceTrackingConnection database, JObject invoice)
    {
        var input = await Fixture.RegisteredInputAsync(database, TestContext.Current.CancellationToken, invoice);
        return await Main.ConvertGraphQlResult(input, database, TestContext.Current.CancellationToken);
    }

    private sealed record Snapshot(DateTime? SourceCreatedAt, DateOnly? DueDate, DateOnly? InvoiceBilledDate,
        DateTime? AccountingEventCreatedAt, DateTime? AccountingApprovedAt);

    /// <summary>Opt-in tests use fresh synthetic sources inside an explicitly selected disposable database.</summary>
    private sealed class MetadataDatabase
    {
        private readonly string _connectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!;
        public InvoiceTrackingConnection Connection { get; }

        private MetadataDatabase()
        {
            Connection = new InvoiceTrackingConnection
            {
                ConfigurationSource = MomentumConfigurationSource.Manual,
                SourceSystem = "metadata-test-" + Guid.NewGuid().ToString("N"),
                ConnectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES_RUNTIME") ?? _connectionString
            };
        }

        public static async Task<MetadataDatabase> CreateAsync()
        {
            var database = new MetadataDatabase();
            await database.ExecuteAsync("""
                INSERT INTO momentum_raindance.sources (source_system, activated_at, baseline_reference)
                VALUES (@source, CURRENT_TIMESTAMP, 'Synthetic metadata baseline');
                """);
            return database;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("source", Connection.SourceSystem);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public async Task<Snapshot> ReadAsync()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT source_created_at, due_date, invoice_billed_date, accounting_event_created_at, accounting_approved_at
                FROM momentum_raindance.invoices WHERE source_system = @source AND ledger_note_id = 'metadata-invoice'
                """, connection);
            command.Parameters.AddWithValue("source", Connection.SourceSystem);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            var snapshot = new Snapshot(NullableField<DateTime>(reader, 0), NullableField<DateOnly>(reader, 1),
                NullableField<DateOnly>(reader, 2), NullableField<DateTime>(reader, 3), NullableField<DateTime>(reader, 4));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
            return snapshot;
        }

        private static T? NullableField<T>(NpgsqlDataReader reader, int ordinal) where T : struct =>
            reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
    }
}
