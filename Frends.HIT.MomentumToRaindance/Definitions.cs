using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

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
    /// From a HcpVault secret path.
    /// </summary>
    [Display(Name = "HcpVault")]
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
    /// Whether to get configuration from JSON, HcpVault, or manual fields.
    /// </summary>
    [DefaultValue(MomentumConfigurationSource.Json)]
    public MomentumConfigurationSource ConfigurationSource { get; set; }

    /// <summary>
    /// HcpVault path to a secret containing the Momentum JSON configuration.
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
    [PasswordPropertyText]
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
    /// Overall Fetch timeout in seconds for secret resolution, authentication and GraphQL retrieval.
    /// </summary>
    [DefaultValue(100)]
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Resolve the selected input source into a Momentum API configuration.
    /// </summary>
    /// <returns>Momentum API configuration.</returns>
    public MomentumApiConfiguration GetMomentumConfiguration()
    {
        if (TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Timeout must be between 1 and 3600 seconds.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        return GetMomentumConfigurationAsync(timeout.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Resolve connection settings asynchronously, including Infisical secret retrieval.
    /// </summary>
    /// <param name="cancellationToken">Cancellation from the calling process.</param>
    /// <returns>Momentum API configuration.</returns>
    public async Task<MomentumApiConfiguration> GetMomentumConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Timeout must be between 1 and 3600 seconds.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        cancellationToken = timeout.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var json = ConfigurationSource switch
        {
            MomentumConfigurationSource.HcpVault => await Helpers.GetInfisicalSecretAsync(VaultPath, cancellationToken).ConfigureAwait(false),
            MomentumConfigurationSource.Json => JsonConfiguration,
            MomentumConfigurationSource.Manual => null,
            _ => throw new ArgumentOutOfRangeException(nameof(ConfigurationSource), "Unknown configuration source.")
        };

        if (ConfigurationSource != MomentumConfigurationSource.Manual)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Momentum JSON configuration is required.");
            try
            {
                return JsonConvert.DeserializeObject<MomentumApiConfiguration>(json)
                    ?? throw new ArgumentException("Momentum JSON configuration is empty.");
            }
            catch (JsonException)
            {
                // Do not retain the exception: parser messages may contain credentials.
                throw new ArgumentException("Momentum configuration must be a valid JSON object.");
            }
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
    [JsonProperty("authurl")]
    public string AuthUrl { get; set; } = "";

    /// <summary>
    /// Momentum GraphQL endpoint URL.
    /// </summary>
    [JsonProperty("graphqlurl")]
    public string GraphQlUrl { get; set; } = "";

    /// <summary>
    /// Momentum username/identifier.
    /// </summary>
    [JsonProperty("username")]
    public string Username { get; set; } = "";

    /// <summary>
    /// Momentum password/API key.
    /// </summary>
    [JsonProperty("password")]
    [PasswordPropertyText]
    public string Password { get; set; } = "";

}

/// <summary>
/// Options for fetching and converting Momentum ledger note accountings.
/// </summary>
public class FetchInput
{
    /// <summary>
    /// Last successfully delivered Momentum local ID. No-work results return this value unchanged.
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
    /// Previously delivered local ID. Only newer nodes are converted; defaults to zero for full conversion.
    /// </summary>
    [DefaultValue(0)]
    public int LastLocalId { get; set; }

    /// <summary>
    /// Raw Momentum GraphQL JSON response.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    public string GraphQlResult { get; set; } = "";
}

/// <summary>
/// Output from fetching Momentum GraphQL data.
/// </summary>
public class FetchResult
{
    /// <summary>
    /// Input checkpoint, unchanged. Fetching alone does not acknowledge invoice processing or delivery.
    /// </summary>
    public int LastLocalId { get; set; }

    /// <summary>
    /// Whether fetching succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Pretty-printed Momentum GraphQL JSON response.
    /// </summary>
    public string ResultFile { get; set; }

    /// <summary>
    /// Informational message.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    /// Creates a fetch result.
    /// </summary>
    /// <param name="success">Whether the fetch succeeded.</param>
    /// <param name="resultFile">Pretty-printed Momentum GraphQL JSON response.</param>
    /// <param name="info">Informational message.</param>
    public FetchResult(bool success, string resultFile, string info)
        : this(success, resultFile, info, 0) { }

    /// <summary>Creates a fetch result with the caller's unchanged checkpoint.</summary>
    /// <param name="success">Whether the fetch succeeded.</param>
    /// <param name="resultFile">Prettified GraphQL JSON.</param>
    /// <param name="info">Operation summary.</param>
    /// <param name="lastLocalId">Input checkpoint.</param>
    [JsonConstructor]
    public FetchResult(bool success, string resultFile, string info, int lastLocalId)
    {
        Success = success;
        ResultFile = resultFile;
        Info = info;
        LastLocalId = lastLocalId;
    }
}

/// <summary>
/// Output from converting a Momentum GraphQL response to Raindance.
/// </summary>
public class ConversionResult
{
    /// <summary>
    /// Suggested output filename: 300K24_yyyyMMdd_HHmmss.txt, using the Frends agent's local time.
    /// Generated once per conversion result. Skip file delivery when NodeCount is zero.
    /// </summary>
    public string Filename { get; set; } = "";

    /// <summary>
    /// Checkpoint to persist after successful file delivery. Unchanged on no work; otherwise the
    /// highest handled local ID. A failed batch throws and returns no advanced checkpoint.
    /// </summary>
    public int LastLocalId { get; set; }

    /// <summary>
    /// Whether the conversion succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of invoices emitted, excluding old nodes and nodes with no non-rounding rows.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// Raindance fixed-width file content. Write to disk/SFTP using ISO-8859-1/Latin-1 encoding.
    /// </summary>
    public string ResultFile { get; set; }

    /// <summary>
    /// Informational message.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    /// Creates a conversion result.
    /// </summary>
    /// <param name="success">Whether the conversion succeeded.</param>
    /// <param name="nodeCount">Number of Momentum ledger note accounting nodes in the response.</param>
    /// <param name="resultFile">Raindance fixed-width file content. Persist using ISO-8859-1/Latin-1 encoding.</param>
    /// <param name="info">Informational message.</param>
    public ConversionResult(bool success, int nodeCount, string resultFile, string info)
        : this(success, nodeCount, resultFile, info, 0) { }

    /// <summary>Creates a conversion result and its proposed delivery checkpoint.</summary>
    /// <param name="success">Whether conversion succeeded.</param>
    /// <param name="nodeCount">Number of emitted invoices.</param>
    /// <param name="resultFile">Latin-1-compatible fixed-width text with CRLF endings.</param>
    /// <param name="info">Operation summary.</param>
    /// <param name="lastLocalId">Checkpoint to store after delivery.</param>
    [JsonConstructor]
    public ConversionResult(bool success, int nodeCount, string resultFile, string info, int lastLocalId)
    {
        Success = success;
        NodeCount = nodeCount;
        ResultFile = resultFile;
        Info = info;
        LastLocalId = lastLocalId;
    }
}
