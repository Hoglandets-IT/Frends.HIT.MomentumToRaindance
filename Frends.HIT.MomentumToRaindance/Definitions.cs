using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>
/// Source for Momentum API connection configuration.
/// </summary>
public enum MomentumConfigurationSource
{
    /// <summary>
    /// From a JSON configuration string.
    /// </summary>
    [Display(Name = "JSON String")]
    Json,

    /// <summary>
    /// From a path in HCP Vault/Infisical.
    /// </summary>
    [Display(Name = "Hashicorp Vault")]
    HcpVault,

    /// <summary>
    /// Manual Momentum connection input fields.
    /// </summary>
    [Display(Name = "Manual Config")]
    Manual
}

/// <summary>
/// Momentum API connection settings.
/// </summary>
[DisplayName("Connection")]
public class MomentumConnection
{
    /// <summary>
    /// Whether to get configuration from JSON, HCP Vault/Infisical, or manual fields.
    /// </summary>
    [DefaultValue(MomentumConfigurationSource.Json)]
    public MomentumConfigurationSource ConfigurationSource { get; set; }

    /// <summary>
    /// HCP Vault/Infisical path to a secret containing the Momentum JSON configuration.
    /// </summary>
    [DefaultValue("")]
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.HcpVault)]
    [Display(Name = "Vault Path")]
    public string VaultPath { get; set; } = "";

    /// <summary>
    /// Momentum API configuration in JSON format.
    /// </summary>
    [DefaultValue("")]
    [DisplayFormat(DataFormatString = "Expression")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Json)]
    [Display(Name = "JSON Momentum Configuration")]
    public string JsonConfiguration { get; set; } = "";

    /// <summary>
    /// Momentum auth endpoint URL.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Manual)]
    [DefaultValue("https://example.invalid/momentum/auth")]
    public string AuthUrl { get; set; } = "";

    /// <summary>
    /// Momentum GraphQL endpoint URL.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Manual)]
    [DefaultValue("https://example.invalid/momentum/graphql")]
    public string GraphQlUrl { get; set; } = "";

    /// <summary>
    /// Momentum API identifier.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Manual)]
    [DefaultValue("momentum-username")]
    [Display(Name = "Username/Identifier")]
    public string Username { get; set; } = "momentum-username";

    /// <summary>
    /// Momentum API key.
    /// </summary>
    [PasswordPropertyText]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Manual)]
    [Display(Name = "Password/API Key")]
    public string Password { get; set; } = "";

    /// <summary>
    /// Resolve the selected input source into a Momentum API configuration.
    /// </summary>
    /// <returns>Momentum API configuration.</returns>
    public MomentumApiConfiguration GetMomentumConfiguration()
    {
        var json = ConfigurationSource switch
        {
            MomentumConfigurationSource.HcpVault => Helpers.GetInfisicalSecret(VaultPath),
            MomentumConfigurationSource.Json => JsonConfiguration,
            _ => null
        };

        if (json is not null)
        {
            var configuration = JsonSerializer.Deserialize<MomentumApiConfiguration>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return configuration ?? throw new InvalidOperationException("Momentum JSON configuration was empty.");
        }

        return new MomentumApiConfiguration
        {
            AuthUrl = AuthUrl,
            GraphQlUrl = GraphQlUrl,
            Username = Username,
            Password = Password
        };
    }
}

/// <summary>
/// Normalized Momentum API configuration.
/// </summary>
public class MomentumApiConfiguration
{
    /// <summary>
    /// Momentum auth endpoint URL.
    /// </summary>
    [JsonPropertyName("authurl")]
    public string AuthUrl { get; set; } = "";

    /// <summary>
    /// Momentum GraphQL endpoint URL.
    /// </summary>
    [JsonPropertyName("graphqlurl")]
    public string GraphQlUrl { get; set; } = "";

    /// <summary>
    /// Momentum username/identifier.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>
    /// Momentum password/API key.
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

}

/// <summary>
/// Options for fetching and converting Momentum ledger note accountings.
/// </summary>
public class FetchInput
{
    /// <summary>
    /// Last processed Momentum local id.
    /// </summary>
    [DefaultValue(0)]
    public int LastLocalId { get; set; }
}

/// <summary>
/// Input for converting an already fetched Momentum GraphQL response.
/// </summary>
public class ConvertInput
{
    /// <summary>
    /// Raw Momentum GraphQL JSON response as bytes. UTF-8 is expected.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    public byte[] GraphQlResult { get; set; } = [];
}

/// <summary>
/// Output from fetching Momentum GraphQL data.
/// </summary>
public class FetchResult
{
    /// <summary>
    /// Whether the conversion succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Raw Momentum GraphQL response bytes encoded as UTF-8.
    /// </summary>
    public byte[] ResultFile { get; set; }

    /// <summary>
    /// Informational message.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    /// Creates a fetch result.
    /// </summary>
    /// <param name="success">Whether the fetch succeeded.</param>
    /// <param name="resultFile">Raw Momentum GraphQL response bytes encoded as UTF-8.</param>
    /// <param name="info">Informational message.</param>
    public FetchResult(bool success, byte[] resultFile, string info)
    {
        Success = success;
        ResultFile = resultFile;
        Info = info;
    }
}

/// <summary>
/// Output from converting a Momentum GraphQL response to Raindance.
/// </summary>
public class ConversionResult
{
    /// <summary>
    /// Whether the conversion succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of Momentum ledger note accounting nodes in the response.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// Raindance fixed-width file bytes encoded as ISO-8859-1/Latin-1.
    /// </summary>
    public byte[] ResultFile { get; set; }

    /// <summary>
    /// Informational message.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    /// Creates a conversion result.
    /// </summary>
    /// <param name="success">Whether the conversion succeeded.</param>
    /// <param name="nodeCount">Number of Momentum ledger note accounting nodes in the response.</param>
    /// <param name="resultFile">Raindance fixed-width file bytes encoded as ISO-8859-1/Latin-1.</param>
    /// <param name="info">Informational message.</param>
    public ConversionResult(bool success, int nodeCount, byte[] resultFile, string info)
    {
        Success = success;
        NodeCount = nodeCount;
        ResultFile = resultFile;
        Info = info;
    }
}
