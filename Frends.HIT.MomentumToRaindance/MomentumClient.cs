using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frends.HIT.MomentumToRaindance;

internal sealed class MomentumClient
{
    private readonly MomentumConnection _connection;
    private readonly JsonSerializerOptions _jsonOptions;

    public MomentumClient(MomentumConnection connection, JsonSerializerOptions jsonOptions)
    {
        _connection = connection;
        _jsonOptions = jsonOptions;
    }

    public async Task<string> FetchLedgerNoteAccountingsSyncAsync(int lastLocalId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = await AuthenticateAsync(httpClient);
        var queryTemplate = await Main.ReadEmbeddedTextAsync("Queries.LedgerNoteAccountingsSync.graphql");
        var query = queryTemplate.Replace("__LAST_LOCAL_ID__", lastLocalId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var request = new GraphQlRequest(
            query,
            new Dictionary<string, object?>());

        using var graphQlMessage = new HttpRequestMessage(HttpMethod.Post, RequiredUri(_connection.GraphQlUrl, nameof(_connection.GraphQlUrl)));
        graphQlMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        graphQlMessage.Content = JsonContent.Create(request, options: _jsonOptions);

        using var graphQlResponse = await httpClient.SendAsync(graphQlMessage);
        var graphQlPayload = await graphQlResponse.Content.ReadAsStringAsync();
        Main.EnsureJsonResponse(graphQlResponse, graphQlPayload, "GraphQL");

        return graphQlPayload;
    }

    private async Task<string> AuthenticateAsync(HttpClient httpClient)
    {
        var authRequest = new AuthRequest(
            Required(_connection.AuthMethod, nameof(_connection.AuthMethod)),
            Required(_connection.Identifier, nameof(_connection.Identifier)),
            Required(_connection.Key, nameof(_connection.Key)),
            _connection.RequestRefreshToken);

        using var message = new HttpRequestMessage(HttpMethod.Post, RequiredUri(_connection.AuthUrl, nameof(_connection.AuthUrl)))
        {
            Content = JsonContent.Create(authRequest, options: _jsonOptions)
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(message);
        var payload = await response.Content.ReadAsStringAsync();
        Main.EnsureJsonResponse(response, payload, "Auth");

        AuthResponse authResponse;
        try
        {
            authResponse = JsonSerializer.Deserialize<AuthResponse>(payload, _jsonOptions)
                ?? throw new InvalidOperationException("Auth response was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Auth response was not valid JSON.", exception);
        }

        return authResponse.Completed?.AccessToken
            ?? throw new InvalidOperationException("Auth response did not contain completed.accessToken.");
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Missing required input: {name}")
            : value;

    private static Uri RequiredUri(string? value, string name) =>
        Uri.TryCreate(Required(value, name), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{name} must be an absolute URL.");
}

internal sealed record AuthRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("identifier")] string Identifier,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("requestrefreshtoken")] bool RequestRefreshToken);

internal sealed record AuthResponse(
    [property: JsonPropertyName("completed")] AuthCompleted? Completed);

internal sealed record AuthCompleted(
    [property: JsonPropertyName("accessToken")] string? AccessToken,
    [property: JsonPropertyName("expiresInSeconds")] int? ExpiresInSeconds);
