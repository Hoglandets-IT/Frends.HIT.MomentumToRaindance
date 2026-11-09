using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class CheckpointInputTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData("0", 0)]
    [InlineData("123", 123)]
    [InlineData(" 00123 ", 123)]
    [InlineData(123L, 123)]
    [InlineData("2147483647", int.MaxValue)]
    public async Task Diagnostic_fetch_accepts_manual_checkpoint_strings_and_integers(object value, int expected)
    {
        // Reproduce a dynamically typed shared-state result assigned by generated Frends code.
        dynamic sharedStateValue = value;
        var input = new FetchInput { LastLocalId = sharedStateValue };
        var calls = 0;
        JObject? requestBody = null;
        using var client = new HttpClient(new TransportTests.FakeHandler(async (request, token) =>
        {
            calls++;
            if (calls == 1)
                return TransportTests.JsonResponse(TransportTests.AuthenticationResponse);
            requestBody = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            return TransportTests.JsonResponse(TransportTests.EmptyResponse);
        }));

        var result = await Main.FetchCoreAsync(TransportTests.Connection(), input,
            TestContext.Current.CancellationToken, client);

        Assert.Equal(2, calls);
        Assert.NotNull(requestBody);
        Assert.Contains("lastLocalId: $lastLocalId", (string?)requestBody["query"]);
        var checkpoint = requestBody["variables"]!["lastLocalId"]!;
        Assert.Equal(JTokenType.Integer, checkpoint.Type);
        Assert.Equal(expected, (int)checkpoint);
        Assert.Equal(expected, result.LastLocalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("-1")]
    [InlineData(-1)]
    [InlineData("2147483648")]
    [InlineData(2147483648L)]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData(true)]
    [InlineData(1.5)]
    [InlineData(1.0)]
    public async Task Invalid_diagnostic_checkpoint_fails_without_network_or_reset(object? value)
    {
        var calls = 0;
        using var client = new HttpClient(new TransportTests.FakeHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TransportTests.JsonResponse(TransportTests.AuthenticationResponse));
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => Main.FetchCoreAsync(TransportTests.Connection(),
            new FetchInput { LastLocalId = value }, TestContext.Current.CancellationToken, client));
        Assert.Equal(0, calls);
        Assert.Throws<ArgumentException>(() => Main.ConvertCore(new PreviewInput
        {
            LastLocalId = value,
            GraphQlResult = Fixture.Envelope(Fixture.Node())
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Diagnostic_conversion_normalizes_manual_checkpoint()
    {
        var converted = Main.ConvertCore(new PreviewInput
        {
            LastLocalId = "5",
            GraphQlResult = Fixture.Envelope(Fixture.Node(5), Fixture.Node(6))
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, converted.NodeCount);
        Assert.Equal(6, converted.LastLocalId);

        var noWork = Main.ConvertCore(new PreviewInput
        {
            LastLocalId = "6",
            GraphQlResult = Fixture.Envelope(Fixture.Node(5), Fixture.Node(6))
        }, TestContext.Current.CancellationToken);
        Assert.Equal(0, noWork.NodeCount);
        Assert.Equal(6, noWork.LastLocalId);
        Assert.Empty(noWork.ResultFile);
    }

    [Theory]
    [InlineData("{}", 0)]
    [InlineData("{\"LastLocalId\":123}", 123)]
    [InlineData("{\"LastLocalId\":\"123\"}", 123)]
    public void Json_input_and_omitted_defaults_normalize_to_integer(string json, int expected)
    {
        var fetch = JsonConvert.DeserializeObject<FetchInput>(json)!;
        var convert = JsonConvert.DeserializeObject<PreviewInput>(json)!;
        Assert.Equal(expected, Main.ParseCheckpoint(fetch.LastLocalId));
        Assert.Equal(expected, Main.ParseCheckpoint(convert.LastLocalId));
    }

    [Fact]
    public void Explicit_json_null_does_not_use_the_omitted_input_default()
    {
        var input = JsonConvert.DeserializeObject<FetchInput>("{\"LastLocalId\":null}")!;
        Assert.Throws<ArgumentException>(() => Main.ParseCheckpoint(input.LastLocalId));
        Assert.Throws<ArgumentException>(() => Main.ParseCheckpoint(1.5m));
        Assert.Throws<ArgumentException>(() => Main.ParseCheckpoint(1m));
    }
}
