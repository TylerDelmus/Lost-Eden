using System;
using Xunit;

/// <summary>
/// Locks <see cref="GroundGridSim"/> to stock GfxControlGroundGrid_t (Process <c>1010e704</c>) and
/// GfxVisualGroundGrid's Update (<c>100164b4</c>), with 71370: 200 x 200 at 1 m, fade in 0.5, out 2, 60 s.
/// </summary>
public class GroundGridSimTests
{
    static float Bits(int i) => BitConverter.Int32BitsToSingle(i);

    static float[] Record71370()
    {
        var f = new float[27];
        f[0] = Bits(0x30c03);
        f[8] = 60f;
        f[9] = Bits(95);
        f[10] = Bits(200);
        f[12] = 1f; f[13] = 1f;
        f[14] = 0.5f; f[15] = 2f;
        f[16] = Bits(-1);
        f[18] = 1f;
        f[19] = 0.25f;
        f[20] = 5f;
        return f;
    }

    [Fact]
    public void Alpha_FadesInThenHoldsThenFadesOut()
    {
        var sim = new GroundGridSim(Record71370());
        sim.Advance(0.25f, 0f);
        Assert.Equal(0.5f, sim.Alpha, 5);
        sim.Advance(10f, 0f);
        Assert.Equal(1f, sim.Alpha, 5);
        sim.Advance(59f, 0f);
        Assert.Equal(0.5f, sim.Alpha, 5);
    }

    [Fact]
    public void Grid_IsCentredOnTheEmitter_AndUvsSpanOnce()
    {
        var sim = new GroundGridSim(Record71370());
        Assert.Equal(99.5f, sim.HalfWidth, 4);
        sim.Point(0, 0, out float x0, out float z0);
        Assert.Equal(-99.5f, x0, 4);
        Assert.Equal(-99.5f, z0, 4);
        sim.Uv(199, 199, out float u, out float v);
        Assert.Equal(1f, u, 5);
        Assert.Equal(1f, v, 5);
    }

    [Fact]
    public void CentredUvFlag_ShiftsByHalfTheGrid()
    {
        float[] rec = Record71370();
        rec[0] = Bits(0x30c03 | GroundGridSim.FlagCentredUv);
        rec[10] = Bits(3);
        var sim = new GroundGridSim(rec);
        sim.Uv(0, 0, out float u, out _);
        Assert.Equal(-0.5f, u, 5);
    }

    /// <summary>71230: the 20 x 20 mode 2 grid both Crystal Overload and Harvested Mind use.</summary>
    static float[] Record71230()
    {
        var f = new float[27];
        f[0] = Bits(0xe203);
        f[8] = 10f;
        f[9] = Bits(58);
        f[10] = Bits(20);
        f[11] = Bits(2);
        f[12] = 3f; f[13] = 3f;
        f[14] = 2f; f[15] = 2f;
        f[16] = Bits(unchecked((int)0xff80c0ff));
        f[18] = 2f;
        f[19] = 0.25f;
        f[20] = 5f;
        f[21] = 0.1f; f[22] = 0.1f;
        f[23] = 0.5f; f[24] = 0.5f;
        return f;
    }

    [Fact]
    public void Mode2_IsTheRadialDistanceOverHalfTheGrid()
    {
        var sim = new GroundGridSim(Record71230());
        Assert.Equal(2, sim.VisualMode);
        Assert.True(sim.Supported);

        // 10016d53: dx and dy are measured from (N - 1) / 2 = 9.5 and divided by the same 9.5,
        // so a corner is sqrt(2) and the middle of an edge is exactly 1.
        // An even N has no vertex on the centre, so the nearest four sit sqrt(0.5) / 9.5 out.
        Assert.Equal((float)(Math.Sqrt(0.5) / 9.5), sim.Falloff(9, 9), 5);
        Assert.Equal(1f, sim.Falloff(0, 9), 2);
        Assert.Equal((float)Math.Sqrt(2.0), sim.Falloff(0, 0), 4);
        Assert.Equal((float)Math.Sqrt(2.0), sim.Falloff(19, 19), 4);
    }

    [Fact]
    public void Mode1_IsTheDiamondDistance_AndOverADifferentHalf()
    {
        float[] rec = Record71230();
        rec[11] = Bits(1);
        var sim = new GroundGridSim(rec);

        // 100165e2 divides by N / 2 = 10, not the table's (N - 1) / 2 = 9.5. Stock really does use
        // the two different halves, so the centre of a mode 1 grid never reaches 0 either.
        Assert.Equal((Math.Abs(10 - 10f) + Math.Abs(10 - 10f)) / 10f, sim.Falloff(10, 10), 5);
        Assert.Equal(0f, sim.Falloff(10, 10), 5);
        Assert.Equal(1f, sim.Falloff(10, 0), 5);     // |0-10| + |10-10| = 10
        Assert.Equal(2f, sim.Falloff(0, 0), 5);      // the corner is twice the edge, not sqrt(2)
    }

    [Fact]
    public void Mode0_LeavesEveryVertexAtFullStrength()
    {
        var sim = new GroundGridSim(Record71370());
        Assert.Equal(0, sim.VisualMode);
        Assert.Equal(0f, sim.Falloff(0, 0), 5);
        Assert.Equal(0f, sim.Falloff(199, 43), 5);
        Assert.Equal(1f, GroundGridSim.Fade(sim.Falloff(199, 43)), 5);
    }

    [Fact]
    public void Fade_FloorsAtZero_ButLetsANaNThrough()
    {
        Assert.Equal(1f, GroundGridSim.Fade(0f), 5);
        Assert.Equal(0.25f, GroundGridSim.Fade(0.75f), 5);
        // Past the edge the vertex is clipped away rather than going negative.
        Assert.Equal(0f, GroundGridSim.Fade(1.5f), 5);
        // 10016623's test ah,0x41 is true for unordered, so stock keeps a NaN instead of clamping it.
        Assert.True(float.IsNaN(GroundGridSim.Fade(float.NaN)));
    }

    [Fact]
    public void VertexAlpha_IsTheFallOffTimesTheColoursOwnAlpha()
    {
        var sim = new GroundGridSim(Record71230());
        // 0xff80c0ff: the rgb goes through untouched and only the alpha is shaded.
        Assert.Equal(0x0080c0ffu, sim.VertexArgb(1f) & 0x00ffffffu);
        Assert.Equal(1f, sim.ColourAlpha, 5);

        // Flag 0x4000 is set, so the alpha also carries the ripple; at phase 0 and t = 1 that is
        // (sin(16) + 1) / 2.
        float expected = GroundGridSim.Fade(1f) * sim.Ripple(1f);
        Assert.Equal((uint)(int)(expected * 255f), sim.VertexArgb(1f) >> 24);

        // Anything past the edge is fully transparent whatever the ripple is doing.
        Assert.Equal(0u, sim.VertexArgb(1.5f) >> 24);
    }

    [Fact]
    public void TheRippleRidesOnTheFallOff_AndOnlyMovesWhileTheUvsDo()
    {
        var sim = new GroundGridSim(Record71230());
        Assert.True(sim.Animates);
        Assert.Equal(0f, sim.Phase, 5);

        // 10016631: sin((t * 32 + phase) / 2) lifted into 0..1, so the wave travels outwards.
        Assert.Equal((float)((Math.Sin(0.0) + 1.0) * 0.5), sim.Ripple(0f), 5);
        Assert.Equal((float)((Math.Sin(16.0) + 1.0) * 0.5), sim.Ripple(1f), 5);

        // 1001690b: the phase moves by the time rate times the step.
        sim.AdvancePhase(0.1f);
        Assert.Equal(0.5f, sim.Phase, 5);
        Assert.Equal((float)((Math.Sin((32.0 + 0.5) * 0.5) + 1.0) * 0.5), sim.Ripple(1f), 5);

        // Flag 0x2000 lifts the vertex by half the same wave.
        Assert.Equal(0.5f * sim.Ripple(0.4f), sim.RippleHeight(0.4f), 5);
    }

    [Fact]
    public void AGridWithNoUvRates_NeverUpdatesAgain()
    {
        // 1010e8d0..1010e908: with all four rates zero stock skips the visual's Update, so 71370
        // keeps the vertices its init built. Nothing in it ripples, which is why that never showed.
        var still = new GroundGridSim(Record71370());
        Assert.False(still.Animates);
        Assert.Equal(0f, still.RippleHeight(1f), 5);

        float[] rec = Record71370();
        rec[26] = -0.02f;
        Assert.True(new GroundGridSim(rec).Animates);
    }
}
