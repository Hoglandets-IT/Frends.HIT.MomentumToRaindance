using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class WriterTests
{
    [Fact]
    public void Invoice_has_exact_record_order_widths_positions_encoding_and_line_endings()
    {
        var result = Fixture.Convert(0, Fixture.Node(amount: 2358m));
        var lines = Fixture.Lines(result.ResultFile);
        Assert.Equal(new[] { 'S', 'H', 'R', 'R', 'K' }, lines.Select(line => line[0]).ToArray());
        Assert.Equal(new[] { 305, 359, 105, 105, 184 }, lines.Select(line => line.Length).ToArray());
        Assert.EndsWith("\r\n", result.ResultFile);
        Assert.DoesNotContain("\n", result.ResultFile.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.Equal("Testkund ÅÄÖ", lines[0][14..54].TrimEnd());
        Assert.Equal("190001010000", lines[0][194..206]);
        Assert.Equal("PRIV", lines[0][224..234].TrimEnd());
        Assert.Equal("190001010000", lines[1][144..156]);
        Assert.Equal(new string(' ', 10), lines[1][199..209]);
        Assert.Equal("Hyra Testlokal", lines[2][2..56].TrimEnd());
        Assert.Equal(new string(' ', 6), lines[2][56..62]);
        Assert.Equal("235800", lines[2][62..77].TrimStart());
        Assert.Equal(' ', lines[2][77]);
        Assert.Equal("K00", lines[2][78..81]);
        Assert.Equal("2026-07 - 2026-09", lines[3][2..62].TrimEnd());
        Assert.Equal(new string(' ', 43), lines[3][62..]);
        Assert.Equal("3410", lines[4][2..12].TrimEnd());
        Assert.Equal("10000", lines[4][12..22].TrimEnd());
        Assert.Equal("20000", lines[4][22..32].TrimEnd());
        Assert.Equal("3000", lines[4][32..42].TrimEnd());
        Assert.Equal("40000", lines[4][42..52].TrimEnd());
        Assert.Equal("870", lines[4][72..82].TrimEnd());
        Assert.Equal("235800", lines[4][124..139].TrimStart());
        Assert.Equal(' ', lines[4][139]);
        Assert.Equal("2607 2609", lines[4][174..184].TrimEnd());

        var bytes = Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback).GetBytes(result.ResultFile);
        Assert.Equal(result.ResultFile.Length, bytes.Length);
        Assert.Contains((byte)0xC5, bytes);
        Assert.Contains((byte)0xC4, bytes);
        Assert.Contains((byte)0xD6, bytes);
    }

    [Fact]
    public void Credit_adjustments_have_matching_negative_markers_in_R_and_K()
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(amount: -3000m)).ResultFile);
        Assert.Equal("300000", lines[2][62..77].TrimStart());
        Assert.Equal('-', lines[2][77]);
        Assert.Equal("300000", lines[4][124..139].TrimStart());
        Assert.Equal('-', lines[4][139]);
    }

    [Fact]
    public void Long_priced_description_is_truncated_to_54_without_using_period_space()
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(text: new string('A', 80))).ResultFile);
        Assert.Equal(new string('A', 54), lines[2][2..56]);
        Assert.Equal(new string(' ', 6), lines[2][56..62]);
        Assert.Equal("2026-07 - 2026-09", lines[3][2..62].TrimEnd());
        Assert.Equal(new string('A', 30), lines[4][144..174]);
    }

    [Theory]
    [InlineData("2026-09", "2026-09", "2609")]
    [InlineData("2026-09 - 2027-08", "2026-09 - 2027-08", "2609 2708")]
    [InlineData(" 2026-07-2026-09 ", "2026-07 - 2026-09", "2607 2609")]
    public void Visible_and_accounting_periods_use_the_same_source_with_their_respective_formats(
        string source, string visible, string accounting)
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(period: source)).ResultFile);
        Assert.Equal(visible, lines[3][2..62].TrimEnd());
        Assert.Equal(accounting, lines[4][174..184].TrimEnd());
        Assert.Equal("Hyra Testlokal", lines[2][2..56].TrimEnd());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Absent_period_emits_no_extra_R_and_leaves_K_period_blank(string? period)
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(period: period)).ResultFile);
        Assert.Equal(new[] { 'S', 'H', 'R', 'K' }, lines.Select(line => line[0]).ToArray());
        Assert.Equal(new string(' ', 10), lines[3][174..184]);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-13 - 2027-01")]
    [InlineData("2026-09 - 2026-07")]
    [InlineData("2026-07 garbage")]
    [InlineData("2026-07 - 2026-09 - 2026-12")]
    public void Malformed_period_is_not_silently_shortened_or_dropped(string period)
    {
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, Fixture.Node(period: period)));
    }

    [Fact]
    public void Each_invoice_row_gets_its_own_period_R_before_its_accounting_records()
    {
        var node = Fixture.Node();
        var extra = Fixture.Row(Fixture.Node(text: "Index", amount: 20m));
        ((JArray)Fixture.Ledger(node)["rows"]!).Add(extra);
        var lines = Fixture.Lines(Fixture.Convert(0, node).ResultFile);
        Assert.Equal(new[] { 'S', 'H', 'R', 'R', 'K', 'R', 'R', 'K' }, lines.Select(line => line[0]).ToArray());
        Assert.Equal("Index", lines[5][2..56].TrimEnd());
        Assert.Equal(lines[3], lines[6]);
    }

    [Fact]
    public void Matching_accounting_codes_are_grouped_with_sign_before_summation()
    {
        var node = Fixture.Node(amount: 100m);
        Fixture.Record(node)["amount"] = 150m;
        var negative = (JObject)Fixture.Record(node).DeepClone();
        negative["amount"] = 50m;
        negative["debit"] = true;
        var receivable = (JObject)negative.DeepClone();
        receivable["accountDistributionCoding"] = "1510 10000 870";
        receivable["amount"] = 100m;
        ((JArray)Fixture.Row(node)["records"]!).Add(negative);
        ((JArray)Fixture.Row(node)["records"]!).Add(receivable);
        var accounting = Assert.Single(Fixture.Lines(Fixture.Convert(0, node).ResultFile), line => line[0] == 'K');
        Assert.Equal("10000", accounting[124..139].TrimStart());
        Assert.Equal(' ', accounting[139]);
    }

    [Fact]
    public void Seven_account_dimensions_do_not_duplicate_motpart_into_free_dimension()
    {
        var node = Fixture.Node();
        Fixture.Record(node)["accountDistributionCoding"] = "3410 10000 20000 3000 40000 5000 870";
        var accounting = Fixture.Lines(Fixture.Convert(0, node).ResultFile).Last();
        Assert.Equal("5000", accounting[52..62].TrimEnd());
        Assert.Equal(new string(' ', 10), accounting[62..72]);
        Assert.Equal("870", accounting[72..82].TrimEnd());
    }

    [Theory]
    [InlineData("3410 10000 20000 3000 40000 5000 6000 870 extra")]
    [InlineData("34101234567 10000 870")]
    public void Unsupported_or_overlong_account_dimensions_fail(string coding)
    {
        var node = Fixture.Node();
        Fixture.Record(node)["accountDistributionCoding"] = coding;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Amount_field_overflow_fails_instead_of_truncating_money()
    {
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, Fixture.Node(amount: 10_000_000_000_000m)));
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, Fixture.Node(amount: 9_999_999_999_999.995m)));
    }

    [Fact]
    public void Maximum_amount_that_fits_is_preserved()
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(amount: 9_999_999_999_999.99m)).ResultFile);
        Assert.Equal("999999999999999", lines[2][62..77]);
        Assert.Equal("999999999999999", lines[4][124..139]);
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("amount")]
    public void Missing_revenue_polarity_or_amount_fails(string property)
    {
        var node = Fixture.Node();
        Fixture.Record(node).Remove(property);
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Negative_accounting_magnitude_is_rejected()
    {
        var node = Fixture.Node();
        Fixture.Record(node)["amount"] = -100m;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Missing_invoice_amount_is_rejected()
    {
        var node = Fixture.Node();
        ((JObject)Fixture.Row(node)["ledgerRow"]!).Remove("netAmount");
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Revenue_amount_must_match_invoice_amount()
    {
        var node = Fixture.Node();
        Fixture.Record(node)["amount"] = 99m;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Missing_revenue_records_cannot_emit_an_unaccounted_nonzero_invoice_row()
    {
        var node = Fixture.Node();
        Fixture.Row(node)["records"] = new JArray();
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Totals_are_checked_after_rounding_each_exported_accounting_group()
    {
        var node = Fixture.Node(amount: 0.01m);
        Fixture.Record(node)["amount"] = 0.005m;
        var second = (JObject)Fixture.Record(node).DeepClone();
        second["accountDistributionCoding"] = "3420 10000 20000 3000 40000 870";
        ((JArray)Fixture.Row(node)["records"]!).Add(second);
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Unknown_vat_type_fails_and_standard_maps_to_K25()
    {
        var node = Fixture.Node();
        Fixture.Row(node)["ledgerRow"]!["vatType"] = new JObject { ["id"] = "standard" };
        Assert.Equal("K25", Fixture.Lines(Fixture.Convert(0, node).ResultFile)[2][78..81]);
        Fixture.Row(node)["ledgerRow"]!["vatType"]!["id"] = "unmapped";
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1234567890123")]
    [InlineData("123\u066456")]
    public void Invalid_or_overlong_customer_identity_is_rejected(string identity)
    {
        var node = Fixture.Node();
        node["ledgerNote"]!["customer"]!["identityOfficialNumber"] = identity;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Unknown_customer_class_is_rejected()
    {
        var node = Fixture.Node();
        node["ledgerNote"]!["customer"]!["nodeClass"]!["displayName"] = "Unmapped class";
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Unsupported_unicode_cannot_be_silently_replaced_by_question_marks()
    {
        Assert.Throws<EncoderFallbackException>(() => Fixture.Convert(0, Fixture.Node(text: "Hyra 🚀")));
    }

    [Fact]
    public void Embedded_line_breaks_and_tabs_become_spaces_without_adding_records()
    {
        var lines = Fixture.Lines(Fixture.Convert(0, Fixture.Node(text: "Hyra\r\nTest\tLokal")).ResultFile);
        Assert.Equal(5, lines.Length);
        Assert.Equal("Hyra  Test Lokal", lines[2][2..56].TrimEnd());
    }

    [Theory]
    [InlineData("Hyra\0Lokal")]
    [InlineData("\u000bHyra")]
    public void Unsupported_control_characters_are_rejected(string text)
    {
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, Fixture.Node(text: text)));
    }
}
