using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Frends.HIT.MomentumToRaindance;

internal sealed class MomentumClient
{
    private static readonly HttpClient SharedHttpClient = Helpers.CreateHttpClient();

    private readonly MomentumApiConfiguration _configuration;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly HttpClient _httpClient;
    private readonly Uri _authUri;
    private readonly Uri _graphQlUri;

    public MomentumClient(
        MomentumApiConfiguration configuration,
        JsonSerializerSettings jsonSettings,
        HttpClient? httpClient = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _jsonSettings = jsonSettings ?? throw new ArgumentNullException(nameof(jsonSettings));
        _httpClient = httpClient ?? SharedHttpClient;

        // Validate both destinations before credentials are sent to either endpoint.
        _authUri = Helpers.RequiredHttpsUri(_configuration.AuthUrl, nameof(_configuration.AuthUrl));
        _graphQlUri = Helpers.RequiredHttpsUri(_configuration.GraphQlUrl, nameof(_configuration.GraphQlUrl));
    }

    public async Task<string> FetchLedgerNoteAccountingsSyncAsync(int lastLocalId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastLocalId);
        cancellationToken.ThrowIfCancellationRequested();

        var token = await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        var query = await Main.ReadEmbeddedTextAsync("Queries.LedgerNoteAccountingsSync.graphql").ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        JObject? result = null;
        var mergedNodes = new JArray();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        int? totalCount = null;
        var previousLocalId = lastLocalId;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new GraphQlRequest(query,
                new Dictionary<string, object?> { ["lastLocalId"] = lastLocalId, ["after"] = after });
            using var graphQlMessage = new HttpRequestMessage(HttpMethod.Post, _graphQlUri);
            graphQlMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            graphQlMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            graphQlMessage.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

            using var graphQlResponse = await _httpClient.SendAsync(graphQlMessage, cancellationToken).ConfigureAwait(false);
            var graphQlPayload = await graphQlResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Main.EnsureJsonResponse(graphQlResponse, graphQlPayload, "GraphQL");
            var page = ParsePage(graphQlPayload);
            var connection = (JObject)page["data"]!["ledgerNoteAccountingsSync"]!;
            var nodes = (JArray)connection["nodes"]!;
            var pageInfo = (JObject)connection["pageInfo"]!;
            var pageTotal = RequiredInteger(connection["totalCount"], 0);
            if (totalCount is not null && totalCount != pageTotal)
                throw InvalidPage("The accounting feed changed its total count during pagination; no complete batch was returned.");
            totalCount = pageTotal;
            var hasNextPage = (bool)pageInfo["hasNextPage"]!;
            var endCursor = (string?)pageInfo["endCursor"];
            if (endCursor is not null && !cursors.Add(endCursor))
                throw InvalidPage("The accounting feed repeated a page cursor; no complete batch was returned.");

            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is not JObject invoice)
                    throw InvalidPage();
                var localId = RequiredInteger(invoice["localId"], 1);
                if (localId <= previousLocalId)
                    throw InvalidPage("Accounting local IDs must strictly increase after the requested checkpoint across every page.");
                previousLocalId = localId;
                mergedNodes.Add(invoice.DeepClone());
            }
            if (mergedNodes.Count > totalCount || (hasNextPage && (nodes.Count == 0 || mergedNodes.Count >= totalCount)))
                throw InvalidPage("Accounting page counts are inconsistent; no complete batch was returned.");
            result ??= page;
            if (!hasNextPage)
            {
                if (mergedNodes.Count != totalCount)
                    throw InvalidPage("The accounting feed ended before its total count was fetched; no complete batch was returned.");
                var resultConnection = (JObject)result["data"]!["ledgerNoteAccountingsSync"]!;
                resultConnection["nodes"] = mergedNodes;
                resultConnection["pageInfo"] = pageInfo.DeepClone();
                cancellationToken.ThrowIfCancellationRequested();
                return result.ToString(Formatting.None);
            }
            if (string.IsNullOrWhiteSpace(endCursor))
                throw InvalidPage("A continuing accounting page requires a nonblank cursor; no complete batch was returned.");
            after = endCursor;
        }
    }

    private static JObject ParsePage(string payload)
    {
        try
        {
            using var text = new StringReader(payload);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
            if (JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }) is not JObject page
                || reader.Read())
                throw InvalidPage();
            if (page["errors"] is { Type: not JTokenType.Null } errors && (errors is not JArray list || list.Count != 0))
                throw InvalidPage("GraphQL returned errors; the entire accounting batch was rejected.");
            if (page["data"] is not JObject data || data["ledgerNoteAccountingsSync"] is not JObject connection
                || connection["nodes"] is not JArray
                || connection["pageInfo"] is not JObject pageInfo
                || pageInfo["hasNextPage"]?.Type != JTokenType.Boolean
                || pageInfo["endCursor"] is not { Type: JTokenType.String or JTokenType.Null })
                throw InvalidPage();
            _ = RequiredInteger(connection["totalCount"], 0);
            return page;
        }
        catch (JsonException)
        {
            // Parser diagnostics can include source values, access tokens, or customer data.
            throw InvalidPage();
        }
    }

    private static int RequiredInteger(JToken? value, int minimum)
    {
        if (value?.Type != JTokenType.Integer
            || !int.TryParse(value.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum)
            throw InvalidPage();
        return parsed;
    }

    private static InvalidOperationException InvalidPage(string message =
        "Momentum must return valid accounting pages with explicit nodes, integer totalCount and local IDs, and complete pageInfo; no complete batch was returned.") => new(message);

    private async Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        var authRequest = new AuthRequest(
            "password",
            Required(_configuration.Username, nameof(_configuration.Username)),
            Required(_configuration.Password, nameof(_configuration.Password)),
            true);

        using var message = new HttpRequestMessage(HttpMethod.Post, _authUri)
        {
            Content = new StringContent(JsonConvert.SerializeObject(authRequest), Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Main.EnsureJsonResponse(response, payload, "Auth");

        AuthResponse authResponse;
        try
        {
            authResponse = JsonConvert.DeserializeObject<AuthResponse>(payload, _jsonSettings)
                ?? throw new InvalidOperationException("Auth response was empty.");
        }
        catch (JsonException)
        {
            // The parser's exception can include secret token values from the response.
            throw new InvalidOperationException("Auth response was not valid JSON.");
        }

        var token = authResponse.Completed?.AccessToken;
        return !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new InvalidOperationException("Auth response did not contain completed.accessToken.");
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Missing required input: {name}")
            : value;
}

internal sealed record AuthRequest(
    [property: JsonProperty("method")] string Method,
    [property: JsonProperty("identifier")] string Identifier,
    [property: JsonProperty("key")] string Key,
    [property: JsonProperty("requestrefreshtoken")] bool RequestRefreshToken);

internal sealed record AuthResponse(
    [property: JsonProperty("completed")] AuthCompleted? Completed);

internal sealed record AuthCompleted(
    [property: JsonProperty("accessToken")] string? AccessToken,
    [property: JsonProperty("expiresInSeconds")] int? ExpiresInSeconds);
