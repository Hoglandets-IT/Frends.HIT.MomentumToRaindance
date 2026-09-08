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
        var original = new ConversionResult(true, 1, "Raindance text", "Converted", lastLocalId);
        var restored = JsonConvert.DeserializeObject<ConversionResult>(JsonConvert.SerializeObject(original));
        Assert.NotNull(restored);
        Assert.True(restored.Success);
        Assert.Equal(original.NodeCount, restored.NodeCount);
        Assert.Equal(original.LastLocalId, restored.LastLocalId);
        Assert.Equal(original.ResultFile, restored.ResultFile);
        Assert.Equal(original.Info, restored.Info);
    }
}
