using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace Frends.HIT.MomentumToRaindance;

internal sealed class SecretType
{
    [JsonProperty("secretValue")]
    public string? SecretValue { get; set; }
}

internal sealed class SecretResponse
{
    [JsonProperty("secret")]
    public SecretType? Secret { get; set; }
}

internal sealed class InfisicalAuthResponse
{
    [JsonProperty("accessToken")]
    public string? AccessToken { get; set; }
}

internal static class Helpers
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    public static string GetInfisicalSecret(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        return GetInfisicalSecretAsync(path, timeout.Token).GetAwaiter().GetResult();
    }

    public static async Task<string> GetInfisicalSecretAsync(
        string path,
        CancellationToken cancellationToken = default,
        HttpClient? httpClient = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var infisicalAddr = RequiredEnvironmentVariable("INFISICAL_ADDR");
        var infisicalClientId = RequiredEnvironmentVariable("INFISICAL_CLIENT_ID");
        var infisicalClientSecret = RequiredEnvironmentVariable("INFISICAL_CLIENT_SECRET");
        var infisicalProject = RequiredEnvironmentVariable("INFISICAL_PROJECT");
        var infisicalEnvironment = RequiredEnvironmentVariable("INFISICAL_ENVIRONMENT");

        var baseUri = RequiredHttpsUri(infisicalAddr, "INFISICAL_ADDR");
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("INFISICAL_ADDR must not contain a query or fragment.");
        }

        var baseAddress = baseUri.AbsoluteUri.TrimEnd('/');
        var separator = path.LastIndexOf('/');
        var secret = path[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(secret) || secret is "." or "..")
        {
            throw new ArgumentException("The secret path must end with a secret name.", nameof(path));
        }

        var secretPath = separator < 0 ? "/" : path[..separator];
        if (!secretPath.StartsWith('/'))
        {
            secretPath = "/" + secretPath;
        }

        // Preserve the directory-name mapping used by the existing RemoteFS integration.
        secretPath = secretPath.Replace('.', '_');
        var secretAddress = baseAddress + "/api/v3/secrets/raw/" + Uri.EscapeDataString(secret)
            + "?workspaceId=" + Uri.EscapeDataString(infisicalProject)
            + "&secretPath=" + Uri.EscapeDataString(secretPath)
            + "&environment=" + Uri.EscapeDataString(infisicalEnvironment);

        var client = httpClient ?? SharedHttpClient;
        using var loginMessage = new HttpRequestMessage(HttpMethod.Post, baseAddress + "/api/v1/auth/universal-auth/login")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    clientId = infisicalClientId,
                    clientSecret = infisicalClientSecret
                }),
                Encoding.UTF8,
                "application/json")
        };
        loginMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var loginResponse = await client.SendAsync(loginMessage, cancellationToken).ConfigureAwait(false);
        var loginPayload = await loginResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Main.EnsureJsonResponse(loginResponse, loginPayload, "Infisical authentication");

        var token = DeserializeResponse<InfisicalAuthResponse>(loginPayload, "Infisical authentication").AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Infisical login response did not include accessToken.");
        }

        using var secretMessage = new HttpRequestMessage(HttpMethod.Get, secretAddress);
        secretMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        secretMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var secretResponse = await client.SendAsync(secretMessage, cancellationToken).ConfigureAwait(false);
        var secretPayload = await secretResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Main.EnsureJsonResponse(secretResponse, secretPayload, "Infisical secret");

        var value = DeserializeResponse<SecretResponse>(secretPayload, "Infisical secret").Secret?.SecretValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Infisical secret response did not include a nonempty secret.secretValue.");
        }

        return value;
    }

    internal static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    internal static Uri RequiredHttpsUri(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required input: {name}");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{name} must be an absolute HTTPS URL without user information or a fragment.");
        }

        return uri;
    }

    private static T DeserializeResponse<T>(string payload, string operation) where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(payload)
                ?? throw new InvalidOperationException($"{operation} response was empty.");
        }
        catch (JsonException)
        {
            // JSON exception messages can contain token or secret values from the response.
            throw new InvalidOperationException($"{operation} response was not valid JSON.");
        }
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing required environment variable: {name}");
    }
}
