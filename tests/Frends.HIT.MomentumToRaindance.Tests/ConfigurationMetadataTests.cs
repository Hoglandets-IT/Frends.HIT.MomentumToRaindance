using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Xunit;

namespace Frends.HIT.MomentumToRaindance.Tests;

public sealed class ConfigurationMetadataTests
{
    [Fact]
    public void HcpVault_is_the_canonical_display_name_with_unchanged_enum_value()
    {
        var field = typeof(MomentumConfigurationSource).GetField(nameof(MomentumConfigurationSource.HcpVault));
        Assert.NotNull(field);
        Assert.Equal("HcpVault", field.GetCustomAttribute<DisplayAttribute>()?.GetName());
        Assert.Equal(1, (int)MomentumConfigurationSource.HcpVault);
    }
}
