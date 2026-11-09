using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class TransportPaginationTests
{
    [Fact]
    public async Task Fetch_drains_all_pages_with_one_authentication_and_fixed_lower_bound_preserving_node_values()
    {
        var firstNode = new JObject { ["localId"] = 101, ["opaqueDate"] = "2026-11-04T10:40:00.123+01:00", ["amount"] = 123456789012.345678m };
        var secondNode = new JObject { ["localId"] = 102, ["optional"] = JValue.CreateNull(), ["description"] = "ÅÄÖ €" };
        var thirdNode = new JObject { ["localId"] = 103, ["nested"] = new JObject { ["value"] = 0.123456789012345678m } };
        var replies = new Queue<string>(new[] {
            TransportTests.AuthenticationResponse,
            TransportTests.PageResponse(3, true, "opaque/A+==", firstNode),
            TransportTests.PageResponse(3, true, "next/B+==", secondNode),
            TransportTests.PageResponse(3, false, "final/C+==", thirdNode)
        });
        var requests = new List<JObject>();
        var authorizations = new List<string?>();
        using var http = new HttpClient(new TransportTests.FakeHandler(async (request, token) => {
            requests.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(token)));
            authorizations.Add(request.Headers.Authorization?.ToString());
            return TransportTests.JsonResponse(replies.Dequeue());
        }));

        var json = await TransportTests.Client(http).FetchLedgerNoteAccountingsSyncAsync(100, TestContext.Current.CancellationToken);
        var response = JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })!;
        var connection = response["data"]!["ledgerNoteAccountingsSync"]!;
        Assert.Equal(4, requests.Count);
        Assert.Null(authorizations[0]);
        Assert.All(authorizations.Skip(1), value => Assert.Equal("Bearer test-access-token", value));
        Assert.All(requests.Skip(1), request => Assert.Equal(100, (int)request["variables"]!["lastLocalId"]!));
        Assert.Equal(JTokenType.Null, requests[1]["variables"]!["after"]!.Type);
        Assert.Equal("opaque/A+==", (string?)requests[2]["variables"]!["after"]);
        Assert.Equal("next/B+==", (string?)requests[3]["variables"]!["after"]);
        Assert.Equal(3, (int)connection["totalCount"]!);
        Assert.False((bool)connection["pageInfo"]!["hasNextPage"]!);
        Assert.Equal("final/C+==", (string?)connection["pageInfo"]!["endCursor"]);
        Assert.True(JToken.DeepEquals(new JArray(firstNode, secondNode, thirdNode), connection["nodes"]));
        Assert.Contains("123456789012.345678", json);
        Assert.Contains("2026-11-04T10:40:00.123+01:00", json);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeated_cursor_is_rejected_including_a_terminal_page(bool secondHasNext)
    {
        var pages = new[] {
            TransportTests.PageResponse(3, true, "repeated-sensitive-marker", Node(1)),
            TransportTests.PageResponse(3, secondHasNext, "repeated-sensitive-marker", Node(2), Node(3))
        };
        await AssertRejected(pages, "repeated-sensitive-marker");
    }

    [Fact]
    public async Task Total_count_change_between_pages_rejects_the_complete_batch()
    {
        await AssertRejected(new[] {
            TransportTests.PageResponse(2, true, "first", Node(1)),
            TransportTests.PageResponse(3, false, "last", Node(2), Node(3))
        });
    }

    [Fact]
    public async Task Partial_graphql_error_on_second_page_cannot_return_first_page_data()
    {
        var second = JObject.Parse(TransportTests.PageResponse(2, false, "last", Node(2)));
        second["errors"] = new JArray(new JObject { ["message"] = "sensitive-marker" });
        await AssertRejected(new[] { TransportTests.PageResponse(2, true, "first", Node(1)), second.ToString() }, "sensitive-marker");
    }

    [Fact]
    public async Task Http_error_on_second_page_rejects_the_batch_without_returning_payload()
    {
        var calls = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler((_, _) => Task.FromResult(++calls switch {
            1 => TransportTests.JsonResponse(TransportTests.AuthenticationResponse),
            2 => TransportTests.JsonResponse(TransportTests.PageResponse(2, true, "first", Node(1))),
            _ => TransportTests.JsonResponse("{\"message\":\"sensitive-marker\"}", HttpStatusCode.ServiceUnavailable)
        })));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => TransportTests.Client(http)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
        Assert.DoesNotContain("sensitive-marker", error.ToString());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public async Task Duplicate_or_decreasing_ids_between_pages_are_rejected(int first, int second)
    {
        await AssertRejected(new[] {
            TransportTests.PageResponse(2, true, "first", Node(first)),
            TransportTests.PageResponse(2, false, "last", Node(second))
        });
    }

    [Fact]
    public async Task Decreasing_ids_within_one_page_are_rejected()
    {
        await AssertRejected(new[] { TransportTests.PageResponse(2, false, "last", Node(2), Node(1)) });
    }

    [Fact]
    public async Task Returned_id_at_or_below_lower_bound_is_rejected()
    {
        var calls = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler((_, _) => Task.FromResult(TransportTests.JsonResponse(
            ++calls == 1 ? TransportTests.AuthenticationResponse : TransportTests.PageResponse(1, false, "last", Node(100))))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => TransportTests.Client(http)
            .FetchLedgerNoteAccountingsSyncAsync(100, TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("2147483648")]
    [InlineData("9999999999999999999999999999999999999")]
    public async Task Local_ids_require_positive_int32_json_integers(string localId)
    {
        var page = "{\"data\":{\"ledgerNoteAccountingsSync\":{\"totalCount\":1,\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[{\"localId\":" + localId + "}]}}}";
        await AssertRejected(new[] { page });
    }

    [Theory]
    [InlineData("totalCount")]
    [InlineData("pageInfo")]
    [InlineData("hasNextPage")]
    [InlineData("endCursor")]
    public async Task Page_metadata_is_required_not_inferred(string missing)
    {
        var page = JObject.Parse(TransportTests.PageResponse(0, false, null));
        var connection = (JObject)page["data"]!["ledgerNoteAccountingsSync"]!;
        if (missing is "totalCount" or "pageInfo") connection.Remove(missing);
        else ((JObject)connection["pageInfo"]!).Remove(missing);
        await AssertRejected(new[] { page.ToString() });
    }

    [Theory]
    [InlineData("totalCount", "\"0\"")]
    [InlineData("totalCount", "0.0")]
    [InlineData("totalCount", "-1")]
    [InlineData("totalCount", "null")]
    [InlineData("totalCount", "2147483648")]
    [InlineData("hasNextPage", "\"false\"")]
    [InlineData("hasNextPage", "0")]
    [InlineData("hasNextPage", "null")]
    [InlineData("endCursor", "123")]
    public async Task Page_metadata_requires_strict_json_types(string field, string value)
    {
        var page = JObject.Parse(TransportTests.PageResponse(0, false, null));
        var connection = (JObject)page["data"]!["ledgerNoteAccountingsSync"]!;
        var target = field == "totalCount" ? connection : (JObject)connection["pageInfo"]!;
        target[field] = JToken.Parse(value);
        await AssertRejected(new[] { page.ToString() });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Continuing_page_requires_usable_cursor(string? cursor)
    {
        await AssertRejected(new[] { TransportTests.PageResponse(2, true, cursor, Node(1)) });
    }

    [Fact]
    public async Task Empty_continuing_page_is_rejected_instead_of_looping()
    {
        await AssertRejected(new[] { TransportTests.PageResponse(1, true, "next") });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task Total_count_and_page_completion_must_match_returned_nodes(int totalCount, bool hasNextPage)
    {
        await AssertRejected(new[] { TransportTests.PageResponse(totalCount, hasNextPage, "next", Node(1)) });
    }

    [Theory]
    [InlineData("{\"data\":\"sensitive-marker\"")]
    [InlineData("{\"data\":null,\"data\":\"sensitive-marker\"}")]
    [InlineData("{\"sensitive-marker\":1.234567890123456789e100}")]
    [InlineData("{} {\"sensitive-marker\":true}")]
    public async Task Malformed_pages_are_rejected_without_parser_details(string payload)
    {
        await AssertRejected(new[] { payload }, "sensitive-marker");
    }

    [Fact]
    public async Task Caller_cancellation_during_second_page_cancels_the_whole_fetch()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler((_, token) => {
            calls++;
            if (calls == 3) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(TransportTests.JsonResponse(calls == 1 ? TransportTests.AuthenticationResponse : TransportTests.PageResponse(2, true, "next", Node(1))));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TransportTests.Client(http).FetchLedgerNoteAccountingsSyncAsync(0, cancellation.Token));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Configured_timeout_is_shared_across_authentication_and_all_pages()
    {
        var connection = TransportTests.Connection();
        connection.TimeoutSeconds = 1;
        var calls = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler(async (_, token) => {
            calls++;
            if (calls == 1) return TransportTests.JsonResponse(TransportTests.AuthenticationResponse);
            await Task.Delay(600, token);
            return TransportTests.JsonResponse(calls == 2
                ? TransportTests.PageResponse(2, true, "first", Node(1))
                : TransportTests.PageResponse(2, false, "last", Node(2)));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Main.FetchCoreAsync(connection, new FetchInput(), TestContext.Current.CancellationToken, http));
        Assert.Equal(3, calls);
    }

    private static JObject Node(int localId) => new() { ["localId"] = localId };

    private static async Task AssertRejected(string[] pages, string? sensitive = null)
    {
        var calls = 0;
        using var http = new HttpClient(new TransportTests.FakeHandler((_, _) => {
            calls++;
            Assert.InRange(calls, 1, pages.Length + 1);
            return Task.FromResult(TransportTests.JsonResponse(calls == 1 ? TransportTests.AuthenticationResponse : pages[calls - 2]));
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TransportTests.Client(http)
            .FetchLedgerNoteAccountingsSyncAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(pages.Length + 1, calls);
        if (sensitive is not null) Assert.DoesNotContain(sensitive, error.ToString());
        Assert.Null(error.InnerException);
    }
}
