using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>
/// Momentum API connection settings.
/// </summary>
public class MomentumConnection
{
    /// <summary>
    /// Momentum auth endpoint URL.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("https://nassjokommun-fastighet-test.momentum.se/Test/NassjoKommun/PmApi/V2/auth")]
    public string AuthUrl { get; set; } = "";

    /// <summary>
    /// Momentum GraphQL endpoint URL.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("https://nassjokommun-fastighet-test.momentum.se/Test/NassjoKommun/PmGraphQL")]
    public string GraphQlUrl { get; set; } = "";

    /// <summary>
    /// Authentication method.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("password")]
    public string AuthMethod { get; set; } = "password";

    /// <summary>
    /// Momentum API identifier.
    /// </summary>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("Centralreskontra")]
    public string Identifier { get; set; } = "Centralreskontra";

    /// <summary>
    /// Momentum API key.
    /// </summary>
    [PasswordPropertyText]
    public string Key { get; set; } = "";

    /// <summary>
    /// Whether to request a refresh token from Momentum.
    /// </summary>
    [DefaultValue(true)]
    public bool RequestRefreshToken { get; set; } = true;
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
/// Output from Momentum to Raindance conversion.
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
    public byte[] RaindanceFile { get; set; }

    /// <summary>
    /// Raw Momentum GraphQL response bytes encoded as UTF-8. Empty when converting from an existing response.
    /// </summary>
    public byte[] GraphQlResultFile { get; set; }

    /// <summary>
    /// Informational message.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    /// Creates a conversion result.
    /// </summary>
    /// <param name="success">Whether the conversion succeeded.</param>
    /// <param name="nodeCount">Number of Momentum ledger note accounting nodes in the response.</param>
    /// <param name="raindanceFile">Raindance fixed-width file bytes encoded as ISO-8859-1/Latin-1.</param>
    /// <param name="graphQlResultFile">Raw Momentum GraphQL response bytes encoded as UTF-8.</param>
    /// <param name="info">Informational message.</param>
    public ConversionResult(bool success, int nodeCount, byte[] raindanceFile, byte[] graphQlResultFile, string info)
    {
        Success = success;
        NodeCount = nodeCount;
        RaindanceFile = raindanceFile;
        GraphQlResultFile = graphQlResultFile;
        Info = info;
    }
}
