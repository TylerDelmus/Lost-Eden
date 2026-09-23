using System;
using Xunit;

/// <summary>
/// Locks <see cref="SkyFlashSim"/> to stock _GfxControlSkyFlash_t (Process <c>100ef141</c>, dynel ctor
/// <c>100ef80f</c>) with 43108 (the hit of 168665 Gift of Life) and 43107 (a hit of 305432).
/// </summary>
public class SkyFlashSimTests
{
    static float I(uint v) => BitConverter.Int32BitsToSingle(unchecked((int)v));

    /// <summary>43108: flags 0x3a03, one cycle of 0.4 + 4.5 s, six cones of ten segments, 60 m tall.</summary>
    static float[] GiftOfLife() => new[]
    {
        I(0x3a03), 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1f, I(13), I(1), 0.4f, 4.5f, I(6), I(10), 4f, 60f, 60f,
        0.45f, 0.45f, 0.00333333f, 0.00333333f, 0f, 0f,
        I(0xaa888888), I(0xaaffffff), I(0x44666666), I(0xffffffff), I(0x05333333), I(0x05ffffff),
    };

    /// <summary>43107: flags 0x7e03 (0x400 grows down, 0x4000 on the ground), 0.4 + 5.5 s.</summary>
    static float[] Purification() => new[]
    {
        I(0x7e03), 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1f, I(13), I(1), 0.4f, 5.5f, I(6), I(10), 4f, 100f, 60f,
        0.25f, 0.2f, 0.0833333f, 0.00333333f, 1.9f, 0f,
        I(0x20888888), I(0xbbffffff), I(0x33aaaaaa), I(0x88ffffff), I(0x05ffffff), I(0x05ffffff),
    };

    [Fact]
    public void Ends_AfterItsCycles()
    {
        var sim = new SkyFlashSim(GiftOfLife());
        Assert.True(sim.Step(4.85f));
        Assert.False(sim.Step(4.9f));
    }

    [Fact]
    public void PhaseOne_MixesC0IntoC2_AndFollows()
    {
        var sim = new SkyFlashSim(GiftOfLife());
        Assert.True(sim.Step(0f));
        Assert.Equal(0xaa888888u, sim.BottomColour);
        Assert.Equal(0xaaffffffu, sim.TopColour);

        sim.Step(0.2f); // k = 0.5: halves of 0xaa888888 and 0x44666666
        Assert.Equal(0x77777777u, sim.BottomColour);
        Assert.True(sim.Follow);
        Assert.Equal(60f, sim.Height);
        Assert.Equal(0f, sim.Lift);
    }

    [Fact]
    public void PhaseTwo_StartsOnC2AndC3()
    {
        var sim = new SkyFlashSim(GiftOfLife());
        sim.Step(0.4f); // c = field 11: phase 2 at k = 0
        Assert.Equal(0x44666666u, sim.BottomColour);
        Assert.Equal(0xffffffffu, sim.TopColour);
        Assert.True(sim.Follow);
    }

    [Fact]
    public void Cones_StepOutward_AndStandOnThePlace()
    {
        var sim = new SkyFlashSim(GiftOfLife());
        sim.Step(1f);
        sim.Place(1f, 2f, 3f);
        Assert.Equal(6, sim.Cones.Length);
        for (int i = 0; i < 6; i++)
        {
            ShockWaveSim.Cone cone = sim.Cones[i];
            Assert.Equal(2f, cone.Y);
            Assert.Equal(60f, cone.Height);
            Assert.Equal(0.45 + i * 0.00333333, cone.BottomRadius, 5);
            Assert.Equal(22 * 3, cone.Vertices.Length);
        }
        ShockWaveSim.Cone first = sim.Cones[0];
        Assert.Equal(0.45f, first.Vertices[0], 5); // bottom (r, 0, 0)
        Assert.Equal(0f, first.Vertices[1]);
        Assert.Equal(60f, first.Vertices[4]);      // top (r, 60, 0)
        Assert.Equal(sim.BottomColour, first.Bottom);
        Assert.Equal(sim.TopColour, first.Top);
    }

    [Fact]
    public void GrowDown_ComesFromTheTop_InPhaseOne()
    {
        var sim = new SkyFlashSim(Purification());
        sim.Step(0.2f); // k = 0.5, u = 0.2 / 5.9
        double u = (double)(float)(0.2f / (double)5.9f);
        double bottom = (float)(1.9 * u + 0.25);
        Assert.Equal(0.5 * bottom + 0.5 * 0.2, sim.BottomRadius, 5);
        Assert.Equal(0.2f, sim.TopRadius, 5);
        Assert.Equal(0.5 * 0.0833333 + 0.5 * 0.00333333, sim.StepBottom, 5);
        Assert.Equal(30f, sim.Height, 4);
        Assert.Equal(30f, sim.Lift, 4);

        sim.Step(1f); // phase 2: full height, no lift
        Assert.Equal(60f, sim.Height);
        Assert.Equal(0f, sim.Lift);
    }

    [Fact]
    public void HeightOverGround_KeptOnlyWithinField17()
    {
        var sim = new SkyFlashSim(Purification());
        Assert.Equal(10f, sim.HeightOverGround(15f, 5f));
        Assert.Equal(60f, sim.HeightOverGround(60f, 0f));
        Assert.Equal(0f, sim.HeightOverGround(61f, 0f));
        Assert.Equal(0f, sim.HeightOverGround(4f, 5f));
    }

    [Fact]
    public void ANegativeBottomRadius_IsFlippedAndScaled_OnADynel()
    {
        float[] f = GiftOfLife();
        f[18] = -2f; // 12510
        var sim = new SkyFlashSim(f);
        sim.ScaleBottomRadius(0.5f);
        Assert.Equal(1f, sim.BottomRadiusField);

        var positive = new SkyFlashSim(GiftOfLife());
        positive.ScaleBottomRadius(0.5f);
        Assert.Equal(0.45f, positive.BottomRadiusField);
    }

    [Fact]
    public void ConeTurn_IsIOf628OverCount()
    {
        var sim = new SkyFlashSim(GiftOfLife());
        Assert.Equal(0f, sim.ConeTurn(0));
        Assert.Equal((float)(3 * 6.28000020980835 / 6), sim.ConeTurn(3));
    }
}
