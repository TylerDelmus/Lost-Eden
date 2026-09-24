using System;
using Xunit;

/// <summary>
/// Locks <see cref="SpiralSim"/> to stock _GfxControlSpiral_t (Process <c>100f4cb5</c>) and
/// <see cref="SpiralRibbon"/> to GfxVisualSpiral (ctor <c>10021eb4</c>, draw <c>10021924</c>), with 43010
/// (the hit of 282 nanos, e.g. 25994): duration 4, material 32.
/// </summary>
public class SpiralSimTests
{
    [Fact]
    public void Step_GrowsForHalfTheDurationThenEatsItsTail()
    {
        var sim = new SpiralSim(4f);
        sim.Step(1f);
        Assert.Equal(0f, sim.Start);
        Assert.Equal(0.5f, sim.End);
        sim.Step(3f);
        Assert.Equal(0.5f, sim.Start, 6);
        Assert.Equal(1f, sim.End);
        sim.Step(4.5f); // p = 2.25: the last range stays
        Assert.Equal(0.5f, sim.Start, 6);
        Assert.Equal((float)(4.5 * 3.700000047683716), sim.Spin, 4);
    }

    [Fact]
    public void Ribbon_ClimbsOneTurnAtRadiusPointSix_AndFadesAtBothEnds()
    {
        var r = new SpiralRibbon(SpiralSim.RibbonOffset(0));
        r.Build(2f, 0f, 1f);
        Assert.Equal(0, r.First);
        Assert.Equal(26, r.End);
        Assert.Equal(0.6f, r.X[0], 5);
        Assert.Equal(0f, r.Y[0]);
        Assert.Equal(0f, r.Z[0], 5);
        Assert.Equal(-0.3f, r.Y[1], 5);
        Assert.Equal(0.3f * 6.28f, r.Y[24], 4);
        Assert.Equal(1f, r.V[0]);
        Assert.Equal(0f, r.V[1]);
        // u starts at -age and steps 6.28 * 0.6 / 1.5 / 12 per pair.
        Assert.Equal(-2f, r.U[0]);
        Assert.Equal(-2f + 6.28f * 0.6f / 1.5f / 12f, r.U[2], 5);
        Assert.Equal(0f, r.Alpha[0]);
        Assert.Equal(0f, r.Alpha[1]);
        Assert.Equal(1f, r.Alpha[2]);
        Assert.Equal(0f, r.Alpha[24]);
        Assert.Equal(0f, r.Alpha[25]);
    }

    [Fact]
    public void SecondRibbon_StartsHalfATurnRound()
    {
        var r = new SpiralRibbon(SpiralSim.RibbonOffset(1));
        r.Build(0f, 0f, 1f);
        Assert.Equal(3.14f, SpiralSim.RibbonOffset(1), 5);
        Assert.Equal((float)(Math.Cos(3.14f) * 0.6), r.X[0], 5);
    }

    [Fact]
    public void Start_MovesTheFirstPairPartWay_AndFadesThere()
    {
        var full = new SpiralRibbon(0f);
        full.Build(0f, 0f, 1f);
        var r = new SpiralRibbon(0f);
        r.Build(0f, 1f / 24f, 1f); // f = 0.5: pair 0 halfway to pair 1
        Assert.Equal(0, r.First);
        Assert.Equal((full.X[0] + full.X[2]) * 0.5f, r.X[0], 5);
        Assert.Equal((full.Y[1] + full.Y[3]) * 0.5f, r.Y[1], 5);
        Assert.Equal((full.U[0] + full.U[2]) * 0.5f, r.U[0], 5);
        Assert.Equal(0f, r.Alpha[0]);

        r.Build(0f, 0.5f, 1f); // f = 6
        Assert.Equal(12, r.First);
        Assert.Equal(full.X[12], r.X[12], 5);
        Assert.Equal(0f, r.Alpha[12]);
        Assert.Equal(0f, r.Alpha[13]);
        Assert.Equal(1f, r.Alpha[14]);
    }

    [Fact]
    public void End_MovesTheLastPairBack_AndStopsThere()
    {
        var full = new SpiralRibbon(0f);
        full.Build(0f, 0f, 1f);
        var r = new SpiralRibbon(0f);
        r.Build(0f, 0f, 13f / 24f); // f = 6.5: ceil 7, pair 7 halfway back to pair 6
        Assert.Equal(16, r.End);
        Assert.Equal((full.X[12] + full.X[14]) * 0.5f, r.X[14], 5);
        Assert.Equal(0f, r.Alpha[14]);
        Assert.Equal(0f, r.Alpha[15]);
        Assert.Equal(1f, r.Alpha[12]);

        r.Build(0f, 0f, 0f); // at the very start: one quad, all faded
        Assert.Equal(4, r.End - r.First);
    }

    [Fact]
    public void Draw_ResetsAStartAboveOne_AndLiftsAnEndBelowTheStart()
    {
        var r = new SpiralRibbon(0f);
        r.Build(0f, 1.5f, 1f);
        Assert.Equal(0, r.First);
        r.Build(0f, 0.5f, 0.25f); // end = start
        Assert.Equal(12, r.First);
        Assert.Equal(14, r.End);
    }

    [Fact]
    public void HalfAngle_WrapsPastAFullTurn()
    {
        Assert.Equal(0.5f, SpiralSim.HalfAngle(1f));
        double turns = 7.0 / 6.2831854820251465;
        Assert.Equal((float)((turns - Math.Floor(turns)) * Math.PI), SpiralSim.HalfAngle(7f), 5);
    }
}
