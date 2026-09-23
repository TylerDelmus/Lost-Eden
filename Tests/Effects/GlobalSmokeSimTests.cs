using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Locks <see cref="GlobalSmokeSim"/> to stock _GfxControlGlobalSmoke_t (loader <c>100e060f</c>,
/// Process <c>100dfc38</c>), with the two records a nano reaches: 12279 (kind 0) and 12252 (kind 2).
/// </summary>
public class GlobalSmokeSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static Func<double> Fixed(params double[] values)
    {
        var q = new Queue<double>(values);
        return () =>
        {
            double v = q.Dequeue();
            q.Enqueue(v);
            return v;
        };
    }

    /// <summary>12279 — kind 0, the fountain 305431 Kneel Before Immortality uses.</summary>
    static float[] Record12279()
    {
        var f = new float[32];
        f[0] = Bits(5);
        f[7] = 1000f;
        f[8] = 2f;
        f[9] = Bits(49);
        f[10] = Bits(0);
        f[11] = 0.4f; f[12] = 0.15f;
        f[13] = 3.2f;
        f[14] = 2f; f[15] = 10f;
        f[16] = 2f; f[17] = 10f;
        f[18] = 1f; f[19] = 0.3f; f[20] = 0.6f; f[21] = 1f;
        f[22] = 0f; f[23] = 0.3f; f[24] = 0.3f; f[25] = 1f;
        f[26] = 1.7f;
        f[28] = 1f; f[29] = 1f;
        f[30] = Bits(10); f[31] = Bits(3);
        return f;
    }

    /// <summary>12252 — kind 2, the drift 304336 Chilling Air reaches through 12250.</summary>
    static float[] Record12252()
    {
        var f = new float[32];
        f[0] = Bits(5);
        f[2] = -0.2f; f[3] = 0.1f;
        f[7] = 2002f;
        f[8] = 3f;
        f[9] = Bits(49);
        f[10] = Bits(2);
        f[11] = 0.4f; f[12] = 0.15f;
        f[13] = 0.6f;
        f[14] = 0.3f; f[15] = 1f;
        f[16] = 0.3f; f[17] = 1f;
        f[18] = 0.1f; f[19] = 1f; f[20] = 1f; f[21] = 1f;
        f[22] = 0.05f; f[23] = 1f; f[24] = 1f; f[25] = 1f;
        f[26] = 1.4f;
        f[28] = 1f; f[29] = 0.5f;
        f[30] = Bits(1); f[31] = Bits(0);
        return f;
    }

    [Fact]
    public void ReadsTheRecordTheWayTheLoaderDoes()
    {
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.5));
        Assert.Equal(49, sim.Material);
        Assert.Equal(0, sim.Kind);
        Assert.Equal(3.2f, sim.Speed, 4);
        Assert.Equal(2f, sim.WidthFrom, 4);
        Assert.Equal(10f, sim.WidthTo, 4);
        Assert.Equal(1.7f, sim.ParticleLife, 4);
        Assert.Equal(10, sim.BudgetBase);
        Assert.Equal(3, sim.BudgetMask);
        Assert.True(sim.Supported);
        Assert.Equal(64, GlobalSmokeSim.Capacity);

        Assert.Equal(2, new GlobalSmokeSim(Record12252(), Fixed(0.5)).Kind);
        // Nothing else is drawn: no nano reaches the other five kinds.
        Assert.True(GlobalSmokeSim.IsPortedKind(0));
        Assert.True(GlobalSmokeSim.IsPortedKind(2));
        Assert.False(GlobalSmokeSim.IsPortedKind(1));
        Assert.False(GlobalSmokeSim.IsPortedKind(6));
    }

    [Fact]
    public void EveryOneOfTheSixtyFourSlotsStartsLongDead()
    {
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.5));
        foreach (var p in sim.Particles)
        {
            Assert.False(p.Alive);
            Assert.Equal(GlobalSmokeSim.NeverBorn, p.Birth, 3);
        }
    }

    [Fact]
    public void TheEmitterStopsEarlyEnoughForTheLastParticleToFinish()
    {
        // 100dfd15: 12279 runs 2 s and its particles live 1.7, so spawning stops at 0.3.
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.5));
        float cut = sim.Duration - sim.ParticleLife;     // 0.3, give or take a float's worth
        Assert.True(sim.Emitting(cut));
        Assert.True(sim.Emitting(cut - 0.01f));
        Assert.False(sim.Emitting(cut + 0.01f));

        // A negative duration never stops — 12501 and the rest of the ambient smokes.
        float[] forever = Record12279();
        forever[8] = -1f;
        var endless = new GlobalSmokeSim(forever, Fixed(0.5));
        Assert.True(endless.Emitting(1000f));
    }

    [Fact]
    public void Kind0LaunchesUpwardsFromUnderTheEmitter()
    {
        // The random stream feeds the rejection sampler: (0.9, 0.5, 0.1) maps to (0.8, 0, -0.8),
        // which is inside the unit sphere, then the trailing draw is the spawn's dip.
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.9, 0.5, 0.1, 0.25));
        sim.Process(0.1f, 1f / 30f, 5f, 10f, -3f);

        ref GlobalSmokeSim.Particle p = ref sim.Particles[0];
        Assert.True(p.Alive);
        Assert.Equal(0.1f, p.Birth, 4);
        Assert.Equal(5f, p.X, 4);
        Assert.Equal(-3f, p.Z, 4);
        // 100e02a8: the spawn sits up to a metre below the emitter.
        Assert.True(p.Y < 10f);
        Assert.True(p.Y >= 9f);
        // 100e025f: a downward draw is flipped, so the fountain never fires into the ground.
        Assert.True(p.VelY >= 0f);
        // The speed is field 13 because the direction is a unit vector.
        float speed = (float)Math.Sqrt(p.VelX * p.VelX + p.VelY * p.VelY + p.VelZ * p.VelZ);
        Assert.Equal(3.2f, speed, 3);
        // Birth size is field 14 / field 16, not the ramp's end.
        Assert.Equal(2f, p.Width, 4);
        Assert.Equal(2f, p.Height, 4);
    }

    [Fact]
    public void Kind2DriftsOutAlongXAndSags()
    {
        var sim = new GlobalSmokeSim(Record12252(), Fixed(0.0));
        sim.Process(0f, 1f / 30f, 0f, 0f, 0f);

        ref GlobalSmokeSim.Particle p = ref sim.Particles[0];
        Assert.True(p.Alive);
        // 100e0391: (rand/655350 + 1) * field13, with rand at 0 that is exactly field 13.
        Assert.Equal(0.6f, p.VelX, 4);
        Assert.Equal(0.6f * -0.4f, p.VelY, 4);
        Assert.Equal(0f, p.VelZ, 4);
        // Field 31 is 0, so the budget is exactly field 30 — one particle a call.
        Assert.Equal(1, sim.Budget(0f));
        int alive = 0;
        foreach (var q in sim.Particles)
            if (q.Alive) alive++;
        Assert.Equal(1, alive);
    }

    [Fact]
    public void Kind2IsPulledDownButKind0IsNot()
    {
        var drift = new GlobalSmokeSim(Record12252(), Fixed(0.0));
        drift.Process(0f, 0.1f, 0f, 0f, 0f);
        float launched = drift.Particles[0].VelY;
        drift.Process(0.1f, 0.1f, 0f, 0f, 0f);
        // 100e00ba: field 29 is added to the velocity, so it is climbing back out of its sag.
        Assert.Equal(launched + 0.5f * 0.1f, drift.Particles[0].VelY, 4);

        var fountain = new GlobalSmokeSim(Record12279(), Fixed(0.9, 0.5, 0.1, 0.25));
        fountain.Process(0f, 0.1f, 0f, 0f, 0f);
        float v = fountain.Particles[0].VelY;
        fountain.Process(0.1f, 0.1f, 0f, 0f, 0f);
        Assert.Equal(v, fountain.Particles[0].VelY, 5);
    }

    [Fact]
    public void AParticleGrowsOverItsLife_ThenFreesItsSlot()
    {
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.9, 0.5, 0.1, 0.25));
        sim.Process(0f, 0.1f, 0f, 0f, 0f);
        Assert.Equal(2f, sim.Particles[0].Width, 4);

        // Half-way through its 1.7 s life the width is half-way from field 14 to field 15.
        sim.Process(0.85f, 0.1f, 0f, 0f, 0f);
        Assert.Equal(6f, sim.Particles[0].Width, 3);
        Assert.Equal(6f, sim.Particles[0].Height, 3);

        // 100dffbd: past the life the slot goes dead on the very next call.
        sim.Process(1.71f, 0.1f, 0f, 0f, 0f);
        Assert.False(sim.Particles[0].Alive);
    }

    [Fact]
    public void TheScreenAngleLeansNeighbouringSpritesOppositeWays()
    {
        // 100e012f: ((slot & 1) - 0.5) * age * (slot - 4).
        Assert.Equal(-0.5f * 2f * (0 - 4), GlobalSmokeSim.Angle(0, 2f), 5);
        Assert.Equal(0.5f * 2f * (1 - 4), GlobalSmokeSim.Angle(1, 2f), 5);
        // Slot 4 never leans, whatever the age.
        Assert.Equal(0f, GlobalSmokeSim.Angle(4, 7f), 5);
        // At age 0 nothing leans.
        Assert.Equal(0f, GlobalSmokeSim.Angle(9, 0f), 5);
    }

    [Fact]
    public void TheColourIsTheSharedStockRamp()
    {
        var sim = new GlobalSmokeSim(Record12279(), Fixed(0.9, 0.5, 0.1, 0.25));
        sim.Process(0f, 0.1f, 0f, 0f, 0f);
        // Fields 18-21 are the start A,R,G,B: 1, 0.3, 0.6, 1.
        Assert.Equal(0xff4c99ffu, sim.Particles[0].Argb);

        // At the end it is fields 22-25: 0, 0.3, 0.3, 1 — the same blue, gone transparent.
        sim.Process(1.7f, 0.1f, 0f, 0f, 0f);
        Assert.Equal(0x004c4cffu, sim.Particles[0].Argb);
    }
}
