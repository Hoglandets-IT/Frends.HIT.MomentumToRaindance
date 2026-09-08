using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Frends.HIT.MomentumToRaindance;

internal static class RaindanceWriter
{
    private static readonly Encoding SwedishIsoEncoding = Encoding.GetEncoding(
        "iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static byte[] ToBytes(
        GraphQlResponse<LedgerNoteAccountingsSyncData> result,
        CancellationToken cancellationToken = default) =>
        SwedishIsoEncoding.GetBytes(ToText(result, cancellationToken));

    public static string ToText(
        GraphQlResponse<LedgerNoteAccountingsSyncData> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var nodes = result.Data?.LedgerNoteAccountingsSync?.Nodes ?? [];
        var builder = new StringBuilder();

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasInvoiceRows(node))
            {
                continue;
            }

            builder.Append(BuildCustomerRecord(node)).Append("\r\n");
            builder.Append(BuildInvoiceHeaderRecord(node)).Append("\r\n");
            var invoicePeriod = InvoicePeriod(node.LedgerNote?.RefersToPeriodDisplayName);

            foreach (var ledger in node.Ledgers ?? [])
            {
                foreach (var row in ledger.Rows ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsRoundingRow(row))
                    {
                        continue;
                    }

                    var groupedRecords = GroupRevenueRecords(row.Records).ToArray();
                    ValidateInvoiceRow(row, groupedRecords);
                    builder.Append(BuildInvoiceRowRecord(row)).Append("\r\n");

                    if (invoicePeriod is not null)
                    {
                        builder.Append(BuildInvoicePeriodRecord(invoicePeriod)).Append("\r\n");
                    }

                    foreach (var grouped in groupedRecords)
                    {
                        builder.Append(BuildAccountingRecord(node, row, grouped)).Append("\r\n");
                    }
                }
            }
        }

        var text = builder.ToString();
        // Validate even the string result: Frends persists it separately using Latin-1.
        _ = SwedishIsoEncoding.GetByteCount(text);
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    internal static bool HasInvoiceRows(LedgerNoteAccountingNode node) =>
        node.Ledgers?.Any(ledger => ledger.Rows?.Any(row => !IsRoundingRow(row)) == true) == true;

    private static string BuildCustomerRecord(LedgerNoteAccountingNode node)
    {
        var distribution = PrimaryDistribution(node);
        var address = distribution?.PostalAddress;
        var customer = node.LedgerNote?.Customer;
        var customerType = CustomerType(customer?.NodeClass?.DisplayName)
            ?? throw new InvalidOperationException("The invoice customer has an unsupported or missing customer class.");
        var identity = RequiredCustomerIdentity(customer?.IdentityOfficialNumber);
        var record = new FixedWidthRecord(305);

        record.Put(1, 1, "S");
        record.PutText(15, 40, FirstNonEmpty(customer?.DisplayName, FirstAddressLine(address)));
        record.PutText(55, 40, address?.CareOf);
        record.PutText(95, 40, JoinNonEmpty(" ", address?.StreetAddress1, address?.StreetAddress2));
        record.Put(135, 9, address?.PostCode);
        record.PutText(145, 30, address?.City);
        record.Put(175, 16, CustomerVatNumber(customerType, customer?.IdentityOfficialNumber));
        record.Put(195, 12, identity);
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
        record.PutText(65, 30, FirstNonEmpty(distribution?.OrdererReference1, distribution?.OrdererReference2));
        record.Put(145, 12, RequiredCustomerIdentity(customer?.IdentityOfficialNumber));

        return record.ToString();
    }

    private static string BuildInvoiceRowRecord(LedgerRowEntry row)
    {
        var ledgerRow = row.LedgerRow;
        var amount = ledgerRow?.NetAmount;
        var record = new FixedWidthRecord(105);

        record.Put(1, 1, "R");
        // Amount-bearing invoice rows allow 54 text characters; text-only rows allow 60.
        record.PutText(3, 54, FirstNonEmpty(ledgerRow?.Text?.TextDetailed, ledgerRow?.Text?.Text));
        record.Put(63, 15, FormatAmount(amount), Align.Right);
        record.Put(78, 1, amount < 0 ? "-" : null);
        record.Put(79, 3, VatCode(ledgerRow?.VatType?.Id));

        return record.ToString();
    }

    private static string BuildInvoicePeriodRecord(string invoicePeriod)
    {
        var record = new FixedWidthRecord(105);

        record.Put(1, 1, "R");
        record.Put(3, 60, invoicePeriod);

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
        record.PutText(145, 30, FirstNonEmpty(row.LedgerRow?.Text?.TextDetailed, row.LedgerRow?.Text?.Text));
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
            // Momentum's accounting flag describes the journal-side posting, while this
            // Raindance invoice layout expects the transaction polarity: ordinary revenue
            // rows are debit/blank and negative adjustments are credit/'-'.
            if (record.Amount is not decimal magnitude || magnitude < 0m)
            {
                throw new InvalidOperationException("A revenue accounting record must contain a nonnegative amount magnitude.");
            }

            if (record.Debit is not bool debit)
            {
                throw new InvalidOperationException("A revenue accounting record must contain an explicit debit flag.");
            }

            var signed = magnitude * (debit ? -1m : 1m);

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

    private static void ValidateInvoiceRow(LedgerRowEntry row, IReadOnlyList<GroupedAccounting> groupedRecords)
    {
        if (row.LedgerRow?.NetAmount is not decimal netAmount)
        {
            throw new InvalidOperationException("An invoice row must contain ledgerRow.netAmount.");
        }

        var accountingTotal = groupedRecords.Sum(group => SignedMinorUnits(group.SignedAmount));
        if (accountingTotal != SignedMinorUnits(netAmount))
        {
            throw new InvalidOperationException("The invoice row amount does not match its exported revenue accounting total.");
        }
    }

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

    private static string VatCode(string? vatTypeId) =>
        vatTypeId switch
        {
            null => "K00",
            "standard" => "K25",
            _ => throw new InvalidOperationException("An invoice row has an unsupported VAT type.")
        };

    private static string FormatAmount(decimal? amount)
    {
        if (amount is null)
        {
            throw new InvalidOperationException("An amount-bearing record is missing its amount.");
        }

        return Math.Abs(SignedMinorUnits(amount.Value)).ToString("0", CultureInfo.InvariantCulture);
    }

    private static decimal SignedMinorUnits(decimal amount)
    {
        const decimal maximumMinorUnits = 999_999_999_999_999m;
        if (Math.Abs(amount) >= 10_000_000_000_000m)
        {
            throw new InvalidOperationException("The amount exceeds the 15-digit Raindance amount field.");
        }

        var minorUnits = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (Math.Abs(minorUnits) > maximumMinorUnits)
        {
            throw new InvalidOperationException("The rounded amount exceeds the 15-digit Raindance amount field.");
        }

        return minorUnits;
    }

    private static string? Periodization(string? periodDisplayName)
    {
        var periods = ParsePeriods(periodDisplayName);

        return periods.Length switch
        {
            1 => periods[0].ToString("yyMM", CultureInfo.InvariantCulture),
            >= 2 => $"{periods[0].ToString("yyMM", CultureInfo.InvariantCulture)} {periods[^1].ToString("yyMM", CultureInfo.InvariantCulture)}",
            _ => null
        };
    }

    private static string? InvoicePeriod(string? periodDisplayName)
    {
        var periods = ParsePeriods(periodDisplayName);

        return periods.Length switch
        {
            1 => periods[0].ToString("yyyy-MM", CultureInfo.InvariantCulture),
            >= 2 => $"{periods[0].ToString("yyyy-MM", CultureInfo.InvariantCulture)} - {periods[^1].ToString("yyyy-MM", CultureInfo.InvariantCulture)}",
            _ => null
        };
    }

    private static DateTime[] ParsePeriods(string? periodDisplayName)
    {
        if (string.IsNullOrWhiteSpace(periodDisplayName))
        {
            return [];
        }

        var match = Regex.Match(periodDisplayName.Trim(),
            @"\A(?<start>[0-9]{4}-[0-9]{2})(?:\s*-\s*(?<end>[0-9]{4}-[0-9]{2}))?\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success || !DateTime.TryParseExact(match.Groups["start"].Value,
                "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            throw new InvalidOperationException("The invoice period must be YYYY-MM or YYYY-MM - YYYY-MM with valid months.");
        }

        if (!match.Groups["end"].Success)
        {
            return [start];
        }

        if (!DateTime.TryParseExact(match.Groups["end"].Value,
                "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end < start)
        {
            throw new InvalidOperationException("The invoice period must have a valid end month on or after its start month.");
        }

        return [start, end];
    }

    private static string? DigitsOnly(string? value) =>
        value is null ? null : new string(value.Where(char.IsAsciiDigit).ToArray());

    private static string RequiredCustomerIdentity(string? value)
    {
        if (value is not null)
        {
            _ = NormalizeFieldValue(value);
            if (value.Any(character => char.IsDigit(character) && !char.IsAsciiDigit(character)))
            {
                throw new InvalidOperationException("The customer identity must use ASCII digits.");
            }
        }

        var identity = DigitsOnly(value);
        return !string.IsNullOrEmpty(identity)
            ? identity
            : throw new InvalidOperationException("The invoice customer must have an identity containing ASCII digits.");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) is { } value
            ? NormalizeFieldValue(value)
            : null;

    private static string? JoinNonEmpty(string separator, params string?[] values)
    {
        var populated = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeFieldValue(value!))
            .ToArray();

        return populated.Length == 0 ? null : string.Join(separator, populated);
    }

    private static string NormalizeFieldValue(string value)
    {
        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        if (normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("A Raindance field contains an unsupported control character.");
        }

        return normalized.Trim();
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

            var parts = NormalizeFieldValue(coding)
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
                7 => new AccountDimensions(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], null, parts[6]),
                8 => new AccountDimensions(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7]),
                _ => throw new InvalidOperationException("An accounting coding contains more than eight dimensions.")
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

        public void PutText(int start, int length, string? value) =>
            Put(start, length, value, truncate: true);

        public void Put(int start, int length, string? value, Align align = Align.Left, bool truncate = false)
        {
            if (start < 1 || length < 1 || start - 1 > _buffer.Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "The field must fit within the fixed-width record.");
            }

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var normalized = NormalizeFieldValue(value);

            if (normalized.Length > length)
            {
                if (!truncate)
                {
                    throw new InvalidOperationException($"The field at position {start} exceeds its {length}-character width.");
                }

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
