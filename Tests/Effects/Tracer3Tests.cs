using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Locks <see cref="Tracer3Sim"/> to stock _GfxControlTracer3_t (init <c>100ff7f3</c>, step
/// <c>100ff754</c>) and <see cref="Nano0Sim"/> to _GfxControlNano0_t (init <c>100e7146</c>, Process
/// <c>100e6f76</c>), with 2662 (the tracer of 28609 Freezing Surge) and its child 2685.
/// </summary>
public class Tracer3Tests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record2662()
    {
        var f = new float[16];
        f[0] = Bits(2);
        f[8] = 20f;
        f[9] = Bits(-1);
        f[10] = 20f;
        f[11] = 1f;
        f[12] = 0.1f;
        f[13] = 0.3f;
        f[14] = 1f;
        f[15] = Bits(2685);
        return f;
    }

    static float[] Record2685()
    {
        var f = new float[40];
        f[0] = Bits(0x705);
        f[8] = -1f;
        f[9] = Bits(10);
        f[10] = 112.5f;
        f[11] = 1f;
        for (int i = 12; i <= 15; i++)
            f[i] = 2f;
        f[16] = 1f; f[17] = 1f; f[18] = 1f; f[19] = 1f;
        f[20] = 0f; f[21] = 1f; f[22] = 1f; f[23] = 1f;
        f[26] = 6.28319f;
        f[27] = -1.5708f;
        f[28] = 1.5708f;
        f[29] = 1f;
        f[30] = 1f;
        f[31] = Bits(1);
        f[32] = 0.5f;
        f[33] = 0.5f;
        f[34] = 0.5f;
        f[35] = 0.5f;
        return f;
    }

    [Fact]
    public void Tracer3_FliesAtMostTheLineIn0Point2Seconds_AndArrives()
    {
        var sim = new Tracer3Sim(Record2662(), 0f, 1f, 0f, 3f, 1f, 0f);
        Assert.Equal(3f, sim.Length, 6);
        Assert.Equal(15f, sim.Speed, 5); // min(20, 5 * 3)
        Assert.Equal(2685, sim.ChildId);

        Assert.False(sim.Position(0.1f, out float x, out float y, out float z));
        Assert.Equal(1.5f, x, 5);
        Assert.Equal(1f, y, 5);
        Assert.True(sim.Position(0.2f, out x, out _, out _));
        Assert.Equal(3f, x, 5);
        Assert.True(sim.Position(0.5f, out x, out _, out _));
        Assert.Equal(3f, x, 5); // held at the end
    }

    [Fact]
    public void Tracer3_OnALongLine_KeepsField10()
    {
        var sim = new Tracer3Sim(Record2662(), 0f, 0f, 0f, 0f, 0f, 10f);
        Assert.Equal(20f, sim.Speed);
        Assert.False(sim.Position(0.25f, out _, out _, out float z));
        Assert.Equal(5f, z, 5);
    }

    [Fact]
    public void Tracer3_ALineUnderOneCentimetre_IsReadyAtOnce()
    {
        Assert.True(new Tracer3Sim(Record2662(), 0f, 0f, 0f, 0.005f, 0f, 0f).ReadyAtStart);
        Assert.False(new Tracer3Sim(Record2662(), 0f, 0f, 0f, 0.02f, 0f, 0f).ReadyAtStart);
    }

    [Fact]
    public void Tracer3_PacksItsColourLikeStock()
    {
        var sim = new Tracer3Sim(Record2662(), 0f, 0f, 0f, 1f, 0f, 0f);
        Assert.Equal(0xFF194CFFu, sim.Argb);
    }

    static Nano0Sim MakeNano0(float[] fields, float ox = 0f)
        => new Nano0Sim(fields, 0, 15, () => 0.5f, () => 0, ox, 0f, 0f, null);

    static List<Sprite2Type0Visual.Sprite> Alive(Nano0Sim sim)
    {
        var list = new List<Sprite2Type0Visual.Sprite>();
        foreach (Sprite2Type0Visual.Sprite s in sim.Sprites)
            if (s.Alive)
                list.Add(s);
        return list;
    }

    [Fact]
    public void Nano0_Init_FiresItsBurstAtTheLocator_AndSizesThePool()
    {
        Nano0Sim sim = MakeNano0(Record2685(), ox: 2f);
        Assert.Equal((int)(112.5 * 1.5 * 0.5), sim.Sprites.Length);
        List<Sprite2Type0Visual.Sprite> alive = Alive(sim);
        Assert.Single(alive);
        Assert.Equal(2f, alive[0].X);
        Assert.Equal(2f, alive[0].Width);
        Assert.Equal(0.5f, alive[0].Life);
    }

    [Fact]
    public void Nano0_SpreadsItsSpawnsAlongThePathItMoved()
    {
        Nano0Sim sim = MakeNano0(Record2685());
        // 0.1 s at 112.5 a second is 11 due; the emitter moved from x 0 to x 1.
        sim.Step(0.1f, 0f, 1f, 0f, 0f, null, 0f, 0f, 0f);
        List<Sprite2Type0Visual.Sprite> alive = Alive(sim);
        Assert.Equal(12, alive.Count);
        var xs = new List<float>();
        for (int i = 1; i < alive.Count; i++)
            xs.Add(alive[i].X);
        xs.Sort();
        for (int k = 1; k <= 11; k++)
            Assert.Equal(k / 11f, xs[k - 1], 4);

        // The next call starts from where this one ended.
        sim.Step(0.2f, 0f, 1f, 0f, 1f, null, 0f, 0f, 0f);
        var zs = new List<float>();
        foreach (Sprite2Type0Visual.Sprite s in Alive(sim))
            if (s.Z > 0f)
                zs.Add(s.Z);
        zs.Sort();
        Assert.Equal(11, zs.Count);
        Assert.Equal(1f / 11f, zs[0], 4);
        Assert.Equal(1f, zs[10], 4);
    }

    [Fact]
    public void Nano0_ReadsItsWindFieldsOneLowerThanSparks()
    {
        float[] f = Record2685();
        f[35] = 0.75f;
        f[36] = 2f;
        f[37] = Bits(3);
        f[38] = 4f;
        Nano0Sim sim = MakeNano0(f);
        Assert.Equal(0.75f, sim.WindLow);
        Assert.Equal(0.75f, sim.Life1);
        Assert.Equal(2f, sim.WindHigh);
        Assert.Equal(3, sim.WindMode);
    }

    [Fact]
    public void Nano0_Flag0x200_IsReadyOnceThePoolEmpties()
    {
        float[] f = Record2685();
        f[10] = 0f; // just the burst
        Nano0Sim sim = MakeNano0(f);
        Assert.False(sim.Step(0.1f, 0.1f, 0f, 0f, 0f, null, 0f, 0f, 0f));
        Assert.True(sim.Step(0.7f, 0.6f, 0f, 0f, 0f, null, 0f, 0f, 0f));
    }

    [Fact]
    public void Nano0_Terminating_StopsTheSpawnsAFieldLifeBeforeTheEnd()
    {
        Nano0Sim sim = MakeNano0(Record2685());
        sim.TerminateGracefully(1f);
        Assert.Equal(1.5f, sim.Duration);
        int before = Alive(sim).Count;
        sim.Step(1.2f, 0f, 1f, 0f, 0f, null, 0f, 0f, 0f); // age >= duration - life: no spawns
        Assert.Equal(before, Alive(sim).Count);
    }
}
