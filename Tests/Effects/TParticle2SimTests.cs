using System;
using Xunit;

/// <summary>
/// Locks <see cref="TParticle2Sim"/> to stock GfxControlTParticle2_t (init <c>10114310</c>,
/// Process <c>10113b55</c>), using record 73001's own numbers.
/// </summary>
public class TParticle2SimTests
{
    static readonly float[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    /// <summary>Record 73001 (Ravaged Mind / Temporal Fury), fields 0..59, both curves six keys.</summary>
    static float[] Record(int flags = 0x203, int mode = 1, int count = 4)
    {
        var f = new float[60];
        f[0] = Bits(flags);
        f[8] = 5f;
        f[9] = Bits(mode);
        f[10] = Bits(74);
        f[11] = Bits(count);
        f[12] = 0.001f;
        f[13] = 0f;
        f[14] = 1f;
        f[15] = Bits(3);
        f[16] = 0f;
        f[17] = -5f; f[18] = 5f;
        f[19] = -5f; f[20] = 5f;
        f[21] = -5f; f[22] = 5f;
        f[23] = 0f; f[24] = 0f;
        f[25] = 0.15f; f[26] = 0.4f;
        f[27] = 4f; f[28] = 8f;
        f[29] = 0.5f;
        f[30] = 1.01f;
        f[31] = 18f;
        f[32] = Bits(0);
        f[33] = 0.5f;
        for (int b = 0; b < 2; b++)
        {
            int o = b == 0 ? 34 : 47;
            f[o] = Bits(6);
            f[o + 1] = 0f; f[o + 2] = Bits(0x00ffffffu);
            f[o + 3] = 0.1f; f[o + 4] = Bits(0xffffffffu);
            f[o + 5] = 0.25f; f[o + 6] = Bits(0xfff6ff52u);
            f[o + 7] = 0.7f; f[o + 8] = Bits(0xee803000u);
            f[o + 9] = 0.75f; f[o + 10] = Bits(0x50505050u);
            f[o + 11] = 1f; f[o + 12] = Bits(0x00303030u);
        }
        return f;
    }

    [Fact]
    public void TheLoader_ReadsBothCurves_AndTheTrailAndDivisor()
    {
        var sim = new TParticle2Sim(Record(), 0f, 0f, () => 0.5f);
        Assert.Equal(6, sim.NearCurve.Count);
        Assert.Equal(6, sim.FarCurve.Count);
        Assert.Equal(1f, sim.Trail, 4);
        Assert.Equal(0.5f, sim.TailDivisor, 4);
        Assert.Equal(5f, sim.Duration, 4);
        Assert.True(sim.Additive);
    }

    [Fact]
    public void TheInit_SpawnsEveryone_ThenMode1KillsThem()
    {
        // Every draw 0.5: size 6, life 0.275, no jitter, velocity 0 (the ranges are symmetric).
        var sim = new TParticle2Sim(Record(mode: 1), 0f, 0f, () => 0.5f);
        sim.Init(1f, 2f, 3f, Identity);
        foreach (TParticle2Sim.Particle p in sim.Particles)
        {
            Assert.Equal(0f, p.Life, 5);
            Assert.True(p.Visible);
            Assert.Equal(6f, p.Size, 4);
            Assert.Equal(1f, p.X, 4);
            Assert.Equal(2f, p.Y, 4);
            Assert.Equal(3f, p.Z, 4);
        }
    }

    [Fact]
    public void TheInit_LeavesABurstAlive()
    {
        var sim = new TParticle2Sim(Record(mode: 0), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        Assert.All(sim.Particles, p => Assert.Equal(0.275f, p.Life, 4));
    }

    [Fact]
    public void TheSpawn_DrawsInStockOrder()
    {
        // 101144b2: size, life, [frame], x, y, z, vx, vy, vz, roll, spin — and without 0x20000 the
        // frame is a plain 0, so it takes no draw at all.
        var draws = new float[] { 0f, 1f, 0.5f, 0f, 0f, 0f, 1f, 0.5f, 0f, 0.25f };
        int i = 0;
        float[] f = Record(mode: 0, count: 1);
        f[13] = 2f;     // a spawn cube so x, y, z show their draws
        f[23] = 0f; f[24] = 90f;   // a spin range so the last draw shows
        var sim = new TParticle2Sim(f, 0f, 0f, () => draws[i++ % draws.Length]);
        sim.Init(10f, 20f, 30f, Identity);

        TParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(4f, p.Size, 4);                 // draw 0 of [4, 8]
        Assert.Equal(0.4f, p.Life, 4);               // draw 1 of [0.15, 0.4]
        Assert.Equal(0.4f, p.LifeTotal, 4);
        Assert.Equal(0f, p.Frame, 4);                // no 0x20000: always 0, no draw
        Assert.Equal(10f, p.X, 4);                   // draw 0.5: (2*0.5 - 1) * 2 + 10
        Assert.Equal(18f, p.Y, 4);                   // draw 0
        Assert.Equal(28f, p.Z, 4);                   // draw 0
        Assert.Equal(-5f, p.VX, 4);                  // draw 0 of [-5, 5]
        Assert.Equal(5f, p.VY, 4);                   // draw 1
        Assert.Equal(0f, p.VZ, 4);                   // draw 0.5
        Assert.Equal(0f, p.Roll, 4);                 // draw 0 of [0, 2pi)
        // draw 0.25 of [0, 90 degrees], kept in radians by the loader.
        Assert.Equal((float)(0.25 * 90.0 * 0.7853981852531433 / 45.0), p.Spin, 4);
        Assert.Equal(0f, p.AY, 4);                   // field 16
    }

    [Fact]
    public void TheTurn_RotatesVelocityAndAcceleration_NotThePosition()
    {
        // A quarter turn about y: x -> -z, z -> x.
        float[] turn = { 0f, 0f, 1f, 0f, 1f, 0f, -1f, 0f, 0f };
        float[] f = Record(mode: 0, count: 1);
        f[16] = -20f;
        var sim = new TParticle2Sim(f, 0f, 0f, () => 1f);
        sim.Init(1f, 2f, 3f, turn);

        TParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(1f, p.X, 4);
        Assert.Equal(5f, p.VX, 4);    // turn * (5, 5, 5) with this matrix leaves x = z
        Assert.Equal(5f, p.VY, 4);
        Assert.Equal(-5f, p.VZ, 4);
        Assert.Equal(0f, p.AX, 4);    // turn * (0, -20, 0)
        Assert.Equal(-20f, p.AY, 4);
    }

    [Fact]
    public void AStep_MovesThenAccelerates_AndGrowsPerCall()
    {
        float[] f = Record(mode: 0, count: 1);
        f[16] = -10f;   // gravity
        var sim = new TParticle2Sim(f, 0f, 0f, () => 1f);
        sim.Init(0f, 0f, 0f, Identity);
        // size 8, life 0.4, velocity (5, 5, 5), acceleration (0, -10, 0).

        Assert.True(sim.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null));
        TParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(0.5f, p.X, 4);                  // p += v dt
        Assert.Equal(0.5f, p.Y, 4);
        Assert.Equal(5f, p.VX, 4);
        Assert.Equal(4f, p.VY, 4);                   // v += a dt, after the move
        Assert.Equal(8f * 1.01f, p.Size, 4);         // a bare per-call multiply
        Assert.Equal(0.3f, p.Life, 4);
    }

    [Fact]
    public void TheGrowth_IsPerCall_NotPerSecond()
    {
        // Two calls of 0.1 s grow twice; one call of 0.2 s grows once. Stock's frame-rate dependence,
        // which is why the control replays on the fixed clock.
        float[] f = Record(mode: 0, count: 1);
        var a = new TParticle2Sim(f, 0f, 0f, () => 1f);
        a.Init(0f, 0f, 0f, Identity);
        a.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null);
        a.Step(0.1f, 0.2f, 5f, 0f, 0f, 0f, Identity, null);

        var b = new TParticle2Sim(f, 0f, 0f, () => 1f);
        b.Init(0f, 0f, 0f, Identity);
        b.Step(0.2f, 0.2f, 5f, 0f, 0f, 0f, Identity, null);

        Assert.Equal(8f * 1.01f * 1.01f, a.Particles[0].Size, 4);
        Assert.Equal(8f * 1.01f, b.Particles[0].Size, 4);
    }

    [Fact]
    public void ABurst_IsReadyOnceTheLastParticleDies()
    {
        var sim = new TParticle2Sim(Record(mode: 0, count: 2), 0f, 0f, () => 0f);
        sim.Init(0f, 0f, 0f, Identity);
        // Every life is the minimum, 0.15.
        Assert.True(sim.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null));
        Assert.Equal(2, sim.LastAlive);
        Assert.False(sim.Step(0.1f, 0.2f, 5f, 0f, 0f, 0f, Identity, null));
        Assert.Equal(0, sim.LastAlive);
        Assert.All(sim.Particles, p => Assert.False(p.Visible));
    }

    [Fact]
    public void TheEmitter_RefillsAtMostFieldFifteen_AndStopsBeforeTheDuration()
    {
        var sim = new TParticle2Sim(Record(mode: 1, count: 8), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        // Mode 1 starts empty; field 12 is 0.001 s, so one step is long enough, field 15 caps it at 3.
        Assert.True(sim.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null));
        Assert.Equal(3, CountVisible(sim));

        // At age >= duration - field 26 (5 - 0.4) nothing more is emitted.
        Assert.True(sim.Step(0.1f, 4.7f, 5f, 0f, 0f, 0f, Identity, null));
        Assert.Equal(3, CountVisible(sim));
    }

    [Fact]
    public void TheEmitter_WaitsOutTheInterval()
    {
        float[] f = Record(mode: 1, count: 4);
        f[12] = 0.5f;
        var sim = new TParticle2Sim(f, 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);

        sim.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(0, CountVisible(sim));
        for (int i = 0; i < 4; i++)
            sim.Step(0.1f, 0.2f + 0.1f * i, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(3, CountVisible(sim));
    }

    [Fact]
    public void TheBounce_FlipsYAndScalesByFieldTwentyNine()
    {
        float[] f = Record(mode: 0, count: 1);
        f[0] = Bits(0x203 | TParticle2Sim.FlagBounce);
        var sim = new TParticle2Sim(f, 0f, 0f, () => 1f);
        sim.Init(0f, 0f, 0f, Identity);
        // Velocity (5, 5, 5) at y = 0, with the ground at y = 10 so it counts as below ground.
        sim.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, (x, y, z) => 10f);

        TParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(2.5f, p.VX, 4);
        Assert.Equal(-2.5f, p.VY, 4);
        Assert.Equal(2.5f, p.VZ, 4);
    }

    [Fact]
    public void TheColours_ComeFromBothCurves_AtTheAgeFraction()
    {
        var sim = new TParticle2Sim(Record(mode: 0, count: 1), 0f, 0f, () => 0f);
        sim.Init(0f, 0f, 0f, Identity);
        // Life 0.15; after 0.075 the fraction is 0.5.
        sim.Step(0.075f, 0.075f, 5f, 0f, 0f, 0f, Identity, null);

        TParticle2Sim.Particle p = sim.Particles[0];
        uint expected = sim.NearCurve.Evaluate(0.5f);
        Assert.Equal(expected, p.NearArgb);
        Assert.Equal(sim.FarCurve.Evaluate(0.5f), p.FarArgb);
    }

    [Fact]
    public void TheFrame_HoldsWithModeZero_AndRandomStartUsesTheMaterialRange()
    {
        // Every shipped record is frame mode 0, so the spawn frame stands for the whole life.
        float[] f = Record(mode: 0, count: 1);
        f[0] = Bits(0x203 | TParticle2Sim.FlagRandomFrame);
        var sim = new TParticle2Sim(f, 4f, 12f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        Assert.Equal(8f, sim.Particles[0].Frame, 4);

        sim.Step(0.05f, 0.05f, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(8f, sim.Particles[0].Frame, 4);
    }

    [Fact]
    public void TheFrame_LoopsHoldsAndPingPongs()
    {
        // Mode 1 wraps to the first frame.
        float[] f = Record(mode: 0, count: 1);
        f[31] = 100f;
        f[32] = Bits(1);
        var loop = new TParticle2Sim(f, 2f, 5f, () => 0f);
        loop.Init(0f, 0f, 0f, Identity);
        loop.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(2f, loop.Particles[0].Frame, 4);

        // Mode 2 parks at the sentinel and stops moving.
        f[32] = Bits(2);
        var stop = new TParticle2Sim(f, 2f, 5f, () => 0f);
        stop.Init(0f, 0f, 0f, Identity);
        stop.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(TParticle2Sim.StoppedFrame, stop.Particles[0].Frame, 4);
        stop.Step(0.01f, 0.11f, 5f, 0f, 0f, 0f, Identity, null);
        Assert.Equal(TParticle2Sim.StoppedFrame, stop.Particles[0].Frame, 4);

        // Mode 3 turns the rate around at the top.
        f[31] = 20f;
        f[32] = Bits(3);
        var pingPong = new TParticle2Sim(f, 0f, 1f, () => 0f);
        pingPong.Init(0f, 0f, 0f, Identity);
        pingPong.Step(0.1f, 0.1f, 5f, 0f, 0f, 0f, Identity, null);
        // 0 + 20 * 0.1 = 2 is past 1, so the rate flips and steps back: 2 + 0.1 * -20 = 0.
        Assert.Equal(0f, pingPong.Particles[0].Frame, 4);
    }

    [Fact]
    public void TheEmitterSpawn_DropsToTheGroundWithTenThousand_AndTheInitDoesNot()
    {
        float[] f = Record(mode: 1, count: 1);
        f[0] = Bits(0x203 | TParticle2Sim.FlagGroundSpawn);
        var sim = new TParticle2Sim(f, 0f, 0f, () => 0.5f);
        sim.Init(0f, 7f, 0f, Identity);
        Assert.Equal(7f, sim.Particles[0].Y, 4);   // the init has no ground step

        sim.Step(0.1f, 0.1f, 5f, 0f, 7f, 0f, Identity, (x, y, z) => -3f);
        Assert.Equal(-3f, sim.Particles[0].Y, 4);
    }

    [Fact]
    public void TheEmitter_SitsOnTheGroundWithFourHundred()
    {
        float[] f = Record(mode: 1, count: 1);
        f[0] = Bits(0x203 | TParticle2Sim.FlagGroundEmitter);
        var sim = new TParticle2Sim(f, 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        sim.Step(0.1f, 0.1f, 5f, 0f, 50f, 0f, Identity, (x, y, z) => 4f);
        Assert.Equal(4f, sim.Particles[0].Y, 4);
    }

    static int CountVisible(TParticle2Sim sim)
    {
        int n = 0;
        foreach (TParticle2Sim.Particle p in sim.Particles)
        {
            if (p.Visible)
                n++;
        }
        return n;
    }
}
