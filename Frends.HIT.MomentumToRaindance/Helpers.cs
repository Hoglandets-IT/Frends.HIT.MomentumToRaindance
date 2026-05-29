using System.Net;
using System.Security.Authentication;
using System.Text;
using Newtonsoft.Json;

namespace Frends.HIT.MomentumToRaindance;

internal sealed class SecretType
{
    [JsonProperty("secretValue")]
    public string SecretValue { get; set; } = "";
}

internal sealed class SecretResponse
{
    [JsonProperty("secret")]
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
        var response = client.PostAsync(
            infisicalAddr + "/api/v1/auth/universal-auth/login",
            new StringContent(
                JsonConvert.SerializeObject(new
                {
                    clientId = infisicalClientId,
                    clientSecret = infisicalClientSecret
                }),
                Encoding.UTF8,
                "application/json")).Result;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to authenticate to Infisical: " + response.ReasonPhrase);
        }

        var token = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content.ReadAsStringAsync().Result)?["accessToken"]
            ?? throw new InvalidOperationException("Infisical login response did not include accessToken.");

        var items = path.Split('/');
        var secret = items[^1];
        var secretPath = string.Join('/', items.Take(items.Length - 1));

        if (!secretPath.StartsWith("/"))
        {
            secretPath = "/" + secretPath;
        }

        if (secretPath.Contains("."))
        {
            secretPath = secretPath.Replace(".", "_");
        }

        secretPath = System.Web.HttpUtility.UrlEncode(secretPath);
        var fullAddress = infisicalAddr + "/api/v3/secrets/raw/" + secret + "?workspaceId=" + infisicalProject + "&secretPath=" + secretPath + "&environment=" + infisicalEnvironment;

        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

        var request = client.GetAsync(fullAddress).Result;
        if (!request.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to get secret from Infisical: " + request.ReasonPhrase);
        }

        var secretResponse = JsonConvert.DeserializeObject<SecretResponse>(request.Content.ReadAsStringAsync().Result);

        return secretResponse?.Secret.SecretValue
            ?? throw new InvalidOperationException("Infisical secret response did not include secret.secretValue.");
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required environment variable: {name}");
}
