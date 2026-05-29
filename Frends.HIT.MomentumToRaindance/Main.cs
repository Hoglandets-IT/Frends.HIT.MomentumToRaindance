using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace Frends.HIT.MomentumToRaindance;

/// <summary>
/// Frends task methods for fetching Momentum ledger note accountings and converting them to Raindance import files.
/// </summary>
[DisplayName("MomentumToRaindance")]
public class Main
{
    private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();

    /// <summary>
    /// Fetch ledger note accountings from Momentum GraphQL.
    /// </summary>
    /// <param name="connection">Momentum API connection settings.</param>
    /// <param name="input">Fetch options.</param>
    /// <returns>Raw GraphQL response bytes.</returns>
    [DisplayName("Fetch Ledger Note Accountings")]
    public static async Task<FetchResult> FetchLedgerNoteAccountings(
        [PropertyTab] MomentumConnection connection,
        [PropertyTab] FetchInput input)
    {
        var client = new MomentumClient(connection, JsonSettings);
        var graphQlPayload = await client.FetchLedgerNoteAccountingsSyncAsync(input.LastLocalId);

        return new FetchResult(
            success: true,
            resultFile: PrettyPrintJson(graphQlPayload),
            info: "Momentum GraphQL response fetched.");
    }

    /// <summary>
    /// Convert an existing Momentum GraphQL response byte stream to a Raindance fixed-width byte stream.
    /// </summary>
    /// <param name="input">Existing GraphQL result bytes.</param>
    /// <returns>Raindance file bytes.</returns>
    [DisplayName("Convert GraphQL Result")]
    public static ConversionResult ConvertGraphQlResult([PropertyTab] ConvertInput input)
    {
        var result = DeserializeGraphQlResult(input.GraphQlResult);
        var raindanceText = RaindanceWriter.ToText(result);

        return new ConversionResult(
            success: true,
            nodeCount: NodeCount(result),
            resultFile: raindanceText,
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
        var result = JsonConvert.DeserializeObject<GraphQlResponse<LedgerNoteAccountingsSyncData>>(payload, JsonSettings)
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

    private static JsonSerializerSettings CreateJsonSettings()
    {
        return new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };
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
        return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(payload), Formatting.Indented);
    }
}
