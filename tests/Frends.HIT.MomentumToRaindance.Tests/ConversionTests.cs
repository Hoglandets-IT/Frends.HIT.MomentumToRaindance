using Newtonsoft.Json.Linq;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class ConversionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    [InlineData(int.MaxValue)]
    public void Empty_response_preserves_checkpoint(int lastLocalId)
    {
        var result = Fixture.Convert(lastLocalId);
        Assert.True(result.Success);
        Assert.Equal(lastLocalId, result.LastLocalId);
        Assert.Equal(0, result.NodeCount);
        Assert.Empty(result.ResultFile);
    }

    [Fact]
    public void New_nodes_are_sorted_and_highest_id_returned()
    {
        var result = Fixture.Convert(10, Fixture.Node(40, "Last"), Fixture.Node(11, "First"), Fixture.Node(20, "Middle"));
        Assert.True(result.Success);
        Assert.Equal(40, result.LastLocalId);
        Assert.Equal(3, result.NodeCount);
        var descriptions = Fixture.Lines(result.ResultFile)
            .Where(line => line[0] == 'R' && line[78..81] == "K00")
            .Select(line => line[2..56].Trim()).ToArray();
        Assert.Equal(new[] { "First", "Middle", "Last" }, descriptions);
    }

    [Fact]
    public void Reprocessing_after_saved_checkpoint_emits_nothing()
    {
        var payload = Fixture.Envelope(Fixture.Node(3), Fixture.Node(8));
        var first = Main.ConvertGraphQlResult(new ConvertInput { GraphQlResult = payload }, TestContext.Current.CancellationToken);
        var second = Main.ConvertGraphQlResult(new ConvertInput { GraphQlResult = payload, LastLocalId = first.LastLocalId },
            TestContext.Current.CancellationToken);
        Assert.Equal(8, first.LastLocalId);
        Assert.Equal(2, first.NodeCount);
        Assert.Equal(first.LastLocalId, second.LastLocalId);
        Assert.Equal(0, second.NodeCount);
        Assert.Empty(second.ResultFile);
    }

    [Fact]
    public void Stale_nodes_are_filtered_before_invoice_shape_validation()
    {
        var stale = new JObject { ["localId"] = 5 };
        var result = Fixture.Convert(5, stale, Fixture.Node(6));
        Assert.Equal(6, result.LastLocalId);
        Assert.Equal(1, result.NodeCount);
    }

    [Fact]
    public void Only_stale_nodes_preserve_checkpoint()
    {
        var result = Fixture.Convert(50, new JObject { ["localId"] = 10 }, new JObject { ["localId"] = 50 });
        Assert.Equal(50, result.LastLocalId);
        Assert.Empty(result.ResultFile);
    }

    [Fact]
    public void Duplicate_pending_ids_fail_the_whole_batch()
    {
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, Fixture.Node(4), Fixture.Node(4)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_or_nonpositive_node_id_fails(int? localId)
    {
        var node = Fixture.Node();
        node["localId"] = localId is null ? JValue.CreateNull() : new JValue(localId);
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Negative_input_checkpoint_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Fixture.Convert(-1));
    }

    [Fact]
    public void Fractional_node_id_is_rejected_instead_of_rounding_the_checkpoint()
    {
        var node = Fixture.Node();
        node["localId"] = 1.5m;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":{\"ledgerNoteAccountingsSync\":null}}")]
    [InlineData("{\"data\":{\"ledgerNoteAccountingsSync\":{}}}")]
    [InlineData("{\"data\":{\"ledgerNoteAccountingsSync\":{\"nodes\":null}}}")]
    [InlineData("{\"data\":{\"ledgerNoteAccountingsSync\":{\"nodes\":[null]}}}")]
    public void Incomplete_response_is_not_treated_as_successful_empty_response(string payload)
    {
        Assert.Throws<InvalidOperationException>(() => Main.ConvertGraphQlResult(new ConvertInput
        {
            LastLocalId = 42,
            GraphQlResult = payload
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Graphql_errors_reject_partial_data()
    {
        var payload = JObject.Parse(Fixture.Envelope(Fixture.Node(2)));
        payload["errors"] = new JArray(new JObject { ["message"] = "Synthetic upstream failure" });
        Assert.Throws<InvalidOperationException>(() => Main.ConvertGraphQlResult(new ConvertInput
        {
            GraphQlResult = payload.ToString()
        }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("ledgerNote")]
    [InlineData("ledgers")]
    [InlineData("ledgers[0].rows")]
    [InlineData("ledgers[0].rows[0].ledgerRow")]
    public void Missing_required_invoice_shape_fails(string path)
    {
        var node = Fixture.Node();
        node.SelectToken(path)!.Replace(JValue.CreateNull());
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Theory]
    [InlineData("ledgers[0]")]
    [InlineData("ledgers[0].rows[0]")]
    [InlineData("ledgers[0].rows[0].records[0]")]
    public void Null_collection_entries_fail(string path)
    {
        var node = Fixture.Node();
        node.SelectToken(path)!.Replace(JValue.CreateNull());
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Theory]
    [InlineData("changeType")]
    [InlineData("ledgers[0].changeType")]
    [InlineData("ledgers[0].rows[0].changeType")]
    public void Unsupported_change_type_cannot_create_an_accidental_duplicate_invoice(string path)
    {
        var node = Fixture.Node();
        node.SelectToken(path)!["id"] = "updated";
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(0, node));
    }

    [Fact]
    public void Legacy_payload_without_change_types_is_still_supported()
    {
        var node = Fixture.Node();
        node.Remove("changeType");
        Fixture.Ledger(node).Remove("changeType");
        Fixture.Row(node).Remove("changeType");
        Assert.Equal(1, Fixture.Convert(0, node).NodeCount);
    }

    [Fact]
    public void Empty_and_rounding_only_nodes_preserve_checkpoint_when_nothing_is_emitted()
    {
        var emptyLedgers = Fixture.Node(8);
        emptyLedgers["ledgers"] = new JArray();
        var emptyRows = Fixture.Node(9);
        Fixture.Ledger(emptyRows)["rows"] = new JArray();
        var rounding = Fixture.Node(10, "Öresavrundning", 0.05m);
        var result = Fixture.Convert(7, emptyLedgers, emptyRows, rounding);
        Assert.Equal(7, result.LastLocalId);
        Assert.Equal(0, result.NodeCount);
        Assert.Empty(result.ResultFile);
    }

    [Fact]
    public void Mixed_batch_advances_past_validated_noop_nodes_but_counts_only_emitted_invoices()
    {
        var empty = Fixture.Node(12);
        empty["ledgers"] = new JArray();
        var result = Fixture.Convert(7, Fixture.Node(8), empty);
        Assert.Equal(12, result.LastLocalId);
        Assert.Equal(1, result.NodeCount);
        Assert.Single(Fixture.Lines(result.ResultFile), line => line[0] == 'S');
    }

    [Fact]
    public void Invalid_later_node_fails_instead_of_returning_partial_file_and_cursor()
    {
        var invalid = Fixture.Node(3);
        invalid["ledgerNote"] = null;
        Assert.Throws<InvalidOperationException>(() => Fixture.Convert(1, Fixture.Node(2), invalid));
    }

    [Fact]
    public void Cancelled_conversion_returns_no_result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Main.ConvertGraphQlResult(
            new ConvertInput { GraphQlResult = Fixture.Envelope(Fixture.Node()) }, cancellation.Token));
    }
}
