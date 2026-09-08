using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Frends.HIT.MomentumToRaindance.Tests;

/// <summary>Synthetic test data only. No captured customer data or credentials.</summary>
internal static class Fixture
{
    public static JObject Node(int localId = 1, string text = "Hyra Testlokal", decimal amount = 100m,
        string? period = "2026-07 - 2026-09") =>
        JObject.FromObject(new
        {
            id = $"test-node-{localId}",
            localId,
            changeType = new { id = "created" },
            distributions = new[]
            {
                new
                {
                    ordererReference1 = "Testreferens",
                    postalAddress = new
                    {
                        streetAddress1 = "Testgatan 1",
                        postCode = "123 45",
                        city = "Testort",
                        country = new { shortDisplayName = "SE" }
                    }
                }
            },
            ledgerNote = new
            {
                number = "IGNORED123",
                refersToPeriodDisplayName = period,
                customer = new
                {
                    displayName = "Testkund ÅÄÖ",
                    identityOfficialNumber = "19000101-0000",
                    nodeClass = new { displayName = "Privatperson" }
                }
            },
            ledgers = new[]
            {
                new
                {
                    id = "test-ledger",
                    changeType = new { id = "created" },
                    rows = new[]
                    {
                        new
                        {
                            id = "test-row",
                            changeType = new { id = "created" },
                            ledgerRow = new
                            {
                                netAmount = amount,
                                text = new { text, textDetailed = text },
                                vatType = (object?)null
                            },
                            records = new[]
                            {
                                new
                                {
                                    amount = Math.Abs(amount),
                                    debit = amount < 0,
                                    accountDistributionCoding = "3410 10000 20000 3000 40000 870"
                                }
                            }
                        }
                    }
                }
            }
        });

    public static JObject Ledger(JObject node) => (JObject)node["ledgers"]![0]!;
    public static JObject Row(JObject node) => (JObject)Ledger(node)["rows"]![0]!;
    public static JObject Record(JObject node) => (JObject)Row(node)["records"]![0]!;

    public static string Envelope(params JObject[] nodes) => new JObject
    {
        ["data"] = new JObject
        {
            ["ledgerNoteAccountingsSync"] = new JObject { ["nodes"] = new JArray(nodes) }
        }
    }.ToString(Formatting.None);

    public static ConversionResult Convert(int lastLocalId = 0, params JObject[] nodes) =>
        Main.ConvertGraphQlResult(new ConvertInput
        {
            LastLocalId = lastLocalId,
            GraphQlResult = Envelope(nodes)
        });

    public static string[] Lines(byte[] bytes) => System.Text.Encoding.Latin1.GetString(bytes)
        .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}
