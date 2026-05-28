using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frends.HIT.MomentumToRaindance;

internal sealed class SecretType
{
    [JsonPropertyName("secretValue")]
    public string SecretValue { get; set; } = "";
}

internal sealed class SecretResponse
{
    [JsonPropertyName("secret")]
    public SecretType Secret { get; set; } = new();
}

internal static class Helpers
{
    public static string GetInfisicalSecret(string path)
    {
        var infisicalAddr = RequiredEnvironmentVariable("INFISICAL_ADDR");
        var infisicalClientId = RequiredEnvironmentVariable("INFISICAL_CLIENT_ID");
        var infisicalClientSecret = RequiredEnvironmentVariable("INFISICAL_CLIENT_SECRET");
        var infisicalProject = RequiredEnvironmentVariable("INFISICAL_PROJECT");
        var infisicalEnvironment = RequiredEnvironmentVariable("INFISICAL_ENVIRONMENT");

        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            SslProtocols = SslProtocols.Tls12,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var client = new HttpClient(handler);
        using var loginResponse = client.PostAsync(
            $"{infisicalAddr}/api/v1/auth/universal-auth/login",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    clientId = infisicalClientId,
                    clientSecret = infisicalClientSecret
                }),
                Encoding.UTF8,
                "application/json")).Result;

        if (!loginResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to authenticate to Infisical: " + loginResponse.ReasonPhrase);
        }

        var loginPayload = loginResponse.Content.ReadAsStringAsync().Result;
        var token = JsonSerializer.Deserialize<Dictionary<string, string>>(loginPayload)?["accessToken"]
            ?? throw new InvalidOperationException("Infisical login response did not include accessToken.");

        var items = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0)
        {
            throw new InvalidOperationException("Vault path must include a secret name.");
        }

        var secret = items[^1];
        var secretPath = "/" + string.Join('/', items.Take(items.Length - 1));

        if (secretPath.Contains('.'))
        {
            secretPath = secretPath.Replace(".", "_", StringComparison.Ordinal);
        }

        var fullAddress =
            $"{infisicalAddr}/api/v3/secrets/raw/{WebUtility.UrlEncode(secret)}" +
            $"?workspaceId={WebUtility.UrlEncode(infisicalProject)}" +
            $"&secretPath={WebUtility.UrlEncode(secretPath)}" +
            $"&environment={WebUtility.UrlEncode(infisicalEnvironment)}";

        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

        using var secretResponse = client.GetAsync(fullAddress).Result;
        if (!secretResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to get secret from Infisical: " + secretResponse.ReasonPhrase);
        }

        var secretPayload = secretResponse.Content.ReadAsStringAsync().Result;
        var parsedSecret = JsonSerializer.Deserialize<SecretResponse>(
            secretPayload,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return parsedSecret?.Secret.SecretValue
            ?? throw new InvalidOperationException("Infisical secret response did not include secret.secretValue.");
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required environment variable: {name}");
}
