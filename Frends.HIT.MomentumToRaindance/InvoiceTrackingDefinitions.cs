using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Npgsql;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>PostgreSQL connection and stable Momentum dataset identity. Required for production conversion.</summary>
[DisplayName("Invoice tracking database")]
public sealed class InvoiceTrackingConnection
{
    /// <summary>JSON, HcpVault, or a manually supplied PostgreSQL connection string.</summary>
    [DefaultValue(MomentumConfigurationSource.HcpVault)]
    public MomentumConfigurationSource ConfigurationSource { get; set; } = MomentumConfigurationSource.HcpVault;

    /// <summary>Secret path containing { "connectionString": "Host=...;..." }.</summary>
    [DefaultValue("")]
    [DisplayFormat(DataFormatString = "Text")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.HcpVault)]
    public string VaultPath { get; set; } = "";

    /// <summary>JSON object containing connectionString. Never include credentials in logs.</summary>
    [DefaultValue("")]
    [PasswordPropertyText]
    [DisplayFormat(DataFormatString = "Expression")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Json)]
    public string JsonConfiguration { get; set; } = "";

    /// <summary>Npgsql connection string. Use SSL Mode=VerifyFull for production; local development may disable TLS.</summary>
    [DefaultValue("")]
    [PasswordPropertyText]
    [DisplayFormat(DataFormatString = "Expression")]
    [UIHint(nameof(ConfigurationSource), "", MomentumConfigurationSource.Manual)]
    public string ConnectionString { get; set; } = "";

    /// <summary>Permanent identifier for the Momentum dataset/environment, registered and activated offline.
    /// All processes exporting the same invoices must use the same value. Never rename to bypass history.</summary>
    [DefaultValue("")]
    [DisplayFormat(DataFormatString = "Text")]
    public string SourceSystem { get; set; } = "";

    /// <summary>Total timeout for secret retrieval and database work, in seconds.</summary>
    [DefaultValue(100)]
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 100;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceSystem) || SourceSystem.Length > 200 || SourceSystem != SourceSystem.Trim()
            || SourceSystem.Any(char.IsControl))
            throw new ArgumentException("SourceSystem must be a stable, nonblank identifier of at most 200 characters, without surrounding whitespace or controls.");
        if (TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Database timeout must be between 1 and 3600 seconds.");
    }

    internal async Task<string> ResolveAsync(CancellationToken cancellationToken, HttpClient? secretHttpClient = null)
    {
        Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var json = ConfigurationSource switch
        {
            MomentumConfigurationSource.HcpVault => await Helpers.GetInfisicalSecretAsync(VaultPath, cancellationToken, secretHttpClient).ConfigureAwait(false),
            MomentumConfigurationSource.Json => JsonConfiguration,
            MomentumConfigurationSource.Manual => null,
            _ => throw new ArgumentException("Unknown database configuration source.")
        };
        string? raw = ConnectionString;
        if (ConfigurationSource != MomentumConfigurationSource.Manual)
        {
            try
            {
                raw = JsonConvert.DeserializeObject<DatabaseSecret>(json ?? "")?.ConnectionString;
            }
            catch (JsonException)
            {
                throw new ArgumentException("Database configuration must be a JSON object containing connectionString.");
            }
        }
        try
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException();
            var builder = new NpgsqlConnectionStringBuilder(raw);
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)
                || string.IsNullOrWhiteSpace(builder.Username))
                throw new ArgumentException();
            var local = builder.Host is "localhost" or "127.0.0.1" or "::1";
            if (builder.SslMode != SslMode.VerifyFull && !(local && builder.SslMode == SslMode.Disable))
                throw new ArgumentException();
            // Never allow ambient Frends transactions to roll back a claim after bytes were handed out.
            builder.Enlist = false;
            builder.Multiplexing = false;
            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            builder.PersistSecurityInfo = false;
            builder.Timeout = Math.Min(TimeoutSeconds, 30);
            builder.CommandTimeout = TimeoutSeconds;
            builder.ApplicationName = "MomentumToRaindance";
            return builder.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            // Provider/parser exception messages can contain a password or entire connection string.
            throw new ArgumentException("Invalid PostgreSQL configuration. Supply Host, Database, Username and SSL Mode=VerifyFull (or Disable for loopback development).");
        }
    }

    private sealed class DatabaseSecret
    {
        [JsonProperty("connectionString")]
        public string? ConnectionString { get; set; }
    }
}

/// <summary>Evidence of successful external file delivery. Never call before the file writer succeeds.</summary>
public sealed class DeliveryConfirmation
{
    /// <summary>DeliveryId returned by Convert GraphQL Result.</summary>
    [DisplayFormat(DataFormatString = "Expression")]
    public string DeliveryId { get; set; } = "";

    /// <summary>Filename returned by the same conversion.</summary>
    [DisplayFormat(DataFormatString = "Expression")]
    public string Filename { get; set; } = "";

    /// <summary>ContentSha256 returned by the same conversion; identifies the exact encoded file.</summary>
    [DisplayFormat(DataFormatString = "Expression")]
    public string ContentSha256 { get; set; } = "";

    /// <summary>Durable delivery evidence, e.g. Frends execution ID and destination path. No credentials.</summary>
    [DisplayFormat(DataFormatString = "Expression")]
    public string DeliveryReference { get; set; } = "";
}

/// <summary>Recorded file delivery, not confirmation of Raindance import.</summary>
public sealed class DeliveryResult
{
    /// <summary>Confirmed reservation identifier.</summary>
    public string DeliveryId { get; set; } = "";
    /// <summary>True after delivery evidence is durably recorded, including idempotent repeat confirmation.</summary>
    public bool Success { get; set; }
    /// <summary>Number of invoices in this delivery.</summary>
    public int NodeCount { get; set; }
    /// <summary>Checkpoint that can now be saved after successful delivery.</summary>
    public int LastLocalId { get; set; }
}
