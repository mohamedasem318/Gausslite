using Gausslite.Core.Blur;

namespace Gausslite.Core.Tests.Blur;

public sealed class BlurIntensityPresetTests
{
    [Theory]
    [InlineData(BlurIntensityPreset.Light,  10.0f)]
    [InlineData(BlurIntensityPreset.Medium, 20.0f)]
    [InlineData(BlurIntensityPreset.Heavy,  35.0f)]
    public void ToRadius_ReturnsCorrectRadius(BlurIntensityPreset preset, float expected)
    {
        Assert.Equal(expected, BlurIntensityPresets.ToRadius(preset));
    }

    [Fact]
    public void ToRadius_UnknownPreset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlurIntensityPresets.ToRadius((BlurIntensityPreset)99));
    }

    [Fact]
    public void MediumRadius_MatchesBlurPipelineDefault()
    {
        Assert.Equal(BlurPipeline.DefaultBlurRadius, BlurIntensityPresets.MediumRadius);
    }
}
