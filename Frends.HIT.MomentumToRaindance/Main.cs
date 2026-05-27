using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>
/// Frends task methods for converting Momentum ledger note accountings to Raindance import files.
/// </summary>
[DisplayName("MomentumToRaindance")]
public class Main
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Fetch ledger note accountings from Momentum GraphQL and convert them to a Raindance fixed-width byte stream.
    /// </summary>
    /// <param name="connection">Momentum API connection settings.</param>
    /// <param name="input">Fetch options.</param>
    /// <returns>Raindance file bytes and raw GraphQL response bytes.</returns>
    [DisplayName("Fetch and Convert Ledger Note Accountings")]
    public static async Task<ConversionResult> FetchAndConvertLedgerNoteAccountings(
        [PropertyTab] MomentumConnection connection,
        [PropertyTab] FetchInput input)
    {
        var client = new MomentumClient(connection, JsonOptions);
        var graphQlPayload = await client.FetchLedgerNoteAccountingsSyncAsync(input.LastLocalId);
        var result = DeserializeGraphQlResult(graphQlPayload);
        var raindanceBytes = RaindanceWriter.ToBytes(result);
        var graphQlBytes = Encoding.UTF8.GetBytes(PrettyPrintJson(graphQlPayload));

        return new ConversionResult(
            success: true,
            nodeCount: NodeCount(result),
            raindanceFile: raindanceBytes,
            graphQlResultFile: graphQlBytes,
            info: "Momentum response converted to Raindance.");
    }

    /// <summary>
    /// Convert an existing Momentum GraphQL response byte stream to a Raindance fixed-width byte stream.
    /// </summary>
    /// <param name="input">Existing GraphQL result bytes.</param>
    /// <returns>Raindance file bytes.</returns>
    [DisplayName("Convert GraphQL Result")]
    public static ConversionResult ConvertGraphQlResult([PropertyTab] ConvertInput input)
    {
        var payload = Encoding.UTF8.GetString(input.GraphQlResult);
        var result = DeserializeGraphQlResult(payload);
        var raindanceBytes = RaindanceWriter.ToBytes(result);

        return new ConversionResult(
            success: true,
            nodeCount: NodeCount(result),
            raindanceFile: raindanceBytes,
            graphQlResultFile: [],
            info: "GraphQL response converted to Raindance.");
    }

    internal static async Task<string> ReadEmbeddedTextAsync(string resourceSuffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            var availableResources = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource ending with '{resourceSuffix}' was not found. Available resources: {availableResources}");
        }

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    internal static GraphQlResponse<LedgerNoteAccountingsSyncData> DeserializeGraphQlResult(string payload)
    {
        var result = JsonSerializer.Deserialize<GraphQlResponse<LedgerNoteAccountingsSyncData>>(payload, JsonOptions)
            ?? throw new InvalidOperationException("GraphQL response was empty.");

        if (result.Errors is { Count: > 0 })
        {
            var messages = string.Join(Environment.NewLine, result.Errors.Select(error => $"- {error.Message}"));
            throw new InvalidOperationException($"GraphQL returned errors:{Environment.NewLine}{messages}");
        }

        return result;
    }

    internal static void EnsureJsonResponse(HttpResponseMessage response, string payload, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{operation} request failed. {BuildResponseDetails(response, payload)}");
        }

        if (LooksLikeJson(payload))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} endpoint returned a non-JSON response. {BuildResponseDetails(response, payload)}");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleStringConverter());
        return options;
    }

    private static int NodeCount(GraphQlResponse<LedgerNoteAccountingsSyncData> result) =>
        result.Data?.LedgerNoteAccountingsSync?.Nodes.Count ?? 0;

    private static bool LooksLikeJson(string payload)
    {
        var trimmed = payload.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static string BuildResponseDetails(HttpResponseMessage response, string payload)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "<none>";
        return $"Status={(int)response.StatusCode} {response.ReasonPhrase}; Content-Type={contentType}; Body preview={Preview(payload)}";
    }

    private static string Preview(string payload)
    {
        const int maxLength = 500;
        var normalized = new StringBuilder(payload.Length);

        foreach (var character in payload)
        {
            normalized.Append(char.IsControl(character) ? ' ' : character);
        }

        var value = normalized.ToString().Trim();
        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

    private static string PrettyPrintJson(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
    }
}
