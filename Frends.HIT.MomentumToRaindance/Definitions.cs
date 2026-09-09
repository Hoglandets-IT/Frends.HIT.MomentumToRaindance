using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>
/// Source for Momentum API or invoice-tracking database connection configuration.
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
    /// Manual connection input fields.
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
    /// Last successfully delivered Momentum local ID, supplied as an integer or a numeric string
    /// from shared state. Defaults to zero; explicit null, invalid or out-of-range values fail.
    /// </summary>
    [DefaultValue(0)]
    [DisplayFormat(DataFormatString = "Expression")]
    public object? LastLocalId { get; set; } = 0;
}

/// <summary>
/// Input for converting an already fetched Momentum GraphQL response.
/// </summary>
public class ConvertInput
{
    /// <summary>
    /// Previously delivered local ID, supplied as an integer or a numeric string. Only newer nodes
    /// are converted; defaults to zero for full conversion. Invalid values fail rather than resetting.
    /// </summary>
    [DefaultValue(0)]
    [DisplayFormat(DataFormatString = "Expression")]
    public object? LastLocalId { get; set; } = 0;

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
    /// <summary>Durable PostgreSQL reservation ID. Empty when no file is emitted. Pass to Confirm Invoice Delivery after writing once.</summary>
    public string DeliveryId { get; set; } = "";

    /// <summary>SHA-256 of the exact ResultFile bytes, lowercase hexadecimal. Empty when no file is emitted.</summary>
    public string ContentSha256 { get; set; } = "";

    /// <summary>Invoice identities already present in confirmed or historical database records.</summary>
    public int SkippedInvoiceCount { get; set; }

    /// <summary>
    /// Reserved output filename: 300K24_yyyyMMdd_HHmmss.txt. Starts at the agent's local time;
    /// database collisions advance to the next free second. Empty when NodeCount is zero.
    /// </summary>
    public string Filename { get; set; } = "";

    /// <summary>
    /// Highest handled local ID. Empty/stale/rounding-only input preserves the checkpoint;
    /// skipping already recorded invoice identities can advance it without a new file.
    /// For a new delivery persist only after Confirm Invoice Delivery succeeds.
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
    /// Raindance file bytes, already encoded as ISO-8859-1/Latin-1 with CRLF line endings.
    /// Pass directly to the file writer's byte-content input using RAW mode; do not re-encode.
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
    /// <param name="resultFile">Encoded Raindance file bytes. Persist unchanged using RAW mode.</param>
    /// <param name="info">Informational message.</param>
    public ConversionResult(bool success, int nodeCount, byte[] resultFile, string info)
        : this(success, nodeCount, resultFile, info, 0) { }

    /// <summary>Creates a conversion result and its proposed delivery checkpoint.</summary>
    /// <param name="success">Whether conversion succeeded.</param>
    /// <param name="nodeCount">Number of emitted invoices.</param>
    /// <param name="resultFile">Latin-1-encoded fixed-width bytes with CRLF endings.</param>
    /// <param name="info">Operation summary.</param>
    /// <param name="lastLocalId">Checkpoint to store after delivery.</param>
    [JsonConstructor]
    public ConversionResult(bool success, int nodeCount, byte[] resultFile, string info, int lastLocalId)
    {
        Success = success;
        NodeCount = nodeCount;
        ResultFile = resultFile;
        Info = info;
        LastLocalId = lastLocalId;
    }
}
