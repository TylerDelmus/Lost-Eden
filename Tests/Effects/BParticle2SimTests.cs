using System;
using Xunit;

/// <summary>
/// Locks <see cref="BParticle2Sim"/> and <see cref="StockColorCurve"/> to stock GfxControlBParticle2_t
/// (Process <c>1010b499</c>, init <c>1010bf63</c>) and the curve at <c>1011678b</c>.
/// </summary>
public class BParticle2SimTests
{
    static readonly float[] Identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    /// <summary>A record shaped like 71354 with a two-key curve, fields 0..40.</summary>
    static float[] Record(int flags = 3, int mode = 0, int count = 4)
    {
        var f = new float[41];
        f[0] = Bits(flags);
        f[8] = 2f;
        f[9] = Bits(mode);
        f[11] = Bits(count);
        f[12] = 0.01f;
        f[13] = 8f;
        f[14] = 0.92f;
        f[15] = Bits(1);
        f[16] = -20f;
        f[17] = -220f; f[18] = 220f;
        f[19] = 5f; f[20] = 100f;
        f[21] = -220f; f[22] = 220f;
        f[23] = 0f; f[24] = 50f;
        f[25] = 1f; f[26] = 2f;
        f[27] = 15f; f[28] = 22f;
        f[29] = 0.2f;
        f[30] = 1.015f;
        f[33] = 1f;
        f[35] = Bits(2);
        f[36] = 0f; f[37] = Bits(unchecked((int)0xff204080u));
        f[38] = 1f; f[39] = Bits(0x00000000);
        return f;
    }

    [Fact]
    public void Curve_BlendsBytesIn256ths_AndFallsBackToTheLastKey()
    {
        var curve = new StockColorCurve(new[] { 0f, 1f }, new[] { 0xff204080u, 0x00000000u });
        Assert.Equal(0xff204080u, curve.Evaluate(0f));
        // w = 128: each byte halves, truncated.
        Assert.Equal(0x7f102040u, curve.Evaluate(0.5f));
        Assert.Equal(0x00000000u, curve.Evaluate(1f));   // not below the last key: the last key
        Assert.Equal(0x00000000u, curve.Evaluate(-0.1f)); // before the first key: the last key too
    }

    [Fact]
    public void Curve_ReadsCountThenTimeColourPairs()
    {
        int index = 35;
        StockColorCurve curve = StockColorCurve.Read(Record(), ref index);
        Assert.Equal(2, curve.Count);
        Assert.Equal(40, index);
    }

    [Fact]
    public void Burst_SpawnsAllAtOnce_WithTheDrawOrder()
    {
        // Every draw 0.5: size 18.5, life 1.5, jitter 0, velocity mid-range, angle pi, spin 25 deg/s.
        var sim = new BParticle2Sim(Record(), 0f, 0f, () => 0.5f);
        sim.Init(1f, 2f, 3f, Identity);
        BParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(18.5f, p.Size, 4);
        Assert.Equal(1.5f, p.Life, 4);
        Assert.Equal(1.5f, p.LifeTotal, 4);
        Assert.Equal(1f, p.X, 4);
        Assert.Equal(2f, p.Y, 4);
        Assert.Equal(0f, p.VX, 3);
        Assert.Equal(52.5f, p.VY, 3);
        Assert.Equal(-20f, p.AY, 4);
        Assert.Equal((float)Math.PI, p.Angle, 4);
        Assert.Equal((float)(25 * Math.PI / 180), p.Spin, 4);
    }

    [Fact]
    public void Step_MovesThenAccelerates_ThenGrowsAndDrags()
    {
        var sim = new BParticle2Sim(Record(), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        const float dt = 0.02f;
        Assert.True(sim.Step(dt, dt, 2f, 0f, 0f, 0f, Identity, null, 0f));

        BParticle2Sim.Particle p = sim.Particles[0];
        Assert.Equal(52.5f * dt, p.Y, 4);
        float vy = 52.5f - 20f * dt;
        float drag = (float)((0.92 - 1.0) * dt * 100.0);
        Assert.Equal(vy + vy * drag, p.VY, 3);
        Assert.Equal((float)((1.015 - 1.0) * 18.5 * dt * 100.0 + 18.5), p.Size, 3);
        Assert.Equal(1.5f - dt, p.Life, 4);
        Assert.True(p.Visible);
    }

    [Fact]
    public void Burst_IsDoneWhenEveryParticleHasDied()
    {
        var sim = new BParticle2Sim(Record(), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        Assert.True(sim.Step(1f, 1f, 5f, 0f, 0f, 0f, Identity, null, 0f));
        Assert.False(sim.Step(1f, 2f, 5f, 0f, 0f, 0f, Identity, null, 0f));
        Assert.False(sim.Particles[0].Visible);
    }

    [Fact]
    public void Emitter_StartsEmpty_AndRespawnsOncePerInterval()
    {
        var sim = new BParticle2Sim(Record(mode: 1), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        Assert.Equal(0f, sim.Particles[0].Life);

        sim.Step(0.005f, 0.005f, -1f, 0f, 0f, 0f, Identity, null, 0f); // under the 0.01 s interval
        Assert.Equal(0f, sim.Particles[0].Life);

        sim.Step(0.01f, 0.015f, -1f, 0f, 0f, 0f, Identity, null, 0f);
        Assert.Equal(1.5f, sim.Particles[0].Life, 4);
        Assert.Equal(0f, sim.Particles[1].Life); // field 15: one per interval
    }

    [Fact]
    public void Emitter_StopsSpawningLifeMaxBeforeTheEnd()
    {
        var sim = new BParticle2Sim(Record(mode: 1), 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, Identity);
        // Duration 2, field 26 = 2: nothing may spawn once age >= 0.
        sim.Step(0.02f, 0.02f, 2f, 0f, 0f, 0f, Identity, null, 0f);
        Assert.Equal(0f, sim.Particles[0].Life);
    }

    [Fact]
    public void Turn_MapsLocalVelocityThroughTheLocator()
    {
        // A quarter turn about y: local +x goes to world -z.
        float[] turn = { 0f, 0f, 1f, 0f, 1f, 0f, -1f, 0f, 0f };
        float[] rec = Record();
        rec[17] = 10f; rec[18] = 10f; rec[21] = 0f; rec[22] = 0f;
        var sim = new BParticle2Sim(rec, 0f, 0f, () => 0.5f);
        sim.Init(0f, 0f, 0f, turn);
        Assert.Equal(0f, sim.Particles[0].VX, 4);
        Assert.Equal(-10f, sim.Particles[0].VZ, 4);
    }
}
