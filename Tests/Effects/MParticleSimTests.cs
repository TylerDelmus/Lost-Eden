using System;
using Xunit;

/// <summary>
/// Locks <see cref="MParticleSim"/> to stock GfxControlMParticle_t (loader <c>1010f5ee</c>, init
/// <c>10110229</c>, Process <c>1010f92b</c>) with 71045 (71016's debris): 80 asteroid chunks, one burst,
/// gravity -50, bounce 0.5, life 3-4 s, fading over the last half.
/// </summary>
public class MParticleSimTests
{
    static readonly float[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record71045()
    {
        var f = new float[35];
        f[0] = Bits(0x803);
        f[8] = 1f;
        f[11] = Bits(80);
        f[12] = 5f;
        f[13] = 1f;
        f[14] = 0f; f[15] = 0.5f;
        f[16] = -50f;
        f[17] = -80f; f[18] = 80f;
        f[19] = 10f; f[20] = 40f;
        f[21] = -80f; f[22] = 80f;
        f[27] = 150f;
        f[28] = 3f; f[29] = 4f;
        f[30] = 0.006f; f[31] = 0.012f;
        f[32] = 0.5f;
        f[33] = Bits(1);
        f[34] = Bits(9);
        return f;
    }

    static Func<float> Cycle(params float[] values)
    {
        int i = 0;
        return () => values[i++ % values.Length];
    }

    [Fact]
    public void ZeroAxis_IsDrawnOnceAtLoad_AndNormalised()
    {
        var sim = new MParticleSim(Record71045(), Cycle(0.25f, 0.75f));
        Assert.True(sim.Init());
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity, null);
        MParticleSim.Particle p = sim.Particles[0];
        float k = 1f / MathF.Sqrt(3f);
        Assert.Equal(-k, p.AxisX, 5);
        Assert.Equal(k, p.AxisY, 5);
        Assert.Equal(-k, p.AxisZ, 5);
        Assert.Equal(p.AxisX, sim.Particles[79].AxisX);
    }

    [Fact]
    public void FirstCall_SpawnsEveryParticle_InTheBoxes_AtFullAlpha()
    {
        var sim = new MParticleSim(Record71045(), Cycle(0.1f, 0.9f, 0.4f, 0.6f, 0.3f));
        sim.Init();
        Assert.True(sim.Step(0f, 0f, 10f, 2f, -5f, Identity, null));
        Assert.Equal(80, sim.LastAlive);
        foreach (MParticleSim.Particle p in sim.Particles)
        {
            Assert.True(p.Visible);
            Assert.Equal(1f, p.Alpha);
            Assert.InRange(p.X, 9f, 11f);
            Assert.InRange(p.Y, 1f, 3f);
            Assert.InRange(p.VY, 10f, 40f);
            Assert.InRange(p.LifeTotal, 3f, 4f);
            Assert.InRange(p.Scale, 0.006f, 0.012f);
            Assert.InRange(p.Spin, 0f, 150f * MathF.PI / 180f);
            Assert.Equal(-50f, p.AY);
            Assert.Equal(0, p.Model);
        }
    }

    [Fact]
    public void Bounce_ScalesByHundredBDt_AndReversesY()
    {
        float[] f = Record71045();
        f[11] = Bits(1);
        f[13] = 0f;
        f[17] = 2f; f[18] = 2f;
        f[19] = -10f; f[20] = -10f;
        f[21] = 0f; f[22] = 0f;
        var sim = new MParticleSim(f, Cycle(0.25f, 0.75f));
        sim.Init();
        Func<float, float, float, float> ground = (x, y, z) => 0f;

        sim.Step(0f, 0f, 0f, 1f, 0f, Identity, ground);   // spawn at y = 1, above the ground
        sim.Step(0.5f, 0.5f, 0f, 1f, 0f, Identity, ground); // y = -4, v.y = -35
        Assert.Equal(-4f, sim.Particles[0].Y, 4);
        Assert.Equal(-35f, sim.Particles[0].VY, 4);

        sim.Step(0.01f, 0.51f, 0f, 1f, 0f, Identity, ground);
        MParticleSim.Particle p = sim.Particles[0];
        // v.x = 2 - 0.5 * 2 * 0.01 * 100 = 1; v.y = 0.5 * -35 * 0.01 * -100 = 17.5, then gravity.
        Assert.Equal(1f, p.VX, 4);
        Assert.Equal(17.5f - 0.5f, p.VY, 4);
        Assert.Equal(-4f + 17.5f * 0.01f, p.Y, 4);
    }

    [Fact]
    public void NoGround_NeverBounces()
    {
        float[] f = Record71045();
        f[11] = Bits(1);
        var sim = new MParticleSim(f, Cycle(0.25f, 0.75f));
        sim.Init();
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity, (x, y, z) => float.NaN);
        float vx = sim.Particles[0].VX;
        sim.Step(0.1f, 0.1f, 0f, 0f, 0f, Identity, (x, y, z) => float.NaN);
        Assert.Equal(vx, sim.Particles[0].VX);
    }

    [Fact]
    public void Fade_InBelowFadeIn_OutPastOneMinusFadeOut()
    {
        var sim = new MParticleSim(Record71045(), Cycle(0.25f, 0.75f));
        Assert.Equal(1f, sim.FadeAlpha(0.2f));
        Assert.Equal(0.5f, sim.FadeAlpha(0.75f), 5);

        float[] f = Record71045();
        f[14] = 0.2f;
        var withIn = new MParticleSim(f, Cycle(0.25f, 0.75f));
        Assert.Equal(0.5f, withIn.FadeAlpha(0.1f), 5);
    }

    [Fact]
    public void Burst_IsReadyOnceEveryParticleIsDead()
    {
        var sim = new MParticleSim(Record71045(), Cycle(0.25f, 0.75f));
        sim.Init();
        Assert.True(sim.Step(0f, 0f, 0f, 0f, 0f, Identity, null));
        Assert.False(sim.Step(5f, 5f, 0f, 0f, 0f, Identity, null));
        Assert.Equal(0, sim.LastAlive);
    }

    [Fact]
    public void Emitter_KeepsParticleZeroAtMaxLife_AndRespawnsOnePerInterval()
    {
        float[] f = Record71045();
        f[8] = 3f;
        f[9] = Bits(1);
        f[11] = Bits(4);
        f[12] = 0.04f;
        f[28] = 1f; f[29] = 1f;
        var sim = new MParticleSim(f, Cycle(0.25f, 0.75f));
        sim.Init();
        sim.Step(0f, 0f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(1, sim.LastAlive);
        Assert.Equal(1f, sim.Particles[0].Life);

        sim.Step(0.05f, 0.05f, 0f, 0f, 0f, Identity, null); // timer 0.05 >= 0.04: one respawn
        Assert.True(sim.Particles[1].Life > 0f);
        Assert.Equal(0f, sim.Particles[2].Life);
        Assert.Equal(0f, sim.Particles[1].Alpha);          // a respawn shows from the next call

        sim.Step(0.05f, 2.5f, 0f, 0f, 0f, Identity, null); // past duration - field 29: no more
        Assert.Equal(0f, sim.Particles[2].Life);
    }

    [Fact]
    public void ModelPick_IsUniformOverTheList()
    {
        float[] f = Record71045();
        f[33] = Bits(3);
        f = Resize(f, 37);
        f[34] = Bits(5); f[35] = Bits(6); f[36] = Bits(7);
        var sim = new MParticleSim(f, Cycle(0.25f, 0.75f, 0.5f));
        sim.Init();
        Assert.Equal(new[] { 5, 6, 7 }, sim.Models);
        foreach (MParticleSim.Particle p in sim.Particles)
            Assert.InRange(p.Model, 0, 2);
    }

    static float[] Resize(float[] f, int n)
    {
        Array.Resize(ref f, n);
        return f;
    }
}
