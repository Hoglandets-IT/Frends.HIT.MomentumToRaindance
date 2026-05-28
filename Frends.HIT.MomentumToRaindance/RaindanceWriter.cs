using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Frends.HIT.MomentumToRaindance;

internal static class RaindanceWriter
{
    private static readonly Encoding SwedishIsoEncoding = Encoding.Latin1;

    public static byte[] ToBytes(GraphQlResponse<LedgerNoteAccountingsSyncData> result) =>
        SwedishIsoEncoding.GetBytes(ToText(result));

    public static string ToText(GraphQlResponse<LedgerNoteAccountingsSyncData> result)
    {
        var nodes = result.Data?.LedgerNoteAccountingsSync?.Nodes ?? [];
        var builder = new StringBuilder();

        foreach (var node in nodes)
        {
            builder.Append(BuildCustomerRecord(node)).Append("\r\n");
            builder.Append(BuildInvoiceHeaderRecord(node)).Append("\r\n");

            foreach (var ledger in node.Ledgers ?? [])
            {
                foreach (var row in ledger.Rows ?? [])
                {
                    if (IsRoundingRow(row))
                    {
                        continue;
                    }

                    builder.Append(BuildInvoiceRowRecord(row)).Append("\r\n");

                    foreach (var grouped in GroupRevenueRecords(row.Records))
                    {
                        builder.Append(BuildAccountingRecord(node, row, grouped)).Append("\r\n");
                    }
                }
            }
        }

        return builder.ToString();
    }

    private static string BuildCustomerRecord(LedgerNoteAccountingNode node)
    {
        var distribution = PrimaryDistribution(node);
        var address = distribution?.PostalAddress;
        var customer = node.LedgerNote?.Customer;
        var customerType = CustomerType(customer?.NodeClass?.DisplayName);
        var record = new FixedWidthRecord(305);

        record.Put(1, 1, "S");
        record.Put(15, 40, FirstNonEmpty(customer?.DisplayName, FirstAddressLine(address)));
        record.Put(55, 40, address?.CareOf);
        record.Put(95, 40, JoinNonEmpty(" ", address?.StreetAddress1, address?.StreetAddress2));
        record.Put(135, 9, address?.PostCode);
        record.Put(145, 30, address?.City);
        record.Put(175, 16, CustomerVatNumber(customerType, customer?.IdentityOfficialNumber));
        record.Put(195, 12, DigitsOnly(customer?.IdentityOfficialNumber));
        record.Put(210, 2, address?.Country?.ShortDisplayName);
        record.Put(215, 10, FirstMotpart(node));
        record.Put(225, 10, customerType);

        return record.ToString();
    }

    private static string BuildInvoiceHeaderRecord(LedgerNoteAccountingNode node)
    {
        var customer = node.LedgerNote?.Customer;
        var distribution = PrimaryDistribution(node);
        var record = new FixedWidthRecord(359);

        record.Put(1, 1, "H");
        record.Put(65, 30, FirstNonEmpty(distribution?.OrdererReference1, distribution?.OrdererReference2));
        record.Put(145, 12, DigitsOnly(customer?.IdentityOfficialNumber));
        record.Put(200, 10, node.LedgerNote?.Number);

        return record.ToString();
    }

    private static string BuildInvoiceRowRecord(LedgerRowEntry row)
    {
        var ledgerRow = row.LedgerRow;
        var amount = ledgerRow?.NetAmount;
        var record = new FixedWidthRecord(105);

        record.Put(1, 1, "R");
        record.Put(3, 60, FirstNonEmpty(ledgerRow?.Text?.TextDetailed, ledgerRow?.Text?.Text));
        record.Put(63, 15, FormatAmount(amount), Align.Right);
        record.Put(78, 1, amount < 0 ? "-" : null);
        record.Put(79, 3, VatCode(ledgerRow?.VatType?.Id));

        return record.ToString();
    }

    private static string BuildAccountingRecord(
        LedgerNoteAccountingNode node,
        LedgerRowEntry row,
        GroupedAccounting grouped)
    {
        var dimensions = AccountDimensions.Parse(grouped.Coding);
        var amount = grouped.SignedAmount;
        var isCredit = amount < 0;
        var record = new FixedWidthRecord(184);

        record.Put(1, 1, "K");
        record.Put(3, 10, dimensions.Konto);
        record.Put(13, 10, dimensions.Ansvar);
        record.Put(23, 10, dimensions.Verksamhet);
        record.Put(33, 10, dimensions.Aktivitet);
        record.Put(43, 10, dimensions.Objekt);
        record.Put(53, 10, dimensions.Projekt);
        record.Put(63, 10, dimensions.Fri);
        record.Put(73, 10, dimensions.Motpart);
        record.Put(125, 15, FormatAmount(amount), Align.Right);
        record.Put(140, 1, isCredit ? "-" : null);
        record.Put(145, 30, FirstNonEmpty(row.LedgerRow?.Text?.TextDetailed, row.LedgerRow?.Text?.Text));
        record.Put(175, 10, Periodization(node.LedgerNote?.RefersToPeriodDisplayName));

        return record.ToString();
    }

    private static Distribution? PrimaryDistribution(LedgerNoteAccountingNode node) =>
        node.Distributions?.FirstOrDefault();

    private static bool IsRoundingRow(LedgerRowEntry row) =>
        FirstNonEmpty(row.LedgerRow?.Text?.TextDetailed, row.LedgerRow?.Text?.Text)
            ?.Contains("Öresavrundning", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsRevenueAccount(AccountingRecord record)
    {
        var konto = AccountDimensions.Parse(record.AccountDistributionCoding).Konto;
        return !string.IsNullOrWhiteSpace(konto) && konto!.StartsWith("3", StringComparison.Ordinal);
    }

    private static IEnumerable<GroupedAccounting> GroupRevenueRecords(IReadOnlyList<AccountingRecord>? records)
    {
        if (records is null)
        {
            yield break;
        }

        var groups = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var record in records)
        {
            if (!IsRevenueAccount(record))
            {
                continue;
            }

            var key = record.AccountDistributionCoding ?? string.Empty;
            var signed = (record.Amount ?? 0m) * (record.Debit == true ? 1m : -1m);

            if (groups.TryGetValue(key, out var existing))
            {
                groups[key] = existing + signed;
            }
            else
            {
                groups[key] = signed;
                order.Add(key);
            }
        }

        foreach (var key in order)
        {
            var sum = groups[key];
            if (sum == 0m)
            {
                continue;
            }

            yield return new GroupedAccounting(key, sum);
        }
    }

    private sealed record GroupedAccounting(string Coding, decimal SignedAmount);

    private static string? FirstMotpart(LedgerNoteAccountingNode node) =>
        node.Ledgers?
            .SelectMany(ledger => ledger.Rows ?? [])
            .Where(row => !IsRoundingRow(row))
            .SelectMany(row => row.Records ?? [])
            .Where(IsRevenueAccount)
            .Select(record => AccountDimensions.Parse(record.AccountDistributionCoding).Motpart)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? FirstAddressLine(Address? address) =>
        address?.AddressLine?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string? CustomerType(string? nodeClassDisplayName) =>
        nodeClassDisplayName switch
        {
            null => null,
            var value when value.Contains("Privatperson", StringComparison.OrdinalIgnoreCase) => "PRIV",
            var value when value.Contains("Näringsidkare", StringComparison.OrdinalIgnoreCase) => "FTG",
            var value when value.Contains("Föreningar", StringComparison.OrdinalIgnoreCase) => "FTG",
            var value when value.Contains("offentlig", StringComparison.OrdinalIgnoreCase) => "ÖVR",
            _ => null
        };

    private static string? CustomerVatNumber(string? customerType, string? identityOfficialNumber)
    {
        if (customerType != "FTG")
        {
            return null;
        }

        var digits = DigitsOnly(identityOfficialNumber);
        return digits?.Length == 10 ? $"SE{digits}01" : null;
    }

    private static string? VatCode(string? vatTypeId) =>
        vatTypeId switch
        {
            null => "K00",
            "standard" => "K25",
            _ => null
        };

    private static string? FormatAmount(decimal? amount)
    {
        if (amount is null)
        {
            return null;
        }

        var absolute = Math.Abs(amount.Value);
        return decimal.Round(absolute * 100m, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
    }

    private static string? Periodization(string? periodDisplayName)
    {
        if (string.IsNullOrWhiteSpace(periodDisplayName))
        {
            return null;
        }

        var periods = Regex.Matches(periodDisplayName, @"\d{4}-\d{2}")
            .Select(match => match.Value)
            .Select(value => DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("yyMM", CultureInfo.InvariantCulture)
                : null)
            .Where(value => value is not null)
            .ToArray();

        return periods.Length switch
        {
            1 => periods[0],
            >= 2 => $"{periods[0]} {periods[^1]}",
            _ => null
        };
    }

    private static string? DigitsOnly(string? value) =>
        value is null ? null : new string(value.Where(char.IsDigit).ToArray());

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? JoinNonEmpty(string separator, params string?[] values)
    {
        var populated = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return populated.Length == 0 ? null : string.Join(separator, populated);
    }

    private sealed record AccountDimensions(
        string? Konto,
        string? Ansvar,
        string? Verksamhet,
        string? Aktivitet,
        string? Objekt,
        string? Projekt,
        string? Fri,
        string? Motpart)
    {
        public static AccountDimensions Parse(string? coding)
        {
            if (string.IsNullOrWhiteSpace(coding))
            {
                return new AccountDimensions(null, null, null, null, null, null, null, null);
            }

            var parts = coding
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return parts.Length switch
            {
                0 => new AccountDimensions(null, null, null, null, null, null, null, null),
                1 => new AccountDimensions(parts[0], null, null, null, null, null, null, null),
                2 => new AccountDimensions(parts[0], parts[1], null, null, null, null, null, null),
                3 => new AccountDimensions(parts[0], parts[1], null, null, null, null, null, parts[2]),
                4 => new AccountDimensions(parts[0], parts[1], parts[2], null, null, null, null, parts[3]),
                5 => new AccountDimensions(parts[0], parts[1], parts[2], parts[3], null, null, null, parts[4]),
                6 => new AccountDimensions(parts[0], parts[1], parts[2], parts[3], parts[4], null, null, parts[5]),
                _ => new AccountDimensions(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[^1])
            };
        }
    }

    private enum Align
    {
        Left,
        Right
    }

    private sealed class FixedWidthRecord
    {
        private readonly char[] _buffer;

        public FixedWidthRecord(int length)
        {
            _buffer = Enumerable.Repeat(' ', length).ToArray();
        }

        public void Put(int start, int length, string? value, Align align = Align.Left)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            if (normalized.Length > length)
            {
                normalized = normalized[..length];
            }

            var offset = align == Align.Right
                ? start - 1 + length - normalized.Length
                : start - 1;

            for (var index = 0; index < normalized.Length; index++)
            {
                _buffer[offset + index] = normalized[index];
            }
        }

        public override string ToString() => new(_buffer);
    }
}
