using System;
using Xunit;

/// <summary>
/// Locks <see cref="BParticleSim"/> (mode 8) to stock GfxControlBParticle_t (Process <c>1010a909</c>) and
/// GfxVisualBParticle (ctor <c>1000a39b</c>, ProcessParticles <c>100093d9</c>, draw <c>1000a919</c>), with
/// 71352: one particle, delay rate 0.8, life rate 0.5, size 175, white.
/// </summary>
public class BParticleSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record71352()
    {
        var f = new float[44];
        f[0] = Bits(0x603);
        f[8] = -1f;
        f[9] = Bits(8);
        f[10] = Bits(8);
        f[11] = Bits(1);
        f[16] = 0.8f;
        f[17] = 0.5f;
        f[25] = Bits(-1);
        f[26] = Bits(-1);
        f[27] = Bits(0x00ffffff);
        for (int k = 31; k <= 36; k++)
            f[k] = 175f;
        f[37] = 0.2f;
        f[38] = 1f;
        f[39] = Bits(1);
        f[40] = -1f;
        f[41] = -1f;
        f[42] = 1f;
        f[43] = 1f;
        return f;
    }

    [Fact]
    public void Mode8_LeavesTheSentinel_ThenPopsOnceTheDelayRunsOut()
    {
        var sim = new BParticleSim(Record71352(), 0f, 0f, () => 0.5f);
        Assert.Equal(BParticleSim.Sentinel, sim.Particles[0].Life);
        Assert.Equal(0f, sim.Particles[0].Timer);

        sim.Advance(0.02f); // life leaves 999
        Assert.Equal(BParticleSim.Sentinel - 0.5f * 0.02f, sim.Particles[0].Life, 3);

        sim.Advance(0.02f); // the timer goes below 0: pop
        Assert.Equal(0.75f, sim.Particles[0].Life, 5);
        Assert.Equal(BParticleSim.Sentinel, sim.Particles[0].Timer);

        sim.Advance(0.1f);
        Assert.Equal(0.75f - 0.05f, sim.Particles[0].Life, 5);
    }

    [Fact]
    public void Draw_ScalesSizeAndFadeBySinOfLife()
    {
        var sim = new BParticleSim(Record71352(), 0f, 0f, () => 0.5f);
        sim.Advance(0.02f);
        sim.Advance(0.02f);
        Assert.True(sim.Drawn(sim.Particles[0], 1f, out float fade, out float w, out float h));
        float s = (float)Math.Sin(0.75 * Math.PI / 2);
        Assert.Equal(s, fade, 5);
        Assert.Equal(175f * s, w, 3);
        Assert.Equal(175f * s, h, 3);
    }

    [Fact]
    public void NoDuration_KeepsTheMiddleKey()
    {
        var sim = new BParticleSim(Record71352(), 0f, 0f, () => 0.5f);
        Assert.True(sim.Advance(5f));
        Assert.Equal(0xffffffffu, sim.Argb);
        Assert.Equal(175f, sim.Width);
    }

    [Fact]
    public void Keys_BlendStartToMiddle_ThenMiddleToEnd()
    {
        float[] rec = Record71352();
        rec[8] = 2f;
        rec[25] = Bits(0);
        rec[31] = 10f; rec[33] = 20f; rec[35] = 30f;
        rec[40] = 0.25f;
        rec[41] = 0.75f; // stored as 1 - 0.75: the last segment starts at 0.75 and spans 0.25
        var sim = new BParticleSim(rec, 0f, 0f, () => 0.5f);
        sim.Advance(0.25f); // u = 0.125: halfway to the first key
        Assert.Equal(15f, sim.Width, 4);
        sim.Advance(1.5f); // u = 0.875: halfway through the last segment
        Assert.Equal(25f, sim.Width, 4);
        Assert.False(sim.Advance(0.5f)); // u > 1
    }

    [Fact]
    public void DistanceFade_PeaksAtTheMiddleDistance()
    {
        float[] rec = Record71352();
        rec[28] = 10f; rec[29] = 20f; rec[30] = 40f;
        var sim = new BParticleSim(rec, 0f, 0f, () => 0.5f);
        Assert.False(sim.DistanceFade(5f, out _));
        Assert.True(sim.DistanceFade(15f, out float near));
        Assert.Equal(0.5f, near, 5);
        Assert.True(sim.DistanceFade(30f, out float far));
        Assert.Equal(0.5f, far, 5);
    }
}
