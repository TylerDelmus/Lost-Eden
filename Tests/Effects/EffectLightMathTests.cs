using Xunit;

namespace LostEden.Effects.Tests;

public class EffectLightMathTests
{
    [Theory]
    [InlineData(1f, 1f, 1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0.3f, 0.7f, 0.2f)]
    public void FullyFadedColoursEmitNoLight(float r, float g, float b)
    {
        // Regression: the old "alpha 0 means opaque" fallback made a faded-out effect hold a
        // full-brightness light until its control finally died.
        Assert.Equal(0f, EffectLightMath.NanoIntensity(r, g, b, 0f));
        Assert.Equal(0f, EffectLightMath.StarsIntensity(r, g, b, 0f));
    }

    [Fact]
    public void IntensityBelowTheVisibleThresholdIsReachableByFading()
    {
        // The pool drops a light out of HDRP culling under VisibleCandela, so a fading effect has
        // to be able to get there. With the old fallback this was impossible.
        float faded = EffectLightMath.NanoIntensity(1f, 1f, 1f, 1e-9f);
        Assert.True(faded < EffectLightMath.VisibleCandela);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void IntensityScalesLinearlyWithAlpha(float alpha)
    {
        float full = EffectLightMath.NanoIntensity(1f, 1f, 1f, 1f);
        Assert.Equal(full * alpha, EffectLightMath.NanoIntensity(1f, 1f, 1f, alpha), 2);
    }

    [Fact]
    public void IntensityIsMonotonicInAlpha()
    {
        float previous = -1f;
        for (int i = 0; i <= 10; i++)
        {
            float v = EffectLightMath.StarsIntensity(0.9f, 0.4f, 0.1f, i / 10f);
            Assert.True(v >= previous);
            previous = v;
        }
    }

    [Fact]
    public void AlphaIsClampedSoOverbrightColoursCannotExceedTheBudget()
    {
        Assert.Equal(
            EffectLightMath.NanoIntensity(1f, 1f, 1f, 1f),
            EffectLightMath.NanoIntensity(1f, 1f, 1f, 4f),
            2);
        Assert.Equal(0f, EffectLightMath.NanoIntensity(1f, 1f, 1f, -2f));
    }

    [Fact]
    public void SaturatedSingleChannelNanosKeepTheLuminanceFloor()
    {
        // A pure blue nano has luminance 0.07; the 0.5 floor stops it reading as almost black.
        float blue = EffectLightMath.NanoIntensity(0f, 0f, 1f, 1f);
        Assert.Equal(EffectLightMath.BaseNanoCandela * 0.5f, blue, 1);
    }

    [Theory]
    [InlineData(0f, 24f)]
    [InlineData(1f, 40f)]
    [InlineData(1000f, 120f)]
    public void NanoRangeIsClamped(float scale, float expected)
        => Assert.Equal(expected, EffectLightMath.NanoRange(scale), 3);

    [Theory]
    [InlineData(0f, 40f)]
    [InlineData(2f, 64f)]
    [InlineData(1000f, 140f)]
    public void StarsRangeIsClamped(float radius, float expected)
        => Assert.Equal(expected, EffectLightMath.StarsRange(radius), 3);
}
