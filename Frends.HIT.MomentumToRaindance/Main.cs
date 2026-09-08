using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    /// <param name="input">Fetch options and the last delivered checkpoint.</param>
    /// <param name="cancellationToken">Cancellation from the Frends process.</param>
    /// <returns>Prettified GraphQL JSON and the unchanged input checkpoint.</returns>
    [DisplayName("Fetch Ledger Note Accountings")]
    public static Task<FetchResult> FetchLedgerNoteAccountings(
        [PropertyTab] MomentumConnection connection,
        [PropertyTab] FetchInput input,
        CancellationToken cancellationToken = default) =>
        FetchCoreAsync(connection, input, cancellationToken);

    internal static async Task<FetchResult> FetchCoreAsync(
        MomentumConnection connection,
        FetchInput input,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(input);
        ValidateCheckpoint(input.LastLocalId);
        if (connection.TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(connection.TimeoutSeconds), "Timeout must be between 1 and 3600 seconds.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));
        timeout.Token.ThrowIfCancellationRequested();
        var configuration = await connection.GetMomentumConfigurationAsync(timeout.Token).ConfigureAwait(false);
        var client = new MomentumClient(configuration, JsonSettings, httpClient);
        var graphQlPayload = await client.FetchLedgerNoteAccountingsSyncAsync(input.LastLocalId, timeout.Token).ConfigureAwait(false);
        // HTTP 200 may still contain GraphQL errors or a missing/partial data envelope.
        _ = DeserializeGraphQlResult(graphQlPayload);
        var formatted = PrettyPrintJson(graphQlPayload);
        timeout.Token.ThrowIfCancellationRequested();

        return new FetchResult(
            success: true,
            resultFile: formatted,
            info: "Momentum GraphQL response fetched. Fetching does not advance the delivery checkpoint.",
            lastLocalId: input.LastLocalId);
    }

    /// <summary>
    /// Convert newer Momentum nodes to Raindance fixed-width text. The whole batch must succeed.
    /// </summary>
    /// <param name="input">Existing GraphQL JSON and the last delivered checkpoint.</param>
    /// <param name="cancellationToken">Cancellation from the Frends process.</param>
    /// <returns>Latin-1-compatible CRLF text and a checkpoint to persist only after successful file delivery.</returns>
    [DisplayName("Convert GraphQL Result")]
    public static ConversionResult ConvertGraphQlResult(
        [PropertyTab] ConvertInput input,
        CancellationToken cancellationToken = default) => ConvertCore(input, cancellationToken);

    internal static ConversionResult ConvertCore(ConvertInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateCheckpoint(input.LastLocalId);
        cancellationToken.ThrowIfCancellationRequested();
        var result = DeserializeGraphQlResult(input.GraphQlResult);
        var pending = new List<LedgerNoteAccountingNode>();
        var ids = new HashSet<int>();

        foreach (var node in result.Data!.LedgerNoteAccountingsSync!.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node?.LocalId is not int localId || localId <= 0)
                throw new InvalidOperationException("Every Momentum node must have a positive localId.");
            if (localId <= input.LastLocalId)
                continue;
            if (!ids.Add(localId))
                throw new InvalidOperationException("Momentum returned duplicate pending local IDs.");
            ValidateNode(node);
            pending.Add(node);
        }

        var ordered = pending.OrderBy(node => node.LocalId).ToArray();
        var selected = new GraphQlResponse<LedgerNoteAccountingsSyncData>(
            new LedgerNoteAccountingsSyncData(new LedgerNoteAccountingsSyncConnection(ordered)), null);
        var raindanceText = RaindanceWriter.ToText(selected, cancellationToken);
        var invoiceCount = ordered.Count(RaindanceWriter.HasInvoiceRows);
        var lastLocalId = invoiceCount == 0 ? input.LastLocalId : ordered.Max(node => node.LocalId!.Value);
        cancellationToken.ThrowIfCancellationRequested();

        return new ConversionResult(
            success: true,
            nodeCount: invoiceCount,
            resultFile: raindanceText,
            info: invoiceCount == 0
                ? "No new invoice rows. The input checkpoint is unchanged."
                : "GraphQL response converted to Raindance. Persist LastLocalId only after successful file delivery.",
            lastLocalId: lastLocalId)
        {
            Filename = "300K24_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"
        };
    }

    private static void ValidateCheckpoint(int lastLocalId)
    {
        if (lastLocalId < 0)
            throw new ArgumentOutOfRangeException(nameof(lastLocalId), "LastLocalId must be nonnegative.");
    }

    private static void ValidateNode(LedgerNoteAccountingNode node)
    {
        EnsureCreated(node.ChangeType);
        if (node.LedgerNote is null || node.Ledgers is null)
            throw new InvalidOperationException("A pending Momentum node is missing ledgerNote or ledgers.");
        foreach (var ledger in node.Ledgers)
        {
            if (ledger?.Rows is null)
                throw new InvalidOperationException("A pending Momentum ledger is missing rows.");
            EnsureCreated(ledger.ChangeType);
            foreach (var row in ledger.Rows)
            {
                if (row?.LedgerRow is null)
                    throw new InvalidOperationException("A pending Momentum row is missing ledgerRow.");
                EnsureCreated(row.ChangeType);
                if (row.Records?.Any(record => record is null) == true)
                    throw new InvalidOperationException("A pending Momentum row contains a null accounting record.");
            }
        }
    }

    private static void EnsureCreated(ChangeType? changeType)
    {
        // Legacy saved responses may omit changeType. Never silently re-export updates/deletions.
        if (changeType is not null && !string.Equals(changeType.Id, "created", StringComparison.Ordinal))
            throw new InvalidOperationException("Only created Momentum changes can be exported as new invoices.");
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
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    internal static GraphQlResponse<LedgerNoteAccountingsSyncData> DeserializeGraphQlResult(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("GraphQL response was empty.");
        try
        {
            var result = JsonConvert.DeserializeObject<GraphQlResponse<LedgerNoteAccountingsSyncData>>(payload, JsonSettings)
                ?? throw new InvalidOperationException("GraphQL response was empty.");
            if (result.Errors is { Count: > 0 })
                throw new InvalidOperationException("GraphQL returned errors; the entire batch was rejected.");
            if (result.Data?.LedgerNoteAccountingsSync?.Nodes is null)
                throw new InvalidOperationException("GraphQL response must contain data.ledgerNoteAccountingsSync.nodes, including an explicit empty array when there is no work.");
            return result;
        }
        catch (JsonException)
        {
            // GraphQL/parser errors can contain invoice data or credentials. Do not retain them.
            throw new InvalidOperationException("Momentum response must be valid GraphQL JSON with the expected field types.");
        }
    }

    internal static void EnsureJsonResponse(HttpResponseMessage response, string payload, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{operation} request failed. Status={(int)response.StatusCode}.", null, response.StatusCode);
        }

        if (LooksLikeJson(payload))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} endpoint returned a non-JSON response. Status={(int)response.StatusCode}.");
    }

    private static JsonSerializerSettings CreateJsonSettings()
    {
        return new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal
        };
    }

    private static bool LooksLikeJson(string payload)
    {
        var trimmed = payload.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static string PrettyPrintJson(string payload)
    {
        try
        {
            return JsonConvert.DeserializeObject<JToken>(payload, JsonSettings)!.ToString(Formatting.Indented);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("GraphQL response could not be formatted without loss of numeric precision.");
        }
    }
}
