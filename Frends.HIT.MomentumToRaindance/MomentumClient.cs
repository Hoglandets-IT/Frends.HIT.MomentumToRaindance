using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

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
        var queryTemplate = await Main.ReadEmbeddedTextAsync("Queries.LedgerNoteAccountingsSync.graphql").ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var query = queryTemplate.Replace("__LAST_LOCAL_ID__", lastLocalId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var request = new GraphQlRequest(
            query,
            new Dictionary<string, object?>());

        using var graphQlMessage = new HttpRequestMessage(HttpMethod.Post, _graphQlUri);
        graphQlMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        graphQlMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        graphQlMessage.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

        using var graphQlResponse = await _httpClient.SendAsync(graphQlMessage, cancellationToken).ConfigureAwait(false);
        var graphQlPayload = await graphQlResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Main.EnsureJsonResponse(graphQlResponse, graphQlPayload, "GraphQL");

        return graphQlPayload;
    }

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
