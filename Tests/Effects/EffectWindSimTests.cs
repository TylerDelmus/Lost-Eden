using System;
using Xunit;

/// <summary>
/// Locks <see cref="EffectWindSim"/> to the stock wind object (strength <c>100bff68</c>, update
/// <c>100bfc80</c>) and <c>_EffectHandler_t::ComputeWind</c> (<c>100cdee7</c>).
/// </summary>
public class EffectWindSimTests
{
    static Func<int> Seq(params int[] v)
    {
        int i = 0;
        return () => v[i++ % v.Length];
    }

    [Fact]
    public void NoWeather_IsTheBaseDirectionAtTheFloorStrength()
    {
        var sim = new EffectWindSim(Seq(50)); // every gate passes, every kick is zero
        sim.Step(0f);
        Assert.Equal(0.001f, sim.Strength);
        Assert.Equal(0.001f, sim.X, 6);
        Assert.Equal(0f, sim.Y);
        Assert.Equal(0.04f * 0.001f, sim.SmoothX, 7);
    }

    [Fact]
    public void Strength_IsCappedAt33_AndGustWeightFollows()
    {
        var sim = new EffectWindSim(Seq(99)); // both gates fail
        sim.Step(50f);
        Assert.Equal(33f, sim.Strength);
        Assert.Equal(1f, sim.Gust, 6);
        Assert.Equal(33f, sim.X, 4);
        sim.Step(16.5f);
        Assert.Equal(1f - 0.125f, sim.Gust, 5); // 1 - (1 - 0.5)³
    }

    [Fact]
    public void GustB_AddsToTheOutput_ThenDecays()
    {
        // C gate fails (99); B gate passes (0), kick x = 49 / 450 / 0.016, y and z zero.
        var sim = new EffectWindSim(Seq(99, 0, 99, 50, 50));
        sim.Step(33f);
        float kick = (float)(49 / 450.0 / 0.01600000075995922);
        // Strength 33: gust weight 1, m = 1, k = 0.5.
        Assert.Equal(33f + kick * 0.5f, sim.X, 3);
    }

    [Fact]
    public void GustC_TurnsTheBaseWindSlowly()
    {
        // C gate passes (0) with kick x = 0, z = 49...; B gate fails.
        var sim = new EffectWindSim(Seq(0, 50, 99, 99));
        sim.Step(33f);
        Assert.Equal(33f, sim.X, 3);   // this frame's output is W + B, before W turns
        sim.Step(33f);                  // W now leans toward +z, still length 33
        Assert.True(sim.Z > 0f);
        Assert.Equal(33f, (float)Math.Sqrt(sim.X * sim.X + sim.Z * sim.Z), 2);
    }
}
