using System;
using Xunit;

/// <summary>
/// Locks <see cref="ShockWaveSim"/> to stock _GfxControlShockWave_t (Process <c>100ee525</c>) and its
/// GfxVisualGroundRing / GfxVisualCone, with 43103 (the hit of 152838 Magnified Psychic Hammer).
/// </summary>
public class ShockWaveSimTests
{
    static float Bits(uint i) => BitConverter.Int32BitsToSingle(unchecked((int)i));

    static float[] Record43103()
    {
        var f = new float[36];
        f[0] = Bits(0x3801);
        f[8] = 1.5f;
        f[9] = Bits(34);
        f[10] = Bits(40);
        f[11] = Bits(2);
        f[12] = -2f;
        f[13] = 0f;
        f[14] = Bits(0xffff5050);
        f[15] = Bits(0xffff2020);
        f[16] = 4f;
        f[17] = 5f;
        f[18] = Bits(0x00ff7070);
        f[19] = Bits(0x00ff2020);
        f[20] = 10f;
        f[21] = -1f;
        f[22] = 0.21f;
        f[23] = 0.5f;
        f[24] = Bits(2);
        f[25] = Bits(13);
        f[26] = Bits(10);
        f[27] = 1f;
        f[28] = 1f;
        f[29] = 40f;
        f[30] = 0.3f;
        f[31] = 0.3f;
        f[32] = -0.05f;
        f[33] = 0.75f;
        f[34] = Bits(0x80ff2020);
        f[35] = Bits(0x00ffffff);
        return f;
    }

    static ShockWaveSim Make() => new ShockWaveSim(Record43103(), 1f, 2f, 3f, (x, y, z) => float.NaN);

    [Fact]
    public void ColorOps_AreRandy31sByteWiseOnes()
    {
        Assert.Equal(0x80802828u, ShockWaveSim.Scale(0xffff5050u, 0.5f));
        Assert.Equal(0xffffffffu, ShockWaveSim.Add(0x80808080u, 0x90909090u));
    }

    [Fact]
    public void Ring_SpreadsItsRadiiAndColoursOverItsLife()
    {
        ShockWaveSim sim = Make();
        sim.Step(0.75f, 1f, 2f, 3f); // ring 0 at t = 0.5, ring 1 at t = 1/6
        ShockWaveSim.Ring ring = sim.Rings[0];
        Assert.True(ring.Drawn);
        Assert.Equal(1f, ring.X);
        // Inner radius -2 -> 4, outer 0 -> 5; the first pair lies on +x.
        Assert.Equal(1f, ring.Vertices[0], 5);
        Assert.Equal(0.21f, ring.Vertices[1], 5); // no ground: the centre's height, plus field 22
        Assert.Equal(2.5f, ring.Vertices[3], 5);
        Assert.Equal(
            ShockWaveSim.Add(ShockWaveSim.Scale(0xffff5050u, 0.5f), ShockWaveSim.Scale(0x00ff7070u, 0.5f)),
            ring.Inner);
        // Flag 0x1000: u runs round the ring (10 / 40 a step), v 0 inside and field 21 outside.
        Assert.Equal(0.25f, ring.Uvs[4], 5);
        Assert.Equal(0f, ring.Uvs[1]);
        Assert.Equal(-1f, ring.Uvs[3]);
    }

    [Fact]
    public void Flag0x2000_ClampsANegativeRadius()
    {
        ShockWaveSim sim = Make();
        sim.Step(0.3f, 0f, 0f, 0f); // t = 0.2: inner -0.8 -> 0
        Assert.Equal(0f, sim.Rings[0].Vertices[0], 6);
    }

    [Fact]
    public void Rings_StartAPeriodApart_AndGoPastTheirLife()
    {
        ShockWaveSim sim = Make();
        sim.Step(0.4f, 0f, 0f, 0f);
        Assert.True(sim.Rings[0].Drawn);
        Assert.False(sim.Rings[1].Drawn);
        sim.Step(1.6f, 0f, 0f, 0f);
        Assert.False(sim.Rings[0].Alive);
        Assert.True(sim.Rings[1].Alive);
    }

    [Fact]
    public void RunningRings_KeepItAlivePastTheBaseDuration()
    {
        ShockWaveSim sim = Make();
        Assert.False(sim.Step(1.9f, 0f, 0f, 0f)); // life 1.5 is past, ring 1 runs to 2.0
        Assert.True(sim.Step(2.01f, 0f, 0f, 0f));
    }

    [Fact]
    public void Terminate_IsClearedByARunningRing()
    {
        ShockWaveSim sim = Make();
        sim.Terminate();
        Assert.False(sim.Step(0.2f, 0f, 0f, 0f));
    }

    [Fact]
    public void Cones_FlashAtTheStartOfEachPeriod_AndGoAfterTheLast()
    {
        ShockWaveSim sim = Make();
        sim.Step(0.02f, 5f, 0f, 0f); // frac 0.04: alpha 0x80 * 0.4
        Assert.Equal(51u, sim.Cones[0].Bottom >> 24);
        Assert.Equal(5f, sim.Cones[0].X);
        sim.Step(0.075f, 6f, 0f, 0f); // frac 0.15: full
        Assert.Equal(0x80u, sim.Cones[0].Bottom >> 24);
        Assert.Equal(5f, sim.Cones[0].X); // moved only while frac < 0.1
        sim.Step(0.3f, 0f, 0f, 0f); // frac 0.6: 1 - 0.4 / 0.8 lands a hair under 0.5 in floats
        Assert.Equal(63u, sim.Cones[0].Bottom >> 24);
        sim.Step(1.0f, 0f, 0f, 0f); // p = 2 = the ring count
        Assert.False(sim.Cones[0].Alive);
    }

    [Fact]
    public void Cones_TaperOneAfterAnother()
    {
        ShockWaveSim sim = Make();
        ShockWaveSim.Cone second = sim.Cones[1];
        Assert.Equal(0.25f, second.BottomRadius, 5);
        Assert.Equal(0.175f, second.TopRadius, 5);
        Assert.Equal(40f, second.Vertices[4]);
        Assert.Equal(0.25f, second.Vertices[0], 5);
    }
}
