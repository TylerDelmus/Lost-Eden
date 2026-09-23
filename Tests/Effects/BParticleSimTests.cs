using System;
using System.Collections.Generic;
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

    /// <summary>
    /// 71061 (the Orbital Strike plume, and 71032 / 72332 / 72333 bar the frame mode): 30 particles,
    /// motion 0, quad type 2, life policy 3, life rate 4, sizes 2 by 40, clear to white and back.
    /// </summary>
    static float[] Record71061()
    {
        var f = new float[44];
        f[0] = Bits(0x203);
        f[8] = 1.75f;
        f[9] = Bits(59);
        f[10] = Bits(1);
        f[11] = Bits(30);
        f[14] = Bits(2);
        f[15] = Bits(3);
        f[16] = 0.5f;
        f[17] = 4f;
        f[18] = -1f;
        f[19] = -50f; f[20] = 50f;
        f[21] = 5f; f[22] = 10f;
        f[23] = -50f; f[24] = 50f;
        f[25] = Bits(0x00ffffff);
        f[26] = Bits(-1);
        f[27] = Bits(0x00ffffff);
        f[31] = 2f; f[32] = 40f;
        f[33] = 2f; f[34] = 40f;
        f[35] = 2f; f[36] = 40f;
        f[37] = 0.2f;
        f[38] = 1f;
        f[39] = Bits(1);
        f[40] = 0.2f;
        f[41] = 0.2f;
        f[42] = 1f;
        f[43] = 1f;
        return f;
    }

    [Fact]
    public void Mode1_ReadsTheMotionQuadAndLifePolicy()
    {
        var sim = new BParticleSim(Record71061(), 0f, 5f, () => 0.5f);
        Assert.Equal(1, sim.Mode);
        Assert.Equal(0, sim.Motion);
        Assert.Equal(2, sim.QuadType);
        Assert.Equal(3, sim.LifePolicy);
        Assert.True(sim.Supported);
        Assert.Equal(30, sim.Particles.Length);
    }

    [Fact]
    public void Mode1_SpawnsOnTheEmitter_AimedAndLitAtRandom()
    {
        // 10009e46 draws the angle, then the life, then the UV offset.
        float[] one = Record71061();
        one[11] = Bits(1);
        var queue = new Queue<double>(new[] { 0.25, 0.75, 0.5 });
        var sim = new BParticleSim(one, 0f, 5f, () => (float)queue.Dequeue());
        BParticleSim.Particle p = sim.Particles[0];
        Assert.Equal(0f, p.X);
        Assert.Equal(0f, p.Y);
        Assert.Equal(0f, p.Z);
        Assert.Equal(90f, p.Angle, 4);   // 0.25 * 360
        Assert.Equal(0.5f, p.Life, 5);   // 0.75 * 2 - 1
        Assert.Equal(0.5f, p.UvU, 5);
        Assert.Equal(0f, p.UvV);
        Assert.Equal(0f, p.Spin);
        Assert.Equal(1f, p.Scale);
    }

    [Fact]
    public void Mode1_NeverMoves_BecauseMotion0HasNothingToIntegrate()
    {
        var sim = new BParticleSim(Record71061(), 0f, 5f, () => 0.5f);
        for (int n = 0; n < 20; n++)
            sim.Advance(1f / 30f);
        foreach (BParticleSim.Particle p in sim.Particles)
        {
            Assert.Equal(0f, p.X);
            Assert.Equal(0f, p.Y);
            Assert.Equal(0f, p.Z);
        }
    }

    [Fact]
    public void Policy3_RunsTheLifeDown_ThenRestartsFullAndReaimed()
    {
        // 1000914e: below -1 the life goes back to 1 with a new UV offset and angle, in place.
        var sim = new BParticleSim(Record71061(), 0f, 5f, () => 0.5f);
        Assert.Equal(0f, sim.Particles[0].Life, 5); // 0.5 * 2 - 1

        sim.Advance(0.1f);
        Assert.Equal(-0.4f, sim.Particles[0].Life, 5); // 4 per second

        sim.Advance(0.1f);
        Assert.Equal(-0.8f, sim.Particles[0].Life, 5);

        sim.Advance(0.1f); // -1.2 is below -1
        Assert.Equal(1f, sim.Particles[0].Life, 5);
        Assert.Equal(180f, sim.Particles[0].Angle, 4);
        Assert.Equal(0f, sim.Particles[0].X); // the position is left alone, unlike policies 4 and 5
    }

    [Fact]
    public void Policy3_Draw_HidesASpentParticle_AndFadesLinearlyByLife()
    {
        // 1000a8bd: the life is the fade, and the size is untouched.
        var sim = new BParticleSim(Record71061(), 0f, 5f, () => 0.5f);
        sim.Advance(1f / 30f);
        BParticleSim.Particle live = sim.Particles[0];
        live.Life = 0.5f;
        Assert.True(sim.Drawn(live, 1f, out float fade, out float w, out float h));
        Assert.Equal(0.5f, fade, 5);
        Assert.Equal(2f, w, 5);
        Assert.Equal(40f, h, 5);

        live.Life = 0f;
        Assert.False(sim.Drawn(live, 1f, out _, out _, out _));
        live.Life = -0.5f;
        Assert.False(sim.Drawn(live, 1f, out _, out _, out _));
    }

    [Fact]
    public void Policy2_IsASawtoothOverTwo_AndPolicy1AWalkInsideZeroToOne()
    {
        // 100091ed and 10009248, the other two generic life policies.
        float[] two = Record71061();
        two[15] = Bits(2);
        two[17] = 3f;
        var saw = new BParticleSim(two, 0f, 5f, () => 0.5f);
        saw.Particles[0].Life = 1.9f;
        saw.Advance(0.1f); // 2.2 wraps by 2
        Assert.Equal(0.2f, saw.Particles[0].Life, 4);

        float[] walk = Record71061();
        walk[15] = Bits(1);
        var sim = new BParticleSim(walk, 0f, 5f, () => 1f); // r = 1: a full step up
        sim.Particles[0].Life = 0.9f;
        sim.Advance(0.1f); // 0.9 + 1 * 4 * 0.1 = 1.3, past 1, so it reseeds to r
        Assert.Equal(1f, sim.Particles[0].Life, 5);
    }

    [Fact]
    public void TheAngleWrapsTheWayStockDoes_OnlyPastTheEnds()
    {
        float[] rec = Record71061();
        var sim = new BParticleSim(rec, 0f, 5f, () => 0.5f);
        sim.Particles[0].Angle = 360f; // 10008c33 subtracts only when 360 < angle
        sim.Particles[0].Spin = 0f;
        sim.Advance(1f / 30f);
        Assert.Equal(360f, sim.Particles[0].Angle, 4);

        sim.Particles[0].Angle = 359f;
        sim.Particles[0].Spin = 60f;
        sim.Advance(1f / 30f); // 361 wraps
        Assert.Equal(1f, sim.Particles[0].Angle, 4);
    }

    [Fact]
    public void Mode8_StillUsesTheSymmetricQuadAndNoLifePolicy()
    {
        var sim = new BParticleSim(Record71352(), 0f, 0f, () => 0.5f);
        Assert.Equal(0, sim.QuadType);
        Assert.Equal(0, sim.LifePolicy);
        Assert.Equal(0, sim.Motion);
    }
}
