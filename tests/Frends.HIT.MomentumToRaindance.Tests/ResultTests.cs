using System.Globalization;
using Newtonsoft.Json;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class ResultTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    public void Fetch_result_checkpoint_survives_serialization(int lastLocalId)
    {
        var original = new FetchResult(true, "{}", "Fetched", lastLocalId);
        var restored = JsonConvert.DeserializeObject<FetchResult>(JsonConvert.SerializeObject(original));
        Assert.NotNull(restored);
        Assert.True(restored.Success);
        Assert.Equal(original.LastLocalId, restored.LastLocalId);
        Assert.Equal(original.ResultFile, restored.ResultFile);
        Assert.Equal(original.Info, restored.Info);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    public void Conversion_result_checkpoint_survives_serialization(int lastLocalId)
    {
        var original = new ConversionResult(true, 1, "Raindance text", "Converted", lastLocalId)
        {
            Filename = "300KR24_20260908_143025.txt"
        };
        var restored = JsonConvert.DeserializeObject<ConversionResult>(JsonConvert.SerializeObject(original));
        Assert.NotNull(restored);
        Assert.True(restored.Success);
        Assert.Equal(original.NodeCount, restored.NodeCount);
        Assert.Equal(original.Filename, restored.Filename);
        Assert.Equal(original.LastLocalId, restored.LastLocalId);
        Assert.Equal(original.ResultFile, restored.ResultFile);
        Assert.Equal(original.Info, restored.Info);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conversion_returns_a_timestamped_filename_even_when_there_is_no_work(bool hasInvoice)
    {
        var before = DateTime.Now.AddSeconds(-1);
        var result = hasInvoice ? Fixture.Convert(0, Fixture.Node()) : Fixture.Convert(0);
        var after = DateTime.Now;

        Assert.Matches(@"\A300KR24_[0-9]{8}_[0-9]{6}\.txt\z", result.Filename);
        var timestamp = DateTime.ParseExact(result.Filename[8..23], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        Assert.InRange(timestamp, before, after);
        Assert.Equal(hasInvoice ? 1 : 0, result.NodeCount);
        var restored = JsonConvert.DeserializeObject<ConversionResult>(JsonConvert.SerializeObject(result));
        Assert.NotNull(restored);
        Assert.Equal(result.Filename, restored.Filename);
    }
}
