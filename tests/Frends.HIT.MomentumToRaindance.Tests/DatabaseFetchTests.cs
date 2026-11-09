using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class DatabaseFetchTests
{
    [Fact]
    public void Production_tasks_have_no_external_checkpoint_input()
    {
        var fetch = typeof(Main).GetMethod(nameof(Main.FetchLedgerNoteAccountings))!;
        Assert.Equal(typeof(Task<FetchResult>), fetch.ReturnType);
        Assert.DoesNotContain(fetch.GetParameters(), p => p.ParameterType == typeof(FetchInput));
        var database = Assert.Single(fetch.GetParameters(), p => p.ParameterType == typeof(InvoiceTrackingConnection));
        Assert.False(database.IsOptional);
        Assert.NotNull(Attribute.GetCustomAttribute(database, typeof(PropertyTabAttribute)));
        Assert.Null(typeof(ConvertInput).GetProperty("LastLocalId"));
        Assert.Equal(typeof(string), typeof(ConvertInput).GetProperty(nameof(ConvertInput.FetchId))!.PropertyType);
    }

    [PostgresFact]
    public async Task Historical_baseline_crosses_pages_and_fresh_execution_uses_confirmed_database_cursor()
    {
        var database = await CreateSourceAsync();
        await using (var admin = await AdminAsync())
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO momentum_raindance.invoices (source_system,ledger_note_id,first_local_id,historical_reference)
            SELECT @source, 'db-owned-invoice-' || n, 0, 'Synthetic confirmed baseline'
            FROM generate_series(1,56) n
            """, admin))
        {
            seed.Parameters.AddWithValue("source", database.SourceSystem);
            await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var nodes = Enumerable.Range(1, 69).Select(Invoice).ToArray();
        var requests = new List<JObject>();
        using var client = Client(requests, Page(nodes[..50], 69, true, "page-50"), Page(nodes[50..], 69, false, "page-69"));
        var fetched = await Main.FetchTrackedCoreAsync(TransportTests.Connection(), database, TestContext.Current.CancellationToken, client);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal(0, (int)request["variables"]!["lastLocalId"]!));
        Assert.Equal("page-50", (string?)requests[1]["variables"]!["after"]);
        Assert.Equal(0, fetched.LastLocalId);
        Assert.True(Guid.TryParse(fetched.FetchId, out _));
        Assert.Equal(0, await CursorAsync(database));
        // An obsolete Frends binding reset to null/zero cannot affect the new input contract.
        var input = JObject.FromObject(new ConvertInput { FetchId = fetched.FetchId, GraphQlResult = fetched.ResultFile });
        input["LastLocalId"] = null;
        var prepared = await Main.ConvertGraphQlResult(input.ToObject<ConvertInput>()!, database, TestContext.Current.CancellationToken);
        Assert.Equal(13, prepared.NodeCount);
        Assert.Equal(56, prepared.SkippedInvoiceCount);
        Assert.Equal(69, prepared.LastLocalId);
        Assert.Equal(0, await CursorAsync(database));
        var confirmed = await Main.ConfirmInvoiceDelivery(Confirmation(prepared), database, TestContext.Current.CancellationToken);
        Assert.Equal(69, confirmed.LastLocalId);
        Assert.Equal(69, await CursorAsync(database));

        // Reconstruct every process-local object, as after a Frends backend restart.
        var restartedDatabase = Connection(database.SourceSystem);
        var restartedRequests = new List<JObject>();
        using var restartedClient = Client(restartedRequests, Page(Array.Empty<JObject>(), 0, false, null));
        var next = await Main.FetchTrackedCoreAsync(TransportTests.Connection(), restartedDatabase,
            TestContext.Current.CancellationToken, restartedClient);
        Assert.Equal(69, (int)Assert.Single(restartedRequests)["variables"]!["lastLocalId"]!);
        var noWork = await Main.ConvertGraphQlResult(new ConvertInput { FetchId = next.FetchId, GraphQlResult = next.ResultFile },
            restartedDatabase, TestContext.Current.CancellationToken);
        Assert.Equal(0, noWork.NodeCount);
        Assert.Empty(noWork.ResultFile);
        Assert.Equal(69, noWork.LastLocalId);
        Assert.Equal(69, await CursorAsync(restartedDatabase));
    }

    [PostgresFact]
    public async Task Lost_fetch_result_can_be_fetched_again_but_reserved_delivery_blocks_restart_before_api()
    {
        var database = await CreateSourceAsync();
        using var firstClient = Client(new List<JObject>(), Page(new[] { Invoice(1) }, 1, false, null));
        var abandoned = await Main.FetchTrackedCoreAsync(TransportTests.Connection(), database,
            TestContext.Current.CancellationToken, firstClient);
        Assert.Equal(0, await CursorAsync(database));
        using var retryClient = Client(new List<JObject>(), Page(new[] { Invoice(1) }, 1, false, null));
        var retry = await Main.FetchTrackedCoreAsync(TransportTests.Connection(), Connection(database.SourceSystem),
            TestContext.Current.CancellationToken, retryClient);
        Assert.NotEqual(abandoned.FetchId, retry.FetchId);
        var prepared = await Main.ConvertGraphQlResult(new ConvertInput { FetchId = retry.FetchId, GraphQlResult = retry.ResultFile },
            database, TestContext.Current.CancellationToken);
        Assert.Equal(1, prepared.NodeCount);
        var calls = 0;
        using var blockedClient = new HttpClient(new TransportTests.FakeHandler((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("Network must not be reached with an unresolved delivery.");
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Main.FetchTrackedCoreAsync(TransportTests.Connection(),
            Connection(database.SourceSystem), TestContext.Current.CancellationToken, blockedClient));
        Assert.Equal(0, calls);
        Assert.Equal(0, await CursorAsync(database));
    }

    [PostgresFact]
    public async Task Later_page_failure_leaves_no_receipt_reservation_or_cursor_progress()
    {
        var database = await CreateSourceAsync();
        var calls = 0;
        using var client = new HttpClient(new TransportTests.FakeHandler((_, _) =>
        {
            calls++;
            return calls switch
            {
                1 => Task.FromResult(TransportTests.JsonResponse(TransportTests.AuthenticationResponse)),
                2 => Task.FromResult(TransportTests.JsonResponse(Page(new[] { Invoice(1) }, 2, true, "page-1"))),
                _ => throw new HttpRequestException("Synthetic connection failure")
            };
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() => Main.FetchTrackedCoreAsync(TransportTests.Connection(), database,
            TestContext.Current.CancellationToken, client));
        Assert.Equal(0, await CursorAsync(database));
        await using var admin = await AdminAsync();
        await using var counts = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM momentum_raindance.fetch_batches WHERE source_system=@source)
                 + (SELECT count(*) FROM momentum_raindance.deliveries WHERE source_system=@source)
                 + (SELECT count(*) FROM momentum_raindance.invoices WHERE source_system=@source)
            """, admin);
        counts.Parameters.AddWithValue("source", database.SourceSystem);
        Assert.Equal(0L, await counts.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static JObject Invoice(int id)
    {
        var node = Fixture.Node(id);
        node["ledgerNote"]!["id"] = "db-owned-invoice-" + id;
        return node;
    }

    private static string Page(JObject[] nodes, int count, bool hasNext, string? cursor)
    {
        var result = JObject.Parse(Fixture.CompleteEnvelope(nodes));
        var connection = result["data"]!["ledgerNoteAccountingsSync"]!;
        connection["totalCount"] = count;
        connection["pageInfo"] = new JObject { ["hasNextPage"] = hasNext, ["endCursor"] = cursor };
        return result.ToString(Formatting.None);
    }

    private static HttpClient Client(List<JObject> requests, params string[] pages)
    {
        var call = 0;
        return new HttpClient(new TransportTests.FakeHandler(async (request, token) =>
        {
            if (call++ == 0) return TransportTests.JsonResponse(TransportTests.AuthenticationResponse);
            requests.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(token)));
            return TransportTests.JsonResponse(pages[call - 2]);
        }));
    }

    private static InvoiceTrackingConnection Connection(string source) => new()
    {
        SourceSystem = source,
        ConfigurationSource = MomentumConfigurationSource.Manual,
        ConnectionString = Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES_RUNTIME")
            ?? Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!
    };

    private static async Task<InvoiceTrackingConnection> CreateSourceAsync()
    {
        var database = Connection("db-fetch-test-" + Guid.NewGuid().ToString("N"));
        await using var admin = await AdminAsync();
        await using var command = new NpgsqlCommand("INSERT INTO momentum_raindance.sources (source_system,activated_at,baseline_reference) VALUES (@source,now(),'Synthetic DB checkpoint test')", admin);
        command.Parameters.AddWithValue("source", database.SourceSystem);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private static async Task<NpgsqlConnection> AdminAsync()
    {
        var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("MOMENTUM_TEST_POSTGRES")!);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<int> CursorAsync(InvoiceTrackingConnection database)
    {
        await using var admin = await AdminAsync();
        await using var command = new NpgsqlCommand("SELECT last_local_id FROM momentum_raindance.sources WHERE source_system=@source", admin);
        command.Parameters.AddWithValue("source", database.SourceSystem);
        return (int)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static DeliveryConfirmation Confirmation(ConversionResult result) => new()
    {
        DeliveryId = result.DeliveryId,
        Filename = result.Filename,
        ContentSha256 = result.ContentSha256,
        DeliveryReference = "Synthetic database-owned delivery " + result.DeliveryId
    };
}
