using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public class TransportTests
{
    internal const string AuthenticationResponse = """
        {"completed":{"accessToken":"test-access-token","expiresInSeconds":3600}}
        """;
    internal const string EmptyResponse = """
        {"data":{"ledgerNoteAccountingsSync":{"nodes":[]}},"extensions":{"opaque":"2026-09-01T00:00:00.123+02:00","amount":123456789012.345678}}
        """;

    [Fact]
    public async Task Fetch_preserves_checkpoint_and_raw_json_while_authenticating_and_querying()
    {
        var requests = new List<(HttpMethod Method, string Uri, string? Authorization, JObject Body)>();
        using var httpClient = new HttpClient(new FakeHandler(async (request, cancellationToken) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))));
            return JsonResponse(requests.Count == 1 ? AuthenticationResponse : EmptyResponse);
        }));

        var result = await Main.FetchCoreAsync(Connection(), new FetchInput { LastLocalId = 123 },
            TestContext.Current.CancellationToken, httpClient);

        Assert.True(result.Success);
        Assert.Equal(123, result.LastLocalId);
        Assert.Contains('\n', result.ResultFile);
        Assert.True(JToken.DeepEquals(JObject.Parse(EmptyResponse), JObject.Parse(result.ResultFile)));
        Assert.Contains("123456789012.345678", result.ResultFile);
        Assert.Contains("2026-09-01T00:00:00.123+02:00", result.ResultFile);
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal("https://example.invalid/auth", requests[0].Uri);
        Assert.Null(requests[0].Authorization);
        Assert.Equal("password", (string?)requests[0].Body["method"]);
        Assert.Equal("test-identifier", (string?)requests[0].Body["identifier"]);
        Assert.Equal("test-api-key", (string?)requests[0].Body["key"]);
        Assert.Equal("https://example.invalid/graphql", requests[1].Uri);
        Assert.Equal("Bearer test-access-token", requests[1].Authorization);
        Assert.Contains("ledgerNoteAccountingsSync(lastLocalId: 123)", (string?)requests[1].Body["query"]);
        Assert.DoesNotContain("__LAST_LOCAL_ID__", (string?)requests[1].Body["query"]);
        Assert.Empty(Assert.IsType<JObject>(requests[1].Body["variables"]));
    }

    [Fact]
    public async Task Fetching_new_nodes_does_not_acknowledge_their_delivery()
    {
        var payload = Fixture.Envelope(Fixture.Node(localId: 900));
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(
            ++requestCount == 1 ? AuthenticationResponse : payload))));

        var result = await Main.FetchCoreAsync(Connection(), new FetchInput { LastLocalId = 123 },
            TestContext.Current.CancellationToken, httpClient);

        Assert.True(result.Success);
        Assert.Equal(123, result.LastLocalId);
        Assert.Equal(900, (int?)JObject.Parse(result.ResultFile)["data"]!["ledgerNoteAccountingsSync"]!["nodes"]![0]!["localId"]);
    }

    [Fact]
    public async Task Unrepresentable_json_number_is_rejected_without_exposing_its_value()
    {
        const string payload = """
            {"data":{"ledgerNoteAccountingsSync":{"nodes":[]}},"extensions":{"sensitive-marker":1.234567890123456789e100}}
            """;
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(
            ++requestCount == 1 ? AuthenticationResponse : payload))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Main.FetchCoreAsync(
            Connection(), new FetchInput(), TestContext.Current.CancellationToken, httpClient));

        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.DoesNotContain("1.234567890123456789", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("http://example.invalid/graphql")]
    [InlineData("/relative/graphql")]
    [InlineData("https://username:password@example.invalid/graphql")]
    [InlineData("https://example.invalid/graphql#fragment")]
    public async Task Invalid_destination_fails_before_sending_credentials(string graphQlUrl)
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(AuthenticationResponse));
        }));
        var connection = Connection();
        connection.GraphQlUrl = graphQlUrl;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Main.FetchCoreAsync(connection,
            new FetchInput(), TestContext.Current.CancellationToken, httpClient));

        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"completed\":null}")]
    [InlineData("{\"completed\":{\"accessToken\":\"\"}}")]
    [InlineData("{\"completed\":{\"accessToken\":\"  \"}}")]
    [InlineData("{\"completed\":{\"accessToken\":\"sensitive-marker\",\"expiresInSeconds\":\"sensitive-marker\"}}")]
    public async Task Invalid_auth_response_stops_fetch_without_exposing_secrets(string payload)
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(payload));
        }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(httpClient)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));

        Assert.Equal(1, requestCount);
        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Token_with_newlines_is_rejected_without_exposing_its_value()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse("{\"completed\":{\"accessToken\":\"sensitive-marker\\r\\nInjected: value\"}}"));
        }));

        var exception = await Assert.ThrowsAsync<FormatException>(() => Client(httpClient)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));

        Assert.Equal(1, requestCount);
        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_errors_in_either_operation_do_not_include_response_bodies(bool graphQlFailure)
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(graphQlFailure && requestCount == 1
                ? JsonResponse(AuthenticationResponse)
                : JsonResponse("{\"error\":\"sensitive-marker\"}", HttpStatusCode.Unauthorized));
        }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Client(httpClient)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));

        Assert.Equal(graphQlFailure ? 2 : 1, requestCount);
        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task Non_json_auth_error_does_not_include_html_body()
    {
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>sensitive-marker</html>", Encoding.UTF8, "text/html")
        })));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(httpClient)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("sensitive-marker", exception.ToString());
    }

    [Fact]
    public async Task Graphql_errors_do_not_return_a_successful_fetch()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(
            ++requestCount == 1 ? AuthenticationResponse : "{\"errors\":[{\"message\":\"sensitive-marker\"}]}"))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Main.FetchCoreAsync(
            Connection(), new FetchInput { LastLocalId = 123 }, TestContext.Current.CancellationToken, httpClient));

        Assert.Equal(2, requestCount);
        Assert.DoesNotContain("sensitive-marker", exception.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"ledgerNoteAccountingsSync\":{\"nodes\":null}}}")]
    public async Task Missing_connection_or_nodes_is_an_error_not_an_empty_success(string payload)
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(
            ++requestCount == 1 ? AuthenticationResponse : payload))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Main.FetchCoreAsync(
            Connection(), new FetchInput { LastLocalId = 123 }, TestContext.Current.CancellationToken, httpClient));
    }

    [Fact]
    public async Task In_flight_cancellation_reaches_http_handler()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, cancellationToken) =>
        {
            requestCount++;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(JsonResponse(AuthenticationResponse));
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.FetchCoreAsync(Connection(),
            new FetchInput(), cancellation.Token, httpClient));

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Already_cancelled_fetch_does_not_send_a_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(AuthenticationResponse));
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.FetchCoreAsync(Connection(),
            new FetchInput(), cancellation.Token, httpClient));

        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Configured_timeout_cancels_a_stalled_fetch()
    {
        var connection = Connection();
        connection.TimeoutSeconds = 1;
        using var httpClient = new HttpClient(new FakeHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(AuthenticationResponse);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.FetchCoreAsync(connection,
            new FetchInput(), TestContext.Current.CancellationToken, httpClient));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public async Task Invalid_timeout_is_rejected_before_network(int timeout)
    {
        var connection = Connection();
        connection.TimeoutSeconds = timeout;
        var requestCount = 0;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(AuthenticationResponse));
        }));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Main.FetchCoreAsync(connection,
            new FetchInput(), TestContext.Current.CancellationToken, httpClient));

        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Invalid_json_configuration_does_not_expose_parser_details()
    {
        var connection = new MomentumConnection
        {
            ConfigurationSource = MomentumConfigurationSource.Json,
            JsonConfiguration = "{\"authurl\":\"https://example.invalid/auth\",\"password\":sensitive-marker}"
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => connection
            .GetMomentumConfigurationAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Unknown_configuration_source_is_rejected()
    {
        var connection = Connection();
        connection.ConfigurationSource = (MomentumConfigurationSource)999;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => connection
            .GetMomentumConfigurationAsync(TestContext.Current.CancellationToken));
    }

    internal static MomentumConnection Connection() => new()
    {
        ConfigurationSource = MomentumConfigurationSource.Manual,
        AuthUrl = "https://example.invalid/auth",
        GraphQlUrl = "https://example.invalid/graphql",
        Username = "test-identifier",
        Password = "test-api-key"
    };

    private static MomentumClient Client(HttpClient httpClient) => new(new MomentumApiConfiguration
    {
        AuthUrl = "https://example.invalid/auth",
        GraphQlUrl = "https://example.invalid/graphql",
        Username = "test-identifier",
        Password = "test-api-key"
    }, new JsonSerializerSettings(), httpClient);

    internal static HttpResponseMessage JsonResponse(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}

[CollectionDefinition("Infisical environment", DisableParallelization = true)]
public class InfisicalEnvironmentCollection { }

[Collection("Infisical environment")]
public class InfisicalTransportTests
{
    [Fact]
    public async Task Invalid_token_does_not_escape_through_header_validation_exception()
    {
        using var environment = new InfisicalEnvironment();
        using var httpClient = new HttpClient(new TransportTests.FakeHandler((_, _) => Task.FromResult(
            TransportTests.JsonResponse("{\"accessToken\":\"sensitive-marker\\r\\nInjected: value\"}"))));

        var exception = await Assert.ThrowsAsync<FormatException>(() => Helpers.GetInfisicalSecretAsync(
            "/folder/key", TestContext.Current.CancellationToken, httpClient));

        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Secret_lookup_escapes_path_and_parameters_and_limits_bearer_to_lookup()
    {
        using var environment = new InfisicalEnvironment();
        var requests = new List<(string Uri, HttpMethod Method, AuthenticationHeaderValue? Authorization, string? Body)>();
        using var httpClient = new HttpClient(new TransportTests.FakeHandler(async (request, cancellationToken) =>
        {
            requests.Add((request.RequestUri!.AbsoluteUri, request.Method, request.Headers.Authorization,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return TransportTests.JsonResponse(requests.Count == 1
                ? "{\"accessToken\":\"secret-store-token\"}"
                : "{\"secret\":{\"secretValue\":\"test-config-value\"}}");
        }));

        var value = await Helpers.GetInfisicalSecretAsync("/folder.name/momentum & key",
            TestContext.Current.CancellationToken, httpClient);

        Assert.Equal("test-config-value", value);
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://secrets.example.invalid/api/v1/auth/universal-auth/login", requests[0].Uri);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Null(requests[0].Authorization);
        var body = JObject.Parse(requests[0].Body!);
        Assert.Equal("test-client", (string?)body["clientId"]);
        Assert.Equal("test-client-secret", (string?)body["clientSecret"]);
        Assert.Equal(HttpMethod.Get, requests[1].Method);
        Assert.Equal("Bearer secret-store-token", requests[1].Authorization?.ToString());
        Assert.Equal("https://secrets.example.invalid/api/v3/secrets/raw/momentum%20%26%20key?workspaceId=project%26id&secretPath=%2Ffolder_name&environment=test%20env", requests[1].Uri);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"secret\":null}")]
    [InlineData("{\"secret\":{\"secretValue\":\"\"}}")]
    [InlineData("{\"secret\":{\"secretValue\":{\"token\":\"sensitive-marker\"}}}")]
    public async Task Missing_or_malformed_secret_fails_without_sensitive_response_details(string payload)
    {
        using var environment = new InfisicalEnvironment();
        var requestCount = 0;
        using var httpClient = new HttpClient(new TransportTests.FakeHandler((_, _) => Task.FromResult(
            TransportTests.JsonResponse(++requestCount == 1 ? "{\"accessToken\":\"secret-store-token\"}" : payload))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Helpers.GetInfisicalSecretAsync(
            "/folder/key", TestContext.Current.CancellationToken, httpClient));

        Assert.Equal(2, requestCount);
        Assert.DoesNotContain("sensitive-marker", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Invalid_secret_path_is_rejected_before_authentication()
    {
        using var environment = new InfisicalEnvironment();
        var requestCount = 0;
        using var httpClient = new HttpClient(new TransportTests.FakeHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(TransportTests.JsonResponse("{}"));
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => Helpers.GetInfisicalSecretAsync(
            "/folder/", TestContext.Current.CancellationToken, httpClient));

        Assert.Equal(0, requestCount);
    }

    private sealed class InfisicalEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous;

        public InfisicalEnvironment()
        {
            var values = new Dictionary<string, string>
            {
                ["INFISICAL_ADDR"] = "https://secrets.example.invalid/",
                ["INFISICAL_CLIENT_ID"] = "test-client",
                ["INFISICAL_CLIENT_SECRET"] = "test-client-secret",
                ["INFISICAL_PROJECT"] = "project&id",
                ["INFISICAL_ENVIRONMENT"] = "test env"
            };
            _previous = values.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));
            foreach (var (name, value) in values)
                Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
