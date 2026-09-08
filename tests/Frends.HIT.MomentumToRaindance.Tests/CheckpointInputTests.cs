using Newtonsoft.Json;
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
    public async Task Fetch_accepts_shared_state_strings_and_integers_before_parameter_initialization(object value, int expected)
    {
        // Reproduce a dynamically typed shared-state result assigned by generated Frends code.
        dynamic sharedStateValue = value;
        var input = new FetchInput { LastLocalId = sharedStateValue };
        var calls = 0;
        string? query = null;
        using var client = new HttpClient(new TransportTests.FakeHandler(async (request, token) =>
        {
            calls++;
            if (calls == 1)
                return TransportTests.JsonResponse(TransportTests.AuthenticationResponse);
            query = await request.Content!.ReadAsStringAsync(token);
            return TransportTests.JsonResponse(TransportTests.EmptyResponse);
        }));

        var result = await Main.FetchCoreAsync(TransportTests.Connection(), input,
            TestContext.Current.CancellationToken, client);

        Assert.Equal(2, calls);
        Assert.Contains($"lastLocalId: {expected}", query);
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
    public async Task Invalid_checkpoint_fails_in_both_tasks_without_network_or_reset(object? value)
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
        Assert.Throws<ArgumentException>(() => Main.ConvertGraphQlResult(new ConvertInput
        {
            LastLocalId = value,
            GraphQlResult = Fixture.Envelope(Fixture.Node())
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Conversion_normalizes_shared_state_checkpoint_for_filtering_and_passthrough()
    {
        var converted = Main.ConvertGraphQlResult(new ConvertInput
        {
            LastLocalId = "5",
            GraphQlResult = Fixture.Envelope(Fixture.Node(5), Fixture.Node(6))
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, converted.NodeCount);
        Assert.Equal(6, converted.LastLocalId);

        var noWork = Main.ConvertGraphQlResult(new ConvertInput
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
        var convert = JsonConvert.DeserializeObject<ConvertInput>(json)!;
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
