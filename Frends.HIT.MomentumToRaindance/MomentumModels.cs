using Newtonsoft.Json;

namespace Frends.HIT.MomentumToRaindance;

internal sealed record GraphQlRequest(
    [property: JsonProperty("query")] string Query,
    [property: JsonProperty("variables")] IReadOnlyDictionary<string, object?> Variables);

internal sealed record GraphQlResponse<TData>(
    [property: JsonProperty("data")] TData? Data,
    [property: JsonProperty("errors")] IReadOnlyList<GraphQlError>? Errors);

internal sealed record GraphQlError(
    [property: JsonProperty("message")] string? Message);

internal sealed record LedgerNoteAccountingsSyncData(
    [property: JsonProperty("ledgerNoteAccountingsSync")] LedgerNoteAccountingsSyncConnection? LedgerNoteAccountingsSync);

internal sealed record LedgerNoteAccountingsSyncConnection(
    [property: JsonProperty("nodes")] IReadOnlyList<LedgerNoteAccountingNode> Nodes);

internal sealed record LedgerNoteAccountingNode
{
    public string? Id { get; init; }
    public int? LocalId { get; init; }
    public ChangeType? ChangeType { get; init; }
    public IReadOnlyList<Distribution>? Distributions { get; init; }
    public Company? Company { get; init; }
    public LedgerNote? LedgerNote { get; init; }
    public IReadOnlyList<LedgerEntry>? Ledgers { get; init; }
}

internal sealed record ChangeType
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record Distribution
{
    public BankCode? BankCode { get; init; }
    public string? IdentityOfficialNumber { get; init; }
    public string? GlobalLocationNumber { get; init; }
    public string? OrdererReference1 { get; init; }
    public string? OrdererReference2 { get; init; }
    public Address? Address { get; init; }
    public Address? PostalAddress { get; init; }
    public DisplayValue? Type { get; init; }
}

internal sealed record BankCode
{
    public string? Code { get; init; }

    [JsonProperty("bankCode")]
    public string? Value
    {
        get => Code;
        init => Code = value;
    }

    public string? DisplayName { get; init; }
}

internal sealed record Address
{
    public string? Attention { get; init; }
    public string? CareOf { get; init; }
    public string? StreetAddress1 { get; init; }
    public string? StreetAddress2 { get; init; }
    public Country? Country { get; init; }
    public string? AddressLine { get; init; }

    [JsonProperty("address")]
    public string? AddressValue
    {
        get => AddressLine;
        init => AddressLine = value;
    }

    public string? PostCode { get; init; }
    public string? City { get; init; }
}

internal sealed record Country
{
    public string? DisplayName { get; init; }
    public string? ShortDisplayName { get; init; }
}

internal sealed record DisplayValue
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record Company
{
    public string? DisplayName { get; init; }
    public string? OrganisationNumber { get; init; }
    public string? LedgerNoteHeader { get; init; }
    public string? LedgerNoteMessage { get; init; }
    public Actor? Actor { get; init; }
}

internal sealed record Actor
{
    public string? VatRegistrationNumber { get; init; }
    public string? PostalAddress { get; init; }
    public string? PostalAddressPostCode { get; init; }
    public string? PostalAddressCity { get; init; }
}

internal sealed record LedgerNote
{
    public DisplayValue? Type { get; init; }
    public Invoice? Invoice { get; init; }
    public string? Id { get; init; }
    public string? RefersToPeriodDisplayName { get; init; }
    public string? Number { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? PayToTypeDisplayName { get; init; }
    public string? PayToNumber { get; init; }
    public DateTimeOffset? Due { get; init; }
    public decimal? ToPay { get; init; }
    public Contract? Contract { get; init; }
    public Customer? Customer { get; init; }
}

internal sealed record Invoice
{
    public string? Number { get; init; }
    public CreditInvoice? CreditInvoice { get; init; }
}

internal sealed record CreditInvoice
{
    public string? Number { get; init; }
    public LedgerPrimary? LedgerPrimary { get; init; }
}

internal sealed record LedgerPrimary
{
    public LedgerNotesConnection? LedgerNotes { get; init; }
}

internal sealed record LedgerNotesConnection
{
    public IReadOnlyList<LedgerNoteReference>? LedgerNote { get; init; }
}

internal sealed record LedgerNoteReference
{
    public string? Id { get; init; }
    public string? Number { get; init; }
    public decimal? ToPay { get; init; }
}

internal sealed record Contract
{
    public string? Id { get; init; }
    public string? Number { get; init; }
}

internal sealed record Customer
{
    public DisplayValue? NodeClass { get; init; }
    public string? IdentityOfficialNumber { get; init; }
    public string? Number { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record LedgerEntry
{
    public string? Id { get; init; }
    public ChangeType? ChangeType { get; init; }
    public Ledger? Ledger { get; init; }
    public IReadOnlyList<LedgerRowEntry>? Rows { get; init; }
}

internal sealed record Ledger
{
    public DisplayValue? Type { get; init; }
    public DateTimeOffset? StatusStart { get; init; }
    public DateTimeOffset? StatusEnd { get; init; }
    public LedgerObject? Object { get; init; }
}

internal sealed record LedgerObject
{
    public string? Number { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record LedgerRowEntry
{
    public string? Id { get; init; }
    public ChangeType? ChangeType { get; init; }
    public LedgerRow? LedgerRow { get; init; }
    public IReadOnlyList<AccountingRecord>? Records { get; init; }
}

internal sealed record LedgerRow
{
    public string? Id { get; init; }
    public LedgerText? Text { get; init; }
    public Pricing? Pricing { get; init; }
    public decimal? VatAmount { get; init; }
    public decimal? NetAmount { get; init; }
    public DisplayValue? VatType { get; init; }
}

internal sealed record LedgerText
{
    public string? Text { get; init; }
    public string? TextDetailed { get; init; }
}

internal sealed record Pricing
{
    public IReadOnlyList<PricingMessage>? Messages { get; init; }
    public string? Id { get; init; }
}

internal sealed record PricingMessage
{
    public string? Text { get; init; }
    public DateTimeOffset? StatusStart { get; init; }
    public DateTimeOffset? StatusEnd { get; init; }
}

internal sealed record AccountingRecord
{
    public DateTimeOffset? AccountingDate { get; init; }
    public bool? Debit { get; init; }
    public decimal? Amount { get; init; }
    public string? AccountDistributionCoding { get; init; }
}
